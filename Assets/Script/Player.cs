using Fusion;
using UnityEngine;
using Unity.Profiling;

public class Player : NetworkBehaviour, IAfterRender, IBeforeAllTicks, IAfterAllTicks
{
    private static readonly ProfilerMarker flightMarker = new("WitchFlight.Player.Simulate");
    private static readonly ProfilerMarker combatMarker = new("WitchFlight.Player.AuthoritativeCombat");
    [Header("Movement")]
    [SerializeField] private float maxSpeed = 60f;
    [Tooltip("Host/Client 모드에서 내 비행을 즉시 예측합니다. 비교 테스트 시에만 꺼 주세요. 모든 빌드에서 동일하게 설정합니다.")]
    [SerializeField] private bool enableMovementPrediction = true;
    [SerializeField, Min(0.01f)] private float mousePitchSensitivity = 2.5f;
    [SerializeField] private float maxPitch = 60f;
    [SerializeField] private float wallDamageSpeedThreshold = 30f;
    [SerializeField] private float wallDamage = 20f;
    [SerializeField] private float wallDamageCooldown = 0.5f;
    [SerializeField] private float collisionRadius = 0.22f;
    [SerializeField] private float collisionHeight = 1.2f;
    [SerializeField] private float baseAcceleration = 60f;
    [SerializeField] private EquipmentStatTable equipmentStatTable;
    [Header("Network presentation")]
    [Tooltip("PlayerData 정보 UI용 마나 복사 주기입니다. 실제 전투 마나/이동의 틱 주기는 변경하지 않습니다.")]
    [SerializeField, Min(0.05f)] private float infoApSyncInterval = 0.1f;

    [Header("Head look (visual only; flight / lower body unchanged)")]
    [SerializeField] private PlayerHeadLook headLook = new PlayerHeadLook();
    // Two quantized angles reuse the existing aim input, with no per-frame RPC.
    [Networked] private Vector2 HeadLookAngles { get; set; }

    [Header("Lock on")]
    [Tooltip("큰 원(목표 방향)과 작은 원(실제 정면)이 일치했다고 보는 허용 각도입니다. HUD의 정렬 색상에 사용합니다.")]
    [SerializeField, Range(0.1f, 10f)] private float lockAimAlignmentTolerance = 1.5f;
    [Tooltip("이전 IsWithinLockAim 호출과의 호환용 값. 새 마법의 정면 180도 록온 판정에는 사용하지 않습니다.")]
    [SerializeField, Range(0f, 45f)] private float lockAimOffset = 3f;
    [SerializeField, Min(1f)] private float maxLockDistance = 250f;
    [SerializeField, Min(0f)] private float lockAimHeight = 0.8f;
    [SerializeField] private LayerMask lockObstructionMask = ~0;

    [Header("Combat")]
    [Tooltip("마법이 출발하는 위치입니다. ChPrefab 아래 MagicCastRoot의 Position을 조절하세요. 비우면 기존 LockAimPoint를 사용하며, 발사 방향은 기존 조준/록온 규칙을 유지합니다. 네트워크 판정 일치를 위해 애니메이션 본이 아닌 Player 바로 아래에 둡니다.")]
    [SerializeField] private Transform magicCastRoot;
    [SerializeField] private MagicStatTable magicStatTable;
    [SerializeField] private NetworkPrefabRef magicProjectilePrefab;
    [SerializeField, Min(0.01f)] private float parryWindowSeconds = 0.3f;
    [SerializeField, Min(0f)] private float parryCooldownSeconds = 0.3f;
    [SerializeField, Min(0f)] private float defaultHitStunSeconds = 0.25f;
    [SerializeField, Min(0.01f)] private float knockbackDamping = 30f;
    [SerializeField] private string hitReactionState = "AS_Broom_SitFly_Arm_Hit_React";
    [SerializeField] private string deathAnimationState = "AS_Broom_SitFly_Arm_Knock1_Start";

    [Networked] private float Speed { get; set; }
    [Networked] private int SpeedStage { get; set; }
    [Networked] private float acceleration { get; set; }
    [Networked] private float turnSpeed { get; set; }
    [Networked] private float flightMaxSpeed { get; set; }
    [Networked] private PlayerRef LastAttacker { get; set; }
    [Networked] private HatType Hat { get; set; }
    [Networked] private BroomType Broom { get; set; }
    [Networked] private MagicType Magic1 { get; set; }
    [Networked] private MagicType Magic2 { get; set; }
    [Networked] private PlayerConfig AppearanceConfig { get; set; }
    [Networked] private int LoadoutVersion { get; set; }
    [Networked] private NetworkId LockTargetId { get; set; }
    // Index = MagicType (0=None, 1..12=spells). Duplicate equipped spells share a timer.
    [Networked, Capacity(13)] private NetworkArray<TickTimer> MagicCooldowns => default;
    [Networked] private TickTimer ParryTimer { get; set; }
    [Networked] private TickTimer ParryCooldown { get; set; }
    [Networked] public bool IsBoosting { get; private set; }
    [Networked] public bool IsReturningToMap { get; private set; }
    [Networked] public bool DiedFromAltitude { get; private set; }
    [Networked] public TickTimer RespawnTimer { get; private set; }
    [Networked] private TickTimer HitStunTimer { get; set; }
    [Networked] private TickTimer SlowTimer { get; set; }
    [Networked] private TickTimer BindingTimer { get; set; }
    [Networked] public MagicType ChannelMagic { get; private set; }
    [Networked] public Vector3 ChannelEnd { get; private set; }
    [Networked] public NetworkId ChannelTargetId { get; private set; }
    [Networked] private float SlowMultiplier { get; set; }
    [Networked] private Vector3 KnockbackVelocity { get; set; }

    [Networked, OnChangedRender(nameof(NotifyHealthChanged))] public float MaxHp { get; set; }
    [Networked, OnChangedRender(nameof(NotifyHealthChanged))] public float NowHp { get; set; }
    [Networked] public float MaxAp { get; private set; }
    [Networked] public float NowAp { get; private set; }
    [Networked] public float ApRecoveryPerSecond { get; private set; }
    [Networked] public int CurrentMagicSlot { get; private set; }
    [Networked] public int TeamIndex { get; private set; }
    [Networked] public bool IsAlive { get; private set; }
    [Networked] public float LockProgress { get; private set; }
    [Networked] public bool IsFullyLocked { get; private set; }
    [Networked] public float LastReceivedDamage { get; private set; }
    [Networked] public MagicType LastCastMagic { get; private set; }
    [Networked] public int CastSequence { get; private set; }
    [Networked] public int HitSequence { get; private set; }
    [Networked] public int ParrySequence { get; private set; }
    [Networked] public int DeathSequence { get; private set; }

    [SerializeField] private PlayerEquipment equipment;

    private CharacterController characterController;
    private NetworkTransform portalNetworkTransform;
    private bool hasPendingPortalTeleport;
    private Vector3 pendingPortalPosition;
    private TickTimer portalReentryTimer;
    // Replay must start with the SAME path and progress as the server snapshot.
    [Networked] private Vector3 mapReturnStart { get; set; }
    [Networked] private Vector3 mapReturnControl1 { get; set; }
    [Networked] private Vector3 mapReturnControl2 { get; set; }
    [Networked] private Vector3 mapReturnEnd { get; set; }
    [Networked] private Vector3 mapReturnEntryDirection { get; set; }
    [Networked] private Quaternion mapReturnStartRotation { get; set; }
    [Networked] private float mapReturnElapsed { get; set; }
    [Networked] private float mapReturnDuration { get; set; }
    [Networked] private float mapReturnHeight { get; set; }
    [Networked] private float mapReturnSpeed { get; set; }
    [Networked] private int mapReturnSpeedStage { get; set; }
    private CameraFollow localCameraFollow;
    private bool controllerNeedsRenderReset;
    private PlayerAppearance appearance;
    private Animator animator;
    [Networked] private NetworkButtons previousButtons { get; set; }
    private int lastMagicSlot = -1;
    private int renderedLoadoutVersion = -1;
    private int renderedHitSequence;
    private int renderedDeathSequence;
    private bool hasRenderedCombatState;
    [Networked] private float currentPitch { get; set; }
    [Networked] private float currentYaw { get; set; }
    [Networked] private float currentAimTurnSpeed { get; set; }
    [Networked] private float currentTurnSpeed { get; set; }
    private float lockOnTurnSpeed = 360f;
    // Selected broom constants are initialized by the host and replicated once;
    // prediction must not use a client's uninitialized/default equipment values.
    [Networked] private float turnAccel { get; set; }
    [Networked] private float returnSpeed { get; set; }
    [Networked] private float stageTransitionSpeed { get; set; }
    [Networked] private float brakeSpeed { get; set; }
    [Networked] private float boostMultiplier { get; set; }
    [Networked] private float boostApCostPerSecond { get; set; }
    [Networked] private bool boostNeedsRelease { get; set; }
    private float lastWallDamageTime = float.NegativeInfinity;
    private MagicType pendingMagic;
    private NetworkId pendingTargetId;
    private TickTimer castTimer;
    private int castInputTick, pendingInputTick;
    private Vector3 castAimDirection, pendingAimDirection;
    private float channelDamageTime;
    private bool channelNeedsRelease;
    public bool IsChanneling => ChannelMagic != MagicType.None;
    public bool IsBound => TimerIsActive(BindingTimer);
    private CombatPresentation presentation;
    private readonly RaycastHit[] lockHits = new RaycastHit[32];
    private readonly RaycastHit[] instantShotHits = new RaycastHit[32];
    private TickTimer nextInfoApSync;
    private TickTimer nextHitMarkerFeedback;
    private float publishedHp = float.NaN, publishedMaxHp = float.NaN;
    public event System.Action<float, float> HealthChanged;

