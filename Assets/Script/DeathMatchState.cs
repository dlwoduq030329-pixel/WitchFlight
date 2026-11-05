using Fusion;
using UnityEngine;

// Deathmatch-only replicated rules. No flag model, pickup collider, rounds or intro state.
public sealed class DeathMatchState : NetworkBehaviour
{
    public static DeathMatchState Instance { get; private set; }
    private const int MaxSmokeClouds = 32;
    [Networked, Capacity(MaxSmokeClouds)] private NetworkArray<SmokeCloudState> SmokeClouds => default;
    [Networked] private int SmokeSequence { get; set; }
    private readonly GameObject[] smokeViews = new GameObject[MaxSmokeClouds];
    private readonly int[] smokeViewSequences = new int[MaxSmokeClouds];
    public override void Spawned() { Instance = this; }
    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority) BattleManager.Instance?.TickRespawns();
    }
    public override void Render() { RenderSmoke(); }
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ClearSmokeViews();
        if (Instance == this) Instance = null;
    }
    private void OnDestroy() { ClearSmokeViews(); if (Instance == this) Instance = null; }
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_AnnounceKill(string killer, int killerProfile, string victim, int victimProfile)
        => BattleKillFeed.Instance?.ShowKill(killer, killerProfile, victim, victimProfile);
    public bool CanCreateSmoke(NetworkRunner expectedRunner) => Object != null && Object.IsValid &&
        Object.HasStateAuthority && Runner == expectedRunner && BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive &&
        FindFreeSmokeSlot() >= 0;

    private int FindFreeSmokeSlot()
    {
        for (int i = 0; i < SmokeClouds.Length; i++)
            if (!SmokeClouds[i].Lifetime.IsRunning || SmokeClouds[i].Lifetime.Expired(Runner)) return i;
        return -1;
    }

    public bool CreateSmoke(Player caster, MagicStatEntry stats)
    {
        if (caster == null || caster.Object == null || !caster.Object.IsValid ||
            !caster.Object.HasStateAuthority || !caster.IsAlive || !CanCreateSmoke(caster.Runner) ||
            !SmokeCloudState.ValidSettings(stats.radius, stats.effectDuration)) return false;
        int slot = FindFreeSmokeSlot();
        SmokeSequence++;
        SmokeClouds.Set(slot, new SmokeCloudState {
            Center = caster.LockAimPoint, Radius = Mathf.Min(stats.radius, 100f),
            Lifetime = TickTimer.CreateFromSeconds(Runner, Mathf.Min(stats.effectDuration, 120f)),
            CasterTeam = caster.TeamIndex,
            Sequence = SmokeSequence
        });
        return true;
    }

    public bool ContainsSmoke(Vector3 point)
    {
        if (Object == null || !Object.IsValid || (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)) return false;
        for (int i = 0; i < SmokeClouds.Length; i++)
        {
            SmokeCloudState cloud = SmokeClouds[i];
            if (cloud.Lifetime.IsRunning && !cloud.Lifetime.Expired(Runner) && cloud.Contains(point)) return true;
        }
        return false;
    }

    public bool ContainsEnemySmoke(Vector3 point, int team)
    {
        if (Object == null || !Object.IsValid || (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)) return false;
        for (int i = 0; i < SmokeClouds.Length; i++)
        {
            SmokeCloudState cloud = SmokeClouds[i];
            if (cloud.CasterTeam != team && cloud.Lifetime.IsRunning &&
                !cloud.Lifetime.Expired(Runner) && cloud.Contains(point)) return true;
        }
        return false;
    }

    private void RenderSmoke()
    {
        if (Object == null || !Object.IsValid || (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive))
        { ClearSmokeViews(); return; }
        for (int i = 0; i < SmokeClouds.Length; i++)
        {
            SmokeCloudState cloud = SmokeClouds[i];
            bool active = cloud.Lifetime.IsRunning && !cloud.Lifetime.Expired(Runner);
            if (smokeViews[i] != null && (!active || smokeViewSequences[i] != cloud.Sequence))
            { Destroy(smokeViews[i]); smokeViews[i] = null; }
            if (!active || smokeViews[i] != null) continue;
            smokeViewSequences[i] = cloud.Sequence;
            smokeViews[i] = UtilityMagicVisual.CreateSmoke(cloud.Center, cloud.Radius,
                CombatPresentation.Stats(MagicType.Smoke).utilityVfxPrefab);
            TimeVfxSpeed.Bind(smokeViews[i], Runner, cloud.CasterTeam, false);
        }
    }

    private void ClearSmokeViews()
    {
        for (int i = 0; i < smokeViews.Length; i++)
        {
            if (smokeViews[i] != null) Destroy(smokeViews[i]);
            smokeViews[i] = null;
        }
    }

}
