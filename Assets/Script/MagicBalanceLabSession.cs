using System;
using System.Collections.Generic;
using UnityEngine;

// Offline flight lab: ordinary MonoBehaviours, no Fusion components or session.
[DefaultExecutionOrder(-200)]
public sealed partial class MagicBalanceLabSession : MonoBehaviour
{
    public static MagicBalanceLabSession Instance { get; private set; }
    [SerializeField] private GameObject characterPrefab; // read-only visual source; never instantiated
    [SerializeField] private MagicStatTable sourceMagic;
    [SerializeField] private EquipmentStatTable sourceEquipment;
    [SerializeField] private Font panelFont;
    [SerializeField] private Vector3 arenaCenter = new(0, 220, 0);
    public MagicStatTable Magic { get; private set; }
    public EquipmentStatTable Equipment { get; private set; }
    public bool Running { get; private set; }
    public bool Defending => mode == 1;
    public bool UnlimitedMana { get; private set; } = true;
    private MagicBalanceLabActor pilot, bot;
    private Camera worldCamera;
    private bool editing = true, disposing;
    private bool ActorsReady => pilot != null && bot != null;
    private int mode, tab, editActor, hitCount;
    private float lastDamage;
    private MagicType playerMagic = MagicType.Fire, botMagic = MagicType.Fire;
    private BroomType pilotBroom = BroomType.Standard, botBroom = BroomType.Standard;
    private HatType pilotHat = HatType.Classic, botHat = HatType.Classic;
    private float distance = 120, shotInterval = 2, turnInterval = 1.4f, randomTurn = 100;
    private float nextTurn, nextShot, clock, yaw, pitch, charge, cooldown, castAt = -1;
    private MagicStatEntry pendingCast;
    private bool dashPressed, castPressed, castHeld, castReleased;
    private Vector3 escapeDirection;
    private Vector2 scroll;
    private string status = "Photon 없는 로컬 비행 실험실";
    private readonly MagicType[] homingMagics = { MagicType.Fire, MagicType.Ice, MagicType.Binding };
    private string[] magicNames, broomNames, hatNames;
    private GUIStyle titleStyle, labelStyle;
    private readonly List<MagicBalanceLabShot> shots = new();
    private Material shotMaterial;

    private void Awake()
    {
        Instance = this;
        Magic = Instantiate(sourceMagic); Magic.magics = (MagicStatEntry[])sourceMagic.magics.Clone();
        Equipment = Instantiate(sourceEquipment);
        Equipment.brooms = (BroomStatEntry[])sourceEquipment.brooms.Clone();
        Equipment.hats = (HatStatEntry[])sourceEquipment.hats.Clone();
        magicNames = Array.ConvertAll(Magic.magics, x => x.displayName);
        broomNames = Array.ConvertAll(Equipment.brooms, x => x.displayName);
        hatNames = Array.ConvertAll(Equipment.hats, x => x.displayName);
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        shotMaterial = new Material(shader);
        shotMaterial.color = new Color(1, .3f, .55f);
    }

    private void Start()
    {
        worldCamera = Camera.main;
        if (worldCamera == null)
        {
            var cameraRoot = new GameObject("Offline Lab Camera");
            worldCamera = cameraRoot.AddComponent<Camera>();
            worldCamera.tag = "MainCamera";
        }
        worldCamera.farClipPlane = Mathf.Max(1000, worldCamera.farClipPlane);
        CreateActors();
    }

    private void CreateActors()
    {
        if (disposing) return;
        ClearShots();
        if (pilot != null) { pilot.gameObject.SetActive(false); Destroy(pilot.gameObject); }
        if (bot != null) { bot.gameObject.SetActive(false); Destroy(bot.gameObject); }
        pilot = new GameObject("Offline Pilot").AddComponent<MagicBalanceLabActor>();
        bot = new GameObject("Offline Target").AddComponent<MagicBalanceLabActor>();
        pilot.transform.SetParent(transform); bot.transform.SetParent(transform);
        pilot.Initialize(characterPrefab); bot.Initialize(characterPrefab);
        ResetTrial(); SetEditing(true);
        status = "오프라인 준비 완료 · 실험 시작 후 F1로 슬라이더 조절";
    }