    // Fusion invokes this only when a received/rendered HP property changes.
    // Both properties may change in one snapshot; publish that pair only once.
    private void NotifyHealthChanged()
    {
        if (publishedHp == NowHp && publishedMaxHp == MaxHp) return;
        publishedHp = NowHp;
        publishedMaxHp = MaxHp;
        HealthChanged?.Invoke(publishedHp, publishedMaxHp);
    }

    private static readonly System.Collections.Generic.HashSet<Player> combatants = new();
    public static System.Collections.Generic.IEnumerable<Player> ActiveCombatants => combatants;
    private MagicTestBot testBot;
    public bool IsTestBot => testBot != null;

    public static Player LocalPlayer { get; private set; }
    public bool IsPresentationReady => Object != null && Object.IsValid && LoadoutVersion > 0 &&
        renderedLoadoutVersion == LoadoutVersion && TeamIndex > 0 && IsAlive;
    public int CurrentSpeedStage => SpeedStage;
    public float CurrentSpeed => Speed;
    private bool SimulatesMovement => Object.HasStateAuthority ||
        (enableMovementPrediction && Object.HasInputAuthority);
    // Use the exact server-selected broom constant, also on the predicting owner.
    public float ForwardCruiseSpeedRatio => Mathf.Clamp01(Mathf.Max(0f, Speed) /
        Mathf.Max(1f, flightMaxSpeed));

