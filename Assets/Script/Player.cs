using Fusion;
using UnityEngine;

public class Player : NetworkBehaviour
{
    [Networked] private float Speed { get; set; }

    [SerializeField] private float maxSpeed = 60f;
    [SerializeField] private float minSpeed = 0f;
    [SerializeField] private float wallDamageSpeedThreshold = 30f;
    [SerializeField] private float wallDamage = 20f;
    [SerializeField] private float wallDamageCooldown = 0.5f;
    [SerializeField] private float collisionRadius = 0.22f;
    [SerializeField] private float collisionHeight = 1.2f;
    [SerializeField] private float baseAcceleration = 60f;
    [SerializeField] private EquipmentStatTable equipmentStatTable;

    [Networked] private float acceleration { get; set; }
    [Networked] private float turnSpeed { get; set; }

    [SerializeField] private float pitchSpeed = 90f;
    [SerializeField] private float maxPitch = 60f;
    private float lockOnTurnSpeed = 360f;
    private float returnSpeed = 30f;
    private float currentTurnSpeed;
    private float turnAccel = 18f;
    private float currentPitch;
    private float lastWallDamageTime = float.NegativeInfinity;

    [Networked] public float MaxHp { get; set; }
    [Networked] public float NowHp { get; set; }
    [Networked] public float MaxAp { get; private set; }
    [Networked] public float NowAp { get; private set; }
    [Networked] public float ApRecoveryPerSecond { get; private set; }
    [Networked] public int CurrentMagicSlot { get; set; }
    [Networked] private PlayerRef LastAttacker { get; set; }
    [Networked] private HatType Hat { get; set; }
    [Networked] private BroomType Broom { get; set; }
    [Networked] private MagicType Magic1 { get; set; }
    [Networked] private MagicType Magic2 { get; set; }
    [Networked] private int HairLength { get; set; }
    [Networked] private int LoadoutVersion { get; set; }
    [Networked] private NetworkId LockTargetId { get; set; }

    private int lastMagicSlot = -1;
    private int renderedLoadoutVersion = -1;
    private PlayerAppearance appearance;
    private CharacterController characterController;

