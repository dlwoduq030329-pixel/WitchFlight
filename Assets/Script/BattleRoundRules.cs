// Pure rules shared by the local preview and authoritative validation.
public static class BattleRoundRules
{
    public static int ChangedSlots(MagicType baseline1, MagicType baseline2, MagicType first, MagicType second)
        => (baseline1 == first ? 0 : 1) + (baseline2 == second ? 0 : 1);

    public static bool IsAllowedLoadout(MagicType baseline1, MagicType baseline2,
        MagicType first, MagicType second, int limit)
        => first >= MagicType.None && first <= MagicType.Smoke &&
           second >= MagicType.None && second <= MagicType.Smoke && limit >= 1 && limit <= 2 &&
           ChangedSlots(baseline1, baseline2, first, second) <= limit;
}
