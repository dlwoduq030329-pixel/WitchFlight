using System.Collections.Generic;
using Fusion;

// Race=20 is retained only to read old serialized data; it cannot be selected or launched.
public enum BattleMatchMode
{
    OneVsOne = 2, TwoVsTwo = 4, ThreeVsThree = 6, FourVsFour = 8, FiveVsFive = 10,
    OneVsOneBot = 12, TwoVsTwoBot = 14, ThreeVsThreeBot = 16, FourVsFourBot = 18,
    Race = 20, GiantMeowRaid = 21, DeathMatch = 22,
    FiveVsFiveBot = 24 // Keep the existing serialized Race/raid/deathmatch values intact.
}

public static class BattleMatchRules
{
    public const int MaxTeamPlayers = 10;
    private static readonly BattleMatchMode[] roomModes = {
        BattleMatchMode.OneVsOne, BattleMatchMode.TwoVsTwo, BattleMatchMode.ThreeVsThree,
        BattleMatchMode.FourVsFour, BattleMatchMode.FiveVsFive,
        BattleMatchMode.OneVsOneBot, BattleMatchMode.TwoVsTwoBot, BattleMatchMode.ThreeVsThreeBot,
        BattleMatchMode.FourVsFourBot, BattleMatchMode.FiveVsFiveBot, BattleMatchMode.GiantMeowRaid };
    public static int ModeOptionCount => roomModes.Length;
    public static BattleMatchMode Normalize(BattleMatchMode mode) => mode switch
    {
        BattleMatchMode.OneVsOne or BattleMatchMode.TwoVsTwo or BattleMatchMode.ThreeVsThree or
        BattleMatchMode.FourVsFour or BattleMatchMode.FiveVsFive or BattleMatchMode.OneVsOneBot or
        BattleMatchMode.TwoVsTwoBot or BattleMatchMode.ThreeVsThreeBot or BattleMatchMode.FourVsFourBot or
        BattleMatchMode.FiveVsFiveBot or BattleMatchMode.GiantMeowRaid or BattleMatchMode.DeathMatch => mode,
        _ => BattleMatchMode.TwoVsTwo
    };
    public static bool HasBots(BattleMatchMode mode) => mode is BattleMatchMode.OneVsOneBot or BattleMatchMode.TwoVsTwoBot or
        BattleMatchMode.ThreeVsThreeBot or BattleMatchMode.FourVsFourBot or BattleMatchMode.FiveVsFiveBot;
    public static bool IsTeamMode(BattleMatchMode mode) => mode is BattleMatchMode.TwoVsTwo or BattleMatchMode.TwoVsTwoBot or
        BattleMatchMode.ThreeVsThree or BattleMatchMode.FourVsFour or BattleMatchMode.FiveVsFive or
        BattleMatchMode.ThreeVsThreeBot or BattleMatchMode.FourVsFourBot or BattleMatchMode.FiveVsFiveBot;
    public static int PlayerCount(BattleMatchMode mode) => Normalize(mode) switch {
        BattleMatchMode.DeathMatch => 20, BattleMatchMode.OneVsOne or BattleMatchMode.OneVsOneBot => 2,
        BattleMatchMode.ThreeVsThree or BattleMatchMode.ThreeVsThreeBot => 6,
        BattleMatchMode.FourVsFour or BattleMatchMode.FourVsFourBot => 8,
        BattleMatchMode.FiveVsFive or BattleMatchMode.FiveVsFiveBot => 10, _ => 4 };
    public static int DropdownIndex(BattleMatchMode mode) => System.Array.IndexOf(roomModes, Normalize(mode));
    public static BattleMatchMode FromDropdown(int index) => index >= 0 && index < roomModes.Length ? roomModes[index] : BattleMatchMode.TwoVsTwo;
    public static string Label(BattleMatchMode mode) => mode == BattleMatchMode.DeathMatch ? "데스매치" : mode == BattleMatchMode.GiantMeowRaid ? "거대 먀오녀 토벌" : $"{PlayerCount(mode) / 2}vs{PlayerCount(mode) / 2}" + (HasBots(mode) ? "(BOT)" : "");

    // Stable client-relative ordering: self, teammate, opponents. Never depend on spawn/search order.
    public static void CollectRoster(NetworkRunner runner, List<PlayerData> result)
    {
        result.Clear();
        if (runner == null || !runner.IsRunning) return;
        foreach (PlayerRef player in BattleParticipants.Refs(runner))
        {
            PlayerData data = PlayerData.FindPerkData(runner, player);
            if (data != null && data.IsLoadoutInitialized && (NetworkGameManager.Instance != null && NetworkGameManager.Instance.IsDeathMatch ? data.teamIndex > 0 : data.teamIndex == 1 || data.teamIndex == 2))
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
        if (required < 2 || required > MaxTeamPlayers || required % 2 != 0 || roster.Count != required) return false;
        int teamA = 0, teamB = 0;
        foreach (PlayerData data in roster)
            if (data.teamIndex == 1) teamA++; else if (data.teamIndex == 2) teamB++;
        return teamA == required / 2 && teamB == required / 2;
    }
}
