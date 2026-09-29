using Fusion;
using UnityEngine;

public enum HatType { None, Classic, Twisted, Elemental, Serenity, Cosmic }
public enum BroomType { None, Slow, Standard, Speed }
public enum MagicType { None, Fire, Ice, Vision, Thunder, Flare, Smoke, Dark, Decoy, Mine, Scane }
public enum Camp { A, B }

public class PlayerData : NetworkBehaviour
{
    [Networked] public HatType hat { get; set; }
    [Networked] public BroomType broom { get; set; }
    [Networked] public MagicType magic1 { get; set; }
    [Networked] public MagicType magic2 { get; set; }
    [Networked] public bool ready { get; set; }
    [Networked] public Camp camp { get; set; }
    [Networked] public int teamIndex { get; set; }
    [Networked] public int hairLength { get; private set; }

    [Networked] public int hairColor { get; private set; }
    [Networked] public int clothColor { get; private set; }



    [Networked] public bool IsLoadoutInitialized { get; private set; }

    // 전투 중인 Player의 체력/AP 상태를 보존하는 PlayerData 값입니다.
    [Networked] public float BattleMaxHp { get; private set; }
    [Networked] public float BattleCurrentHp { get; private set; }
    [Networked] public float BattleMaxAp { get; private set; }
    [Networked] public float BattleCurrentAp { get; private set; }
    [Networked] public float BattleApRecoveryPerSecond { get; private set; }

    public override void Spawned()
    {
        if (Object.HasInputAuthority)
        {
            RPC_SetLoadout((HatType)DataConfig.hatIndex, (BroomType)DataConfig.broomIndex,
                (MagicType)DataConfig.magic1Index, (MagicType)DataConfig.magic2Index,
                DataConfig.hairLength);
        }

        base.Spawned();

        // 호스트에서만 팀 배정과 PlayerData Dictionary 관리를 수행한다.
        if (Object.HasStateAuthority)
        {
            NetworkGameManager.Instance?.AssignTeamIndex(this);
            NetworkGameManager.Instance?.RegisterPlayerData(this);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetLoadout(HatType selectedHat, BroomType selectedBroom,
        MagicType selectedMagic1, MagicType selectedMagic2, int selectedHairLength)
    {
        hat = NormalizeHat(selectedHat);
        broom = NormalizeBroom(selectedBroom);
        magic1 = NormalizeMagic(selectedMagic1, MagicType.Fire);
        magic2 = NormalizeMagic(selectedMagic2, MagicType.Ice);
        hairLength = Mathf.Max(0, selectedHairLength);
        IsLoadoutInitialized = true;
        NetworkGameManager.Instance?.NotifyPlayerDataInitialized(this);
    }

    private static HatType NormalizeHat(HatType selectedHat)
    {
        return selectedHat == HatType.Classic ||
               selectedHat == HatType.Twisted ||
               selectedHat == HatType.Elemental
            ? selectedHat
            : HatType.Classic;
    }

    private static BroomType NormalizeBroom(BroomType selectedBroom)
    {
        return selectedBroom == BroomType.Slow ||
               selectedBroom == BroomType.Standard ||
               selectedBroom == BroomType.Speed
            ? selectedBroom
            : BroomType.Standard;
    }

    private static MagicType NormalizeMagic(MagicType selectedMagic, MagicType fallback)
    {
        // Enum numbers are also backend save values; retain them and only repair empty/invalid slots.
        int value = (int)selectedMagic;
        return value >= (int)MagicType.Fire && value <= (int)MagicType.Scane
            ? selectedMagic
            : fallback;
    }

    public void SetBattleHealth(float maxHp, float currentHp)
    {
        if (!Object.HasStateAuthority)
            return;

        BattleMaxHp = Mathf.Max(0f, maxHp);
        BattleCurrentHp = Mathf.Clamp(currentHp, 0f, BattleMaxHp);
    }

    public void SetBattleAp(float maxAp, float currentAp, float recoveryPerSecond)
    {
        if (!Object.HasStateAuthority)
            return;

        BattleMaxAp = Mathf.Max(0f, maxAp);
        BattleCurrentAp = Mathf.Clamp(currentAp, 0f, BattleMaxAp);
        BattleApRecoveryPerSecond = Mathf.Max(0f, recoveryPerSecond);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        NetworkGameManager.Instance?.UnregisterPlayerData(this);
    }
}
