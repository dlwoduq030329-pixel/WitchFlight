using Fusion;
using UnityEngine;

public class Player : NetworkBehaviour, IAfterRender
{
    [Header("Movement")]
    [SerializeField] private float maxSpeed = 60f;
    [SerializeField, Min(0.01f)] private float mousePitchSensitivity = 2.5f;
    [SerializeField] private float maxPitch = 60f;
    [SerializeField] private float wallDamageSpeedThreshold = 30f;
    [SerializeField] private float wallDamage = 20f;
    [SerializeField] private float wallDamageCooldown = 0.5f;
    [SerializeField] private float collisionRadius = 0.22f;
    [SerializeField] private float collisionHeight = 1.2f;
    [SerializeField] private float baseAcceleration = 60f;
    [SerializeField] private EquipmentStatTable equipmentStatTable;

    [Header("Lock on")]
    [SerializeField, Min(1f)] private float maxLockDistance = 250f;
    [SerializeField, Min(0f)] private float lockAimHeight = 0.8f;
    [SerializeField] private LayerMask lockObstructionMask = ~0;

    [Header("Combat")]
    [SerializeField] private MagicStatTable magicStatTable;
    [SerializeField] private NetworkPrefabRef magicProjectilePrefab;
    [SerializeField, Min(0.01f)] private float parryWindowSeconds = 0.3f;
    [SerializeField, Min(0f)] private float parryCooldownSeconds = 0.6f;
    [SerializeField, Min(0f)] private float defaultHitStunSeconds = 0.25f;
    [SerializeField, Min(0.01f)] private float knockbackDamping = 30f;
    [SerializeField] private string hitReactionState = "AS_Broom_SitFly_Arm_Hit_React";
    [SerializeField] private string deathAnimationState = "AS_Broom_SitFly_Arm_Knock1_Start";

    [Networked] private float Speed { get; set; }
    [Networked] private int SpeedStage { get; set; }
    [Networked] private float acceleration { get; set; }
    [Networked] private float turnSpeed { get; set; }
    [Networked] private PlayerRef LastAttacker { get; set; }
    [Networked] private HatType Hat { get; set; }
    [Networked] private BroomType Broom { get; set; }
    [Networked] private MagicType Magic1 { get; set; }
    [Networked] private MagicType Magic2 { get; set; }
    [Networked] private int HairLength { get; set; }
    [Networked] private int LoadoutVersion { get; set; }
    [Networked] private NetworkId LockTargetId { get; set; }
    [Networked] private TickTimer MagicCooldown { get; set; }
    [Networked] private TickTimer ParryTimer { get; set; }
    [Networked] private TickTimer ParryCooldown { get; set; }
    [Networked] private TickTimer BoostTimer { get; set; }
    [Networked] private TickTimer BoostCooldown { get; set; }
    [Networked] private TickTimer HitStunTimer { get; set; }
    [Networked] private TickTimer SlowTimer { get; set; }
    [Networked] private TickTimer WindTimer { get; set; }
    [Networked] private TickTimer StealthTimer { get; set; }
    [Networked] private TickTimer RevealTimer { get; set; }
    [Networked] private float SlowMultiplier { get; set; }
    [Networked] private float WindMultiplier { get; set; }
    [Networked] private Vector3 KnockbackVelocity { get; set; }

    [Networked] public float MaxHp { get; set; }
    [Networked] public float NowHp { get; set; }
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
    private CameraFollow localCameraFollow;
    private bool controllerNeedsRenderReset;
    private PlayerAppearance appearance;
    private Animator animator;
    private Renderer[] visualRenderers;
    private bool[] defaultRendererEnabled;
    private NetworkButtons previousButtons;
    private int lastMagicSlot = -1;
    private int renderedLoadoutVersion = -1;
    private int renderedHitSequence;
    private int renderedDeathSequence;
    private bool hasRenderedCombatState;
    private bool lastVisualHidden;
    private float currentPitch;
    private float currentTurnSpeed;
    private float lockOnTurnSpeed = 360f;
    private float turnAccel = 18f;
    private float returnSpeed = 30f;
    private float stageTransitionSpeed = 60f;
    private float brakeSpeed = 80f;
    private float boostMultiplier = 1.35f;
    private float boostDuration = 0.45f;
    private float boostCooldown = 1.5f;
    private float lastWallDamageTime = float.NegativeInfinity;
    private MagicType pendingMagic;
    private Vector3 pendingAimDirection;
    private NetworkId pendingTargetId;
    private TickTimer castTimer;
    private Vector3 inputAimDirection;
    private CombatPresentation presentation;

