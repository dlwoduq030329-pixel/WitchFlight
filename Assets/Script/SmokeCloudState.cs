using Fusion;
using UnityEngine;

// Snapshot data, not a network prefab. The match flag owns these across player respawns.
public struct SmokeCloudState : INetworkStruct
{
    public Vector3 Center;
    public float Radius;
    public TickTimer Lifetime;
    public int Sequence;
    public int CasterTeam;
    public float LockTimePenalty;
    public NetworkBool Empowered;
    public PlayerRef Caster;
    public NetworkId CasterId;
    public TickTimer DamageTick;

    public bool DamageDue(NetworkRunner runner) => Empowered && Lifetime.IsRunning && DamageTick.IsRunning && DamageTick.TargetTick<=Lifetime.TargetTick && DamageTick.Expired(runner);
    public void TickDamage(NetworkRunner runner)
    {
        if(!DamageDue(runner)) return;
        DamageTick=TickTimer.CreateFromSeconds(runner,1f);
        foreach(var enemy in Player.ActiveCombatants)
            if(enemy!=null && enemy.IsAlive && enemy.Runner==runner && enemy.TeamIndex!=CasterTeam && Contains(enemy.LockAimPoint))
                enemy.TakeDamage(10f,Caster,MagicType.Smoke,false,0,0,CasterId);
    }
    public bool Contains(Vector3 point) => Radius > 0f && (point - Center).sqrMagnitude <= Radius * Radius;

    public static bool ValidSettings(float radius, float seconds) =>
        radius > 0f && !float.IsInfinity(radius) && !float.IsNaN(radius) &&
        seconds > 0f && !float.IsInfinity(seconds) && !float.IsNaN(seconds);
}
