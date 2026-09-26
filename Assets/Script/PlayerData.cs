using Fusion;
using UnityEngine;
public enum HatType
{
    None,
    Classic,
    Twisted,
    Elemental,
    Serenity,
    Cosmic
}

public enum BroomType
{
    None,
    Slow,
    Standard,
    Speed
}

public enum MagicType
{
    None,
    Fire,
    Ice,
    Vision,
    Thunder,

    Flare,
    Smoke,
    Dark,

    Decoy,
    Mine,
    Scane

}

public enum Camp
{
    A,
    B
}

public class PlayerData : NetworkBehaviour
{
    [Networked]
    public HatType hat { get; set; }

    [Networked]
    public BroomType broom { get; set; }

    [Networked]
    public MagicType magic1 { get; set; }

    [Networked]
    public MagicType magic2 { get; set; }

    [Networked]
    public bool ready { get; set; }

    [Networked]
    public Camp camp { get; set; }

    [Networked]
    public int hairLength { get; private set; }

    [Networked]
    public bool IsLoadoutInitialized { get; private set; }

    //[Networked]
   // public PlayerRef owner { get; set; }

    public override void Spawned()
    {
        if (Object.HasInputAuthority)
        {
            // 내 PlayerData일 때만
            // 내 로컬 DataConfig 값을 사용

            RPC_SetLoadout((HatType)DataConfig.hatIndex, (BroomType)DataConfig.broomIndex,
                (MagicType)DataConfig.magic1Index, (MagicType)DataConfig.magic2Index,
                DataConfig.hairLength);

        }
        base.Spawned();


        NetworkGameManager.Instance?.RegisterPlayerData(this);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetLoadout(HatType selectedHat, BroomType selectedBroom,
        MagicType selectedMagic1, MagicType selectedMagic2, int selectedHairLength)
    {
        hat = selectedHat;
        broom = selectedBroom;
        magic1 = selectedMagic1;
        magic2 = selectedMagic2;
        hairLength = Mathf.Max(0, selectedHairLength);
        IsLoadoutInitialized = true;
        NetworkGameManager.Instance?.NotifyPlayerDataInitialized(this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        NetworkGameManager.Instance?.UnregisterPlayerData(this);
    }
}
