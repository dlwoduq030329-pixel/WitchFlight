using System;
using UnityEngine;

public enum CatAnimation { StandingIdle, SittingIdle, Loaf, Standing, Walk, Run, Jump, Scratch, Punch, Grooming, Knead, Cheer, Yawn }
// Append new targets: existing assets store these enum values as integers.
public enum CatTutorialTarget { None, Wardrobe, WeaponTab, CustomBack, BattleMenu, Training, CreateRoom, RandomMatch, BattleBack, DeathMatch, JoinRoom }
public enum CatGreetingKind { Welcome, Victory, Defeat }
public enum CatTrainingAction { None, SpeedUpAndDown, AimAndFire, SwitchSlots, Parry, ChoosePerk, Boost, QuickBrake, Emote }

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
    [Tooltip("첫 훈련장 전용. 실습 시작을 누른 후 실제 행동에 성공해야 다음 대사로 진행합니다.")]
    public CatTrainingAction practice;
    public CatAnimation animation = CatAnimation.SittingIdle;
    [Min(0)] public float dreamyPauseSeconds;
    [Tooltip("배열 0~6 (몸 ID 1~7): 멍청 / 뚱냥이 / 츤데레 / 졸림·멍청·바보 / 애교 / 신사 / 공주병")]
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
        new() { personalityLines = new[] { "어? 너 어디서 봤다냥! …매일 봤지옹. 잘 왔다냥!", "쩝쩝… 왔냐냥? 네 간식도 남기려 했는데… 봉지만 남았다옹.", "왔냐옹. 옆자리 비워 둔 건 그냥… 다른 애가 앉을까 봐다냥.", "하암… 꿈에서 네가 왔는데… 아직 꿈인가옹? 볼 좀 꼬집어라냥…", "왔구나냥! 얼굴 가까이 보여 줘옹. 많이 보고 싶었다냥.", "어서 오십시오, 마녀님. 편히 쉬실 수 있도록 곁을 지키겠습니다옹.", "왔구나옹! 인사는 이 몸부터 해야지냥. 오늘도 예쁘지 않느냐옹?" } },
        new() { personalityLines = new[] { "기다리고 있었다냥! 누구를… 아, 너다옹! 생각보다 빨리 떠올랐다냥!", "우물우물… 기다리며 한 입씩 먹었더니 그릇이 비었다냥. 기다림은 배고프다옹.", "늦었네옹. 문 쪽을 보고 있던 건… 바람이 들어와서다냥.", "기다리다 잠깐 졸았다냥… 꿈에서도 기다렸는데… 두 번 기다린 거네옹…", "여기다냥, 여기! 얼른 옆에 와라옹. 붙어 있고 싶어서 기다렸다냥.", "돌아오셨군요, 마녀님. 필요한 일이 있으시면 말씀해 주십시오옹.", "이 몸을 기다리게 하다니옹! 왔으니 됐다냥. 조금만 더 가까이 오라옹." } },
        new() { personalityLines = new[] { "바빴냐냥? 나도 바빴다옹! 발가락 세다가… 몇 개였더라냥?", "바빴냐냥? 일단 앉아서 먹어라옹. 쉬는 김에 내 그릇도 좀 채워 주고냥.", "바빴나 보네옹. 무리하지 말라냥. 네가 아프면… 조용해서 싫다옹.", "바빴구나냥… 난 낮잠 한 번 잤는데… 어, 밖이 몇 번 어두워졌지옹…", "바빴어도 와 줬네냥! 잠깐만 나랑 붙어 쉬었다 가라옹.", "바쁜 시간을 보내셨군요, 마녀님. 잠시 어깨를 펴고 쉬시지요옹.", "바쁜 건 알겠다옹. 그래도 이 몸 생각할 시간은 남겨 둬라냥!" } }
    };
    public CatTutorialStep[] victoryGreetings = {
        new() { personalityLines = new[] { "이겼다냥! 내가 숨을 꼭 참아서… 어, 그건 상관없나옹? 아무튼 이겼다냥!", "쩝… 이겼다냥! 축하 간식은 두 접시다옹. 네 것도 챙겨 달라냥.", "잘했네옹. 걱정한 건 아니고… 잠깐 숨만 참았다냥.", "이겼다냥… 꿈에서도 이겼는데… 꿈보다 네가 더 잘했네옹…", "이겼다냥! 나부터 봐라옹. 제일 먼저 잘했다고 말해 주고 싶었다냥!", "훌륭한 승리였습니다, 마녀님. 끝까지 침착하게 싸우셨습니다옹.", "멋진 승리다옹! 이 몸 옆에 설 마녀라면 이 정도는 해야지냥. 아주 잘했다옹!" } },
        new() { personalityLines = new[] { "마법 실력이 늘었다냥! 몸집은 같은데… 실력은 어디에 넣어 뒀냐옹?", "실력이 쑥 늘었다냥. 든든하게 잘 싸웠으니 든든하게 먹어야지옹.", "전보다 잘하네옹. 계속 보고 있었으니까 알지냥. 칭찬은 한 번뿐이다옹.", "마법이… 전보다 잘 나간다냥. 나도 꿈에서 연습했는데… 깨니 잊었다옹…", "연습한 보람이 있다냥! 네가 잘하니까 내가 다 뿌듯해서 꼬리가 안 멈춘다옹.", "실력이 한층 깊어지셨습니다옹. 꾸준히 연습하신 결과입니다, 마녀님.", "많이 늘었구나옹! 이 몸이 알아본 마녀다냥. 어깨를 펴도 좋다옹." } },
        new() { personalityLines = new[] { "마력이 대단하다냥! 너 사실 마녀였냐옹? …맞다, 알고 있었다냥!", "마력이 아주 든든하다냥. 난 배만 든든한데… 쩝, 역할이 다르다옹.", "마력이 제법이네옹. 다음에도 보여 줘라냥. 궁금한 건 조금뿐이다옹.", "마법이 번쩍했다냥… 눈 감아도 보이네옹… 눈을 안 감았나냥…", "네 마법, 정말 멋졌다냥! 한 번 더 자랑해 줘옹. 난 계속 들어 줄 거다냥.", "인상적인 마력이었습니다옹. 마녀님의 다음 활약도 기대하겠습니다.", "반짝이는 마법이었구나옹! 이 몸의 아름다움 다음으로 눈에 띄었다냥." } }
    };
    public CatTutorialStep[] defeatGreetings = {
        new() { personalityLines = new[] { "졌냐냥? 그럼 다음엔 이기면 된다옹! 계산은 간단하다냥. …계산 맞지옹?", "아쉽다냥… 일단 따뜻한 거 먹고 쉬어라옹. 내 간식도 한 입 나눠 주겠다냥.", "풀이 죽었네옹. 못한 것만 있던 건 아니잖냐냥. 옆에 있어 줄 테니 기운 내라옹.", "졌구나냥… 꿈이면 좋겠는데… 그래도 네가 돌아와서 다행이다옹…", "속상했지냥? 이리 와라옹. 잘 싸웠다고 꼭 붙어서 말해 주겠다냥.", "아쉬운 승부였습니다옹. 애쓰셨습니다, 마녀님. 잠시 숨을 고르시지요.", "고개를 들라옹! 한 번 졌다고 이 몸의 마녀가 빛을 잃진 않는다냥." } },
        new() { personalityLines = new[] { "이길 때도 지는 때도 있다냥! 지금은 지는 때였고… 다음은 달라질 수 있다옹!", "잘되는 날도 안 되는 날도 있다냥. 쩝… 밥은 어느 날이나 챙겨 먹어라옹.", "질 때도 있는 거다옹. 다음에 이기면 되지냥. 또 같이 가 줄 테니까옹.", "이기고 지고… 둘 다 있는 거다냥. 잠은 매번 오는데… 승리는 다음에 올 수도옹…", "이겨도 져도 난 네 편이다냥. 다음에도 꼭 같이 데려가 줘옹.", "승패는 늘 바뀌는 법입니다옹. 이번 경험이 다음 판단에 도움이 될 겁니다.", "이 몸도 늘 뜻대로 되는 건 아니다옹. 그래도 당당함은 안 잃는다냥. 너도 그러라옹." } },
        new() { personalityLines = new[] { "다른 마법도 써 보라냥! 지팡이는 하나인데 마법은 많다옹. 신기하다냥!", "다른 마법도 골라 보라냥. 늘 같은 것만 먹으면… 아니, 쓰면 아쉽지옹.", "다른 마법도 써 봐라옹. 네게 맞는 게 있을 거다냥. 같이 봐는 줄게옹.", "다른 마법도… 써 보라냥. 꿈에서 본 건 없네옹… 그건 내가 만든 건가냥…", "다른 마법도 나랑 골라 보자냥. 네 손에 잘 맞는 걸 찾을 때까지 옆에 있을게옹.", "다른 마법을 시험해 보시는 건 어떻겠습니까옹? 손에 맞는 조합을 찾아보시지요.", "다른 마법을 골라 보라옹! 이 몸도 장식 하나 고르는 데 공을 들인다냥." } },
        new() { personalityLines = new[] { "특전 조합을 바꿔 보라냥! 순서를 바꾸면… 이름도 바뀌나옹? 효과부터 봐라냥!", "마법 강화도 다르게 골라 보라냥. 잘 어울리는 조합은 맛부터… 아, 힘부터 다르다옹.", "특전도 늘 같은 것만 고르지 마라옹. 다른 조합이 더 잘 맞을 수도 있잖냐냥.", "특전 조합을… 바꿔 보라냥. 아까 고른 게 뭐였더라옹… 잘 보고 골라라냥…", "강화 조합도 같이 바꿔 보자냥! 네가 다시 신나게 싸우면 좋겠다옹.", "여러 마법 강화 조합을 시도해 보십시오옹. 전투 방식에 맞는 선택이 있을 겁니다.", "특전 조합도 네게 어울리게 고르라옹! 이 몸에게 어울리는 장식처럼 말이다냥." } }
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
    [Tooltip("CatSeat가 빗자루 하위에 있으면 해당 위치·회전을 그대로 사용합니다(+Z 앞, +Y 위). CatSeat의 로컬 Position/Rotation으로 직접 조절하며 catSeatOffset/catEuler/broomSeatEuler는 적용하지 않습니다. 없으면 빗자루 표면에 자동 배치합니다.")]
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
    public Vector3 lobbyOffset = new Vector3(.55f, .85f, 0);
    [Min(0)] public float orbitRadius = .1f, orbitSpeed = .35f, bobHeight = .08f;
    [Header("로비 하품 / 쓰다듬기 반응")]
    public Vector2 lobbyYawnInterval = new Vector2(18f, 35f);
    [Min(1)] public int petScratchStrokeCount = 8;
    [Min(1f)] public float petScratchWindowSeconds = 10f;
    [Min(0f)] public float petScratchCooldownSeconds = 5f;
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
    [Tooltip("튜토리얼 대사의 초당 출력 글자 수입니다. 클릭하면 문장이 즉시 완성됩니다.")]
    [Min(1f)] public float tutorialCharactersPerSecond = 32f;
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