    public static Player LocalPlayer { get; private set; }
    public bool IsPresentationReady => Object != null && Object.IsValid && LoadoutVersion > 0 &&
        renderedLoadoutVersion == LoadoutVersion && TeamIndex > 0 && IsAlive;
    public int CurrentSpeedStage => SpeedStage;
    public float CurrentSpeed => Speed;
    public MagicStatEntry SelectedMagicStats => GetMagicStats(GetSelectedMagic());
    public float SelectedMagicApCost => CurrentMagicSlot == 3
        ? (magicStatTable != null ? magicStatTable.parryApCost : 8f) : SelectedMagicStats.apCost;

    public bool IsParrying => TimerIsActive(ParryTimer);
    public bool IsHitStunned => TimerIsActive(HitStunTimer);
    public bool IsStealthed => TimerIsActive(StealthTimer) && !TimerIsActive(RevealTimer);
    public bool HasActiveMine => Object != null && MagicProjectile.HasMineOwnedBy(Object.InputAuthority);
    public float MaxLockDistance => maxLockDistance;
    public Vector3 LockAimPoint => transform.position + Vector3.up * lockAimHeight;

    private void Awake()
    {
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
        visualRenderers = GetComponentsInChildren<Renderer>(true);
        defaultRendererEnabled = System.Array.ConvertAll(visualRenderers, renderer => renderer.enabled);
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
        maxSpeed = Mathf.Max(1f, broomStats.maxSpeed);
        turnSpeed = Mathf.Max(0f, broomStats.turnSpeed);
        lockOnTurnSpeed = Mathf.Max(0f, broomStats.lockOnTurnSpeed);
        turnAccel = Mathf.Max(0.01f, broomStats.turnAcceleration);
        returnSpeed = Mathf.Max(0.01f, broomStats.turnReturnSpeed);
        stageTransitionSpeed = Mathf.Max(0.01f, broomStats.speedStageTransitionSpeed);
        brakeSpeed = Mathf.Max(0.01f, broomStats.brakeSpeed);
        boostMultiplier = Mathf.Max(1f, broomStats.boostMultiplier);
        boostDuration = Mathf.Max(0.01f, broomStats.boostDuration);
        boostCooldown = Mathf.Max(0f, broomStats.boostCooldown);

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
        MagicCooldown = TickTimer.None;
        ParryTimer = TickTimer.None;
        ParryCooldown = TickTimer.None;
        BoostTimer = TickTimer.None;
        BoostCooldown = TickTimer.None;
        HitStunTimer = TickTimer.None;
        SlowTimer = TickTimer.None;
        WindTimer = TickTimer.None;
        StealthTimer = TickTimer.None;
        RevealTimer = TickTimer.None;
        SlowMultiplier = 1f;
        WindMultiplier = 1f;
        KnockbackVelocity = default;
        currentPitch = NormalizePitch(transform.eulerAngles.x);
        currentTurnSpeed = 0f;
        pendingMagic = MagicType.None;
        castTimer = TickTimer.None;

        Hat = data.hat;
        Broom = data.broom;
        Magic1 = data.magic1;
        Magic2 = data.magic2;
        HairLength = data.hairLength;
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
            apRecoveryPerSecond = 10f
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
            maxSpeed = 60f,
            turnSpeed = 180f,
            turnAcceleration = 18f,
            turnReturnSpeed = 30f,
            lockOnTurnSpeed = 360f,
            speedStageTransitionSpeed = 60f,
            brakeSpeed = 80f,
            boostMultiplier = 1.35f,
            boostDuration = 0.45f,
            boostCooldown = 1.5f
        };
    }

    private MagicStatEntry GetMagicStats(MagicType magic)
    {
        if (magicStatTable != null)
            return magicStatTable.GetStats(magic);

        Debug.LogError("ChPrefab has no MagicStatTable. The selected spell cannot be cast.", this);
        return default;
    }

    public override void Spawned()
    {
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
        if (!Object.HasStateAuthority)
            return;

        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
        {
            Speed = 0f;
            SpeedStage = 0;
            currentTurnSpeed = 0f;
            previousButtons = default;
            pendingMagic = MagicType.None;
            pendingAimDirection = Vector3.zero;
            pendingTargetId = default;
            castTimer = TickTimer.None;
            KnockbackVelocity = Vector3.zero;
            ClearLockTargetInternal();
            return;
        }
        if (!IsAlive)
            return;

        ResetControllerAfterRender();
        UpdatePendingCast();
        RegenerateAp();

        if (!GetInput(out NetworkInputData data))
        {
            ContinueWithoutActions();
            return;
        }

        ProcessInput(data);
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
        // Do this once per rendered frame, not once per catch-up simulation tick.
        Vector3 simulationPosition = transform.position;
        Quaternion simulationRotation = transform.rotation;
        characterController.enabled = false;
        transform.SetPositionAndRotation(simulationPosition, simulationRotation);
        characterController.enabled = true;
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
        inputAimDirection = data.aimDirection.sqrMagnitude > 0.5f &&
            Vector3.Dot(data.aimDirection.normalized, transform.forward) > 0.5f
            ? data.aimDirection.normalized : transform.forward;

        if (data.suppressActions)
        {
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

        if (buttons.WasPressed(previousButtons, PlayerInputButton.Boost))
            TryStartBoost();

        if (buttons.WasPressed(previousButtons, PlayerInputButton.Parry))
            TryStartParry();

        bool lockHeld = buttons.IsSet(PlayerInputButton.Lock);
        if (lockHeld || previousButtons.IsSet(PlayerInputButton.Lock))
            SetInputLockTarget(data.lockTarget);
        if (lockHeld)
        {
            UpdateLockCharge();
        }
        else if (previousButtons.IsSet(PlayerInputButton.Lock))
        {
            TryCastSelectedMagic();
            ClearLockTargetInternal();
        }

        if (IsHitStunned)
        {
            Speed = Mathf.MoveTowards(Speed, 0f, brakeSpeed * Runner.DeltaTime);
        }
        else
        {
            UpdateSpeed();

            PlayerTurn(data, buttons);

            GoForward();
        }

        ApplyKnockback();
        previousButtons = buttons;
    }

    private void ContinueWithoutActions()
    {
        if (IsHitStunned)
            Speed = Mathf.MoveTowards(Speed, 0f, brakeSpeed * Runner.DeltaTime);
        else
        {
            UpdateSpeed();
            GoForward();
        }
        ApplyKnockback();
    }

    private void SelectMagicSlot(int slot)
    {
        if (CurrentMagicSlot == slot)
            return;

        CurrentMagicSlot = slot;
        ClearLockTargetInternal();
    }

    private void UpdateSpeed()
    {
        float targetSpeed = maxSpeed * SpeedStage / 3f;
        targetSpeed *= GetMovementMultiplier();

        if (TimerIsActive(BoostTimer))
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
        float result = 1f;
        if (TimerIsActive(WindTimer))
            result *= Mathf.Max(1f, WindMultiplier);
        return result;
    }

    private float GetTurnMultiplier()
    {
        return GetMovementMultiplier() *
            (TimerIsActive(SlowTimer) ? Mathf.Clamp(SlowMultiplier, 0.05f, 1f) : 1f);
    }

    private void TryStartBoost()
    {
        if (TimerIsActive(BoostCooldown) || IsHitStunned)
            return;

        if (SpeedStage == 0)
            SpeedStage = 1;

        BoostTimer = TickTimer.CreateFromSeconds(Runner, boostDuration);
        BoostCooldown = TickTimer.CreateFromSeconds(Runner, boostCooldown);
    }

    private void TryStartParry()
    {
        if (TimerIsActive(ParryCooldown) || IsHitStunned)
            return;

        float cost = magicStatTable != null ? magicStatTable.parryApCost : 8f;
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
        if (!stats.requiresTarget || stats.magic == MagicType.None)
            return;

        if (!TryGetValidLockTarget(out _))
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
        if (!SelectedMagicStats.requiresTarget ||
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
        if (TimerIsActive(MagicCooldown) || pendingMagic != MagicType.None)
            return;

        MagicType selectedMagic = GetSelectedMagic();
        if (selectedMagic == MagicType.None)
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

        if (stats.effect == MagicEffectKind.Stealth &&
            stats.minimumEnemyDistance > 0f &&
            HasEnemyWithin(stats.minimumEnemyDistance))
            return;

        if (!TryConsumeAp(stats.apCost))
            return;

        MagicCooldown = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, stats.cooldownSeconds));
        LastCastMagic = selectedMagic;
        CastSequence++;
        if (stats.castSeconds > 0f)
        {
            pendingMagic = selectedMagic;
            pendingAimDirection = inputAimDirection;
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
        return CurrentMagicSlot switch
        {
            1 => Magic1,
            2 => Magic2,
            _ => MagicType.None
        };
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
        inputAimDirection = pendingAimDirection;
        Player target = Runner.TryFindObject(pendingTargetId, out NetworkObject obj) && obj != null
            ? obj.GetComponent<Player>() : null;
        if (stats.requiresTarget && !IsValidLockTarget(target))
            return;
        CastMagic(stats, target);
    }

    private void CastMagic(MagicStatEntry stats, Player target)
    {
        switch (stats.effect)
        {
            case MagicEffectKind.DirectDamage:
            case MagicEffectKind.SlowDamage:
            case MagicEffectKind.AreaDamage:
                LaunchMagic(stats, target);
                break;

            case MagicEffectKind.Interrupt:
                ClearAllLocksTargetingMe();
                MagicProjectile.BreakTracking(this);
                RPC_PresentCast(stats.magic, LockAimPoint, LockAimPoint, 0f);
                break;

            case MagicEffectKind.Stealth:
                ActivateStealth(stats.effectDuration);
                break;

            case MagicEffectKind.MovementBuff:
                WindMultiplier = Mathf.Max(1f, stats.movementMultiplier);
                WindTimer = TickTimer.CreateFromSeconds(Runner, stats.effectDuration);
                RPC_PresentCast(stats.magic, LockAimPoint, LockAimPoint, stats.effectDuration);
                break;

            case MagicEffectKind.Decoy:
                ActivateStealth(stats.activationDelay);
                RPC_PresentCast(stats.magic, LockAimPoint, LockAimPoint, stats.effectDuration);
                break;

            case MagicEffectKind.Mine:
                LaunchMagic(stats, null);
                break;

            case MagicEffectKind.Scan:
                RevealEnemies(stats);
                MagicProjectile.RevealMines(this, stats.radius, stats.effectDuration);
                RPC_PresentCast(stats.magic, LockAimPoint, LockAimPoint, stats.radius);
                break;
        }
    }

    private void LaunchMagic(MagicStatEntry stats, Player target)
    {
        Vector3 direction = inputAimDirection.sqrMagnitude > 0.5f
            ? inputAimDirection.normalized : transform.forward;
        Vector3 origin = LockAimPoint;
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
                shot.GetComponent<MagicProjectile>().Initialize(this, target, stats, direction);
            return;
        }

        // Vision is an aimed instant shot, not an invisible target-only damage call.
        float range = Mathf.Max(0.1f, stats.range);
        Vector3 end = origin + direction * range;
        RaycastHit[] hits = Physics.SphereCastAll(origin, Mathf.Max(0.01f, stats.projectileRadius),
            direction, range, lockObstructionMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.transform.IsChildOf(transform))
                continue;
            Player victim = hit.collider.GetComponentInParent<Player>();
            end = hit.point;
            if (stats.effect != MagicEffectKind.AreaDamage &&
                victim != null && victim.IsAlive && victim.TeamIndex != TeamIndex)
                victim.ReceiveMagicHit(stats, Object.InputAuthority);
            if (stats.effect == MagicEffectKind.AreaDamage)
                end = origin + direction * Mathf.Max(0f, hit.distance - 0.02f);
            break;
        }
        if (stats.effect == MagicEffectKind.AreaDamage)
        {
            foreach (Player victim in FindObjectsByType<Player>(FindObjectsSortMode.None))
            {
                if (victim.IsAlive && victim.TeamIndex != TeamIndex &&
                    (victim.LockAimPoint - end).sqrMagnitude <= stats.radius * stats.radius &&
                    MagicProjectile.HasBlastSight(end, victim))
                    victim.ReceiveMagicHit(stats, Object.InputAuthority);
            }
        }
        RPC_PresentCast(stats.magic, origin, end, 0f);
    }

    // Called only by authoritative projectiles/rays; Ice's status cannot pass through a parry.
    public void ReceiveMagicHit(MagicStatEntry stats, PlayerRef attacker)
    {
        if (Object == null || !Object.HasStateAuthority || !IsAlive)
            return;
        bool parried = IsParrying && attacker != PlayerRef.None;
        int hitBefore = HitSequence;
        TakeDamage(stats.damage, attacker, stats.magic, true, stats.knockbackForce, stats.hitStunSeconds);
        if (!parried && HitSequence != hitBefore && IsAlive && stats.effect == MagicEffectKind.SlowDamage)
            ApplySlow(stats.movementMultiplier, stats.effectDuration);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PresentCast(MagicType magic, Vector3 origin, Vector3 end, float value)
    {
        presentation ??= GetComponent<CombatPresentation>();
        if (magic == MagicType.Decoy)
            presentation?.ShowDecoy(value);
        else if (magic == MagicType.Scane)
            presentation?.ShowScan(value);
        else
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

    private void ActivateStealth(float duration)
    {
        if (!Object.HasStateAuthority)
            return;

        StealthTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, duration));
        RevealTimer = TickTimer.None;
        ClearAllLocksTargetingMe();
        MagicProjectile.BreakTracking(this);
        RPC_PresentCast(MagicType.Smoke, LockAimPoint, LockAimPoint, duration);
    }




    private void RevealEnemies(MagicStatEntry stats)
    {
        float radius = Mathf.Max(stats.radius, stats.range);
        foreach (Player candidate in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (!candidate.IsEnemyOf(this))
                continue;

            if (Vector3.SqrMagnitude(candidate.transform.position - transform.position) > radius * radius)
                continue;

            candidate.RevealFor(stats.effectDuration);
        }
    }

    private void RevealFor(float duration)
    {
        if (!Object.HasStateAuthority)
            return;

        StealthTimer = TickTimer.None;
        RevealTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, duration));
    }

    private bool HasEnemyWithin(float distance)
    {
        float distanceSqr = distance * distance;
        foreach (Player candidate in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (!candidate.IsEnemyOf(this))
                continue;

            if (Vector3.SqrMagnitude(candidate.transform.position - transform.position) <= distanceSqr)
                return true;
        }

        return false;
    }

    public void TakeDamage(
        float damage,
        PlayerRef attacker,
        MagicType sourceMagic = MagicType.None,
        bool canBeParried = true,
        float knockbackForce = 0f,
        float hitStunSeconds = -1f)
    {
        if (!Object.HasStateAuthority || !IsAlive || damage <= 0f ||
            (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive))
            return;

        if (canBeParried && attacker != PlayerRef.None && IsParrying)
        {
            ReflectDamage(attacker, damage, sourceMagic, knockbackForce, hitStunSeconds);
            return;
        }

        LastAttacker = attacker;
        LastReceivedDamage = damage;
        HitSequence++;
        NowHp = Mathf.Max(NowHp - damage, 0f);
        ApplyHitStun(hitStunSeconds >= 0f ? hitStunSeconds : defaultHitStunSeconds);
        ApplyKnockbackFrom(attacker, knockbackForce);
        SyncHealthToPlayerData();

        if (NowHp <= 0f)
            Die();
    }

    private void ReflectDamage(
        PlayerRef attacker,
        float damage,
        MagicType sourceMagic,
        float knockbackForce,
        float hitStunSeconds)
    {
        ParrySequence++;

        // Friendly-fire mines may have been cast by this defender; parry still means zero damage.
        if (attacker == Object.InputAuthority)
            return;

        Player attackerPlayer = FindPlayer(attacker);
        if (attackerPlayer != null)
        {
            attackerPlayer.TakeDamage(
                damage,
                Object.InputAuthority,
                sourceMagic,
                false,
                knockbackForce,
                hitStunSeconds
            );
        }
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
        NetworkObject playerObject = NetworkGameManager.Instance?.GetPlayerObject(playerRef);
        if (playerObject != null)
            return playerObject.GetComponent<Player>();

        foreach (Player candidate in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (candidate.Object != null && candidate.Object.InputAuthority == playerRef)
                return candidate;
        }

        return null;
    }

    private void Die()
    {
        if (!IsAlive)
            return;

        IsAlive = false;
        if (characterController != null)
            characterController.enabled = false;
        Speed = 0f;
        SpeedStage = 0;
        pendingMagic = MagicType.None;
        DeathSequence++;
        ClearLockTargetInternal();
        BattleManager.Instance?.PlayerKilled(Object.InputAuthority, LastAttacker);
    }

    private void SyncHealthToPlayerData()
    {
        PlayerData data = NetworkGameManager.Instance?.GetPlayerData(Object.InputAuthority);
        data?.SetBattleHealth(MaxHp, NowHp);
    }

    private void SyncApToPlayerData()
    {
        PlayerData data = NetworkGameManager.Instance?.GetPlayerData(Object.InputAuthority);
        data?.SetBattleAp(MaxAp, NowAp, ApRecoveryPerSecond);
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

    public void RestoreAp(float amount)
    {
        if (!Object.HasStateAuthority || amount <= 0f)
            return;

        NowAp = Mathf.Min(MaxAp, NowAp + amount);
        SyncApToPlayerData();
    }

    private void RegenerateAp()
    {
        if (MaxAp <= 0f || NowAp >= MaxAp || ApRecoveryPerSecond <= 0f)
            return;

        NowAp = Mathf.Min(MaxAp, NowAp + ApRecoveryPerSecond * Runner.DeltaTime);
        SyncApToPlayerData();
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
        if (Object.HasInputAuthority && target != null)
            RPC_SetLockTarget(target.Id);
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

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetLockTarget(NetworkId targetId)
    {
        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
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
        if (target == null || !target.IsTargetableBy(this))
            return false;

        float range = SelectedMagicStats.range > 0f
            ? Mathf.Min(maxLockDistance, SelectedMagicStats.range) : maxLockDistance;
        if (Vector3.SqrMagnitude(target.transform.position - transform.position) > range * range)
            return false;

        return HasLineOfSight(target);
    }

    private bool HasLineOfSight(Player target)
    {
        Vector3 origin = LockAimPoint;
        Vector3 destination = target.LockAimPoint;
        Vector3 direction = destination - origin;
        float distance = direction.magnitude;
        if (distance <= 0.01f)
            return true;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction / distance,
            distance,
            lockObstructionMask,
            QueryTriggerInteraction.Ignore
        );

        float nearestDistance = float.PositiveInfinity;
        Player nearestPlayer = null;
        bool nearestIsObstacle = false;
        foreach (RaycastHit hit in hits)
        {
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
        foreach (Player candidate in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (candidate == this || candidate.Object == null || !candidate.Object.HasStateAuthority)
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
        currentPitch -= data.look.y * mousePitchSensitivity * GetTurnMultiplier();
        currentPitch = Mathf.Clamp(currentPitch, -maxPitch, maxPitch);

        float effectiveTurnSpeed = turnSpeed * GetTurnMultiplier();
        if (buttons.IsSet(PlayerInputButton.TurnLeft))
            currentTurnSpeed = Mathf.Lerp(currentTurnSpeed, -effectiveTurnSpeed, turnAccel * Runner.DeltaTime);
        else if (buttons.IsSet(PlayerInputButton.TurnRight))
            currentTurnSpeed = Mathf.Lerp(currentTurnSpeed, effectiveTurnSpeed, turnAccel * Runner.DeltaTime);
        else
            currentTurnSpeed = Mathf.Lerp(currentTurnSpeed, 0f, returnSpeed * Runner.DeltaTime);

        float yaw = transform.rotation.eulerAngles.y + currentTurnSpeed * Runner.DeltaTime;
        transform.rotation = Quaternion.Euler(currentPitch, yaw, 0f);
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
        ApplyStealthVisibility();
        ApplyCombatAnimation();
    }

    private void ApplyReplicatedLoadout()
    {
        if (renderedLoadoutVersion == LoadoutVersion)
            return;

        equipment ??= GetComponent<PlayerEquipment>();
        if (equipment == null)
        {
            Debug.LogError("ChPrefab has no PlayerEquipment.", this);
            return;
        }

        equipment.ApplyLoadout(Hat, Broom, Magic1, Magic2);

        appearance ??= GetComponent<PlayerAppearance>();
        appearance?.ApplyHairLength(HairLength);
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

    private void ApplyStealthVisibility()
    {
        bool shouldHide = IsStealthed && !Object.HasInputAuthority;
        if (lastVisualHidden == shouldHide)
            return;

        if (visualRenderers == null || visualRenderers.Length == 0)
            visualRenderers = GetComponentsInChildren<Renderer>(true);

        for (int index = 0; index < visualRenderers.Length; index++)
        {
            Renderer renderer = visualRenderers[index];
            if (renderer != null)
                renderer.enabled = !shouldHide &&
                    (defaultRendererEnabled == null || index >= defaultRendererEnabled.Length || defaultRendererEnabled[index]);
        }

        lastVisualHidden = shouldHide;
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
        if (LocalPlayer == this)
            LocalPlayer = null;
    }
}
