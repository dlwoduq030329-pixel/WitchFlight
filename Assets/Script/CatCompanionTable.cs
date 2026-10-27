using System;
using UnityEngine;

public enum CatAnimation { StandingIdle, SittingIdle, Loaf, Standing, Walk, Run, Jump, Scratch, Punch, Grooming, Knead, Cheer }
public enum CatTutorialTarget { None, Wardrobe, WeaponTab, CustomBack, BattleMenu, Training, CreateRoom, RandomMatch, BattleBack }
public enum CatGreetingKind { Welcome, Victory, Defeat }

[Serializable]
public class CatAnimationEntry
{
    public CatAnimation animation;
    public string stateName;
    [Min(.1f)] public float seconds = 2f;
}

[Serializable]
public class CatTutorialStep
{
    public string id;
    public CatTutorialTarget highlight;
    public bool waitForTargetClick;
    public CatAnimation animation = CatAnimation.SittingIdle;
    [Min(0)] public float dreamyPauseSeconds;
    [Tooltip("몸 ID 순서: 멍청 / 친절 / 츤데레 / 멍때림 / 애교 / 신사 / 공주병")]
    [TextArea(2, 5)] public string[] personalityLines = new string[7];
    public string Line(int bodyId) => personalityLines != null && personalityLines.Length > 0
        ? personalityLines[Mathf.Clamp(bodyId - 1, 0, personalityLines.Length - 1)] : string.Empty;
}

