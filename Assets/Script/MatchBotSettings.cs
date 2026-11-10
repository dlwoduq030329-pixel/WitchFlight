using UnityEngine;

public enum MatchBotRole { Objective, Support, Fighter }

public static class MatchBotRoles
{
    public static MatchBotRole ForTeamSlot(int teamSize, int botOrder)
    {
        if (teamSize < 3) return MatchBotRole.Objective;
        if (botOrder == 0 || (teamSize >= 5 && botOrder == 2)) return MatchBotRole.Support;
        if (botOrder == 1 || (teamSize == 4 && botOrder == 2) || (teamSize >= 5 && botOrder == 3)) return MatchBotRole.Fighter;
        return MatchBotRole.Objective;
    }
    public static bool CoversSupport(MatchBotRole role, int livingSupports, int missingSupports, int fighterOrder)
        => role == MatchBotRole.Fighter && (livingSupports == 0 || fighterOrder < missingSupports);
    public static bool IsAttack(MagicType magic) => magic != MagicType.None && magic != MagicType.Healing &&
        magic != MagicType.MeowFestival && magic != MagicType.TimeStop && magic != MagicType.Flare &&
        magic != MagicType.Smoke && magic != MagicType.Binding;
    public static void SupportLoadout(int order, bool doubleSupport, ref MagicType first, ref MagicType second)
    {
        if (doubleSupport) { first = MagicType.Healing; second = MagicType.MeowFestival; return; }
        if (!IsAttack(first)) first = IsAttack(second) ? second : MagicType.Vision;
        second = order % 2 == 0 ? MagicType.Healing : MagicType.MeowFestival;
    }
}

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
    [Header("대쉬 / 박치기 (호스트에서 시도별 1회 추첨)")]
    public bool useCombatDash = true;
    public bool useBodyRam = true;
    [Min(.1f)] public float dashDecisionSeconds = 1.1f;
    [Range(0f, 1f)] public float dashUseChance = .85f;
    [Range(0f, 1f)] public float dashMistakeChance = .2f;
    [Tooltip("대쉬 판단 실수 시 입력이 늦어지는 시간. 지연 중 길이 막히거나 목표가 가까워지면 취소합니다.")]
    public Vector2 dashMistakeDelay = new Vector2(.4f, .85f);
    [Min(5f)] public float dashTravelDistance = 25f;
    [Range(0f, 1f)] public float ramAttemptChance = .65f;
    [Range(0f, 1f)] public float ramMistakeChance = .3f;
    [Min(5f)] public float ramApproachDistance = 55f;
    [Min(.1f)] public float ramCommitSeconds = 2.2f;
    [Min(.1f)] public float ramRetrySeconds = 3.5f;
    [Min(0f)] public float ramMissDistance = 4f;
    [Min(0f)] public float ramLeadSeconds = .55f;
    [Header("유도탄 감속을 이용한 급선회 회피")]
    public bool useHomingEvasion = true;
    [Min(.1f)] public float evasionScanSeconds = .35f;
    [Min(.1f)] public float evasionRetrySeconds = 1.6f;
    [Min(5f)] public float evasionDetectionRange = 100f;
    [Min(.1f)] public float evasionLookAheadSeconds = 1.8f;
    [Tooltip("현재 선회력이 발사 당시의 이 비율 이하일 때 급선회로 떨쳐내기를 시도합니다. 실제 투사체 속도와 선회 곡선을 사용합니다.")]
    [Range(0f, 1f)] public float evasionWeakTurnRatio = .65f;
    [Range(0f, 1f)] public float evasionUseChance = .75f;
    [Range(0f, 1f)] public float evasionMistakeChance = .25f;
    [Min(0f)] public float evasionMistakeDelay = .45f;
    [Range(45f, 150f)] public float evasionTurnAngle = 110f;
    [Min(.1f)] public float evasionHoldSeconds = 1.2f;
    [Header("3~5인 팀전 역할 / 호위")]
    [Min(5f)] public float escortDistance = 28f;
    [Min(5f)] public float escortThreatRange = 100f;
    [Min(10f)] public float fighterLeashDistance = 180f;
    [Tooltip("아군 깃발이 안전할 때만 허용하는 전투기의 기회 공격 거리. 봇 위치 기준이며 깃발 호위 범위도 벗어날 수 없습니다.")]
    [Min(0f)] public float fighterOpportunityDistance = 70f;
    [Tooltip("깃발 보유 아군의 체력 비율이 이 값 미만이면 암살을 중단하고 호위합니다.")]
    [Range(0f, 1f)] public float fighterCarrierHealthMinimum = .7f;
    [Min(5f)] public float isolationRadius = 65f;
    [Header("포위 공격")]
    [Min(5f)] public float encirclementRadius = 45f;
    [Min(0f)] public float encirclementHeight = 12f;
    [Range(0f, 1f)] public float encirclementLeadSeconds = .35f;
    [Min(1f)] public float encirclementArrivalDistance = 8f;
    [Range(0f, 1f)] public float doubleSupportChance = .15f;
    [Range(0f, 1f)] public float pressuredDoubleSupportChance = .4f;
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
            if (row != null && row.opponent == source && row.preferred >= MagicType.Fire && row.preferred <= MagicType.BladeRain) return row.preferred;
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
            if (candidate >= MagicType.Fire && candidate <= MagicType.BladeRain && Offensive(candidate) &&
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
            if (id < 1 || id > (hat ? (int)HatType.Cosmic : (int)BroomType.Cleaner)) continue;
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
