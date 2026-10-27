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
    [Header("비행 경로 회피")]
    [Min(.1f)] public float obstacleLookAheadSeconds = 1.5f;
    [Min(.1f)] public float routeHoldSeconds = 1.2f;
    [Min(.1f)] public float stuckSeconds = 1f;
    [Min(.1f)] public float reactionSeconds = .3f;
    [Min(.1f)] public float attackPauseSeconds = .65f;
    [Header("패링 반응 / 입력 타이밍 (호스트에서 1회 추첨)")]
    public bool useParry = true;
    [Range(0f,1f)] public float parryGoodTimingChance = .55f;
    [Range(0f,1f)] public float parryEarlyShareOfMistakes = .5f;
    [Min(.01f)] public float parryReactionSeconds = .12f;
    [Min(.1f)] public float parryLookAheadSeconds = 1.5f;
    [Min(1f)] public float parryDetectionRange = 160f;
    public Vector2 parryMistakeSeconds = new Vector2(.1f,.25f);
    [Header("깃발 회수 / 생존")]
    [Min(2f)] public float pickupApproachDistance = 14f;
    [Range(.05f, .9f)] public float seekHealthBelow = .4f;
    [Range(.1f, 1f)] public float resumeCombatAbove = .7f;
    [Min(5f)] public float healthSearchDistance = 180f;
    [Min(5f)] public float carrierSafeDistance = 45f;
    [Header("포탈 지름길")]
    public bool usePortals = true;
    [Min(0f)] public float portalMinimumSaving = 10f;
    [Min(0f)] public float portalTraversalCost = 4f;
    [Header("논타겟 조준 오차 (호스트에서 결정)")]
    [Range(0f, 1f)] public float nonTargetMissChance = .35f;
    [Min(0f)] public float aimSpreadDegrees = 1.5f;
    [Min(0f)] public float missAngleDegrees = 12f;
    [Min(.1f)] public float aimCorrectionSeconds = .65f;
    [Min(0f)] public float areaMissRadiusMultiplier = 1.4f;
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
