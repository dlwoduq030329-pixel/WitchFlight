using System.Collections.Generic;
using Fusion;

public enum BattleMatchMode { OneVsOne = 2, TwoVsTwo = 4, OneVsOneBot = 12, TwoVsTwoBot = 14 }

public static class BattleMatchRules
{
    public static bool HasBots(BattleMatchMode mode) => mode == BattleMatchMode.OneVsOneBot || mode == BattleMatchMode.TwoVsTwoBot;
    public static bool IsTeamMode(BattleMatchMode mode) => PlayerCount(mode) == 4;
    public static int PlayerCount(BattleMatchMode mode) => mode == BattleMatchMode.OneVsOne || mode == BattleMatchMode.OneVsOneBot ? 2 : 4;
    public static int DropdownIndex(BattleMatchMode mode) => mode == BattleMatchMode.OneVsOne ? 0 : mode == BattleMatchMode.TwoVsTwo ? 1 : mode == BattleMatchMode.OneVsOneBot ? 2 : 3;
    public static BattleMatchMode FromDropdown(int index) => index == 0 ? BattleMatchMode.OneVsOne : index == 1 ? BattleMatchMode.TwoVsTwo : index == 2 ? BattleMatchMode.OneVsOneBot : BattleMatchMode.TwoVsTwoBot;
    public static string Label(BattleMatchMode mode) => (PlayerCount(mode) == 2 ? "1 vs 1" : "2 vs 2") + (HasBots(mode) ? " (BOT)" : "");

    // Stable client-relative ordering: self, teammate, opponents. Never depend on spawn/search order.
    public static void CollectRoster(NetworkRunner runner, List<PlayerData> result)
    {
        result.Clear();
        if (runner == null || !runner.IsRunning) return;
        foreach (PlayerRef player in BattleParticipants.Refs(runner))
        {
            PlayerData data = PlayerData.FindPerkData(runner, player);
            if (data != null && data.IsLoadoutInitialized && (data.teamIndex == 1 || data.teamIndex == 2))
                result.Add(data);
        }
        PlayerData local = result.Find(d => d.ParticipantRef == runner.LocalPlayer);
        result.Sort((a, b) =>
        {
            int aSide = a == local ? 0 : local != null && a.teamIndex == local.teamIndex ? 1 : 2;
            int bSide = b == local ? 0 : local != null && b.teamIndex == local.teamIndex ? 1 : 2;
            int side = aSide.CompareTo(bSide);
            return side != 0 ? side : a.ParticipantRef.RawEncoded.CompareTo(b.ParticipantRef.RawEncoded);
        });
    }

    public static bool IsFullBalanced(IReadOnlyList<PlayerData> roster, int required)
    {
        if ((required != 2 && required != 4) || roster.Count != required) return false;
        int teamA = 0, teamB = 0;
        foreach (PlayerData data in roster)
            if (data.teamIndex == 1) teamA++; else if (data.teamIndex == 2) teamB++;
        return teamA == required / 2 && teamB == required / 2;
    }
}
