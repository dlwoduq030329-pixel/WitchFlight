using Fusion;
using UnityEngine;

public struct RaidResultRow : INetworkStruct
{
    public PlayerRef Player;
    public NetworkString<_32> Name;
    public float Damage, Taken;
    public int Kills, Deaths;
}

public sealed partial class GiantMeowBoss
{
    [Networked, Capacity(4)] public NetworkArray<RaidResultRow> Results => default;
    [Networked, Capacity(4)] public NetworkArray<BattleAwardResult> Awards => default;
    [Networked, Capacity(4)] public NetworkArray<int> PraiseCounts => default;
    [Networked, Capacity(4)] public NetworkArray<PlayerRef> Voters => default;
    [Networked] public bool AwardsReady { get; private set; }

    private void InitializeResults()
    {
        int slot = 0;
        foreach (var data in PlayerData.PerkPlayers)
        {
            if (data == null || data.Object == null || !data.Object.IsValid || data.Runner != Runner || data.IsMatchBot || slot >= Results.Length) continue;
            Results.Set(slot++, new RaidResultRow { Player = data.ParticipantRef, Name = data.playerName });
        }
    }
    public RaidResultRow ResultFor(PlayerRef player)
    {
        for (int i = 0; i < Results.Length; i++) if (Results[i].Player == player) return Results[i];
        return default;
    }
    public void RecordTaken(PlayerRef player, float damage, bool died)
    {
        if (!Object.HasStateAuthority || !IsTargetable || damage <= 0f) return;
        for (int i = 0; i < Results.Length; i++)
        {
            var row = Results[i];
            if (row.Player != player) continue;
            row.Taken += damage;
            if (died) row.Deaths = 1;
            Results.Set(i, row);
            return;
        }
    }
    public void RecordDealt(NetworkId attacker, float damage, bool killed)
    {
        if (!Object.HasStateAuthority || damage <= 0f || !Runner.TryFindObject(attacker, out var obj) || obj == null) return;
        var player = obj.GetComponent<RaidPlayer>();
        if (player == null) return;
        for (int i = 0; i < Results.Length; i++)
        {
            var row = Results[i];
            if (row.Player != player.ParticipantRef) continue;
            row.Damage += damage;
            if (killed) row.Kills++;
            Results.Set(i, row);
            return;
        }
    }

    private static float AwardValue(RaidResultRow row, int category) => category switch
    { 0 => row.Damage, 1 => row.Taken, 2 => row.Kills, _ => row.Deaths };

    private void CaptureRaidAwards()
    {
        // Choose real category leaders, preferring different players when tied.
        var used = new bool[4];
        var categories = new[] { BattleAward.Attacker, BattleAward.Shield, BattleAward.Assassin, BattleAward.NeedsHelp };
        for (int category = 0; category < 4; category++)
        {
            int best = -1;
            float highest = -1f;
            for (int i = 0; i < Results.Length; i++)
            {
                var row = Results[i];
                if (row.Player == PlayerRef.None) continue;
                float value = AwardValue(row, category);
                if (value > highest || (value == highest && best >= 0 && used[best] && !used[i]))
                { best = i; highest = value; }
            }
            if (best < 0) continue;
            used[best] = true;
            var winner = Results[best];
            Awards.Set(category, new BattleAwardResult { Category = categories[category], Winner = winner.Player,
                Name = winner.Name, Value = highest, Team = 1 });
        }
        AwardsReady = true;
    }
    public bool HasPraised(PlayerRef player)
    {
        for (int i = 0; i < Voters.Length; i++) if (Voters[i] == player) return true;
        return false;
    }
    public bool RequestPraise(int panel)
    {
        if (!AwardsReady || panel < 0 || panel >= 4 || Awards[panel].Winner == PlayerRef.None || HasPraised(Runner.LocalPlayer)) return false;
        RPC_Praise(panel);
        return true;
    }
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Praise(int panel, RpcInfo info = default)
    {
        if (Phase != BattleStartPhase.Ended || !AwardsReady || panel < 0 || panel >= 4 || Awards[panel].Winner == PlayerRef.None) return;
        PlayerRef sender = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
        if (sender == PlayerRef.None || ResultFor(sender).Player != sender || HasPraised(sender)) return;
        for (int i = 0; i < Voters.Length; i++)
        {
            if (Voters[i] != PlayerRef.None) continue;
            Voters.Set(i, sender);
            // Keep each award card's votes independent of other cards for this player.
            PraiseCounts.Set(panel, PraiseCounts[panel] + 1);
            break;
        }
    }
}
