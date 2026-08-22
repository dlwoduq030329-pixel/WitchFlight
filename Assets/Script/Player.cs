using Fusion;
using UnityEngine;

public class Player : NetworkBehaviour
{
    [Networked]
    private float Speed { get; set; }

    [SerializeField]
    private float maxSpeed = 20f;

    [SerializeField]
    private float minSpeed = 0f;

    [Networked]
    private float acceleration { get; set; }

    [Networked]
    private float turnSpeed { get; set; }

    [SerializeField]
    float pitchSpeed = 20f;

    [SerializeField]
    float maxPitch = 60f;

    float maxTurnSpeed = 20f;
    float returnSpeed = 20f;
    float currentTurnSpeed;
    float turnAccel = 6f;
    private float currentPitch;

    [Networked]
    public float MaxHp { get; set; }

    [Networked]
    public float NowHp { get; set; }

    // 현재 들고 있는 마법 슬롯
    // 1 = magic1
    // 2 = magic2

    private int lastMagicSlot = -1;

    [Networked]
    public int CurrentMagicSlot { get; set; }

    [SerializeField]
    private PlayerEquipment equipment;

    [Networked]
    private PlayerRef LastAttacker { get; set; }


    public void InitPlayer(PlayerData data)
    {
        // PlayerData를 기준으로 전투 스탯 결정
        MaxHp = 200/GetAccelerationHP(data);
        NowHp = MaxHp;

        acceleration = GetAccelerationHP(data) * 10;

        // 기본적으로 1번 마법을 들고 시작
        CurrentMagicSlot = 1;

        // 장비 초기화
        equipment.Init(
            data.magic1,
            data.magic2,
            data.hat,
            data.broom
        );
    }




    private int GetAccelerationHP(PlayerData data)
    {
        return (int)data.broom;
    }

    public void TakeDamage(float damage,PlayerRef attacker)
    {
        if (!Object.HasStateAuthority)
            return;

        LastAttacker = attacker;

        NowHp -= damage;

        NowHp = Mathf.Max(
            NowHp,
            0
        );

        if (NowHp <= 0)
        {
            Die();
        }
    }

    public override void Spawned()
    {
        if (!Object.HasInputAuthority)
            return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        CameraManager.Instance.SetTarget(this.gameObject);
    }
    private void Die()
    {
        BattleManager.Instance.PlayerKilled(
            Object.InputAuthority, // 죽은 사람
            LastAttacker           // 죽인 사람
        );
    }

    private void Update()
    {
        // 내 캐릭터만 1, 2번 입력을 받음
        if (Object.HasInputAuthority)
        {
            MagicInput();
        }

        CamSet();
    }


    private void MagicInput()
    {
        if (!Object.HasInputAuthority)
            return;

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

        if (!GetInput(out NetworkInputData data))
            return;


        PlayerSpeed(data);

        PlayerTurn(data);

        // 항상 전진
        GoForward();
    }


    public void PlayerSpeed(NetworkInputData data)
    {
        if (data.accelerate)
        {
            Speed += acceleration * Runner.DeltaTime;
        }

        if (data.decelerate)
        {
            Speed -= acceleration * Runner.DeltaTime;
        }

        Speed = Mathf.Clamp(
            Speed,
            minSpeed,
            maxSpeed
        );
    }
    public override void Render()
    {
        if (lastMagicSlot == CurrentMagicSlot)
            return;

        lastMagicSlot = CurrentMagicSlot;

        equipment.ChangeMagic(CurrentMagicSlot);
    }


    public void PlayerTurn(NetworkInputData data)
    {
        currentPitch -= data.mouseY *
                        pitchSpeed *
                        Runner.DeltaTime;

        currentPitch = Mathf.Clamp(
            currentPitch,
            -maxPitch,
            maxPitch
        );

        Vector3 angles = transform.rotation.eulerAngles;

        float yaw = angles.y;

        if (data.turnLeft)
        {
            currentTurnSpeed = Mathf.Lerp(
                currentTurnSpeed,
                -maxTurnSpeed,
                turnAccel * Runner.DeltaTime
            );
        }
        else if (data.turnRight)
        {
            currentTurnSpeed = Mathf.Lerp(
                currentTurnSpeed,
                maxTurnSpeed,
                turnAccel * Runner.DeltaTime
            );
        }
        else
        {
            currentTurnSpeed = Mathf.Lerp(
                currentTurnSpeed,
                0,
                returnSpeed * Runner.DeltaTime
            );
        }

        yaw += currentTurnSpeed * Runner.DeltaTime;

        transform.rotation =
            Quaternion.Euler(
                currentPitch,
                yaw,
                0
            );
    }




    private void GoForward()
    {
        transform.position +=
            transform.forward *
            Speed *
            Runner.DeltaTime;
    }

    private void parry()
    {

    }


    private void CamSet()
    {

        if (Input.GetMouseButtonDown(1))
        {
            CameraManager.Instance.SetCam(true);
        }

        if (Input.GetMouseButtonUp(1))
        {
            CameraManager.Instance.SetCam(false);
        }
    }
}