[CreateAssetMenu(menuName = "WitchFlight/Cat Companion Table")]
public sealed class CatCompanionTable : ScriptableObject
{
    private static CatCompanionTable cached;
    public static CatCompanionTable Default => cached != null ? cached : cached = Resources.Load<CatCompanionTable>("CatCompanionTable");
    public GameObject catPrefab, paperAirplanePrefab, tutorialPrefab;
    public bool useCatForSummon = true;
    [Header("로비 인사 (각 행은 몸 ID 1~7의 말투)")]
    [Min(.1f)] public float greetingSeconds = 3f;
    [Min(.05f)] public float greetingFadeSeconds = .35f;
    public CatTutorialStep[] returningGreetings = {
        new() { personalityLines = new[] { "어라? 너 누구… 아! 오랜만이다냥!", "오랜만이다냥! 다시 만나서 반갑다옹~", "오랜만이네. 딱히 반가운 건 아니다냥!", "……어? 오랜만이다냥…", "오랜만이다냥~ 보고 싶었다냥♡", "오랜만에 뵙습니다, 마녀님. 반갑다옹.", "오랜만이구나냥! 이 몸을 만나러 왔구나?" } },
        new() { personalityLines = new[] { "기다리고 있었다냥! 뭘 기다렸더라… 너다냥!", "기다리고 있었다냥~ 오늘도 함께하자옹!", "이제 왔냐옹? 기다린 건 아니라니까냥…", "……기다리다가… 잠깐 멍했냥…", "기다리고 있었다냥~ 얼른 이리 오라냥♡", "마녀님의 귀환을 기다리고 있었습니다옹.", "이 몸을 기다리게 하다니! 어서 오라냥~" } },
        new() { personalityLines = new[] { "바쁜가보다냥… 나도 바쁘다냥. 낮잠 자야 하냥!", "요즘 바쁜가보다냥… 잠깐 쉬어 가라냥~", "바쁜가 보네냥. 무리하지는 말라옹…", "……바빴냥? 시간은 참… 빨리 가냥…", "바쁜가보다냥… 그래도 나랑 조금만 놀자냥♡", "많이 바쁘셨나 봅니다옹. 편히 쉬어 가십시오냥.", "바쁘더라도 이 몸을 잊으면 안 된다냥!" } }
    };
    public CatTutorialStep[] victoryGreetings = {
        new() { personalityLines = new[] { "짜릿한 승부였다냥! 털이 쭈뼛 섰다냥!", "정말 짜릿한 승부였다냥! 멋졌어냥~", "제법 짜릿했네냥. 이번엔 좀 멋있었다옹.", "……번쩍… 짜릿한 승부였다냥…", "짜릿한 승부였다냥~ 우리 마녀 최고다냥♡", "참으로 짜릿한 승부였습니다, 마녀님.", "이 몸의 마녀답게 짜릿한 승리였다냥!" } },
        new() { personalityLines = new[] { "마법 실력이 많이 늘었다냥! 키도 늘었냥?", "마법 실력이 많이 늘었다냥~ 노력한 보람이 있구나옹!", "마법 실력이 늘긴 했네냥. 칭찬은 한 번뿐이다옹!", "……마법이… 전보다 능숙해졌냥…", "마법 실력이 쑥쑥 늘었다냥~ 자랑스럽다냥♡", "마법 실력이 크게 향상되셨습니다옹. 훌륭합니다.", "마법 실력이 늘었구나냥! 이 몸의 가르침 덕이다옹." } },
        new() { personalityLines = new[] { "마력이 엄청나다냥! 엄청난 마녀가… 이미 마녀였냥!", "강력한 마력이 느껴진다냥! 엄청난 마녀가 될 거야옹!", "강한 마력이 느껴지네냥… 대단한 마녀가 되겠어옹.", "……마력이 반짝… 엄청난 마녀가 되겠냥…", "강력한 마력이다냥~ 세상에서 제일 멋진 마녀가 되라냥♡", "강력한 마력이 느껴집니다옹. 위대한 마녀가 되실 겁니다.", "강력한 마력이다냥! 이 몸에 걸맞은 대단한 마녀가 되라옹!" } }
    };
    public CatTutorialStep[] defeatGreetings = {
        new() { personalityLines = new[] { "아쉽다냥… 다음엔 이기는 주문을 외우자냥!", "아쉽다냥… 그래도 끝까지 잘 싸웠다옹.", "아쉽긴 하네냥… 너무 풀이 죽지는 말라옹.", "……아… 아쉽다냥…", "아쉽다냥… 내가 토닥토닥해 주겠다냥♡", "아쉬운 승부였습니다옹. 고개를 드십시오, 마녀님.", "아쉽지만 이 몸의 마녀가 기죽을 수는 없다냥!" } },
        new() { personalityLines = new[] { "승리가 있으면 패배도 있다냥! 그다음은 간식이냥?", "승리가 있으면 패배도 있는 법이라냥~ 다음을 준비하자옹.", "승리도 패배도 있는 거다냥. 다음엔 이기면 되잖냐옹.", "……이길 때도… 질 때도 있는 거냥…", "승리도 패배도 함께다냥~ 다음에도 곁에 있겠다냥♡", "승리가 있으면 패배도 있는 법입니다옹. 좋은 경험이었습니다.", "패배도 승리로 가는 과정이다냥! 이 몸은 믿고 있다옹." } },
        new() { personalityLines = new[] { "다른 마법을 써 보면 어떻냥? 간식 마법은 없냥?", "다른 마법을 사용해 보는 건 어떻냥? 잘 맞는 마법을 찾자옹~", "다른 마법도 써 보라냥. 네게 더 맞을지도 모르잖냐옹.", "……다른 마법도… 있었지냥… 써 볼까옹?", "다른 마법도 함께 골라 보자냥~ 분명 잘 맞을 거다냥♡", "다른 마법을 시험해 보시는 건 어떻겠습니까옹?", "다른 마법을 골라 보라냥! 이 몸의 안목을 믿어라옹." } },
        new() { personalityLines = new[] { "마법 강화를 다양하게 써 보라냥! 다양하면 맛있… 아니 강하다냥!", "마법 강화를 다양하게 사용해 보라냥~ 새로운 방법이 보일 거다옹!", "강화도 늘 같은 것만 고르지 말라냥. 바꿔 보면 되잖냐옹.", "……강화 조합을… 바꿔 보는 것도 좋겠냥…", "마법 강화도 이것저것 골라 보자냥~ 같이 강해지자옹♡", "여러 마법 강화 조합을 시도해 보시길 권합니다옹.", "강화 조합을 다양하게 써 보라냥! 이 몸에게 어울리는 전략이 필요하다옹." } }
    };

