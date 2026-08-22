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

    //[Networked]
   // public PlayerRef owner { get; set; }

    public override void Spawned()
    {
        if (Object.HasInputAuthority)
        {
            // 내 PlayerData일 때만
            // 내 로컬 DataConfig 값을 사용

            hat = (HatType)DataConfig.hatIndex;
            broom = (BroomType)DataConfig.broomIndex;
            magic1 = (MagicType)DataConfig.magic1Index;
            magic2 = (MagicType)DataConfig.magic2Index;

        }
        base.Spawned();


        NetworkGameManager.Instance.RegisterPlayerData(this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        NetworkGameManager.Instance.UnregisterPlayerData(this);
    }
}