    [SerializeField] private PlayerEquipment equipment;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        if (characterController == null)
        {
            // ChPrefab의 모든 네트워크 인스턴스에 동일한 충돌 범위를 제공합니다.
            characterController = gameObject.AddComponent<CharacterController>();
            characterController.radius = collisionRadius;
            characterController.height = collisionHeight;
            characterController.center = new Vector3(0f, collisionHeight * 0.5f, 0f);
            characterController.stepOffset = 0.3f;
            characterController.skinWidth = 0.08f;
        }
    }

    public void InitPlayer(PlayerData data)
    {
        if (!Object.HasStateAuthority || data == null)
            return;

        HatStatEntry hatStats = GetHatStats(data.hat);
        BroomStatEntry broomStats = GetBroomStats(data.broom);

        // 모자: AP 최대치와 초당 회복량을 결정합니다.
        MaxAp = Mathf.Max(1f, hatStats.maxAp);
        NowAp = MaxAp;
        ApRecoveryPerSecond = Mathf.Max(0f, hatStats.apRecoveryPerSecond);

        // 빗자루: 체력과 선회 성능만 결정합니다.
        MaxHp = Mathf.Max(1f, broomStats.maxHp);
        NowHp = MaxHp;
        turnSpeed = Mathf.Max(0f, broomStats.turnSpeed);
        turnAccel = Mathf.Max(0.01f, broomStats.turnAcceleration);
        returnSpeed = Mathf.Max(0.01f, broomStats.turnReturnSpeed);
        lockOnTurnSpeed = Mathf.Max(0f, broomStats.lockOnTurnSpeed);

        // 가속은 빗자루와 분리된 공통 이동값입니다.
        acceleration = Mathf.Max(0f, baseAcceleration);
        CurrentMagicSlot = 1;

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
            turnSpeed = 180f,
            turnAcceleration = 18f,
            turnReturnSpeed = 30f,
            lockOnTurnSpeed = 360f
        };
    }

    public void TakeDamage(float damage, PlayerRef attacker)
    {
        if (!Object.HasStateAuthority)
            return;

        LastAttacker = attacker;
        NowHp = Mathf.Max(NowHp - damage, 0f);
        SyncHealthToPlayerData();

        if (NowHp <= 0)
            Die();
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

    // 향후 스킬 구현에서 State Authority가 호출할 AP 소비 API입니다.
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

    public override void Spawned()
    {
        // CharacterController는 생성 직후 첫 Move에서 스폰 좌표를 (0, 0, 0)으로
        // 되돌릴 수 있다. Fusion의 NetworkCharacterController와 같은 방식으로
        // 한 번 재활성화해 Runner가 적용한 스폰 위치를 유지한다.
        if (characterController != null)
        {
            characterController.enabled = false;
            characterController.enabled = true;
        }

        if (!Object.HasInputAuthority)
            return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        CameraManager cameraManager = CameraManager.Instance;
        if (cameraManager == null)
            cameraManager = FindFirstObjectByType<CameraManager>();

        if (cameraManager != null)
            cameraManager.SetTarget(gameObject);
        else
            Debug.LogError("Battle scene has no CameraManager.");
    }

    private void Die()
    {
        BattleManager.Instance.PlayerKilled(Object.InputAuthority, LastAttacker);
    }

    private void Update()
    {
        if (Object.HasInputAuthority)
            MagicInput();

        CamSet();
    }

    private void MagicInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            if (CurrentMagicSlot == 1) return;
            lastMagicSlot = CurrentMagicSlot;
            CurrentMagicSlot = 1;
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            if (CurrentMagicSlot == 2) return;
            lastMagicSlot = CurrentMagicSlot;
            CurrentMagicSlot = 2;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        RegenerateAp();

        if (!GetInput(out NetworkInputData data))
            return;

        PlayerSpeed(data);

        if (!TurnTowardLockTarget())
            PlayerTurn(data);

        GoForward();
    }

    public void PlayerSpeed(NetworkInputData data)
    {
        if (data.accelerate)
            Speed += acceleration * Runner.DeltaTime;

        if (data.decelerate)
            Speed -= acceleration * Runner.DeltaTime;

        Speed = Mathf.Clamp(Speed, minSpeed, maxSpeed);
    }

    public override void Render()
    {
        ApplyReplicatedLoadout();

        if (lastMagicSlot == CurrentMagicSlot)
            return;

        lastMagicSlot = CurrentMagicSlot;
        equipment.ChangeMagic(CurrentMagicSlot);
    }

    private void ApplyReplicatedLoadout()
    {
        if (renderedLoadoutVersion == LoadoutVersion)
            return;

        if (equipment == null)
            equipment = GetComponent<PlayerEquipment>();

        if (equipment == null)
        {
            Debug.LogError("ChPrefab has no PlayerEquipment.", this);
            return;
        }

        equipment.ApplyLoadout(Hat, Broom, Magic1, Magic2);

        if (appearance == null)
            appearance = GetComponent<PlayerAppearance>();

        if (appearance != null)
            appearance.ApplyHairLength(HairLength);

        renderedLoadoutVersion = LoadoutVersion;
    }

    public void PlayerTurn(NetworkInputData data)
    {
        currentPitch -= data.mouseY * pitchSpeed * Runner.DeltaTime;
        currentPitch = Mathf.Clamp(currentPitch, -maxPitch, maxPitch);

        float yaw = transform.rotation.eulerAngles.y;

        if (data.turnLeft)
            currentTurnSpeed = Mathf.Lerp(currentTurnSpeed, -turnSpeed, turnAccel * Runner.DeltaTime);
        else if (data.turnRight)
            currentTurnSpeed = Mathf.Lerp(currentTurnSpeed, turnSpeed, turnAccel * Runner.DeltaTime);
        else
            currentTurnSpeed = Mathf.Lerp(currentTurnSpeed, 0f, returnSpeed * Runner.DeltaTime);

        yaw += currentTurnSpeed * Runner.DeltaTime;
        transform.rotation = Quaternion.Euler(currentPitch, yaw, 0f);
    }

    public void SetLockTarget(NetworkObject target)
    {
        if (Object.HasInputAuthority)
            RPC_SetLockTarget(target.Id);
    }

    public void ClearLockTarget()
    {
        if (Object.HasInputAuthority)
            RPC_ClearLockTarget();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetLockTarget(NetworkId targetId)
    {
        LockTargetId = targetId;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ClearLockTarget()
    {
        LockTargetId = default;
    }

    private bool TurnTowardLockTarget()
    {
        if (!Runner.TryFindObject(LockTargetId, out NetworkObject targetObject) || targetObject == null)
            return false;

        Vector3 direction = targetObject.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
            return false;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, lockOnTurnSpeed * Runner.DeltaTime);
        return true;
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
        if (!Object || !Object.HasStateAuthority || Speed < wallDamageSpeedThreshold)
            return;

        // 바닥/천장 접촉은 제외하고, 일정 속도 이상의 벽 충돌만 피해 처리합니다.
        if (Mathf.Abs(hit.normal.y) > 0.5f || Time.time < lastWallDamageTime + wallDamageCooldown)
            return;

        lastWallDamageTime = Time.time;
        TakeDamage(wallDamage, PlayerRef.None);
    }

    private void CamSet()
    {
        if (!Object.HasInputAuthority)
            return;

        if (Input.GetMouseButtonDown(1))
            CameraManager.Instance.SetCam(true);

        if (Input.GetMouseButtonUp(1))
            CameraManager.Instance.SetCam(false);
    }
}