    public string Greeting(CatGreetingKind kind, int bodyIndex)
    {
        var lines = kind == CatGreetingKind.Victory ? victoryGreetings :
            kind == CatGreetingKind.Defeat ? defeatGreetings : returningGreetings;
        if (lines == null || lines.Length == 0) return string.Empty;
        return lines[UnityEngine.Random.Range(0, lines.Length)]?.Line(bodyIndex) ?? string.Empty;
    }
    [Header("Names by body index (1..7)")]
    public string[] catNames = { "쿠로 먀녀", "치즈 먀녀", "먼지 먀녀", "자반 먀녀", "고등어 먀녀", "턱시도 먀녀", "시로 먀녀" };
    public string GetCatName(int bodyIndex)
    {
        int index = Mathf.Clamp(bodyIndex - 1, 0, 6);
        if (catNames != null && index < catNames.Length && !string.IsNullOrWhiteSpace(catNames[index])) return catNames[index];
        return index switch { 0 => "쿠로 먀녀", 1 => "치즈 먀녀", 2 => "먼지 먀녀", 3 => "자반 먀녀", 4 => "고등어 먀녀", 5 => "턱시도 먀녀", _ => "시로 먀녀" };
    }
    [Header("Appearance / seating (metres)")]
    [Min(.05f)] public float catHeight = .42f;
    public Vector3 catEuler = Vector3.zero;
    public Vector3 planeScale = new Vector3(.45f, .45f, .45f);
    public Vector3 catSeatOffset = new Vector3(0, .003f, -.05f);
    public Vector3 broomSeatOffset = new Vector3(0, .18f, -.75f);
    public Vector3 broomSeatEuler;
    [Tooltip("CatSeat가 빗자루 하위에 있으면 우선 사용. 없으면 빗자루 표면에 자동 배치합니다.")]
    public bool autoBroomSeat = true;
    [Tooltip("자동 착석 계산용 빗자루 로컬 좌표계. 기본 빗자루 모델은 -X가 앞쪽입니다. CatSeat를 직접 넣으면 사용하지 않습니다.")]
    public Vector3 broomSeatingFrameEuler = new Vector3(0, -90, 0);
    [Range(.05f, .45f)] public float broomRearSeatRatio = .22f;
    public Vector3 broomSurfaceOffset = new Vector3(0, .003f, 0);
    [Header("Summoned cat team feedback only")]
    public Color allyColor = new Color(.1f, .4f, 1f, 1f), enemyColor = new Color(1f, .08f, .12f, 1f);
    [Range(0, 10)] public float summonOutlineWidth = 4;
    public float summonHaloSize = .85f;
    [Range(0, 1)] public float summonHaloAlpha = .65f;
    public Vector2 summonHealthBarSize = new Vector2(.65f, .055f);
    public float summonHealthBarOffset = .12f;
    [Header("VS paper-plane companion")]
    public Vector3 vsCatOffset = new Vector3(.48f, -.3f, .12f);
    [Tooltip("VS 카메라에서 마녀 외곽과 고양이/비행기 사이의 최소 여백. 고양이는 화면 밖으로 일부 잘릴 수 있습니다.")]
    [Min(0f)] public float vsCharacterGap = .15f;
    [Min(.1f)] public float vsMinimumHalfHeight = .85f;
    [Header("Lobby floating companion")]
    public Vector3 lobbyOffset = new Vector3(.85f, .85f, 0);
    [Min(0)] public float orbitRadius = .22f, orbitSpeed = .35f, bobHeight = .08f;
    [Min(.05f)] public float lobbyReturnStableSeconds = .15f;
    [Min(.05f)] public float lobbyRevealSeconds = .45f;
    public bool lobbyFollowCursor = true;
    [Range(0f, 60f)] public float lobbyHeadYaw = 35f;
    [Range(0f, 40f)] public float lobbyHeadPitch = 22f;
    [Min(.1f)] public float lobbyLookResponse = 7f;
    [Header("Portrait camera (outside the map)")]
    public Vector3 portraitStagePosition = new Vector3(10000, 10000, 10000);
    public Vector3 portraitCameraOffset = new Vector3(.65f, .5f, 1.3f);
    [Tooltip("튜토리얼 초상화의 화면 범위. 작게 설정할수록 고양이가 크게 보입니다.")]
    [Min(.1f)] public float portraitCameraSize = .42f;
    [Range(128, 1024)] public int portraitResolution = 512;
    [Header("Tutorial highlight")]
    public Color highlightColor = new Color(1f, .8f, .16f, 1);
    public Vector2 highlightPadding = new Vector2(14, 14);
    public AudioClip highlightSound;
    [Min(.1f)] public float highlightPulseSeconds = .8f;
    [Header("Summoning")]
    [Min(.1f)] public float summonBoardingSeconds = .7f;
    [Min(0)] public float summonJumpHeight = .65f;
    public CatAnimationEntry[] animations;
    public CatTutorialStep[] tutorial;
    [Header("첫 훈련장 안내 (완료 후 3·2·1)")]
    [Tooltip("{accelerate}, {aim}, {parry}, {slot1}, {slot2}, {boost}는 현재 설정 키로 치환됩니다.")]
    public CatTutorialStep[] trainingTutorial;

    public CatAnimationEntry GetAnimation(CatAnimation animation)
    {
        if (animations != null) foreach (var entry in animations)
            if (entry != null && entry.animation == animation) return entry;
        return null;
    }
}
