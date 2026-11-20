// Pure rules shared by the local preview and authoritative validation.
public static class BattleRoundRules
{
    public static int PossessionWinner(int team1Ticks, int team2Ticks)
        => team1Ticks == team2Ticks ? 0 : team1Ticks > team2Ticks ? 1 : 2;

    public static float OvertimeGraceSeconds(float heldSeconds)
        => heldSeconds > 60f ? 1.5f : 15f - 12f * System.Math.Max(0f, System.Math.Min(1f, heldSeconds / 60f));

    public static bool CanStartComeback(int winner, int carrierTeam)
        => winner != 0 && (carrierTeam == 1 || carrierTeam == 2) && carrierTeam != winner;

    public static float PossessionShare(int team1Ticks, int team2Ticks, int team)
    {
        long total = (long)team1Ticks + team2Ticks;
        if (total <= 0 || (team != 1 && team != 2)) return 0f;
        return (float)((team == 1 ? team1Ticks : team2Ticks) / (double)total);
    }
    public static int ChangedSlots(MagicType baseline1, MagicType baseline2, MagicType first, MagicType second)
        => (baseline1 == first ? 0 : 1) + (baseline2 == second ? 0 : 1);

    public static bool IsAllowedLoadout(MagicType baseline1, MagicType baseline2,
        MagicType first, MagicType second, int limit)
        => first >= MagicType.None && first <= MagicType.BladeRain &&
           second >= MagicType.None && second <= MagicType.BladeRain && limit >= 1 && limit <= 2 &&
           ChangedSlots(baseline1, baseline2, first, second) <= limit;
}
