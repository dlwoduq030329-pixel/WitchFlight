using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class GiantMeowRaidDirector : MonoBehaviour
{
    public static GiantMeowRaidDirector Instance { get; private set; }
    [SerializeField] private NetworkPrefabRef bossPrefab;
    [SerializeField] private Transform bossSpawn;
    [SerializeField, Min(30f)] private float playerStartDistance = 85f;
    [SerializeField] private Image healthFill;
    [SerializeField] private TMP_Text healthLabel, patternLabel;
    [SerializeField] private GameObject healthRoot;
    public GiantMeowBoss Boss { get; private set; }
    private float nextReady, nextUi, nextReward;
    private bool rewardObserved;
    private int displayedPattern = -1;
    private int displayedHealth = -1, displayedMaximum = -1, displayedAbsorption = -1;
    private int displayedCores = -1;
    private RectTransform healthRect;
    private static readonly string[] PatternNames = { "화염 탄막벽 · 빈틈으로 이동하세요", "비전 마법진 · 조준을 피하며 빈틈을 찾으세요", "마력 흡수 · 마력핵을 나눠 파괴하세요", "", "화염 브레스", "천둥 · 잔류 지역까지 벗어나세요", "꼬마 먀오녀 10마리 소환 · 함께 처치하세요" };
    public BattleStartPhase Phase => Boss != null && Boss.Object != null && Boss.Object.IsValid ? Boss.Phase : BattleStartPhase.WaitingForPlayers;
    public float CountdownRemaining => Boss != null && Boss.Object != null && Boss.Object.IsValid ? Boss.StageTimer.RemainingTime(Boss.Runner) ?? 0 : 0;
    public bool Finished => Phase == BattleStartPhase.Ended;
    public bool Cleared => Finished && Boss != null && Boss.Health <= 0f;

    private void Awake()
    {
        Instance = this;
        healthRect = healthFill.rectTransform;
        healthRoot.SetActive(false);
    }
    public void SpawnBoss(NetworkRunner runner, Dictionary<PlayerRef, PlayerData> participants)
    {
        if (!runner.IsServer || Boss != null) return;
        int count = 0;
        foreach (var data in participants.Values) if (data != null && !data.IsMatchBot) count++;
        runner.Spawn(bossPrefab, bossSpawn.position, bossSpawn.rotation, PlayerRef.None,
            onBeforeSpawned: (r, obj) => obj.GetComponent<GiantMeowBoss>().Configure(Mathf.Clamp(count, 1, 4)));
    }
    public void Register(GiantMeowBoss boss) => Boss = boss;
    public void SpawnPose(PlayerRef participant, Dictionary<PlayerRef, PlayerData> participants, out Vector3 position, out Quaternion rotation)
    {
        int slot = 0;
        foreach (var pair in participants) if (pair.Key.RawEncoded < participant.RawEncoded) slot++;
        Vector3 forward = bossSpawn.forward;
        position = bossSpawn.position + forward * playerStartDistance + bossSpawn.right * ((slot - (participants.Count - 1) * .5f) * 5f);
        rotation = Quaternion.LookRotation(bossSpawn.position - position);
    }
    private void Update()
    {
        var local = PlayerData.Local;
        if (local != null && local.Object != null && local.Object.IsValid && Boss != null && Phase == BattleStartPhase.WaitingForPlayers &&
            Player.LocalPlayer != null && Player.LocalPlayer.IsPresentationReady && Time.unscaledTime >= nextReady)
        { nextReady = Time.unscaledTime + .5f; Boss.RPC_Ready(); }
        if (Boss == null || Boss.Object == null || !Boss.Object.IsValid) return;
        healthRoot.SetActive(Phase != BattleStartPhase.WaitingForPlayers && !Finished);
        float health = Boss.MaxHealth > 0 ? Mathf.Clamp01(Boss.Health / Boss.MaxHealth) : 0;
        if (!Mathf.Approximately(healthRect.anchorMax.x, health)) healthRect.anchorMax = new Vector2(health, 1);
        if (Time.unscaledTime >= nextUi)
        {
            nextUi = Time.unscaledTime + .1f;
            int hp = Mathf.CeilToInt(Boss.Health), maximum = Mathf.CeilToInt(Boss.MaxHealth);
            if (displayedHealth != hp || displayedMaximum != maximum)
            {
                displayedHealth = hp; displayedMaximum = maximum;
                healthLabel.SetText("거대 먀오녀  {0:0} / {1:0}", hp, maximum);
            }
            int state = (int)Phase * 100 + (int)Boss.Pattern * 10 + (int)Boss.Stage + (Boss.Exposed ? 1000 : 0);
            if (Boss.Absorbing)
            {
                int absorbed = Mathf.CeilToInt(Boss.AbsorbedDamage);
                if (displayedPattern != state || displayedAbsorption != absorbed || displayedCores != Boss.RemainingCores)
                {
                    displayedAbsorption = absorbed;
                    displayedCores = Boss.RemainingCores;
                    patternLabel.SetText("마력핵 {0:0}개 · 핵을 나눠 파괴하세요! 흡수한 피해 {1:0}", displayedCores, absorbed);
                }
            }
            else if (displayedPattern != state)
            {
                patternLabel.text = Boss.Exposed ? "마력핵 파괴 성공! 폭발 취소 · 지금 집중 공격하세요!" :
                    Phase == BattleStartPhase.Intro ? "성 뒤에서 거대한 마력이 느껴집니다…" :
                    Phase == BattleStartPhase.Countdown ? "토벌 준비" : Finished ? (Cleared ? "토벌 성공" : "토벌 실패") :
                    PatternNames[(int)Boss.Pattern] + (Boss.Stage == RaidPatternStage.Warning ? " · 공격 예고" :
                    Boss.Stage == RaidPatternStage.Recovery ? " · 공격 기회" : "");
            }
            displayedPattern = state;
        }
        if (Finished)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (Cleared && !rewardObserved && local != null && TitleProgression.Ready && Time.unscaledTime >= nextReward)
            {
                nextReward = Time.unscaledTime + 1f;
                GrantClearTitle();
                rewardObserved = TitleProgression.Owns(46);
            }
        }
    }
    private void GrantClearTitle()
    {
        if (!Cleared || !TitleProgression.Ready) return;
        var progress = TitleProgression.Read();
        if (progress == null || progress.Owns(46)) return;
        progress.unlocked.Add(46);
        DataConfig.titleProgressJson = JsonUtility.ToJson(progress);
    }
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
