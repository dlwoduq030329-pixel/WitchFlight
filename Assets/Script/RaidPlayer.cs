using Fusion;
using UnityEngine;

public sealed class RaidPlayer : Player
{
    private Burn raidBurn;
    private float observedHealth;
    private int observedHit;
    public override void Spawned()
    {
        base.Spawned();
        observedHealth = NowHp;
        observedHit = HitSequence;
        HealthChanged += RecordRaidHealth;
    }
    private void RecordRaidHealth(float health, float maximum)
    {
        if (Object != null && Object.IsValid && Object.HasStateAuthority && HitSequence != observedHit && health < observedHealth)
            GiantMeowRaidDirector.Instance?.Boss?.RecordTaken(ParticipantRef, observedHealth - health, health <= 0f);
        observedHealth = health;
        observedHit = HitSequence;
    }
    public override void Despawned(Fusion.NetworkRunner runner, bool hasState)
    {
        HealthChanged -= RecordRaidHealth;
        base.Despawned(runner, hasState);
    }
    public void ReceiveRaidBreath(MagicStatEntry hit, NetworkId boss, Vector3 direction, float burnSeconds, float burnDps)
    {
        if (!Object.HasStateAuthority || !IsAlive) return;
        float before = NowHp;
        ReceiveMagicHit(hit, PlayerRef.None, boss, direction);
        if (!IsAlive || NowHp >= before) return;
        raidBurn ??= new Burn();
        // Reuse one burn record, and refresh its duration instead of stacking allocations or DPS.
        raidBurn.attacker = PlayerRef.None;
        raidBurn.remaining = Mathf.Max(raidBurn.remaining, burnSeconds);
        raidBurn.damagePerSecond = Mathf.Max(0, burnDps);
        raidBurn.interval = .25f;
        if (!perkBurns.Contains(raidBurn))
        {
            raidBurn.nextTick = perkClock + raidBurn.interval;
            perkBurns.Add(raidBurn);
        }
        HasBurnStatus = true;
    }
}
