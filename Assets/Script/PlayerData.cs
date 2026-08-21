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
    public PlayerRef owner { get; set; }

    public override void Spawned()
    {
        base.Spawned();

        hat = (HatType)DataConfig.hatIndex;
        broom = (BroomType)DataConfig.broomIndex;
        magic1 = (MagicType)DataConfig.magic2Index;
        magic2 = (MagicType)DataConfig.magic2Index;


        NetworkGameManager.Instance.RegisterPlayerData(this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        NetworkGameManager.Instance.UnregisterPlayerData(this);
    }
}
