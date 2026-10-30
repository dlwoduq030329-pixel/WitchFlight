using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum BattleAward { Attacker, Shield, Assassin, Strategist, NeedsHelp, Speedster, WallCrash, Seal }

public struct BattleAwardResult : INetworkStruct
{
    public BattleAward Category;
    public PlayerRef Winner;
    public NetworkString<_32> Name;
    public float Value;
    public int Team;
}

public static class BattleAwardRules
{
    public static string Title(BattleAward award) => award switch
    {
        BattleAward.Attacker => "최고의 공격수", BattleAward.Shield => "최고의 방패",
        BattleAward.Assassin => "최고의 암살자", BattleAward.Strategist => "최고의 전략가",
        BattleAward.NeedsHelp => "도움이 필요한 친구", BattleAward.Speedster => "속도광",
        BattleAward.WallCrash => "박치기 공룡", _ => "물개"
    };
    public static bool IsTime(BattleAward award) => award == BattleAward.Strategist || award == BattleAward.Speedster || award == BattleAward.Seal;
    public static string Unit(BattleAward award) => IsTime(award) ? "초" : award == BattleAward.Attacker || award == BattleAward.Shield ? "딜량" : "회";
    public static string Count(BattleAward award, float value)
    {
        int count = Mathf.Max(0, Mathf.FloorToInt(value));
        return IsTime(award) ? $"{count / 60:00}:{count % 60:00}" : Mathf.RoundToInt(value).ToString("N0");
    }
    public static float Value(PlayerData data, BattleAward award) => award switch
    {
        BattleAward.Attacker => data.BattleDamageDealt, BattleAward.Shield => data.BattleDamageTaken,
        BattleAward.Assassin => data.BattleKills, BattleAward.Strategist => data.BattleFlagSeconds,
        BattleAward.NeedsHelp => data.BattleDeaths, BattleAward.Speedster => data.BattleBoostSeconds,
        BattleAward.WallCrash => data.BattleWallCrashes, _ => data.BattleUnderwaterSeconds
    };

    public static BattleAwardResult[] SelectBalancedAwards(List<PlayerData> roster)
    {
        if (roster.Count == 0) return null;
        var leaders = new List<int>[8];
        for (int category = 0; category < leaders.Length; category++)
        {
            leaders[category] = new List<int>();
            float highest = float.NegativeInfinity;
            for (int player = 0; player < roster.Count; player++)
            {
                float value = Value(roster[player], (BattleAward)category);
                if (value > highest) { highest = value; leaders[category].Clear(); }
                if (value == highest) leaders[category].Add(player);
            }
        }
        var counts = new int[roster.Count];
        var categories = new int[4]; var winners = new int[4];
        var bestCategories = new int[4]; var bestWinners = new int[4];
        int bestDistinct = -1, bestRepeats = int.MaxValue, ties = 0;
        void Search(int slot, int nextCategory)
        {
            if (slot == 4)
            {
                int distinct = 0, repeats = 0;
                foreach (int count in counts) { if (count > 0) distinct++; repeats += count * count; }
                if (distinct < bestDistinct || (distinct == bestDistinct && repeats > bestRepeats)) return;
                if (distinct > bestDistinct || repeats < bestRepeats)
                { bestDistinct = distinct; bestRepeats = repeats; ties = 0; }
                // Random choice among equally representative sets, including tied winners.
                if (Random.Range(0, ++ties) != 0) return;
                categories.CopyTo(bestCategories, 0); winners.CopyTo(bestWinners, 0);
                return;
            }
            for (int category = nextCategory; category <= 8 - (4 - slot); category++)
                foreach (int player in leaders[category])
                {
                    categories[slot] = category; winners[slot] = player; counts[player]++;
                    Search(slot + 1, category + 1);
                    counts[player]--;
                }
        }
        Search(0, 0);
        var result = new BattleAwardResult[4];
        for (int i = 0; i < result.Length; i++)
        {
            var winner = roster[bestWinners[i]]; var category = (BattleAward)bestCategories[i];
            result[i] = new BattleAwardResult { Category = category, Winner = winner.ParticipantRef,
                Name = winner.playerName, Value = Value(winner, category), Team = winner.teamIndex };
        }
        for (int i = 0; i < result.Length; i++)
        { int pick = Random.Range(i, result.Length); (result[i], result[pick]) = (result[pick], result[i]); }
        return result;
    }
}

public partial class BattleFlag
{
    [Networked, Capacity(4)] public NetworkArray<BattleAwardResult> Awards => default;
    [Networked] public bool AwardsReady { get; private set; }
    [Networked, Capacity(4)] public NetworkArray<int> PraiseSequences => default;
    [Networked, Capacity(4)] public NetworkArray<PlayerRef> PraiseVoters => default;

    public bool HasPraised(PlayerRef voter)
    {
        if (voter == PlayerRef.None) return false;
        for (int i = 0; i < PraiseVoters.Length; i++) if (PraiseVoters[i] == voter) return true;
        return false;
    }

    private void SelectMatchAwards()
    {
        if (!Object.HasStateAuthority || AwardsReady) return;
        var roster = new List<PlayerData>();
        foreach (PlayerData data in PlayerData.PerkPlayers)
            if (data != null && data.Object != null && data.Object.IsValid && data.Runner == Runner && data.IsLoadoutInitialized)
                roster.Add(data);
        if (roster.Count == 0) return;
        roster.Sort((a, b) => a.ParticipantRef.RawEncoded.CompareTo(b.ParticipantRef.RawEncoded));
        BattleAwardResult[] selected = BattleAwardRules.SelectBalancedAwards(roster);
        for (int slot = 0; slot < 4; slot++)
            Awards.Set(slot, selected[slot]);
        AwardsReady = true;
    }

    // One vote per human per match. The host owns the ledger, including duplicate RPC rejection.
    public bool RequestPraise(int panel)
    {
        if (Object == null || !Object.IsValid || !HasEnded || !AwardsReady || panel < 0 || panel >= 4 ||
            HasPraised(Runner.LocalPlayer)) return false;
        RPC_RequestPraise(panel);
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestPraise(int panel, RpcInfo info = default)
    {
        if (!HasEnded || !AwardsReady || panel < 0 || panel >= 4) return;
        PlayerRef sender = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
        var data = PlayerData.FindPerkData(Runner, sender);
        if (data == null || data.IsMatchBot || !data.IsLoadoutInitialized) return;
        if (HasPraised(sender) || Awards[panel].Winner == PlayerRef.None) return;
        int empty = -1;
        for (int i = 0; i < PraiseVoters.Length; i++)
            if (PraiseVoters[i] == PlayerRef.None) { empty = i; break; }
        if (empty < 0) return;
        PraiseVoters.Set(empty, sender);
        PlayerRef winner = Awards[panel].Winner;
        // A player can win several categories; every card shows that player's total votes.
        for (int i = 0; i < 4; i++)
            if (Awards[i].Winner == winner) PraiseSequences.Set(i, PraiseSequences[i] + 1);
    }
}
