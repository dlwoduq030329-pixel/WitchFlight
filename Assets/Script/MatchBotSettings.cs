using UnityEngine;

[CreateAssetMenu(menuName = "WitchFlight/Match Bot Settings")]
public sealed class MatchBotSettings : ScriptableObject
{
    [System.Serializable] public class Counter
    {
        public MagicType opponent, preferred;
        public Counter(MagicType source, MagicType answer) { opponent = source; preferred = answer; }
    }
    private static MatchBotSettings cached;
    public static MatchBotSettings Default => cached != null ? cached : cached = Resources.Load<MatchBotSettings>("MatchBotSettings") ?? CreateInstance<MatchBotSettings>();
    [Min(1)] public float tailDistance = 18;
    [Min(1)] public float obstacleProbeDistance = 16;
    [Min(.1f)] public float reactionSeconds = .3f;
    [Min(.1f)] public float attackPauseSeconds = .65f;
    [Tooltip("ChPrefab에 외형이 연결된 모자만 지정합니다.")]
    public HatType[] availableHats = { HatType.Classic, HatType.Twisted, HatType.Elemental };
    public BroomType[] availableBrooms = { BroomType.Slow, BroomType.Standard, BroomType.Speed };
    [Tooltip("같은 팀 봇의 카운터 조합이 겹칠 때 두 번째 마법으로 사용할 공격 마법 후보입니다.")]
    public MagicType[] alternativeAttacks = { MagicType.Vision, MagicType.Fire, MagicType.Ice, MagicType.Thunder };
    public Counter[] counters = {
        new(MagicType.Fire,MagicType.Flare), new(MagicType.Ice,MagicType.Flare),
        new(MagicType.Binding,MagicType.Flare), new(MagicType.Vision,MagicType.Smoke),
        new(MagicType.Thunder,MagicType.Binding), new(MagicType.Healing,MagicType.Fire),
        new(MagicType.Dark,MagicType.Healing), new(MagicType.Curse,MagicType.Vision),
        new(MagicType.Mine,MagicType.Vision), new(MagicType.Razier,MagicType.Binding),
        new(MagicType.Flare,MagicType.Vision), new(MagicType.Smoke,MagicType.Fire)
    };
    public MagicType CounterFor(MagicType source)
    {
        if (counters != null) foreach (var row in counters)
            if (row != null && row.opponent == source && row.preferred >= MagicType.Fire && row.preferred <= MagicType.Smoke) return row.preferred;
        return MagicType.Fire;
    }
    private static bool Offensive(MagicType magic) => magic != MagicType.Flare && magic != MagicType.Smoke && magic != MagicType.Healing && magic != MagicType.Binding;
    public void CounterLoadout(MagicType first, MagicType second, out MagicType a, out MagicType b)
    {
        a = CounterFor(first); b = CounterFor(second);
        if (a == b || (!Offensive(a) && !Offensive(b))) b = a == MagicType.Vision ? MagicType.Fire : MagicType.Vision;
    }
    public void DiversifyCounterLoadout(MagicType primary, ref MagicType secondary)
    {
        // Preserve the primary counter. Never replace it just to avoid a duplicate.
        if (alternativeAttacks == null) return;
        foreach (var candidate in alternativeAttacks)
            if (candidate >= MagicType.Fire && candidate <= MagicType.Smoke && Offensive(candidate) &&
                candidate != primary && candidate != secondary) { secondary = candidate; return; }
    }
    public static PlayerConfig RandomAppearance(System.Collections.Generic.IReadOnlyList<PlayerConfig> teammates = null)
    {
        var config = PlayerConfig.Default;
        var settings = Default;
        config.hatIndex = PickEquipment(settings.availableHats, teammates, true, config.hatIndex);
        config.broomIndex = PickEquipment(settings.availableBrooms, teammates, false, config.broomIndex);
        config.bangsLength = Random.value; config.bangsDirection = Random.value;
        config.sideHairLength = Random.value; config.ahogeLength = Random.value;
        config.hairColor = Random.ColorHSV(0,1,.15f,.8f,.6f,1);
        config.clothColor = Random.ColorHSV(0,1,.15f,.8f,.5f,1);
        config.eyeColor = Random.ColorHSV(0,1,.3f,1,.6f,1);
        var eyes = EyeTexturePresetTable.Default;
        if (eyes != null && eyes.Count > 0 && eyes.TryGetPreset(Random.Range(0, eyes.Count), out var eye))
            config.eyeColor = eye.savedColorKey;
        return config;
    }

    private static int PickEquipment<T>(T[] configured, System.Collections.Generic.IReadOnlyList<PlayerConfig> teammates,
        bool hat, int fallback) where T : System.Enum
    {
        var candidates = new System.Collections.Generic.List<int>();
        int leastUsed = int.MaxValue;
        if (configured != null) foreach (var entry in configured)
        {
            int id = System.Convert.ToInt32(entry);
            if (id < 1 || id > (hat ? (int)HatType.Cosmic : (int)BroomType.Speed)) continue;
            int used = 0;
            if (teammates != null) foreach (var other in teammates)
                if ((hat ? other.hatIndex : other.broomIndex) == id) used++;
            if (used > leastUsed) continue;
            if (used < leastUsed) { candidates.Clear(); leastUsed = used; }
            if (!candidates.Contains(id)) candidates.Add(id);
        }
        return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : fallback;
    }
}
