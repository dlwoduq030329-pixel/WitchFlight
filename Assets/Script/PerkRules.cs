using System;
using System.Collections.Generic;

// Pure rules: candidate IDs are fixed per slot/stage, never array indices.
public static class PerkRules
{
    public const int MaxChoices = 4;
    public static PerkId Spell(MagicType magic, int stage)
    {
        if (stage < 1 || stage > 2) return PerkId.None;
        int first = magic switch {
            MagicType.Fire => 1, MagicType.Ice => 3, MagicType.Vision => 5, MagicType.Thunder => 7,
            MagicType.Healing => 9, MagicType.Binding => 11, MagicType.Dark => 13, MagicType.Curse => 15,
            MagicType.Mine => 17, MagicType.Razier => 19, MagicType.Flare => 21, MagicType.Smoke => 23, _ => 0 };
        return first == 0 ? PerkId.None : (PerkId)(first + stage - 1);
    }
    public static bool Common(PerkId id) => (int)id >= 25 && (int)id <= 34;
    public static int Bit(PerkId id) => Common(id) ? 1 << ((int)id - 25) : 0;
    public static int RequiredXp(int first, float growth, int earned)
        => (int)Math.Min(100000000, Math.Ceiling(Math.Max(1, first) * Math.Pow(Math.Max(1.01, growth), Math.Clamp(earned,0,3))));
    public static PerkId Candidate(MagicType magic, int level, int otherLevel, PerkId common)
        => level < (otherLevel == 2 ? 1 : 2) ? Spell(magic, level + 1) : otherLevel == 2 || level == 2 ? common : PerkId.None;
    public static bool CanUpgrade(int level, int other) => level >= 0 && level < (other == 2 ? 1 : 2);
    public static PerkId CommonCandidate(int mask, int seed, PerkId excluded = PerkId.None)
    {
        for (int i = 0; i < 10; i++) {
            PerkId id = (PerkId)(25 + (int)((uint)seed % 10 + i) % 10);
            if ((mask & Bit(id)) == 0 && id != excluded) return id;
        }
        return PerkId.None;
    }
}

// Authority-local event ledger. Its clock advances only during actual combat.
// Life keys are NetworkIds, so healing cannot restore a spent damage quota.
public sealed class PerkXpLedger
{
    private readonly Queue<(float time, int xp)> actions = new();
    private readonly Dictionary<uint, float> damage = new();
    private readonly Dictionary<uint, int> quotas = new();
    private readonly HashSet<uint> kills = new();
    private readonly Dictionary<int, float> rewards = new();
    public float Time { get; private set; }
    private int windowTotal, lastFlagRound = -1, lastBonusRound = -1;
    public void Advance(float seconds) => Time += Math.Max(0, seconds);
    public int Cap(int xp, float window, int limit)
    {
        while (actions.Count > 0 && actions.Peek().time <= Time - Math.Max(1,window)) windowTotal -= actions.Dequeue().xp;
        int granted = Math.Max(0, Math.Min(xp, limit - windowTotal));
        if (granted > 0) { actions.Enqueue((Time,granted)); windowTotal += granted; }
        return granted;
    }
    public int Damage(uint life, float amount, float lifeMaxHp, float fraction, int maxQuotas, int xp)
    {
        if (amount <= 0 || lifeMaxHp <= 0) return 0;
        damage.TryGetValue(life, out float total); quotas.TryGetValue(life, out int paid);
        total += amount / lifeMaxHp;
        int next = Math.Min(Math.Max(1,maxQuotas),(int)Math.Floor((total + .000001f) / Math.Max(.01f,fraction)));
        damage[life] = total; quotas[life] = next;
        return Math.Max(0,next-paid) * Math.Max(0,xp);
    }
    public bool RewardReady(int category, float cooldown)
    {
        if (rewards.TryGetValue(category,out float at) && Time < at) return false;
        rewards[category] = Time + Math.Max(0,cooldown); return true;
    }
    public bool Kill(uint life) => kills.Add(life);
    public bool Flag(int round) { if (lastFlagRound == round) return false; lastFlagRound=round; return true; }
    public bool RoundBonus(int round) { if (lastBonusRound >= round) return false; lastBonusRound=round; return true; }
}
