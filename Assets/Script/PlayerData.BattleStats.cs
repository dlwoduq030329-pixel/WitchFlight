using Fusion;
using UnityEngine;

public partial class PlayerData
{
    // Match totals live on PlayerData, not the character that is destroyed on respawn.
    // Only authoritative combat events change them; the HUD sends no RPCs.
    [Networked] public float BattleDamageDealt { get; private set; }
    [Networked] public float BattleDamageTaken { get; private set; }
    [Networked] public int BattleKills { get; private set; }
    [Networked] public int BattleDeaths { get; private set; }
    [Networked] public float BattleFlagSeconds { get; private set; }
    [Networked] public float BattleBoostSeconds { get; private set; }
    [Networked] public float BattleUnderwaterSeconds { get; private set; }
    [Networked] public int BattleWallCrashes { get; private set; }
    [Networked] public int BattleStatsRevision { get; private set; }
    private NetworkId lastRecordedDeathObject;

    private bool CanRecordBattleStats => Object != null && Object.IsValid && Object.HasStateAuthority &&
        BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive;

    public void ResetBattleStats()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
        BattleDamageDealt = BattleDamageTaken = 0f;
        BattleKills = BattleDeaths = 0;
        BattleFlagSeconds = BattleBoostSeconds = BattleUnderwaterSeconds = 0f;
        BattleWallCrashes = 0;
        lastRecordedDeathObject = default;
        ResetTitleMatch();
        BattleStatsRevision++;
    }

    private Suimono.Core.SuimonoModule statsWater;
    private float nextWaterLookup;
    private void TickBattleActivity()
    {
        if (!CanRecordBattleStats) return;
        Player player = Player.FindInRunner(Runner, ParticipantRef);
        if (player == null || !player.IsAlive) return;
        float dt = Runner.DeltaTime;
        if (BattleFlag.Instance != null && BattleFlag.Instance.IsCarriedBy(Runner, ParticipantRef)) BattleFlagSeconds += dt;
        if (player.IsBoosting) BattleBoostSeconds += dt;
        if (statsWater == null && Time.unscaledTime >= nextWaterLookup)
        { statsWater = FindFirstObjectByType<Suimono.Core.SuimonoModule>(); nextWaterLookup = Time.unscaledTime + 2f; }
        if (statsWater != null && statsWater.isActiveAndEnabled &&
            statsWater.TryGetSurfaceHeight(player.transform.position, out float y) && player.transform.position.y < y)
            BattleUnderwaterSeconds += dt;
    }

    public void RecordWallCrash()
    {
        if (CanRecordBattleStats) { BattleWallCrashes++; BattleStatsRevision++; }
    }

    // Called AFTER parry and HP clamping. Environment damage counts as received only;
    // spending HP to cast Dark is not a damage event. Training dummies are excluded.
    public void RecordBattleDamage(PlayerRef attacker, float actualHpLost)
    {
        if (!CanRecordBattleStats || actualHpLost <= 0f || float.IsNaN(actualHpLost) || float.IsInfinity(actualHpLost)) return;
        BattleDamageTaken = Mathf.Min(100000000f, BattleDamageTaken + actualHpLost);
        BattleStatsRevision++;
        PlayerData source = attacker == PlayerRef.None ? null : FindPerkData(Runner, attacker);
        RecordTitleHit(source);
        if (!IsEnemyForStats(source) || !source.CanRecordBattleStats) return;
        source.BattleDamageDealt = Mathf.Min(100000000f, source.BattleDamageDealt + actualHpLost);
        source.BattleStatsRevision++;
    }

    public void RecordBattleDeath(NetworkId characterId, PlayerData killer, MagicType magic = MagicType.None, bool altitude = false)
    {
        if (!CanRecordBattleStats || characterId.Equals(default(NetworkId)) || characterId.Equals(lastRecordedDeathObject)) return;
        lastRecordedDeathObject = characterId;
        BattleDeaths++;
        RecordTitleDeath(killer, magic, altitude);
        BattleStatsRevision++;
        if (!IsEnemyForStats(killer) || !killer.CanRecordBattleStats) return;
        killer.BattleKills++;
        killer.BattleStatsRevision++;
    }

    private bool IsEnemyForStats(PlayerData other) => other != null && other != this &&
        other.Object != null && other.Object.IsValid && other.Runner == Runner &&
        other.ParticipantRef != ParticipantRef &&
        teamIndex > 0 && other.teamIndex > 0 && teamIndex != other.teamIndex;
}