    private void Update()
    {
        if (!ActorsReady) return;
        if (Input.GetKeyDown(KeyCode.F1)) SetEditing(!editing);
        if (Input.GetKeyDown(KeyCode.R) && !editing) ResetTrial();
        if (editing || !Running) { castHeld = false; return; }
        yaw += Input.GetAxisRaw("Mouse X") * 3 * ControlSettings.SensitivityMultiplier;
        pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 3 * ControlSettings.SensitivityMultiplier, -75, 75);
        dashPressed |= ControlSettings.Down(ControlAction.Boost);
        bool held = ControlSettings.Held(ControlAction.LockOn);
        castPressed |= held && !castHeld; castReleased |= !held && castHeld; castHeld = held;
    }

    private void FixedUpdate()
    {
        if (!ActorsReady || !Running) return;
        float dt = Time.fixedDeltaTime; clock += dt; cooldown = Mathf.Max(0, cooldown - dt);
        pilot.Simulate(Quaternion.Euler(pitch, yaw, 0), false, !editing && Input.GetKey(KeyCode.S), dashPressed, UnlimitedMana, dt);
        dashPressed = false;
        Vector3 botDirection = Defending ? pilot.LockAimPoint - bot.LockAimPoint : EscapeDirection(bot);
        bool botDash = false;
        if (!Defending && useReactiveBotEvasion) UpdateReactiveBot(dt, ref botDirection, out botDash);
        bot.Simulate(Quaternion.LookRotation(botDirection), Defending, false, botDash, true, dt);
        if (Defending && clock >= nextShot)
        {
            var stats = Magic.GetStats(botMagic);
            if (CanAim(bot, pilot, stats)) Fire(bot, pilot, stats, (pilot.LockAimPoint - bot.CastPoint).normalized);
            nextShot = clock + Mathf.Max(.1f, shotInterval);
        }
        var selected = Magic.GetStats(playerMagic);
        if (!editing && castHeld && selected.requiresTarget && CanAim(pilot, bot, selected)) charge += dt;
        else if (!castReleased) charge = 0;
        if (!editing && cooldown <= 0 && castAt < 0 &&
            ((!selected.requiresTarget && castPressed) || (selected.requiresTarget && castReleased && charge >= selected.lockChargeSeconds && CanAim(pilot, bot, selected))))
        {
            if (SupportsFlight(selected) && (UnlimitedMana || pilot.NowAp >= selected.apCost))
            {
                pilot.NowAp = Mathf.Max(0, pilot.NowAp - selected.apCost);
                pendingCast = selected; castAt = clock + Mathf.Max(0, selected.castSeconds);
                cooldown = selected.cooldownSeconds;
            }
            else status = SupportsFlight(selected) ? "마나 부족" : "이 마법은 수치 편집만 지원합니다. 특수 패턴 시험은 실제 훈련장을 사용하세요.";
        }
        if (castReleased) charge = 0;
        castPressed = castReleased = false;
        if (castAt >= 0 && clock >= castAt)
        {
            Vector3 aim = Quaternion.Euler(pitch, yaw, 0) * Vector3.forward;
            if (worldCamera != null) aim = (worldCamera.transform.position + aim * Mathf.Max(10, pendingCast.range) - pilot.CastPoint).normalized;
            Fire(pilot, pendingCast.requiresTarget ? bot : null, pendingCast, aim);
            castAt = -1;
        }
        foreach (var shot in shots) if (shot != null && shot.gameObject.activeSelf) shot.Simulate(dt);
    }

    private static bool SupportsFlight(MagicStatEntry stats) => stats.effect == MagicEffectKind.DirectDamage ||
        stats.effect == MagicEffectKind.SlowDamage || stats.effect == MagicEffectKind.Binding || stats.effect == MagicEffectKind.LifeSteal;

    private bool CanAim(MagicBalanceLabActor from, MagicBalanceLabActor to, MagicStatEntry stats)
    {
        if (from == null || to == null || Vector3.Distance(from.LockAimPoint, to.LockAimPoint) > stats.range) return false;
        if (from == pilot && Vector3.Dot(Quaternion.Euler(pitch, yaw, 0) * Vector3.forward, to.LockAimPoint - from.LockAimPoint) <= 0) return false;
        Vector3 delta = to.LockAimPoint - from.CastPoint;
        return !Physics.Raycast(from.CastPoint, delta.normalized, out var hit, delta.magnitude, ~0, QueryTriggerInteraction.Ignore) ||
            hit.collider.GetComponentInParent<MagicBalanceLabActor>() == to;
    }

    private void Fire(MagicBalanceLabActor owner, MagicBalanceLabActor target, MagicStatEntry stats, Vector3 aim)
    {
        if (stats.projectileSpeed <= 0)
        {
            if (Physics.SphereCast(owner.CastPoint, Mathf.Max(.001f, stats.hitscanRadius), aim, out var hit, stats.range, ~0, QueryTriggerInteraction.Ignore))
            {
                var victim = hit.collider.GetComponentInParent<MagicBalanceLabActor>();
                if (victim != null && victim != owner) { victim.Hit(stats); RecordHit(stats.damage); }
            }
            return;
        }
        MagicBalanceLabShot available = null;
        foreach (var shot in shots) if (!shot.gameObject.activeSelf) { available = shot; break; }
        if (available == null)
        {
            if (shots.Count >= 128) return;
            available = new GameObject("Offline Projectile").AddComponent<MagicBalanceLabShot>();
            available.transform.SetParent(transform); available.Initialize(shotMaterial); shots.Add(available);
        }
        available.Launch(this, owner, target, stats, aim);
    }

    public void RecordHit(float damage) { hitCount++; lastDamage = damage; }
    private void ClearShots() { foreach (var shot in shots) if (shot != null) shot.Retire(); }

    private void LateUpdate()
    {
        if (!ActorsReady || worldCamera == null) return;
        Quaternion view = Quaternion.Euler(pitch, yaw, 0);
        worldCamera.transform.SetPositionAndRotation(pilot.LockAimPoint - view * Vector3.forward * 9 + Vector3.up * 1.5f, view);
    }

    private void SetEditing(bool value)
    {
        editing = value; Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = value;
        dashPressed = castPressed = castReleased = castHeld = false; charge = 0;
    }

    private void ResetTrial()
    {
        if (!ActorsReady) return;
        ClearShots();
        ResetReactiveBot();
        ResetReactiveBot();
        pilot.Broom = pilotBroom; pilot.Hat = pilotHat; bot.Broom = botBroom; bot.Hat = botHat;
        pilot.RefreshBalanceLabEquipment(Equipment, true); bot.RefreshBalanceLabEquipment(Equipment, true);
        pilot.ResetPose(arenaCenter, Quaternion.identity);
        bot.ResetPose(arenaCenter + Vector3.forward * distance, Defending ? Quaternion.Euler(0, 180, 0) : Quaternion.identity);
        yaw = pitch = charge = cooldown = 0; castAt = -1; hitCount = 0;
        escapeDirection = Vector3.forward; nextTurn = clock + turnInterval; nextShot = clock + shotInterval;
    }

    internal Vector3 EscapeDirection(MagicBalanceLabActor actor)
    {
        if (actor == null || pilot == null) return Vector3.forward;
        if (useReactiveBotEvasion)
            return (actor.transform.position - arenaCenter).sqrMagnitude > 210 * 210
                ? (arenaCenter - actor.transform.position).normalized : actor.transform.forward;
        if (clock >= nextTurn)
        {
            nextTurn = clock + Mathf.Max(.2f, turnInterval) * UnityEngine.Random.Range(.75f, 1.25f);
            Vector3 away = actor.transform.position - pilot.transform.position; away.y = 0;
            if ((actor.transform.position - arenaCenter).sqrMagnitude > 210 * 210) away = arenaCenter - actor.transform.position;
            if (away.sqrMagnitude < .01f) away = actor.transform.forward;
            escapeDirection = Quaternion.AngleAxis(UnityEngine.Random.Range(-randomTurn, randomTurn), Vector3.up) * away.normalized;
            if (randomTurn <= .01f) escapeDirection = actor.transform.forward;
        }
        return escapeDirection;
    }

    private float Slider(string name, float value, float min, float max, string format = "0.00")
    {
        GUILayout.Label(name + "  " + value.ToString(format), labelStyle);
        return GUILayout.HorizontalSlider(value, min, max, GUILayout.Height(22));
    }

    private void OnGUI()
    {
        if (Instance != this) return;
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { font = panelFont, fontSize = 20, fontStyle = FontStyle.Bold };
            labelStyle = new GUIStyle(GUI.skin.label) { font = panelFont, fontSize = 14, wordWrap = true };
        }
        var previousFont = GUI.skin.font;
        Matrix4x4 previousMatrix = GUI.matrix;
        bool previousEnabled = GUI.enabled;
        try
        {
            if (panelFont != null) GUI.skin.font = panelFont;
            float scale = Mathf.Clamp(Screen.height / 900f, .65f, 1.6f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = Screen.width / scale, height = Screen.height / scale;
            if (!editing && ActorsReady)
            {
                GUI.Label(new Rect(16, 12, width - 32, 32), "F1: 수치 조절 / R: 거리 초기화 · " + (Defending ? "수비" : "공격"), labelStyle);
                GUI.Label(new Rect(16, 44, width - 32, 64), $"거리 {(pilot.LockAimPoint - bot.LockAimPoint).magnitude:0.0} · 속도 {pilot.Speed:0.0} · HP {pilot.NowHp:0} · MP {pilot.NowAp:0}\n적중 {hitCount}회 · 최근 피해 {lastDamage:0.0} · 대쉬 {pilot.DashCharges}/3 · 충전 {pilot.RechargeProgress:P0}", labelStyle);
                GUI.Label(new Rect(width * .5f - 8, height * .5f - 16, 32, 32), "+", titleStyle);
                if (worldCamera != null)
                {
                    Vector3 target = worldCamera.WorldToViewportPoint(bot.LockAimPoint);
                    if (target.z > 0 && target.x >= 0 && target.x <= 1 && target.y >= 0 && target.y <= 1)
                        GUI.Label(new Rect(target.x * width - 40, (1 - target.y) * height - 32, 140, 32), $"적 · {charge:0.0}초", labelStyle);
                }
                return;
            }
            // Dispose every opened layout even if a scene teardown interrupts UI drawing.
            using (new GUILayout.AreaScope(new Rect(Mathf.Max(8, width - 450), 12, 438, height - 24), string.Empty, GUI.skin.box))
            {
                GUILayout.Label("마법 밸런스 실험실 · 오프라인", titleStyle);
                GUILayout.Label(status, labelStyle);
                GUILayout.Label("비행/피격 수치 시험 · 특전/소환/특수 패턴은 제외", labelStyle);
                if (ActorsReady) DrawReadyControls();
                else
                {
                    GUILayout.Label("실험 캐릭터가 준비되지 않았습니다.", labelStyle);
                    GUI.enabled = !disposing;
                    if (GUILayout.Button("실험 다시 준비", GUILayout.Height(30))) CreateActors();
                }
                GUI.enabled = Magic != null && Equipment != null && !disposing;
#if UNITY_EDITOR
                if (GUILayout.Button("현재 수치 저장 → 실제 배틀에 적용", GUILayout.Height(32)))
                {
                    try { status = MagicBalanceLabSave.SaveAssets(Magic, Equipment, sourceMagic, sourceEquipment); }
                    catch (Exception error) { status = "저장 실패: " + error.Message; Debug.LogException(error, this); }
                }
                GUILayout.Label("저장하면 마법·장비 원본 에셋에 반영되어 Play 종료 후에도 유지됩니다. 저장하지 않은 변경은 종료 시 초기화됩니다.", labelStyle);
#else
                GUILayout.Label("실제 배틀 데이터 저장은 Unity 에디터에서 이 씬을 Play한 뒤 사용하세요.", labelStyle);
#endif
                GUILayout.Label("속도·선회·감속은 즉시 반영, 수명은 다음 발사부터 반영.", labelStyle);
            }
        }
        finally { GUI.matrix = previousMatrix; GUI.skin.font = previousFont; GUI.enabled = previousEnabled; }
    }

    private void DrawReadyControls()
    {
        int nextMode = GUILayout.Toolbar(mode, new[] { "공격: 적이 도망", "수비: 적이 발사" });
        if (nextMode != mode) { mode = nextMode; ResetTrial(); }
        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button(Running ? "마우스 잠금 (F1)" : "실험 시작", GUILayout.Height(30)))
            { Running = true; SetEditing(false); }
            if (GUILayout.Button("거리/체력 초기화", GUILayout.Height(30))) ResetTrial();
            if (GUILayout.Button("정지", GUILayout.Height(30))) { Running = false; ClearShots(); }
        }
        UnlimitedMana = GUILayout.Toggle(UnlimitedMana, "내 마나 무제한 (두 캐릭터 모두 사망하지 않음)");
        GUILayout.Label($"현재 거리 {(pilot.LockAimPoint - bot.LockAimPoint).magnitude:0.0} / 내 HP {pilot.NowHp:0} / 적 HP {bot.NowHp:0}", labelStyle);
        GUILayout.Label($"적중 {hitCount}회 / 최근 피해 {lastDamage:0.0} / 록온 {charge:0.0}초 / 대쉬 {pilot.DashCharges}/3", labelStyle);
        tab = GUILayout.Toolbar(tab, new[] { "마법", "빗자루 / 모자", "실험 조건" });
        using (var view = new GUILayout.ScrollViewScope(scroll))
        {
            scroll = view.scrollPosition;
            if (tab == 0) DrawMagic();
            else if (tab == 1) DrawEquipment();
            else DrawConditions();
        }
    }

    private void DrawMagic()
    {
        editActor = GUILayout.Toolbar(editActor, new[] { "내 마법", "수비 봇 마법" });
        MagicType selected = editActor == 0 ? playerMagic : botMagic;
        if (editActor == 0)
        {
            int index = Array.FindIndex(Magic.magics, x => x.magic == selected);
            int next = GUILayout.SelectionGrid(Mathf.Max(0, index), magicNames, 3);
            if (next != index)
            {
                playerMagic = Magic.magics[next].magic;
                if (Magic.magics[next].requiresTarget) distance = Mathf.Min(distance, Magic.magics[next].range * .95f);
                ResetTrial(); selected = playerMagic;
            }
        }
        else
        {
            int index = Array.IndexOf(homingMagics, selected);
            int next = GUILayout.Toolbar(Mathf.Max(0, index), new[] { "화염", "얼음", "속박" });
            if (next != index)
            { botMagic = homingMagics[next]; distance = Mathf.Min(distance, Magic.GetStats(botMagic).range * .95f); ResetTrial(); selected = botMagic; }
            GUILayout.Label("수비 봇은 유도 마법 3종을 마나 소모 없이 반복 발사합니다. 발사 간격은 실험 조건에서 조절하세요.", labelStyle);
        }
        int slot = Array.FindIndex(Magic.magics, x => x.magic == selected);
        if (slot < 0) return;
        var entry = Magic.magics[slot];
        bool before = GUI.changed; GUI.changed = false;
        entry.projectileSpeed = Slider("투사체 속도 (0 = 즉발)", entry.projectileSpeed, 0, 200);
        entry.range = Slider("록온 / 시전 사거리", entry.range, 10, 300);
        entry.projectileLifetime = Slider("기본 수명 (초)", entry.projectileLifetime, .2f, 15);
        entry.damage = Slider("피해량", entry.damage, 0, 200);
        entry.apCost = Slider("마나 소모", entry.apCost, 0, 200);
        entry.cooldownSeconds = Slider("쿨타임 (초)", entry.cooldownSeconds, .1f, 30);
        Magic.useIndividualHomingTurnSpeeds = GUILayout.Toggle(Magic.useIndividualHomingTurnSpeeds, "마법별 선회력 사용");
        if (Magic.useIndividualHomingTurnSpeeds)
            entry.projectileTurnSpeed = Slider("선택한 마법 선회력 (도/초)", entry.projectileTurnSpeed, 0, 720);
        else Magic.homingTurnSpeed = Slider("공통 유도 선회력 (도/초)", Magic.homingTurnSpeed, 0, 720);
        Magic.homingTurnReferenceSpeed = Slider("선회력 기준 속도 (m/s)", Magic.homingTurnReferenceSpeed, 10, 150);
        Magic.homingTurnSpeedExponent = Slider("속도-선회력 곡선 지수", Magic.homingTurnSpeedExponent, 1, 4);
        Magic.homingMaxTurnLossPerSecond = Slider("선회 감속 초당 상한", Magic.homingMaxTurnLossPerSecond, 0, 1, "0.000");
        Magic.homingSpeedLossPerSecond = Slider("초당 감속 비율", Magic.homingSpeedLossPerSecond, 0, .3f, "0.000");
        Magic.homingSpeedLossPerDegree = Slider("선회 1도당 감속 비율", Magic.homingSpeedLossPerDegree, 0, .03f, "0.0000");
        Magic.homingTravelDistanceMultiplier = Slider("유도 비행 거리 배수", Magic.homingTravelDistanceMultiplier, 1, 4);
        Magic.homingLifetimeMultiplier = Slider("유도 수명 배수", Magic.homingLifetimeMultiplier, 1, 3);
        Magic.homingLeadSeconds = Slider("예측 조준 최대 시간", Magic.homingLeadSeconds, 0, 5);
        Magic.homingFallThreshold = Slider("추락 시작 속도 비율", Magic.homingFallThreshold, .01f, .8f);
        if (GUI.changed) { Magic.magics[slot] = entry;  }
        GUI.changed |= before;
    }

    private void DrawEquipment()
    {
        editActor = GUILayout.Toolbar(editActor, new[] { "내 장비", "적 장비" });
        BroomType broom = editActor == 0 ? pilotBroom : botBroom;
        HatType hat = editActor == 0 ? pilotHat : botHat;
        int bi = Array.FindIndex(Equipment.brooms, x => x.broom == broom);
        int nextBroom = GUILayout.SelectionGrid(Mathf.Max(0, bi), broomNames, 3);
        if (nextBroom != bi) { bi = nextBroom; if (editActor == 0) pilotBroom = Equipment.brooms[bi].broom; else botBroom = Equipment.brooms[bi].broom; ResetTrial(); }
        int hi = Array.FindIndex(Equipment.hats, x => x.hat == hat);
        int nextHat = GUILayout.SelectionGrid(Mathf.Max(0, hi), hatNames, 3);
        if (nextHat != hi) { hi = nextHat; if (editActor == 0) pilotHat = Equipment.hats[hi].hat; else botHat = Equipment.hats[hi].hat; ResetTrial(); }
        if (bi < 0 || hi < 0) return;
        var b = Equipment.brooms[bi]; var h = Equipment.hats[hi];
        bool before = GUI.changed; GUI.changed = false;
        float cruise = Slider("기본 2단계 비행 속도", b.maxSpeed * 2 / 3, 5, 80);
        b.maxSpeed = cruise * 1.5f;
        b.turnSpeed = Slider("기체 선회력 (도/초)", b.turnSpeed, 10, 360);
        b.boostMultiplier = Slider("부스트 배수 (3단계 기준)", b.boostMultiplier, 1, 3);
        b.maxHp = Slider("최대 체력", b.maxHp, 50, 500);
        h.maxAp = Slider("최대 마나", h.maxAp, 30, 500);
        h.apRecoveryPerSecond = Slider("초당 마나 회복", h.apRecoveryPerSecond, 0, 50);
        GUILayout.Label("같은 종류의 장비를 착용한 캐릭터에 함께 적용됩니다.", labelStyle);
        if (GUI.changed)
        { Equipment.brooms[bi] = b; Equipment.hats[hi] = h; pilot.RefreshBalanceLabEquipment(Equipment); bot.RefreshBalanceLabEquipment(Equipment); }
        GUI.changed |= before;
    }

    private void DrawConditions()
    {
        bool reactive = GUILayout.Toggle(useReactiveBotEvasion, "전투 봇과 같은 유도탄 회피 판단 사용");
        if (reactive != useReactiveBotEvasion) { useReactiveBotEvasion = reactive; ResetReactiveBot(); }
        GUILayout.Label(Defending ? "수비 모드: 봇은 제자리에서 발사합니다." : botEvasionStatus, labelStyle);
        distance = Slider("초기 거리 (초기화 버튼으로 배치)", distance, 10, 300);
        shotInterval = Slider("수비 봇 발사 간격 (초)", shotInterval, .3f, 8);
        if (!useReactiveBotEvasion)
        {
            turnInterval = Slider("도주 봇 방향 선택 간격 (초)", turnInterval, .2f, 5);
            randomTurn = Slider("도주 봇 랜덤 선회 폭 (0 = 직진)", randomTurn, 0, 180);
        }
        if (GUILayout.Button("선택한 유도 마법 사거리의 95%에 배치"))
        { distance = Magic.GetStats(Defending ? botMagic : playerMagic).range * .95f; ResetTrial(); }
        GUILayout.Label("공격: 좌클릭으로 도주 봇 공격. 유도 마법은 길게 누른 뒤 놓아 발사.\n수비: 제자리 봇이 나를 향해 반복 발사.\n마우스: 방향 / S: 감속 / Shift: 대쉬\nF1: 조작 ↔ 슬라이더. 편집 중에도 비행합니다.\n회피 판단을 켜고 속도·감속을 조절해 보세요.\n비교용 직진은 회피 판단을 끄고 도주 선회 폭을 0으로 설정합니다.", labelStyle);
    }

    private void OnDestroy()
    {
        disposing = true;
        if (Instance == this) Instance = null;
        if (Magic != null) Destroy(Magic);
        if (Equipment != null) Destroy(Equipment);
        if (shotMaterial != null) Destroy(shotMaterial);
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }
}