    public float SteeringTurnRate => Mathf.Max(0f, turnSpeed * GetTurnMultiplier());
    public MagicStatEntry SelectedMagicStats => GetMagicStats(GetSelectedMagic());
    public MagicStatTable MagicTable => magicStatTable;
    public float SelectedMagicApCost => CurrentMagicSlot == 3
        ? (magicStatTable != null ? magicStatTable.parryApCost : 16f) : SelectedMagicStats.apCost;
    public bool IsParrying => TimerIsActive(ParryTimer);
    // Read-only HUD eligibility; actual input and damage still run on StateAuthority.
    public float ParryWindowSeconds => Mathf.Max(0f,
        magicStatTable != null ? magicStatTable.parryWindowSeconds : parryWindowSeconds);
    public bool CanStartParryNow => Object != null && Object.IsValid && IsAlive && !IsReturningToMap &&
        BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive &&
        !IsHitStunned && !TimerIsActive(ParryCooldown) &&
        NowAp >= Mathf.Max(0f, magicStatTable != null ? magicStatTable.parryApCost : 16f);
    public Vector3 ParryHintVelocity => (IsBound || IsHitStunned || IsTestBot ? Vector3.zero : transform.forward * Speed) + KnockbackVelocity;
    public float ParryHintRadius => characterController != null
        ? characterController.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z))
        : collisionRadius;
    public bool IsHitStunned => TimerIsActive(HitStunTimer);
    public bool IsStealthed => false; // Compatibility for external presentation components.
    // Smoke is presentation-only concealment, NOT stealth / untargetability.
    public bool IsHiddenBySmokeFor(Player observer) => Object != null && Object.IsValid &&
        observer != null && observer.Object != null &&
        observer.Object.IsValid && observer.Runner == Runner && observer != this &&
        TeamIndex > 0 && observer.TeamIndex > 0 && TeamIndex != observer.TeamIndex && BattleFlag.Instance != null &&
        BattleFlag.Instance.Runner == Runner && BattleFlag.Instance.ContainsSmoke(LockAimPoint);
    public bool HasActiveMine => Object != null && MagicProjectile.HasMineOwnedBy(Object.InputAuthority, Runner);
    public float MaxLockDistance => maxLockDistance;
    public Vector3 LockAimPoint => transform.position + Vector3.up * lockAimHeight;
    public Vector3 MagicCastPosition => magicCastRoot != null ? magicCastRoot.position : LockAimPoint;

    public bool IsAimAligned(Vector3 desiredDirection)
    {
        return IsFiniteDirection(desiredDirection) && desiredDirection.sqrMagnitude > 0.0001f &&
            Vector3.Angle(transform.forward, desiredDirection) <= Mathf.Clamp(lockAimAlignmentTolerance, 0.1f, 10f);
    }

    public bool IsWithinLockAim(Vector3 desiredDirection)
    {
        float allowedAngle = Mathf.Clamp(lockAimAlignmentTolerance, 0.1f, 10f) + Mathf.Clamp(lockAimOffset, 0f, 45f);
        return IsFiniteDirection(desiredDirection) && desiredDirection.sqrMagnitude > 0.0001f &&
            Vector3.Angle(transform.forward, desiredDirection) <= allowedAngle;
    }

    private void Awake()
    {
        testBot = GetComponent<MagicTestBot>();
        characterController = GetComponent<CharacterController>();
        if (characterController == null)
        {
            characterController = gameObject.AddComponent<CharacterController>();
            characterController.radius = collisionRadius;
            characterController.height = collisionHeight;
            characterController.center = new Vector3(0f, collisionHeight * 0.5f, 0f);
            characterController.stepOffset = 0.3f;
            characterController.skinWidth = 0.08f;
        }

        equipment ??= GetComponent<PlayerEquipment>();
        appearance = GetComponent<PlayerAppearance>();
        animator = GetComponent<Animator>();
        presentation = GetComponent<CombatPresentation>();
    }

    public void InitPlayer(PlayerData data)
    {
        if (!Object.HasStateAuthority || data == null)
            return;

        HatStatEntry hatStats = GetHatStats(data.hat);
        BroomStatEntry broomStats = GetBroomStats(data.broom);

        MaxAp = Mathf.Max(1f, hatStats.maxAp);
        NowAp = MaxAp;
        ApRecoveryPerSecond = Mathf.Max(0f, hatStats.apRecoveryPerSecond);

        MaxHp = Mathf.Max(1f, broomStats.maxHp);
        NowHp = MaxHp;
        flightMaxSpeed = Mathf.Max(1f, broomStats.maxSpeed);
        turnSpeed = Mathf.Max(0f, broomStats.turnSpeed);
        lockOnTurnSpeed = Mathf.Max(0f, broomStats.lockOnTurnSpeed);
        turnAccel = Mathf.Max(0.01f, broomStats.turnAcceleration);
        returnSpeed = Mathf.Max(0.01f, broomStats.turnReturnSpeed);
        stageTransitionSpeed = Mathf.Max(0.01f, broomStats.speedStageTransitionSpeed);
        brakeSpeed = Mathf.Max(0.01f, broomStats.brakeSpeed);
        boostMultiplier = Mathf.Max(1f, broomStats.boostMultiplier);
        boostApCostPerSecond = Mathf.Max(0f, broomStats.boostApCostPerSecond);

        acceleration = Mathf.Max(0f, baseAcceleration);
        CurrentMagicSlot = 1;
        TeamIndex = data.teamIndex;
        IsAlive = true;
        Speed = 0f;
        SpeedStage = 0;
        LastReceivedDamage = 0f;
        LastCastMagic = MagicType.None;
        LockTargetId = default;
        LockProgress = 0f;
        IsFullyLocked = false;
        for (int i = 0; i < MagicCooldowns.Length; i++) MagicCooldowns.Set(i, TickTimer.None);
        ParryTimer = TickTimer.None;
        ParryCooldown = TickTimer.None;
        IsBoosting = false;
        boostNeedsRelease = false;
        IsReturningToMap = false;
        DiedFromAltitude = false;
        RespawnTimer = TickTimer.None;
        nextHitMarkerFeedback = TickTimer.None;
        HitStunTimer = TickTimer.None;
        SlowTimer = TickTimer.None;
        BindingTimer = TickTimer.None;
        ChannelMagic = MagicType.None;
        ChannelTargetId = default;
        channelDamageTime = 0f;
        channelNeedsRelease = false;
        SlowMultiplier = 1f;
        KnockbackVelocity = default;
        currentPitch = NormalizePitch(transform.eulerAngles.x);
        currentYaw = transform.eulerAngles.y;
        currentAimTurnSpeed = 0f;
        currentTurnSpeed = 0f;
        previousButtons = default;
        HeadLookAngles = Vector2.zero;
        pendingMagic = MagicType.None;
        castTimer = TickTimer.None;

        Hat = data.hat;
        Broom = data.broom;
        Magic1 = data.magic1;
        Magic2 = data.magic2;
        AppearanceConfig = data.GetPlayerConfig().Sanitized();
        LoadoutVersion++;

        SyncHealthToPlayerData();
        SyncApToPlayerData();
    }

    private HatStatEntry GetHatStats(HatType selectedHat)
    {
        if (equipmentStatTable != null)
            return equipmentStatTable.GetHatStats(selectedHat);

        Debug.LogError("ChPrefab has no EquipmentStatTable. Using fallback hat stats.", this);
        return new HatStatEntry
        {
            hat = HatType.None,
            maxAp = 100f,
            apRecoveryPerSecond = 5f
        };
    }

    private BroomStatEntry GetBroomStats(BroomType selectedBroom)
    {
        if (equipmentStatTable != null)
            return equipmentStatTable.GetBroomStats(selectedBroom);

        Debug.LogError("ChPrefab has no EquipmentStatTable. Using fallback broom stats.", this);
        return new BroomStatEntry
        {
            broom = BroomType.None,
            maxHp = 200f,
            maxSpeed = Mathf.Max(1f, maxSpeed),
            turnSpeed = 180f,
            turnAcceleration = 18f,
            turnReturnSpeed = 30f,
            lockOnTurnSpeed = 360f,
            speedStageTransitionSpeed = 60f,
            brakeSpeed = 80f,
            boostMultiplier = 1.35f,
            boostApCostPerSecond = 40f
        };
    }

    private MagicStatEntry GetMagicStats(MagicType magic)
    {
        if (magicStatTable != null)
            return magicStatTable.GetStats(magic);

        magicStatTable = Resources.Load<MagicStatTable>("MagicStatTable");
        return magicStatTable != null ? magicStatTable.GetStats(magic) : MagicStatTable.DefaultEntry(magic);
    }

    public override void Spawned()
    {
        combatants.Add(this);
        if (testBot != null && Object.HasStateAuthority) InitializeTestBot();
        if (presentation == null) presentation = gameObject.AddComponent<CombatPresentation>();
        // Predicted input owners use Fusion's local timeline; remote characters
        // interpolate server snapshots. The fallback remains available for A/B tests.
        Object.ForceRemoteRenderTimeframe = !SimulatesMovement;
        publishedHp = publishedMaxHp = float.NaN;
        NotifyHealthChanged(); // OnChangedRender does not initialize the spawn snapshot.
        if (GetComponent<hpfollow>() == null) gameObject.AddComponent<hpfollow>();
        // Network characters must never follow this client's global lobby settings.
        appearance ??= GetComponent<PlayerAppearance>();
        appearance?.UnbindFromDataConfig();
        if (characterController != null)
        {
            characterController.enabled = false;
            characterController.enabled = true;
        }

        if (!Object.HasInputAuthority)
            return;

        LocalPlayer = this;

        bool releaseCursor = BattleManager.Instance == null ||
            !BattleManager.Instance.IsGameplayActive || CombatPresentation.MenuOpen;
        Cursor.lockState = releaseCursor ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = releaseCursor;

        CameraManager cameraManager = CameraManager.Instance;
        if (cameraManager == null)
            cameraManager = FindFirstObjectByType<CameraManager>();

        if (cameraManager != null)
        {
            localCameraFollow = cameraManager.GetComponent<CameraFollow>();
            cameraManager.SetTarget(gameObject);
        }
        else
            Debug.LogError("Battle scene has no CameraManager.");
    }

    public override void FixedUpdateNetwork()
    {
        if (!SimulatesMovement || LoadoutVersion <= 0)
            return;
        using var simulationSample = flightMarker.Auto();

        HeadLookAngles = Vector2.zero;

        if (Object.HasStateAuthority && (BattleManager.Instance == null ||
            !BattleManager.Instance.IsGameplayActive || !IsAlive || IsReturningToMap || IsHitStunned))
            StopChannel();

        // Every tick must receive a held input to sustain boost.
        IsBoosting = false;

        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
        {
            IsReturningToMap = false;
            hasPendingPortalTeleport = false;
            Speed = 0f;
            SpeedStage = 0;
            currentTurnSpeed = 0f;
            previousButtons = default;
            pendingMagic = MagicType.None;
            pendingTargetId = default;
            castTimer = TickTimer.None;
            KnockbackVelocity = Vector3.zero;
            ClearLockTargetInternal();
            return;
        }
        if (!IsAlive)
        {
            IsReturningToMap = false;
            hasPendingPortalTeleport = false;
            return;
        }

        ResetControllerAfterRender();
        if (testBot != null)
        {
            if (Object.HasStateAuthority) testBot.Simulate(this);
            return;
        }
        if (CheckAltitudeLimit())
            return;
        if (Object.HasStateAuthority && hasPendingPortalTeleport)
        {
            ApplyPortalTeleport();
            CheckAltitudeLimit();
            return;
        }
        if (UpdateMapBoundaryReturn())
        {
            if (CheckAltitudeLimit())
                return;
            // Consume edge inputs without executing them when control resumes.
            previousButtons = GetInput(out NetworkInputData ignoredInput) ? ignoredInput.buttons : default;
            RegenerateAp();
            return;
        }
        if (Object.HasStateAuthority) UpdatePendingCast();

        if (!GetInput(out NetworkInputData data))
        {
            if (Object.HasStateAuthority) StopChannel();
            ContinueWithoutActions();
            if (CheckAltitudeLimit())
                return;
            RegenerateAp();
            return;
        }

        ProcessInput(data);
        if (!data.suppressActions && !IsReturningToMap && !IsHitStunned)
            HeadLookAngles = headLook.GetAngles(transform, data.aimDirection);
        if (CheckAltitudeLimit())
            return;
        if (!IsBoosting)
            RegenerateAp();
    }

    private bool CheckAltitudeLimit()
    {
        if (!Object.HasStateAuthority) return false;
        MapBoundaryTable table = BattleManager.Instance != null ? BattleManager.Instance.MapBoundary : null;
        if (!IsAlive || table == null || !table.ExceedsAltitudeLimit(transform.position.y))
            return false;

        // Environmental death: no parry and no stale attacker kill credit.
        // Reuse health replication, flag drop and the existing respawn sequence.
        DiedFromAltitude = true;
        hasPendingPortalTeleport = false;
        TakeDamage(Mathf.Max(1f, NowHp), PlayerRef.None, MagicType.None, false, 0f, 0f);
        return true;
    }

    private bool UpdateMapBoundaryReturn()
    {
        if (!IsReturningToMap)
        {
            MapBoundaryTable table = BattleManager.Instance.MapBoundary;
            if (table == null || !table.boundaryEnabled || !table.HasValidBounds || table.Contains(transform.position))
                return false;

            Vector3 travel = transform.forward * (Speed < -0.01f ? -1f : 1f);
            travel.y = 0f;
            if (travel.sqrMagnitude < 0.0001f)
                travel = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * Vector3.forward;
            mapReturnEntryDirection = travel.normalized;
            mapReturnStart = transform.position;
            mapReturnStartRotation = transform.rotation;
            table.CreateReturnPath(mapReturnStart, mapReturnEntryDirection,
                out Vector3 control1, out Vector3 control2, out Vector3 end);
            mapReturnControl1 = control1;
            mapReturnControl2 = control2;
            mapReturnEnd = end;
            mapReturnElapsed = 0f;
            mapReturnDuration = Mathf.Max(0.25f, table.returnDuration);
            mapReturnHeight = Mathf.Max(0f, table.climbHeight);
            mapReturnSpeedStage = Mathf.Clamp(Mathf.Abs(SpeedStage), 1, 3);
            mapReturnSpeed = Mathf.Max(Mathf.Abs(Speed), flightMaxSpeed * mapReturnSpeedStage / 3f);
            IsReturningToMap = true;
            pendingMagic = MagicType.None;
            castTimer = TickTimer.None;
            KnockbackVelocity = Vector3.zero;
            currentTurnSpeed = 0f;
            ClearLockTargetInternal();
        }

        mapReturnElapsed += Runner.DeltaTime;
        float t = Mathf.Clamp01(mapReturnElapsed / mapReturnDuration);
        MapBoundaryTable.EvaluateReturnPath(mapReturnStart, mapReturnControl1, mapReturnControl2,
            mapReturnEnd, mapReturnHeight, t, out Vector3 position, out Vector3 tangent);
        Quaternion rotation = tangent.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(tangent.normalized, Vector3.up) : transform.rotation;
        rotation = Quaternion.Slerp(mapReturnStartRotation, rotation, Mathf.Clamp01(t * 5f));
        if (t >= 1f)
        {
            position = mapReturnEnd;
            rotation = Quaternion.LookRotation(-mapReturnEntryDirection, Vector3.up);
        }

        // Scripted flight ignores blocking scenery; keep the actual network root moving.
        // Never call Teleport every tick: other clients should see a continuous U-turn.
        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (controllerWasEnabled) characterController.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (controllerWasEnabled) characterController.enabled = true;
        controllerNeedsRenderReset = false;

        if (t >= 1f)
        {
            currentPitch = 0f;
            currentYaw = rotation.eulerAngles.y;
            currentAimTurnSpeed = 0f;
            currentTurnSpeed = 0f;
            KnockbackVelocity = Vector3.zero;
            SpeedStage = mapReturnSpeedStage;
            Speed = mapReturnSpeed;
            IsReturningToMap = false;
        }
        return true;
    }

    // Physics callbacks may run outside Fusion ticks; only queue the request here.
    public bool TryQueuePortalTeleport(Vector3 destination, float reentryDelay)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority ||
            Runner == null || !Runner.IsRunning || !IsAlive || IsReturningToMap ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive ||
            hasPendingPortalTeleport || !portalReentryTimer.ExpiredOrNotRunning(Runner))
            return false;

        if (portalNetworkTransform == null)
            portalNetworkTransform = GetComponent<NetworkTransform>();
        if (portalNetworkTransform == null)
        {
            Debug.LogWarning("Portal requires NetworkTransform on the Player root.", this);
            return false;
        }

        pendingPortalPosition = destination;
        hasPendingPortalTeleport = true;
        portalReentryTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, reentryDelay));
        return true;
    }

    private void ApplyPortalTeleport()
    {
        hasPendingPortalTeleport = false;
        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (controllerWasEnabled)
            characterController.enabled = false;
        try
        {
            // Replicate a teleport, not an interpolated flight across the map.
            // Preserve flight direction, speed, HP, equipment and input authority.
            portalNetworkTransform.Teleport(pendingPortalPosition);
        }
        finally
        {
            if (controllerWasEnabled)
                characterController.enabled = true;
        }
        controllerNeedsRenderReset = false;
    }

    private void ResetControllerAfterRender()
    {
        if (!controllerNeedsRenderReset)
            return;
        controllerNeedsRenderReset = false;
        if (characterController == null || !characterController.enabled)
            return;

        // NetworkTransform.BeforeAllTicks has already restored the simulation pose.
        // Reset the controller's cached render-space pose before the first Move call,
        // just as Fusion's NetworkCharacterController does when restoring its state.
        // Do this once per simulation batch, not once per catch-up simulation tick.
        Vector3 simulationPosition = transform.position;
        Quaternion simulationRotation = transform.rotation;
        characterController.enabled = false;
        transform.SetPositionAndRotation(simulationPosition, simulationRotation);
        characterController.enabled = true;
    }

    void IBeforeAllTicks.BeforeAllTicks(bool resimulation, int tickCount)
    {
        // A single frame may contain a rollback batch AND a forward batch. Reset
        // the CC cache at the first movement tick of EACH batch, after Fusion's
        // NetworkTransform has restored the correct simulation pose.
        if (tickCount > 0) controllerNeedsRenderReset = true;
    }

    void IAfterAllTicks.AfterAllTicks(bool resimulation, int tickCount)
    {
        // Coalesce informational UI state after simulation, never during owner replay.
        // Also flush a final partial value when regeneration is zero or play is paused.
        if (Object.HasStateAuthority && LoadoutVersion > 0 && tickCount > 0)
            SyncApToPlayerData();
    }

    void IAfterRender.AfterRender()
    {
        controllerNeedsRenderReset = true;
        if (Object != null && Object.IsValid && Object.HasInputAuthority && localCameraFollow != null)
            localCameraFollow.FollowRenderedPlayer(this);
    }

    private void ProcessInput(NetworkInputData data)
    {
        NetworkButtons buttons = data.buttons;

        if (data.suppressActions)
        {
            if (Object.HasStateAuthority) StopChannel();
            ClearLockTargetInternal();
            pendingMagic = MagicType.None;
            previousButtons = default;
            ContinueWithoutActions();
            return;
        }

        if (!IsAlive)
        {
            previousButtons = buttons;
            return;
        }

        if (buttons.WasPressed(previousButtons, PlayerInputButton.Accelerate))
            SpeedStage = Mathf.Min(SpeedStage + 1, 3);

        if (buttons.WasPressed(previousButtons, PlayerInputButton.Decelerate))
            SpeedStage = Mathf.Max(SpeedStage - 1, -3);

        if (buttons.WasPressed(previousButtons, PlayerInputButton.MagicSlot1))
            SelectMagicSlot(1);

        if (buttons.WasPressed(previousButtons, PlayerInputButton.MagicSlot2))
            SelectMagicSlot(2);

        if (buttons.WasPressed(previousButtons, PlayerInputButton.MagicSlot3))
            SelectMagicSlot(3);

        UpdateHeldBoost(buttons.IsSet(PlayerInputButton.Boost));

        if (!IsHitStunned)
            PlayerTurn(data, buttons);

        // Never replay attacks, spawning, damage, RPCs or another player's state
        // while predicting/re-simulating this owner's movement.
        if (Object.HasStateAuthority) ProcessAuthoritativeCombat(data, buttons);
        else if (Object.HasInputAuthority) ProcessPredictedProjectileInput(data, buttons);

        if (IsBound)
        {
            Speed = 0f;
            KnockbackVelocity = Vector3.zero;
        }
        else if (IsHitStunned)
        {
            Speed = Mathf.MoveTowards(Speed, 0f, brakeSpeed * Runner.DeltaTime);
        }
        else
        {
            UpdateSpeed();

            GoForward();
        }

        ApplyKnockback();
        previousButtons = buttons;
    }

    private void ProcessAuthoritativeCombat(NetworkInputData data, NetworkButtons buttons)
    {
        castInputTick = Runner.Tick.Raw;
        castAimDirection = data.magicAimDirection;
        using var combatSample = combatMarker.Auto();
        if (buttons.WasPressed(previousButtons, PlayerInputButton.Parry)) TryStartParry();
        bool held = buttons.IsSet(PlayerInputButton.Lock);
        bool pressed = buttons.WasPressed(previousButtons, PlayerInputButton.Lock);
        bool released = !held && previousButtons.IsSet(PlayerInputButton.Lock);
        MagicStatEntry stats = SelectedMagicStats;

        if (!held) channelNeedsRelease = false;
        if (IsHitStunned) { StopChannel(); ClearLockTargetInternal(); return; }

        if (stats.IsChanneled)
        {
            ClearLockTargetInternal();
            if (held && !channelNeedsRelease) UpdateChannel(stats, data.lockTarget, data.aimDirection);
            else StopChannel();
            return;
        }
        StopChannel();
        if (stats.requiresTarget)
        {
            if (held || released) SetInputLockTarget(data.lockTarget);
            if (held) UpdateLockCharge();
            if (released)
            {
                TryCastSelectedMagic();
                ClearLockTargetInternal();
            }
        }
        else
        {
            ClearLockTargetInternal();
            // Instant spells fire on press. Only lock-on spells fire on release.
            if (pressed) TryCastSelectedMagic();
        }
    }

    // Only lock progress is rollback-predicted here. No gameplay cast, mana mutation,
    // damage, NetworkObject spawn or RPC may run on the predicting client.
    private void ProcessPredictedProjectileInput(NetworkInputData data, NetworkButtons buttons)
    {
        MagicStatEntry stats = SelectedMagicStats;
        bool held = buttons.IsSet(PlayerInputButton.Lock);
        bool released = !held && previousButtons.IsSet(PlayerInputButton.Lock);
        bool pressed = buttons.WasPressed(previousButtons, PlayerInputButton.Lock);
        if (IsHitStunned || CurrentMagicSlot == 3 || stats.IsChanneled)
        { ClearLockTargetInternal(); return; }
        if (stats.requiresTarget)
        {
            if (held || released) SetInputLockTarget(data.lockTarget);
            if (held) UpdateLockCharge();
            if (released)
            {
                if (!stats.requiresFullLock || IsFullyLocked) TryPredictProjectile(stats);
                ClearLockTargetInternal();
            }
        }
        else
        {
            ClearLockTargetInternal();
            if (pressed) TryPredictProjectile(stats);
        }
    }

    private void TryPredictProjectile(MagicStatEntry stats)
    {
        int magicIndex = (int)stats.magic;
        if (!Runner.IsForward || !magicProjectilePrefab.IsValid || stats.projectileSpeed <= 0f ||
            (magicStatTable != null && !magicStatTable.predictLocalProjectiles) ||
            magicIndex <= 0 || magicIndex >= MagicCooldowns.Length || stats.IsChanneled ||
            TimerIsActive(MagicCooldowns[magicIndex]) || NowAp < stats.apCost ||
            (stats.healthCost > 0f && NowHp <= stats.healthCost)) return;
        Player target = null;
        if (stats.requiresTarget && !TryGetValidLockTarget(out target)) return;
        ProjectilePredictionView.Predict(this, target, stats, Runner.Tick.Raw);
    }

    private void ContinueWithoutActions()
    {
        if (IsBound) { Speed = 0f; KnockbackVelocity = Vector3.zero; return; }
        if (IsHitStunned)
            Speed = Mathf.MoveTowards(Speed, 0f, brakeSpeed * Runner.DeltaTime);
        else { UpdateSpeed(); GoForward(); }
        ApplyKnockback();
    }

    private void SelectMagicSlot(int slot)
    {
        if (CurrentMagicSlot == slot)
            return;

        CurrentMagicSlot = slot;
        if (Object.HasStateAuthority)
        {
            StopChannel();
            pendingMagic = MagicType.None;
            channelNeedsRelease = previousButtons.IsSet(PlayerInputButton.Lock);
        }
        ClearLockTargetInternal();
    }

    private void UpdateSpeed()
    {
        float targetSpeed = flightMaxSpeed * SpeedStage / 3f;
        targetSpeed *= GetMovementMultiplier();

        if (IsBoosting)
            targetSpeed *= boostMultiplier;

        // Brake to zero before reversing direction; magnitude decides acceleration in reverse too.
        bool reversing = Speed * targetSpeed < 0f;
        if (reversing)
            targetSpeed = 0f;
        float rate = reversing || Mathf.Abs(targetSpeed) < Mathf.Abs(Speed)
            ? brakeSpeed : stageTransitionSpeed * GetMovementMultiplier();
        Speed = Mathf.MoveTowards(Speed, targetSpeed, rate * Runner.DeltaTime);
    }

    private float GetMovementMultiplier()
    {
        return TimerIsActive(SlowTimer) ? Mathf.Clamp(SlowMultiplier, 0.05f, 1f) : 1f;
    }

    private float GetTurnMultiplier()
    {
        return GetMovementMultiplier();
    }

    private void UpdateHeldBoost(bool held)
    {
        if (!held)
        {
            boostNeedsRelease = false;
            return;
        }
        if (boostNeedsRelease || IsHitStunned || IsBound)
            return;

        float cost = boostApCostPerSecond * Runner.DeltaTime;
        if (!TryConsumeMovementAp(cost))
        {
            // Exhaust the remaining fraction and avoid rapid on/off boosting as AP regenerates.
            TryConsumeMovementAp(NowAp);
            boostNeedsRelease = true;
            return;
        }

        if (SpeedStage == 0)
            SpeedStage = 1;

        IsBoosting = true;
        if (cost > 0f && NowAp <= 0f)
            boostNeedsRelease = true;
    }

    private void TryStartParry()
    {
        if (TimerIsActive(ParryCooldown) || IsHitStunned)
            return;

        float cost = magicStatTable != null ? magicStatTable.parryApCost : 16f;
        if (!TryConsumeAp(cost))
            return;
        ParryTimer = TickTimer.CreateFromSeconds(Runner,
            magicStatTable != null ? magicStatTable.parryWindowSeconds : parryWindowSeconds);
        ParryCooldown = TickTimer.CreateFromSeconds(Runner,
            magicStatTable != null ? magicStatTable.parryCooldownSeconds : parryCooldownSeconds);
    }

    private void UpdateLockCharge()
    {
        MagicStatEntry stats = GetMagicStats(GetSelectedMagic());
        if (!CanAcquireMagicTarget() || !stats.requiresTarget || !TryGetValidLockTarget(out _))
        {
            ClearLockTargetInternal();
            return;
        }

        float chargeSeconds = Mathf.Max(0.01f, stats.lockChargeSeconds);
        LockProgress = Mathf.Clamp01(LockProgress + Runner.DeltaTime / chargeSeconds);
        IsFullyLocked = LockProgress >= 1f;
    }

    private void SetInputLockTarget(NetworkId targetId)
    {
        if (!CanAcquireMagicTarget() || !SelectedMagicStats.requiresTarget ||
            !Runner.TryFindObject(targetId, out NetworkObject targetObject) ||
            targetObject == null || !IsValidLockTarget(targetObject.GetComponent<Player>()))
        {
            ClearLockTargetInternal();
            return;
        }
        if (!LockTargetId.Equals(targetId))
        {
            ClearLockTargetInternal();
            LockTargetId = targetId;
        }
    }

    private void TryCastSelectedMagic()
    {
        if (IsHitStunned)
            return;

        if (CurrentMagicSlot == 3)
        {
            TryStartParry();
            return;
        }
        if (pendingMagic != MagicType.None)
            return;

        MagicType selectedMagic = GetSelectedMagic();
        if (selectedMagic == MagicType.None)
            return;
        int magicIndex = (int)selectedMagic;
        if (magicIndex <= 0 || magicIndex >= MagicCooldowns.Length || TimerIsActive(MagicCooldowns[magicIndex]))
            return;

        MagicStatEntry stats = GetMagicStats(selectedMagic);
        if (stats.magic == MagicType.None)
            return;

        Player target = null;
        if (stats.requiresTarget)
        {
            if (!TryGetValidLockTarget(out target))
                return;

            if (stats.requiresFullLock && !IsFullyLocked)
                return;

            if (stats.range > 0f && Vector3.Distance(transform.position, target.transform.position) > stats.range)
                return;
        }

        if (stats.IsChanneled || (stats.healthCost > 0f && NowHp <= stats.healthCost)) return;
        if (stats.effect == MagicEffectKind.Healing && NowHp >= MaxHp) return;
        if (stats.effect == MagicEffectKind.Smoke &&
            (!SmokeCloudState.ValidSettings(stats.radius, stats.effectDuration) ||
             BattleFlag.Instance == null || !BattleFlag.Instance.CanCreateSmoke(Runner))) return;
        if (stats.projectileSpeed > 0f && !magicProjectilePrefab.IsValid) return;

        if (!TryConsumeAp(stats.apCost))
            return;
        if (stats.healthCost > 0f)
        {
            NowHp -= stats.healthCost; // A cost is not a hit and cannot be parried.
            SyncHealthToPlayerData();
        }

        MagicCooldowns.Set(magicIndex, TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, stats.cooldownSeconds)));
        LastCastMagic = selectedMagic;
        CastSequence++;
        if (stats.castSeconds > 0f && stats.effect != MagicEffectKind.Flare && stats.effect != MagicEffectKind.Smoke)
        {
            pendingMagic = selectedMagic;
            pendingInputTick = castInputTick;
            pendingAimDirection = castAimDirection;
            pendingTargetId = target != null ? target.Object.Id : default;
            castTimer = TickTimer.CreateFromSeconds(Runner, stats.castSeconds);
        }
        else
        {
            CastMagic(stats, target);
        }
    }

    public MagicType GetSelectedMagic()
    {
        return GetMagicInSlot(CurrentMagicSlot);
    }

    // Shared by local selection/HUD, rollback prediction and authoritative input/RPCs.
    // A cooldown must prevent charging, not just reject the eventual release.
    public bool CanAcquireMagicTarget()
    {
        if (Object == null || !Object.IsValid || Runner == null || !IsAlive ||
            IsHitStunned || IsReturningToMap)
            return false;

        int index = (int)GetSelectedMagic();
        return index > 0 && index < MagicCooldowns.Length &&
            SelectedMagicStats.UsesAutoTarget && !TimerIsActive(MagicCooldowns[index]);
    }

    public MagicType GetMagicInSlot(int slot)
    {
        return slot switch
        {
            1 => Magic1,
            2 => Magic2,
            _ => MagicType.None
        };
    }

    // Read-only UI access to authoritative replicated timers. Never start cooldowns in UI.
    public float GetMagicCooldownRemaining(int slot)
    {
        if (Object == null || !Object.IsValid || Runner == null || !Runner.IsRunning) return 0f;
        TickTimer timer;
        if (slot == 3) timer = ParryCooldown;
        else
        {
            int index = (int)GetMagicInSlot(slot);
            if (index <= 0 || index >= MagicCooldowns.Length) return 0f;
            timer = MagicCooldowns[index];
        }
        return Mathf.Max(0f, timer.RemainingTime(Runner) ?? 0f);
    }

    public float GetMagicCooldownDuration(int slot)
    {
        if (slot == 3) return Mathf.Max(0f, magicStatTable != null ? magicStatTable.parryCooldownSeconds : parryCooldownSeconds);
        MagicType magic = GetMagicInSlot(slot);
        return magic == MagicType.None || magicStatTable == null ? 0f : Mathf.Max(0f, magicStatTable.GetStats(magic).cooldownSeconds);
    }

    private void UpdatePendingCast()
    {
        if (pendingMagic == MagicType.None)
            return;
        if (IsHitStunned)
        {
            pendingMagic = MagicType.None;
            return;
        }
        if (!castTimer.Expired(Runner))
            return;

        MagicStatEntry stats = GetMagicStats(pendingMagic);
        pendingMagic = MagicType.None;
        Player target = Runner.TryFindObject(pendingTargetId, out NetworkObject obj) && obj != null
            ? obj.GetComponent<Player>() : null;
        if (stats.requiresTarget && !IsValidLockTarget(target))
            return;
        castInputTick = pendingInputTick;
        castAimDirection = pendingAimDirection;
        CastMagic(stats, target);
    }

    private void CastMagic(MagicStatEntry stats, Player target)
    {
        if (stats.effect == MagicEffectKind.Flare)
        {
            MagicProjectile.BreakHomingFor(this);
            RPC_PresentCast(stats.magic, LockAimPoint, -transform.forward, 0f);
        }
        else if (stats.effect == MagicEffectKind.Smoke)
        {
            // Persistent replicated area survives the caster's death/despawn.
            BattleFlag.Instance?.CreateSmoke(this, stats);
        }
        else if (stats.effect == MagicEffectKind.Healing)
        {
            RestoreHealth(stats.healing);
            RPC_PresentCast(stats.magic, LockAimPoint, LockAimPoint, 0f);
        }
        else LaunchMagic(stats, target);
    }

    private void LaunchMagic(MagicStatEntry stats, Player target)
    {
        Vector3 origin = MagicCastPosition;
        // Resolve the latest target position at execution time, including delayed casts.
        // Locked projectiles use their target; only Vision/Dark instant spells use the Dot.
        Vector3 direction = MagicProjectile.ResolveLaunchDirection(stats.requiresTarget,
            transform.forward, transform.forward, target != null ? target.LockAimPoint - origin : Vector3.zero);
        direction = ResolveDotCastDirection(stats, castAimDirection, direction);
        if (stats.projectileSpeed > 0f)
        {
            if (!magicProjectilePrefab.IsValid)
            {
                Debug.LogError("ChPrefab has no MagicProjectile network prefab.", this);
                return;
            }
            NetworkObject shot = Runner.Spawn(magicProjectilePrefab, origin,
                Quaternion.LookRotation(direction), Object.InputAuthority);
            if (shot != null)
            {
                shot.GetComponent<MagicProjectile>().Initialize(this, target, stats, direction,
                    IsTestBot ? 0 : castInputTick);
                RPC_PresentCast(stats.magic, origin, origin, 0f);
            }
            return;
        }

        Vector3 end = TraceMagic(stats, origin, direction, out Player victim);
        if (stats.effect == MagicEffectKind.AreaDamage)
        {
            foreach (Player enemy in combatants)
            {
                if (enemy == null || enemy.Runner != Runner) continue;
                if (enemy != null && enemy.IsEnemyOf(this) &&
                    (enemy.LockAimPoint - end).sqrMagnitude <= stats.radius * stats.radius &&
                    MagicProjectile.HasBlastSight(end, enemy))
                    enemy.ReceiveMagicHit(stats, Object.InputAuthority, Object.Id);
            }
        }
        else if (victim != null && victim.IsEnemyOf(this))
            victim.ReceiveMagicHit(stats, Object.InputAuthority, Object.Id);

        RPC_PresentCast(stats.magic, origin, end, 0f);
    }

    // Local input-only query: converge the physical cast root onto the point under the Dot.
    // Use a thin camera ray for picking, then let the host perform the configured SphereCast
    // from MagicCastPosition. This avoids shoulder-camera parallax and cannot bypass walls.
    public Vector3 GetDotAimDirection(Ray cameraRay)
    {
        if (!IsFiniteDirection(cameraRay.direction) || cameraRay.direction.sqrMagnitude < 0.0001f)
            return Vector3.zero;
        // Both slots are considered so switching slots and firing in the same input tick works.
        float range = Mathf.Max(0.1f, Mathf.Max(GetMagicStats(Magic1).range, GetMagicStats(Magic2).range));
        MagicStatEntry aimQuery = new MagicStatEntry { range = range, hitscanRadius = 0f };
        Vector3 point = TraceMagic(aimQuery, cameraRay.origin, cameraRay.direction.normalized, out _);
        Vector3 offset = point - MagicCastPosition;
        return offset.sqrMagnitude > 0.0001f ? offset.normalized : cameraRay.direction.normalized;
    }

    private static Vector3 ResolveDotCastDirection(MagicStatEntry stats, Vector3 dotDirection, Vector3 fallback)
    {
        if ((stats.magic == MagicType.Vision || stats.magic == MagicType.Dark) && stats.projectileSpeed <= 0f &&
            IsFiniteDirection(dotDirection) && dotDirection.sqrMagnitude > 0.0001f)
            return dotDirection.normalized;
        return fallback;
    }

    // Shared authoritative sweep for hitscan and sustained straight beams.
    // Independent from projectileRadius: widening hitscan never changes flying spells.
    private Vector3 TraceMagic(MagicStatEntry stats, Vector3 origin, Vector3 direction, out Player victim)
    {
        victim = null;
        float range = Mathf.Max(0.1f, stats.range);
        float radius = Mathf.Max(0f, stats.hitscanRadius);
        int count = radius > 0f
            ? Physics.SphereCastNonAlloc(origin, radius, direction, instantShotHits, range, lockObstructionMask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, direction, instantShotHits, range, lockObstructionMask, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = instantShotHits;
        if (count == hits.Length)
        {
            hits = radius > 0f
                ? Physics.SphereCastAll(origin, radius, direction, range, lockObstructionMask, QueryTriggerInteraction.Ignore)
                : Physics.RaycastAll(origin, direction, range, lockObstructionMask, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        float nearest = range;
        Vector3 end = origin + direction * range;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.transform.IsChildOf(transform) || hit.distance >= nearest ||
                hit.collider.GetComponentInParent<MagicProjectile>() != null) continue;
            nearest = hit.distance;
            victim = hit.collider.GetComponentInParent<Player>();
            end = origin + direction * Mathf.Max(0f, hit.distance - 0.02f);
        }
        return end;
    }

    public void RestoreHealth(float amount)
    {
        if (!Object.HasStateAuthority || !IsAlive || amount <= 0f) return;
        NowHp = Mathf.Min(MaxHp, NowHp + amount);
        SyncHealthToPlayerData();
    }

    private void UpdateChannel(MagicStatEntry stats, NetworkId inputTarget, Vector3 aimDirection)
    {
        if (pendingMagic != MagicType.None || IsHitStunned || IsReturningToMap) { StopChannel(); return; }
        int index = (int)stats.magic;
        if (index <= 0 || index >= MagicCooldowns.Length) { StopChannel(); return; }
        if (!IsChanneling && TimerIsActive(MagicCooldowns[index])) return;

        Player target = null;
        Vector3 end;
        if (stats.effect == MagicEffectKind.GuidedChannel)
        {
            if (!Runner.TryFindObject(inputTarget, out NetworkObject obj) || obj == null ||
                (target = obj.GetComponent<Player>()) == null || !IsValidLockTarget(target) ||
                !IsFiniteDirection(aimDirection) || aimDirection.sqrMagnitude < 0.001f ||
                Vector3.Angle(aimDirection, target.LockAimPoint - LockAimPoint) > stats.autoAimHalfAngle)
            { StopChannel(); return; }
            end = target.LockAimPoint;
        }
        else end = TraceMagic(stats, MagicCastPosition, transform.forward, out target);

        float perSecond = stats.ChannelManaPerSecond(MaxAp);
        float paidTime = perSecond > 0f ? Mathf.Min(Runner.DeltaTime, NowAp / perSecond) : Runner.DeltaTime;
        if (paidTime <= 0.000001f) { StopChannel(); channelNeedsRelease = true; return; }
        if (!TryConsumeAp(perSecond * paidTime)) { StopChannel(); channelNeedsRelease = true; return; }
        if (!IsChanneling)
        {
            ChannelMagic = stats.magic;
            LastCastMagic = stats.magic;
            CastSequence++;
            channelDamageTime = 0f;
        }
        // Do not carry damage time from an old victim across a wall or target change.
        NetworkId nextTarget = target != null && target.IsEnemyOf(this) ? target.Object.Id : default;
        if (!ChannelTargetId.Equals(nextTarget)) channelDamageTime = 0f;
        ChannelTargetId = nextTarget;
        ChannelEnd = end;
        channelDamageTime += paidTime;
        bool exhausted = perSecond > 0f && NowAp <= 0.0001f;
        if (channelDamageTime + 0.00001f >= Mathf.Max(0.02f, stats.channelTickSeconds) || exhausted)
        {
            if (target != null && target.IsEnemyOf(this))
            {
                MagicStatEntry tick = stats;
                tick.damage = Mathf.Max(0f, stats.damagePerSecond) * channelDamageTime;
                tick.hitStunSeconds = 0f; // A beam must not stun-lock every damage tick.
                tick.knockbackForce = 0f;
                int parries = target.ParrySequence;
                target.ReceiveMagicHit(tick, Object.InputAuthority, Object.Id);
                if (target.ParrySequence != parries)
                {
                    StopChannel();
                    channelNeedsRelease = true;
                    return;
                }
            }
            channelDamageTime = 0f;
        }
        if (exhausted) { StopChannel(); channelNeedsRelease = true; }
    }

    private void StopChannel()
    {
        if (!Object.HasStateAuthority) return;
        if (ChannelMagic != MagicType.None)
        {
            int index = (int)ChannelMagic;
            if (index > 0 && index < MagicCooldowns.Length)
                MagicCooldowns.Set(index, TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, GetMagicStats(ChannelMagic).cooldownSeconds)));
        }
        ChannelMagic = MagicType.None;
        ChannelTargetId = default;
        channelDamageTime = 0f;
    }

    // Called only by authoritative projectiles/rays; Ice's status cannot pass through a parry.
    public void ReceiveMagicHit(MagicStatEntry stats, PlayerRef attacker, NetworkId attackerId = default)
    {
        if (Object == null || !Object.HasStateAuthority || !IsAlive ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive) return;
        // Binding also counts as an incoming magic hit, despite its zero damage.
        if (IsParrying)
        {
            ParrySequence++;
            return;
        }
        if (stats.effect == MagicEffectKind.Binding)
        {
            BindingTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, stats.effectDuration));
            Speed = 0f;
            KnockbackVelocity = Vector3.zero;
            NotifyDamageDirection(attacker, attackerId);
            NotifyConfirmedMagicHit(attacker, attackerId);
            return;
        }
        int hitBefore = HitSequence;
        TakeDamage(stats.damage, attacker, stats.magic, false, stats.knockbackForce, stats.hitStunSeconds, attackerId);
        if (HitSequence == hitBefore) return;
        NotifyConfirmedMagicHit(attacker, attackerId);
        if (IsAlive && stats.effect == MagicEffectKind.SlowDamage)
            ApplySlow(stats.movementMultiplier, stats.effectDuration);
        if (stats.effect == MagicEffectKind.LifeSteal && stats.healOnHit > 0f)
            FindPlayer(attacker)?.RestoreHealth(stats.healOnHit);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PresentCast(MagicType magic, Vector3 origin, Vector3 end, float value)
    {
        presentation ??= GetComponent<CombatPresentation>();
        presentation?.ShowCast(magic, origin, end);
    }

    private void ApplySlow(float multiplier, float duration)
    {
        if (!Object.HasStateAuthority)
            return;

        SlowMultiplier = Mathf.Clamp(multiplier, 0.05f, 1f);
        SlowTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, duration));
    }

    private void ApplyHitStun(float duration)
    {
        if (!Object.HasStateAuthority || duration <= 0f)
            return;

        HitStunTimer = TickTimer.CreateFromSeconds(Runner, duration);
    }

    public void TakeDamage(
        float damage,
        PlayerRef attacker,
        MagicType sourceMagic = MagicType.None,
        bool canBeParried = true,
        float knockbackForce = 0f,
        float hitStunSeconds = -1f,
        NetworkId attackerId = default)
    {
        if (!Object.HasStateAuthority || !IsAlive || damage <= 0f ||
            (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive))
            return;

        if (canBeParried && attacker != PlayerRef.None && IsParrying)
        {
            ParrySequence++; // Successful block: no reflected damage.
            return;
        }

        LastAttacker = attacker;
        LastReceivedDamage = damage;
        HitSequence++;
        NotifyDamageDirection(attacker, attackerId);
        NowHp = Mathf.Max(NowHp - damage, testBot != null && testBot.Immortal ? 1f : 0f);
        ApplyHitStun(hitStunSeconds >= 0f ? hitStunSeconds : defaultHitStunSeconds);
        ApplyKnockbackFrom(attacker, knockbackForce);
        SyncHealthToPlayerData();

        if (NowHp <= 0f)
            Die();
    }

    private void NotifyDamageDirection(PlayerRef attacker, NetworkId attackerId)
    {
        if (!Object.HasStateAuthority || testBot != null || Object.InputAuthority == PlayerRef.None) return;
        Player source = null;
        if (!attackerId.Equals(default(NetworkId)))
        {
            // NetworkId also identifies bots, which do not have a PlayerRef.
            // Never substitute a respawned character if this exact shooter has disappeared.
            if (Runner.TryFindObject(attackerId, out NetworkObject sourceObject) && sourceObject != null && sourceObject.IsValid)
                source = sourceObject.GetComponent<Player>();
        }
        else if (attacker != PlayerRef.None) source = FindPlayer(attacker);
        if (source == null || source == this || source.Object == null || !source.Object.IsValid || source.TeamIndex == TeamIndex)
            return;
        // Snapshot on the authority at impact time, not at cast time or RPC reception time.
        RPC_ReceivedDamageDirection(source.LockAimPoint);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority, Channel = RpcChannel.Reliable)]
    private void RPC_ReceivedDamageDirection(Vector3 attackerPositionAtHit)
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority) return;
        BattleHud.ShowReceivedDamage(this, attackerPositionAtHit);
    }

    // Runs after an accepted damage/status hit, including lethal hits and immortal dummies.
    // Do not use IsEnemyOf here: it excludes a victim that just died from this hit.
    private void NotifyConfirmedMagicHit(PlayerRef attacker, NetworkId attackerId)
    {
        if (Object == null || !Object.HasStateAuthority) return;
        Player source = null;
        if (!attackerId.Equals(default(NetworkId)))
        {
            if (Runner.TryFindObject(attackerId, out NetworkObject sourceObject) && sourceObject != null && sourceObject.IsValid)
                source = sourceObject.GetComponent<Player>();
        }
        else if (attacker != PlayerRef.None) source = FindPlayer(attacker);
        if (source == null || source == this || source.Object == null || !source.Object.IsValid ||
            !source.Object.HasStateAuthority || source.Object.InputAuthority == PlayerRef.None) return;
        bool enemy = TeamIndex > 0 && source.TeamIndex > 0
            ? TeamIndex != source.TeamIndex : Object.InputAuthority != source.Object.InputAuthority;
        if (!enemy || source.TimerIsActive(source.nextHitMarkerFeedback)) return;
        // Coalesce same-tick AOE / very fast beam hits. No per-frame polling or broadcasts.
        source.nextHitMarkerFeedback = TickTimer.CreateFromSeconds(Runner, 0.05f);
        source.RPC_ConfirmedMagicHit();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority, Channel = RpcChannel.Reliable)]
    private void RPC_ConfirmedMagicHit()
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority) return;
        BattleHud.ShowHitMarker(this);
    }

    private void ApplyKnockbackFrom(PlayerRef attacker, float knockbackForce)
    {
        if (knockbackForce <= 0f)
            return;

        Player attackerPlayer = FindPlayer(attacker);
        if (attackerPlayer == null)
            return;

        Vector3 direction = transform.position - attackerPlayer.transform.position;
        if (direction.sqrMagnitude < 0.001f)
            direction = transform.forward;

        KnockbackVelocity = direction.normalized * knockbackForce;
    }

    private void ApplyKnockback()
    {
        if (KnockbackVelocity.sqrMagnitude <= 0.001f)
            return;

        Vector3 delta = KnockbackVelocity * Runner.DeltaTime;
        if (characterController != null)
            characterController.Move(delta);
        else
            transform.position += delta;

        KnockbackVelocity = Vector3.MoveTowards(
            KnockbackVelocity,
            Vector3.zero,
            knockbackDamping * Runner.DeltaTime
        );
    }

    private Player FindPlayer(PlayerRef playerRef)
    {
        return FindInRunner(Runner, playerRef);
    }

    // Battle spawning registers exactly one current character with SetPlayerObject.
    // Avoid allocating scene-wide arrays, including for area damage and mine ticks.
    public static Player FindInRunner(NetworkRunner runner, PlayerRef playerRef)
    {
        return runner != null && runner.IsRunning &&
            runner.TryGetPlayerObject(playerRef, out NetworkObject playerObject) &&
            playerObject != null && playerObject.IsValid
            ? playerObject.GetComponent<Player>() : null;
    }


    private void InitializeTestBot()
    {
        MaxHp = Mathf.Max(1f, testBot.MaximumHealth);
        NowHp = MaxHp;
        MaxAp = NowAp = 100f;
        TeamIndex = testBot.Team;
        IsAlive = true;
        Magic1 = testBot.AttackMagic;
        Magic2 = MagicType.None;
        CurrentMagicSlot = 1;
        SlowMultiplier = 1f;
        Speed = 0f;
        SpeedStage = 0;
        LoadoutVersion = 1;
    }

    // Called only by the host-driven test bot. No user input/PlayerData or match slot is required.
    public bool FireTestBotMagic(MagicType magic, Player target)
    {
        if (testBot == null || !Object.HasStateAuthority || !IsAlive || IsHitStunned ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive ||
            target == null || target.Runner != Runner || !target.IsTargetableBy(this)) return false;
        MagicStatEntry stats = GetMagicStats(magic);
        if (!stats.requiresTarget || stats.projectileSpeed <= 0f || !magicProjectilePrefab.IsValid ||
            (target.LockAimPoint - LockAimPoint).sqrMagnitude > stats.range * stats.range ||
            !HasLineOfSight(target)) return false;
        Vector3 direction = target.LockAimPoint - LockAimPoint;
        if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction);
        LastCastMagic = magic;
        CastSequence++;
        LaunchMagic(stats, target);
        return true;
    }

    private void Die()
    {
        if (!IsAlive)
            return;

        StopChannel();
        IsAlive = false;
        RespawnTimer = testBot == null && BattleManager.Instance != null
            ? TickTimer.CreateFromSeconds(Runner, BattleManager.Instance.RespawnDelaySeconds)
            : TickTimer.None;
        IsReturningToMap = false;
        IsBoosting = false;
        if (characterController != null)
            characterController.enabled = false;
        Speed = 0f;
        SpeedStage = 0;
        pendingMagic = MagicType.None;
        DeathSequence++;
        ClearLockTargetInternal();
        if (testBot == null) BattleManager.Instance?.PlayerKilled(Object.InputAuthority, LastAttacker);
    }

    private void SyncHealthToPlayerData()
    {
        NotifyHealthChanged(); // Host UI is event-driven too, including training dummies.
        if (testBot != null) return;
        PlayerData data = NetworkGameManager.Instance?.GetPlayerData(Object.InputAuthority);
        data?.SetBattleHealth(MaxHp, NowHp);
    }

    private void SyncApToPlayerData()
    {
        if (!Object.HasStateAuthority || testBot != null) return;
        PlayerData data = NetworkGameManager.Instance?.GetPlayerData(Object.InputAuthority);
        if (data == null) return;
        bool changed = data.BattleCurrentAp != NowAp || data.BattleMaxAp != MaxAp ||
            data.BattleApRecoveryPerSecond != ApRecoveryPerSecond;
        if (!changed) return;
        bool endpoint = NowAp <= 0f || NowAp >= MaxAp || data.BattleMaxAp != MaxAp;
        if (!endpoint && !nextInfoApSync.ExpiredOrNotRunning(Runner)) return;
        nextInfoApSync = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.05f, infoApSyncInterval));
        data.SetBattleAp(MaxAp, NowAp, ApRecoveryPerSecond);
    }

    public bool TryConsumeAp(float amount)
    {
        if (!Object.HasStateAuthority)
            return false;

        amount = Mathf.Max(0f, amount);
        if (NowAp < amount)
            return false;

        NowAp -= amount;
        SyncApToPlayerData();
        return true;
    }

    private bool TryConsumeMovementAp(float amount)
    {
        // Only boost uses predicted spending. Magic costs and damage stay on the host.
        // NowAp is networked, so each rollback starts from authoritative mana again.
        if (!SimulatesMovement) return false;
        amount = Mathf.Max(0f, amount);
        if (NowAp < amount) return false;
        NowAp -= amount;
        return true;
    }

    public void RestoreAp(float amount)
    {
        if (!Object.HasStateAuthority || amount <= 0f)
            return;

        NowAp = Mathf.Min(MaxAp, NowAp + amount);
        SyncApToPlayerData();
    }

    private void RegenerateAp()
    {
        if (IsChanneling || (SelectedMagicStats.IsChanneled && previousButtons.IsSet(PlayerInputButton.Lock)) ||
            MaxAp <= 0f || NowAp >= MaxAp || ApRecoveryPerSecond <= 0f)
            return;

        NowAp = Mathf.Min(MaxAp, NowAp + ApRecoveryPerSecond * Runner.DeltaTime);
    }

    public void ReportBattleSceneReady()
    {
        if (Object != null && Object.IsValid && Object.HasInputAuthority && IsPresentationReady)
            RPC_ReportBattleSceneReady();
    }

    public void RequestBattleStart()
    {
        if (Object != null && Object.IsValid && Object.HasInputAuthority)
            RPC_RequestBattleStart();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ReportBattleSceneReady()
    {
        BattleFlag.Instance?.MarkPlayerReady(Object.InputAuthority);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestBattleStart()
    {
        BattleFlag.Instance?.RequestStart();
    }

    public void SetLockTarget(NetworkObject target)
    {
        enemyLockOn targeting = GetComponent<enemyLockOn>();
        if (Object.HasInputAuthority && target != null && targeting != null &&
            targeting.CanLockTarget(target.GetComponent<Player>()) &&
            targeting.TryGetAlignedAim(out Vector3 direction))
            RPC_SetLockTarget(target.Id, direction);
    }

    public void ClearLockTarget()
    {
        if (Object.HasInputAuthority)
            RPC_ClearLockTarget();
    }

    public bool HasLockTarget(NetworkObject target)
    {
        return target != null && LockTargetId.Equals(target.Id);
    }

    // HUD lookup uses Fusion's registry, not a scene-wide allocating search.
    public Player GetDisplayedLockTarget()
    {
        if (!CanAcquireMagicTarget() ||
            !Runner.TryFindObject(LockTargetId, out NetworkObject target) || target == null)
            return null;
        return target.GetComponent<Player>();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetLockTarget(NetworkId targetId, Vector3 desiredDirection)
    {
        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive ||
            !CanAcquireMagicTarget() || !SelectedMagicStats.requiresTarget || !IsFiniteDirection(desiredDirection))
        {
            ClearLockTargetInternal();
            return;
        }
        if (!Runner.TryFindObject(targetId, out NetworkObject targetObject) || targetObject == null)
        {
            ClearLockTargetInternal();
            return;
        }

        Player target = targetObject.GetComponent<Player>();
        if (!IsValidLockTarget(target))
        {
            ClearLockTargetInternal();
            return;
        }

        if (!LockTargetId.Equals(targetId))
        {
            LockTargetId = targetId;
            LockProgress = 0f;
            IsFullyLocked = false;
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ClearLockTarget()
    {
        ClearLockTargetInternal();
    }

    private bool TryGetValidLockTarget(out Player target)
    {
        target = null;
        if (!Runner.TryFindObject(LockTargetId, out NetworkObject targetObject) || targetObject == null)
            return false;

        target = targetObject.GetComponent<Player>();
        if (!IsValidLockTarget(target))
        {
            target = null;
            return false;
        }

        return true;
    }

    private bool IsValidLockTarget(Player target)
    {
        if (target == null || !target.IsTargetableBy(this) ||
            Vector3.Dot(transform.forward, target.LockAimPoint - LockAimPoint) < 0f)
            return false;

        float range = SelectedMagicStats.range > 0f
            ? Mathf.Min(maxLockDistance, SelectedMagicStats.range) : maxLockDistance;
        if (Vector3.SqrMagnitude(target.transform.position - transform.position) > range * range)
            return false;

        return HasLineOfSight(target);
    }

    private bool HasLineOfSight(Player target)
    {
        Vector3 origin = MagicCastPosition;
        Vector3 destination = target.LockAimPoint;
        Vector3 direction = destination - origin;
        float distance = direction.magnitude;
        if (distance <= 0.01f)
            return true;

        int count = Physics.RaycastNonAlloc(origin, direction / distance, lockHits,
            distance, lockObstructionMask, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = lockHits;
        // A full buffer may omit the nearest wall: preserve correct occlusion in
        // unusually dense geometry, rather than allowing a shot through a wall.
        if (count == lockHits.Length)
        {
            hits = Physics.RaycastAll(origin, direction / distance, distance,
                lockObstructionMask, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }

        float nearestDistance = float.PositiveInfinity;
        Player nearestPlayer = null;
        bool nearestIsObstacle = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                continue;

            if (hit.distance >= nearestDistance)
                continue;

            nearestDistance = hit.distance;
            nearestPlayer = hit.collider.GetComponentInParent<Player>();
            nearestIsObstacle = nearestPlayer == null;
        }

        return !nearestIsObstacle && (nearestPlayer == null || nearestPlayer == target);
    }

    private void ClearLockTargetInternal()
    {
        LockTargetId = default;
        LockProgress = 0f;
        IsFullyLocked = false;
    }

    public void InterruptLock()
    {
        if (!Object.HasStateAuthority)
            return;

        ClearLockTargetInternal();
    }

    private void ClearAllLocksTargetingMe()
    {
        foreach (Player candidate in combatants)
        {
            if (candidate == null || candidate.Runner != Runner) continue;
            if (candidate == null || candidate == this || !candidate.Object.HasStateAuthority)
                continue;

            if (candidate.LockTargetId.Equals(Object.Id))
                candidate.ClearLockTargetInternal();
        }
    }

    public bool IsEnemyOf(Player other)
    {
        if (other == null || other == this || !IsAlive || !other.IsAlive)
            return false;

        if (TeamIndex > 0 && other.TeamIndex > 0)
            return TeamIndex != other.TeamIndex;

        return Object != null && other.Object != null &&
               Object.InputAuthority != other.Object.InputAuthority;
    }

    public bool IsTargetableBy(Player requester)
    {
        return requester != null && requester != this && IsAlive &&
               !IsStealthed && IsEnemyOf(requester);
    }


    private void PlayerTurn(NetworkInputData data, NetworkButtons buttons)
    {
        if (data.steerToAim && IsFiniteDirection(data.aimDirection) && data.aimDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 forward = data.aimDirection.normalized;
            Vector3 up = IsFiniteDirection(data.aimUp) ? data.aimUp : transform.up;
            up = Vector3.ProjectOnPlane(up, forward);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(transform.up, forward);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(transform.right, forward);
            Quaternion desired = Quaternion.LookRotation(forward, up.normalized);
            float angle = Quaternion.Angle(transform.rotation, desired);
            float targetRate = angle > 0.01f ? SteeringTurnRate : 0f;
            float response = targetRate > currentAimTurnSpeed ? turnAccel : returnSpeed;
            currentAimTurnSpeed = Mathf.Lerp(currentAimTurnSpeed, targetRate,
                1f - Mathf.Exp(-Mathf.Max(0.01f, response) * Runner.DeltaTime));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desired,
                Mathf.Max(0f, currentAimTurnSpeed) * Runner.DeltaTime);
            // Only needed if the user switches back to legacy control mode.
            currentPitch = NormalizePitch(transform.eulerAngles.x);
            currentYaw = transform.eulerAngles.y;
            currentTurnSpeed = 0f;
            return;
        }
        currentAimTurnSpeed = 0f;
        currentPitch -= data.look.y * mousePitchSensitivity * GetTurnMultiplier();
        currentPitch = Mathf.Clamp(currentPitch, -maxPitch, maxPitch);

        float effectiveTurnSpeed = turnSpeed * GetTurnMultiplier();
        if (buttons.IsSet(PlayerInputButton.TurnLeft))
            currentTurnSpeed = Mathf.Lerp(currentTurnSpeed, -effectiveTurnSpeed, turnAccel * Runner.DeltaTime);
        else if (buttons.IsSet(PlayerInputButton.TurnRight))
            currentTurnSpeed = Mathf.Lerp(currentTurnSpeed, effectiveTurnSpeed, turnAccel * Runner.DeltaTime);
        else
            currentTurnSpeed = Mathf.Lerp(currentTurnSpeed, 0f, returnSpeed * Runner.DeltaTime);

        currentYaw += currentTurnSpeed * Runner.DeltaTime;
        transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
    }

    private static bool IsFiniteDirection(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z) && value.sqrMagnitude <= 4f;
    }

    private static float NormalizePitch(float eulerPitch)
    {
        return eulerPitch > 180f ? eulerPitch - 360f : eulerPitch;
    }

    private void GoForward()
    {
        Vector3 movement = transform.forward * Speed * Runner.DeltaTime;
        if (characterController != null)
            characterController.Move(movement);
        else
            transform.position += movement;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!Object || !Object.HasStateAuthority || !IsAlive || Mathf.Abs(Speed) < wallDamageSpeedThreshold)
            return;

        if (Mathf.Abs(hit.normal.y) > 0.5f || Time.time < lastWallDamageTime + wallDamageCooldown)
            return;

        lastWallDamageTime = Time.time;
        TakeDamage(wallDamage, PlayerRef.None);
    }

    public override void Render()
    {
        if (!IsAlive && DeathSequence > 0 && characterController != null)
            characterController.enabled = false;
        ApplyReplicatedLoadout();
        ApplyEquipmentSlot();
        ApplyCombatAnimation();
    }

    private void Update()
    {
        // Remove only last frame's cosmetic offset before Animator evaluates the new pose.
        // This also prevents drift if animation is disabled or culled.
        headLook.RestorePose();
    }

    private void LateUpdate()
    {
        bool canLook = Object != null && Object.IsValid && IsAlive && !IsTestBot &&
            BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive &&
            !IsReturningToMap && !IsHitStunned;
        Vector2 angles = canLook ? HeadLookAngles : Vector2.zero;
        if (canLook && Object.HasInputAuthority)
        {
            // Local head responds at display rate, independently of tick / network latency.
            angles = !CombatPresentation.MenuOpen && localCameraFollow != null &&
                !localCameraFollow.IsBoundaryPresentationActive &&
                localCameraFollow.TryGetSteeringInput(out Vector3 aim, out _)
                ? headLook.GetAngles(transform, aim) : Vector2.zero;
        }
        headLook.Apply(transform, animator, angles, canLook, Time.deltaTime);
    }

    private void OnDisable() => headLook.Reset();

    private void ApplyReplicatedLoadout()
    {
        if (LoadoutVersion <= 0 || renderedLoadoutVersion == LoadoutVersion)
            return;

        if (testBot != null) { renderedLoadoutVersion = LoadoutVersion; return; }
        equipment ??= GetComponent<PlayerEquipment>();
        if (equipment == null)
        {
            Debug.LogError("ChPrefab has no PlayerEquipment.", this);
            return;
        }

        equipment.ApplyLoadout(Hat, Broom, Magic1, Magic2);

        appearance ??= GetComponent<PlayerAppearance>();
        appearance?.ApplyPlayerConfig(AppearanceConfig);
        renderedLoadoutVersion = LoadoutVersion;
    }

    private void ApplyEquipmentSlot()
    {
        if (lastMagicSlot == CurrentMagicSlot)
            return;

        lastMagicSlot = CurrentMagicSlot;
        equipment ??= GetComponent<PlayerEquipment>();
        equipment?.ChangeMagic(CurrentMagicSlot);
    }

    private void ApplyCombatAnimation()
    {
        if (!hasRenderedCombatState)
        {
            renderedHitSequence = HitSequence;
            renderedDeathSequence = DeathSequence;
            hasRenderedCombatState = true;
            return;
        }

        if (renderedHitSequence != HitSequence)
        {
            renderedHitSequence = HitSequence;
            PlayAnimatorState(hitReactionState);
        }

        if (renderedDeathSequence != DeathSequence)
        {
            renderedDeathSequence = DeathSequence;
            PlayAnimatorState(deathAnimationState);
        }
    }

    private void PlayAnimatorState(string stateName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(stateName))
            return;

        int stateHash = Animator.StringToHash(stateName);
        if (animator.HasState(0, stateHash))
            animator.CrossFadeInFixedTime(stateHash, 0.05f);
    }

    private bool TimerIsActive(TickTimer timer)
    {
        return Runner != null && timer.IsRunning && !timer.Expired(Runner);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        headLook.Reset();
        combatants.Remove(this);
        if (LocalPlayer == this)
            LocalPlayer = null;
    }

    private void OnDestroy()
    {
        combatants.Remove(this);
        if (LocalPlayer == this) LocalPlayer = null;
    }
}
