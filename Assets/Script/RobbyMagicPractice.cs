using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Lobby-only sandbox. No Player, BattleManager, rewards, debuffs or PvP hit registration.
// Uses the equipped IDs and the same balancing/VFX assets; all hits have one allowed target.
[DefaultExecutionOrder(200)] // Read the muzzle after animation and the lobby camera's LateUpdate.
public sealed class RobbyMagicPractice : MonoBehaviour
{
    public static float DisplayedLockProgress { get; private set; }
    [SerializeField] private RobbyPracticeDummy target;
    [SerializeField] private Slider[] skillSliders = new Slider[2];
    [SerializeField] private MagicStatTable magicTable;
    [SerializeField] private RectTransform aimReticle;
    [SerializeField] private bool traceCasts = true;
    [SerializeField, Min(.05f)] private float instantBeamSeconds = .3f;
    [SerializeField, Min(.01f)] private float instantBeamWidth = .22f;
    private readonly Dictionary<MagicType, RobbyInstantMagicVisual> instantBeams = new();
    [SerializeField, Range(1f, 60f)] private float lockCone = 18f;
    [Header("Copied BattleHud lock presentation")]
    [SerializeField] private Image lockChargeMarker, lockCompleteMarker;
    [SerializeField] private float lockChargeStartSize = 500f, lockChargeEndSize = 50f;
    [SerializeField] private Color lockChargeColor = Color.white, lockCompleteColor = Color.green;
    [SerializeField] private float lockChargeStartAlpha = .25f, lockChargeEndAlpha = 1f;
    private readonly MagicStatEntry[] slots = new MagicStatEntry[2];
    private readonly float[] readyAt = new float[2];
    private readonly Image[][] icons = new Image[2][];
    private readonly TMP_Text[] cooldownText = new TMP_Text[2];
    private readonly TMP_Text[] costText = new TMP_Text[2];
    private readonly GameObject[] selectedFx = new GameObject[2];
    private readonly int[] lastSeconds = { -1, -1 };
    private readonly RaycastHit[] hits = new RaycastHit[64];
    private readonly List<Shot> shots = new List<Shot>(128);
    private readonly List<Burst> bursts = new List<Burst>(8);
    private PlayerRobby owner;
    private Transform castRoot;
    private int selected, bladeLeft;
    private float charge, bladeUntil, nextBlade, nextChannel;
    private bool held, visible;
    private bool acquiredLock;
    private bool pendingRelease, pendingBlade;
    private MagicStatEntry pendingStats;
    private float pendingReadyAt;
    private float pendingCharge;
    private float nextProjectileTrace;
    private Canvas canvas;
    private CombatMagicVisual channel;
    private GameObject bladeView;
    private GameObject fallbackProjectile, festivalPrefab;
    private Material fallbackMaterial;
    private readonly List<Orbit> orbits = new List<Orbit>(3);
    private struct Orbit { public GameObject view; public float expires, phase; }
    private struct Shot
    {
        public GameObject view;
        public Vector3 position, direction;
        public float expires;
        public MagicStatEntry stats;
        public bool guided;
    }
    private struct Burst
    {
        public MagicStatEntry stats;
        public Vector3 origin, direction;
        public int remaining;
        public float next, interval;
        public bool guided;
    }
    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        if (magicTable == null) magicTable = MagicStatTable.Default;
        festivalPrefab = Resources.Load<GameObject>("MeowFestivalVisual");
        fallbackProjectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fallbackProjectile.name = "Lobby projectile template";
        fallbackProjectile.SetActive(false);
        fallbackProjectile.transform.SetParent(transform, false);
        fallbackProjectile.transform.localScale = Vector3.one * .25f;
        var collider = fallbackProjectile.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        fallbackMaterial = CombatPresentation.CreateEffectMaterial(new Color(.6f, .35f, 1f));
        var renderer = fallbackProjectile.GetComponent<Renderer>(); renderer.sharedMaterial = fallbackMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        HideLock();
        for (int i = 0; i < 2; i++)
        {
            if (i >= skillSliders.Length || skillSliders[i] == null) continue;
            var slider = skillSliders[i];
            icons[i] = slider.GetComponentsInChildren<Image>(true);
            foreach (var text in slider.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name.ToLowerInvariant().Contains("cool")) cooldownText[i] = text;
                if (text.name == "costPercent") costText[i] = text;
            }
            foreach (var node in slider.GetComponentsInChildren<Transform>(true))
                if (node.name == "FX_CircularFeather_B" || node.name == "FX" || node.name == "fx")
                { selectedFx[i] = node.gameObject; break; }
            slider.interactable = false; slider.minValue = 0; slider.maxValue = 1;
        }
        RefreshLoadout();
    }
    private void OnEnable() { DataConfig.Changed += RefreshLoadout; }
    private void OnDisable()
    {
        DataConfig.Changed -= RefreshLoadout;
        Cancel();
        for (int i = 0; i < shots.Count; i++) MagicVfxPool.Release(shots[i].view);
        shots.Clear(); bursts.Clear();
        foreach (var beam in instantBeams.Values) if (beam != null) beam.gameObject.SetActive(false);
        for (int i = 0; i < orbits.Count; i++) MagicVfxPool.Release(orbits[i].view);
        orbits.Clear();
    }
    private void RefreshLoadout()
    {
        if (magicTable == null) return;
        for (int i = 0; i < 2; i++)
        {
            var magic = (MagicType)(i == 0 ? DataConfig.magic1Index : DataConfig.magic2Index);
            if (slots[i].magic != magic) { readyAt[i] = 0; Cancel(); }
            slots[i] = magicTable.GetStats(magic);
            if (icons[i] != null) foreach (var image in icons[i])
            {
                // Keep frames, number badges and decoration authored by the designer.
                string n = image.name.ToLowerInvariant();
                if (n == "background" || n == "fillbg" || n == "fill" || n == "backimage" || n == "sliderimage" || n == "icon")
                    image.sprite = magicTable.GetIcon(magic);
            }
            if (costText[i] != null) costText[i].SetText("0 MP");
        }
        UpdateSelection();
    }
    private void UpdateSelection()
    {
        if (owner != null) owner.SelectPracticeStaff(slots[selected].magic);
        for (int i = 0; i < 2; i++) if (selectedFx[i] != null)
        {
            selectedFx[i].SetActive(i == selected);
            if (i == selected) foreach (var p in selectedFx[i].GetComponentsInChildren<ParticleSystem>(true)) p.Play();
        }
    }
    private Vector3 Origin => castRoot.position;
    private void LateUpdate()
    {
        bool active = RobbyPlayerCamera.CanUsePractice && target != null && magicTable != null &&
            PlayerRobby.LocalPlayer != null && PlayerRobby.LocalPlayer.PracticeMagicRoot != null;
        if (visible != active) { visible = active; if (canvas != null) canvas.enabled = active; if (!active) Cancel(); }
        if (canvas != null && !active && canvas.enabled) canvas.enabled = false;
        TickShots(); TickBursts();
        TickOrbits();
        if (!active) return;
        if (owner != PlayerRobby.LocalPlayer)
        {
            owner = PlayerRobby.LocalPlayer;
            castRoot = owner.PracticeMagicRoot;
            RefreshLoadout();
        }
        if (pendingRelease && Time.time >= pendingReadyAt && owner.PracticeRaiseRemaining <= .00001f)
        {
            pendingRelease = false;
            if (pendingBlade)
            {
                Fire(pendingStats, Origin, AimDirection(pendingStats), false);
                if (--bladeLeft <= 0) ClearBlade();
            }
            else ExecuteCast(pendingStats);
            owner.FinishPracticeRelease();
        }
        for (int i = 0; i < 2; i++) if (skillSliders[i] != null)
        {
            float left = Mathf.Max(0, readyAt[i] - Time.time);
            float value = slots[i].cooldownSeconds > 0 ? 1 - left / slots[i].cooldownSeconds : 1;
            if (!Mathf.Approximately(skillSliders[i].value, value)) skillSliders[i].SetValueWithoutNotify(value);
            int seconds = Mathf.CeilToInt(left);
            if (seconds != lastSeconds[i] && cooldownText[i] != null)
            { cooldownText[i].SetText(seconds > 0 ? "{0}" : "", seconds); lastSeconds[i] = seconds; }
        }
        if (Input.GetKeyDown(KeyCode.Alpha1)) Select(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) Select(1);
        var stats = slots[selected];
        bool lockable = stats.requiresTarget && CanLock(stats);
        ShowLock(held && lockable, stats.lockChargeSeconds > 0 ? Mathf.Clamp01(charge / stats.lockChargeSeconds) : 1);
        if (bladeLeft > 0 && Time.time >= bladeUntil) ClearBlade();
        if (Input.GetMouseButtonDown(0))
        {
            if (pendingRelease) return;
            TraceCast($"입력: {stats.magic}, 슬롯={selected + 1}, 남은 쿨타임={Mathf.Max(0, readyAt[selected] - Time.time):F2}");
            if (bladeLeft > 0 && Time.time >= nextBlade)
            {
                QueueRelease(stats, true);
                nextBlade = Time.time + Mathf.Max(.1f, stats.bladeShotInterval);
                return;
            }
            if (Time.time < readyAt[selected] || stats.magic == MagicType.None)
            { TraceCast("발사 거절: 쿨타임 중이거나 장착 마법 없음"); return; }
            held = true; charge = 0; acquiredLock = false;
            owner.SetPracticeCasting(true);
            if (stats.effect == MagicEffectKind.BladeVolley)
            {
                Cast(stats); held = false;
            }
            else if (!stats.requiresTarget && !stats.IsChanneled && stats.effect != MagicEffectKind.DelayedArea)
            { Cast(stats); held = false; }
        }
        if (held && Input.GetMouseButton(0))
        {
            if (!stats.requiresTarget || lockable) charge += Time.deltaTime;
            else { charge = 0; acquiredLock = false; }
            if (stats.requiresTarget && lockable && charge >= stats.lockChargeSeconds && !acquiredLock)
            { acquiredLock = true; TraceCast("록온 완료: 마우스를 놓으면 발사"); }
            if (stats.IsChanneled && owner.PracticeRaiseRemaining <= .00001f)
            {
                if (channel == null) channel = CombatMagicVisual.Beam(stats.magic, stats.beamVfxPrefab, stats.beamMaterial);
                Vector3 end = AimEnd(stats);
                channel.SetEndpoints(Origin, end);
                if (Time.time >= nextChannel)
                { HitSegment(Origin, end, stats.hitscanRadius, stats); nextChannel = Time.time + Mathf.Max(.05f, stats.channelTickSeconds); }
            }
        }
        if (held && Input.GetMouseButtonUp(0))
        {
            if (stats.IsChanneled) { StopChannel(); BeginCooldown(stats); }
            // The HUD and charging input already established this lock. Do not
            // reacquire from the shifted camera / animated muzzle on release:
            // this used to silently cancel a visibly completed lock.
            else if (!stats.requiresTarget || (acquiredLock && target.gameObject.activeInHierarchy)) Cast(stats);
            else TraceCast($"발사 취소: 록온 미완료 ({charge:F2}/{stats.lockChargeSeconds:F2}초), 대상 검사={lockable}");
            held = false; charge = 0; if (!pendingRelease) owner.SetPracticeCasting(false);
        }
    }
    private void Select(int slot) { if (selected == slot) return; Cancel(); selected = slot; UpdateSelection(); if (owner != null) owner.PlayPracticeEquip(); }
    private void Cancel() { pendingRelease = false; held = false; charge = 0; acquiredLock = false; StopChannel(); ClearBlade(); HideLock(); if (owner != null) owner.SetPracticeCasting(false); }
    private void ClearBlade() { bladeLeft = 0; MagicVfxPool.Release(bladeView); bladeView = null; }
    private void StopChannel() { if (channel != null) Destroy(channel.gameObject); channel = null; }
    private void BeginCooldown(MagicStatEntry stats) => readyAt[selected] = Time.time + stats.cooldownSeconds;
    private bool CanLock(MagicStatEntry stats)
    {
        var camera = RobbyPlayerCamera.PracticeCamera;
        if (camera == null || !target.gameObject.activeInHierarchy) return false;
        Vector3 delta = target.AimPoint - RobbyPlayerCamera.TargetingPosition;
        return delta.magnitude <= stats.range && Vector3.Angle(AimRay(camera).direction, delta) <= lockCone &&
            !Blocked(Origin, target.AimPoint, out _);
    }
    private Vector3 AimEnd(MagicStatEntry stats)
    {
        if (stats.UsesAutoTarget && CanLock(stats)) return target.AimPoint;
        var c = RobbyPlayerCamera.PracticeCamera;
        Ray ray = AimRay(c);
        Vector3 end = ray.GetPoint(Mathf.Max(1, stats.range));
        if (Blocked(ray.origin, end, out Vector3 point)) end = point;
        // Intersect the dummy's target sphere along the aim ray; do not snap an
        // unaimed spell sideways to its centre. Other players are never targets.
        Vector3 toward = target.AimPoint - ray.origin;
        float along = Vector3.Dot(toward, ray.direction);
        float perpendicularSquared = toward.sqrMagnitude - along * along;
        float radiusSquared = target.Radius * target.Radius;
        if (along > 0 && perpendicularSquared <= radiusSquared)
        {
            float contact = Mathf.Max(0, along - Mathf.Sqrt(Mathf.Max(0, radiusSquared - perpendicularSquared)));
            if (contact < Vector3.Distance(ray.origin, end)) end = ray.GetPoint(contact);
        }
        return end;
    }
    private Ray AimRay(Camera camera)
    {
        if (aimReticle == null) return camera.ViewportPointToRay(new Vector3(.5f, .5f));
        Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCamera, aimReticle.TransformPoint(aimReticle.rect.center));
        return camera.ScreenPointToRay(screen);
    }
    private Vector3 AimDirection(MagicStatEntry stats) => (AimEnd(stats) - Origin).normalized;
    private void Cast(MagicStatEntry stats)
    {
        BeginCooldown(stats);
        QueueRelease(stats, false);
    }
    private void QueueRelease(MagicStatEntry stats, bool blade)
    {
        owner.PreparePracticeRelease();
        pendingStats = stats; pendingBlade = blade; pendingRelease = true;
        pendingCharge = charge;
        pendingReadyAt = Time.time + Mathf.Max(0, stats.castSeconds);
    }
    private void ExecuteCast(MagicStatEntry stats)
    {
        Vector3 origin = Origin, direction = AimDirection(stats);
        Play(stats.castVfxPrefab, origin, Quaternion.LookRotation(direction), stats.vfxLifetime);
        BattleAudio.PlayMagic(stats.magic, MagicSound.Cast, origin);
        switch (stats.effect)
        {
            case MagicEffectKind.BladeVolley:
                bladeLeft = Mathf.Max(1, stats.bladeCharges); bladeUntil = Time.time + Mathf.Max(.1f, stats.bladeAbilityDuration);
                bladeView = MagicVfxPool.Rent(stats.bladeWeaponVfxPrefab, origin, owner.transform.rotation, castRoot);
                if (bladeView != null) { bladeView.transform.localPosition = stats.bladeWeaponOffset; bladeView.transform.localRotation = Quaternion.Euler(stats.bladeWeaponEuler); }
                break;
            case MagicEffectKind.MagicStorm:
                bursts.Add(new Burst { stats = stats, origin = origin, direction = direction,
                    remaining = Mathf.Max(1, stats.stormShotCount), next = Time.time,
                    interval = Mathf.Max(.01f, stats.stormDuration / Mathf.Max(1, stats.stormShotCount)) }); break;
            case MagicEffectKind.BladeRain:
                int circles = Mathf.Clamp(Mathf.FloorToInt((stats.apCost + Mathf.Max(0, pendingCharge - stats.lockChargeSeconds) * stats.rainManaPerSecond) / Mathf.Max(1, stats.rainManaPerCircle)), 2, Mathf.Max(2, stats.rainMaxCircles));
                for (int i = 0; i < circles; i++)
                {
                    Vector3 p = origin - owner.transform.forward + owner.transform.right * (i % 2 == 0 ? -1 : 1) * (1 + i / 2 * .6f) + Vector3.up * (i / 2 * .5f);
                    Play(stats.rainCircleVfxPrefab, p, owner.transform.rotation, 10 * Mathf.Max(.01f, stats.rainBladeInterval) + .5f);
                    bursts.Add(new Burst { stats = stats, origin = p, direction = (target.AimPoint - p).normalized,
                        remaining = 10, next = Time.time, interval = Mathf.Max(.01f, stats.rainBladeInterval), guided = true });
                }
                break;
            case MagicEffectKind.DelayedArea:
                Vector3 center = AimEnd(stats);
                var warning = MagicAreaVisual.Create(stats.areaWarningVfxPrefab, "Lobby thunder warning");
                warning.SetArea(center, stats.radius, new Color(1, 0, 0, .3f), 0);
                Destroy(warning.gameObject, Mathf.Max(.1f, stats.strikeDelay));
                bursts.Add(new Burst { stats = stats, origin = center, remaining = 1, next = Time.time + Mathf.Max(.1f, stats.strikeDelay) }); break;
            case MagicEffectKind.MeowFestival:
                for (int i = 0; i < 3; i++) orbits.Add(new Orbit { view = MagicVfxPool.Rent(festivalPrefab, origin, Quaternion.identity),
                    expires = Time.time + stats.effectDuration, phase = i * Mathf.PI * 2 / 3 }); break;
            case MagicEffectKind.Healing:
                CombatTransientEffect.PlayPulse(origin, Color.green, 1.5f, .5f); break;
            case MagicEffectKind.Flare:
                Play(stats.utilityVfxPrefab, origin, Quaternion.LookRotation(-owner.transform.forward), stats.effectDuration);
                CombatTransientEffect.PlayPulse(origin - owner.transform.forward, new Color(1, .4f, .05f), 2f, .5f); break;
            case MagicEffectKind.Smoke:
                var smoke = UtilityMagicVisual.CreateSmoke(origin, stats.radius, stats.utilityVfxPrefab);
                MagicVfxPool.Release(smoke, Mathf.Max(.5f, stats.effectDuration)); break;
            case MagicEffectKind.TimeField:
                Play(stats.timeFieldVfxPrefab, origin, Quaternion.identity, Mathf.Max(.5f, stats.effectDuration)); break;
            case MagicEffectKind.PursuitSummon: case MagicEffectKind.Mine:
                Vector3 summon = origin + owner.transform.forward * Mathf.Max(1, stats.placementDistance);
                Play(stats.summonVfxPrefab != null ? stats.summonVfxPrefab : festivalPrefab, summon, owner.transform.rotation, stats.effectDuration);
                bursts.Add(new Burst { stats = stats, origin = summon, direction = (target.AimPoint - summon).normalized,
                    remaining = Mathf.Clamp(Mathf.CeilToInt(stats.effectDuration / Mathf.Max(.1f, stats.summonAttackInterval)), 1, 120),
                    next = Time.time + stats.activationDelay, interval = Mathf.Max(.1f, stats.summonAttackInterval), guided = true }); break;
            default: Fire(stats, origin, direction, stats.requiresTarget); break;
        }
    }
    private void TickBursts()
    {
        for (int i = bursts.Count - 1; i >= 0; i--)
        {
            var b = bursts[i]; if (Time.time < b.next) continue;
            if (b.stats.effect == MagicEffectKind.DelayedArea)
            {
                MagicAreaVisual.ThunderBurst(b.origin, b.stats);
                if (target != null && Vector3.Distance(b.origin, target.AimPoint) <= b.stats.radius + target.Radius) Impact(b.stats, b.origin);
            }
            else
            {
                if (b.stats.effect == MagicEffectKind.MagicStorm && owner != null && RobbyPlayerCamera.PracticeCamera != null)
                { b.origin = Origin; b.direction = Quaternion.Euler(Random.Range(-b.stats.stormSpreadAngle, b.stats.stormSpreadAngle), Random.Range(-b.stats.stormSpreadAngle, b.stats.stormSpreadAngle), 0) * AimDirection(b.stats); }
                Fire(b.stats, b.origin, b.direction, b.guided);
            }
            if (--b.remaining <= 0) bursts.RemoveAt(i);
            else { b.next = Time.time + b.interval; bursts[i] = b; }
        }
    }
    private void Fire(MagicStatEntry stats, Vector3 origin, Vector3 direction, bool guided)
    {
        if (direction.sqrMagnitude < .001f) { TraceCast("발사 거절: 에임 방향 길이가 0"); return; }
        if (stats.projectileSpeed <= 0)
        {
            TraceCast($"{stats.magic}: 즉발 광선 시전 (데이터의 projectileSpeed={stats.projectileSpeed})");
            Vector3 end = origin + direction * Mathf.Max(1, stats.range);
            if (Blocked(origin, end, out Vector3 p)) end = p;
            // End the visual on the dummy instead of extending through it to the far distance.
            Vector3 offset = target.AimPoint - origin;
            float along = Vector3.Dot(offset, direction);
            float radius = target.Radius + Mathf.Max(0, stats.hitscanRadius);
            if (along > 0 && along < Vector3.Distance(origin, end) &&
                (offset - direction * along).sqrMagnitude <= radius * radius)
                end = origin + direction * along;
            if (!instantBeams.TryGetValue(stats.magic, out var beam) || beam == null)
            {
                var root = new GameObject("Lobby " + stats.magic + " instant beam (reused)");
                beam = root.AddComponent<RobbyInstantMagicVisual>();
                beam.Initialize(stats); instantBeams[stats.magic] = beam;
            }
            beam.Show(origin, end, instantBeamSeconds, instantBeamWidth);
            HitSegment(origin, end, stats.hitscanRadius, stats); return;
        }
        if (shots.Count >= 256) { TraceCast("발사 거절: 동시 투사체 상한 256"); return; }
        var view = MagicVfxPool.Rent(stats.projectileVfxPrefab != null ? stats.projectileVfxPrefab : fallbackProjectile, origin, Quaternion.LookRotation(direction));
        if (view == null) view = MagicVfxPool.Rent(fallbackProjectile, origin, Quaternion.LookRotation(direction));
        shots.Add(new Shot { view = view, position = origin, direction = direction, guided = guided,
            expires = Time.time + Mathf.Max(.1f, stats.projectileLifetime), stats = stats });
        if (traceCasts && Time.unscaledTime >= nextProjectileTrace)
        {
            nextProjectileTrace = Time.unscaledTime + .5f; // Do not flood the console for a volley.
            TraceCast($"투사체 생성: {stats.magic}, 시작={origin}, 속도={stats.projectileSpeed}, 수명={stats.projectileLifetime}, 외형={(view != null ? view.name : "없음")}");
        }
    }
    private void TickShots()
    {
        for (int i = shots.Count - 1; i >= 0; i--)
        {
            var shot = shots[i];
            if (Time.time >= shot.expires || target == null) { MagicVfxPool.Release(shot.view); shots.RemoveAt(i); continue; }
            if (shot.guided) shot.direction = Vector3.RotateTowards(shot.direction, (target.AimPoint - shot.position).normalized,
                shot.stats.projectileTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0);
            Vector3 end = shot.position + shot.direction * shot.stats.projectileSpeed * Time.deltaTime;
            bool wall = Blocked(shot.position, end, out Vector3 wallPoint);
            if (wall) end = wallPoint;
            bool hit = HitSegment(shot.position, end, shot.stats.projectileRadius, shot.stats);
            if (wall || hit) { MagicVfxPool.Release(shot.view); shots.RemoveAt(i); continue; }
            shot.position = end;
            if (shot.view != null) shot.view.transform.SetPositionAndRotation(end, Quaternion.LookRotation(shot.direction));
            shots[i] = shot;
        }
    }
    private bool HitSegment(Vector3 start, Vector3 end, float radius, MagicStatEntry stats)
    {
        if (target == null || !target.gameObject.activeInHierarchy || stats.TargetsAllies) return false;
        Vector3 delta = end - start;
        float t = delta.sqrMagnitude > .00001f ? Mathf.Clamp01(Vector3.Dot(target.AimPoint - start, delta) / delta.sqrMagnitude) : 0;
        if (Vector3.Distance(start + delta * t, target.AimPoint) > target.Radius + radius) return false;
        Impact(stats, start); return true;
    }
    private void Impact(MagicStatEntry stats, Vector3 from)
    {
        target.Hit(target.AimPoint - from);
        CombatPresentation.ShowImpact(target.AimPoint, stats.magic);
    }
    private bool Blocked(Vector3 start, Vector3 end, out Vector3 point)
    {
        point = end; Vector3 delta = end - start; float distance = delta.magnitude;
        if (distance < .0001f) return false;
        int count = Physics.RaycastNonAlloc(start, delta / distance, hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        bool blocked = false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.transform.IsChildOf(target.transform)) continue;
            bool avatar = false;
            foreach (var player in PlayerRobby.ActivePlayers)
                if (player != null && hit.transform.IsChildOf(player.transform)) { avatar = true; break; }
            if (avatar || hit.distance >= distance) continue;
            distance = hit.distance; point = hit.point; blocked = true;
        }
        return blocked;
    }
    private static void Play(GameObject prefab, Vector3 position, Quaternion rotation, float seconds)
    {
        var view = MagicVfxPool.Rent(prefab, position, rotation);
        if (view != null) MagicVfxPool.Release(view, Mathf.Max(.1f, seconds));
    }
    private void TickOrbits()
    {
        for (int i = orbits.Count - 1; i >= 0; i--)
        {
            var orbit = orbits[i];
            if (owner == null || Time.time >= orbit.expires) { MagicVfxPool.Release(orbit.view); orbits.RemoveAt(i); continue; }
            if (orbit.view == null) continue;
            float angle = Time.time * 2 + orbit.phase;
            Vector3 offset = new Vector3(Mathf.Cos(angle), .2f, Mathf.Sin(angle));
            orbit.view.transform.SetPositionAndRotation(owner.transform.position + Vector3.up + offset,
                Quaternion.LookRotation(new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle))));
        }
    }
    private void OnDestroy()
    {
        foreach (var beam in instantBeams.Values) if (beam != null) Destroy(beam.gameObject);
        if (fallbackMaterial != null) Destroy(fallbackMaterial);
    }
    private void HideLock()
    {
        DisplayedLockProgress = 0;
        if (lockChargeMarker != null) lockChargeMarker.gameObject.SetActive(false);
        if (lockCompleteMarker != null) lockCompleteMarker.gameObject.SetActive(false);
    }
    private void TraceCast(string message)
    {
        if (traceCasts) Debug.Log("[Robby/Magic] " + message, this);
    }
    private void ShowLock(bool show, float progress)
    {
        if (!show || canvas == null) { HideLock(); return; }
        Vector3 screen = RobbyPlayerCamera.PracticeCamera.WorldToScreenPoint(target.AimPoint);
        if (screen.z <= 0) { HideLock(); return; }
        bool completed = progress >= 1;
        DisplayedLockProgress = progress;
        Image image = completed ? lockCompleteMarker : lockChargeMarker;
        if (lockChargeMarker != null) lockChargeMarker.gameObject.SetActive(!completed);
        if (lockCompleteMarker != null) lockCompleteMarker.gameObject.SetActive(completed);
        if (image == null) return;
        RectTransform parent = image.rectTransform.parent as RectTransform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out Vector2 point);
        image.rectTransform.anchoredPosition = point;
        image.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(lockChargeStartSize, lockChargeEndSize, progress);
        Color color = completed ? lockCompleteColor : lockChargeColor;
        if (!completed) color.a *= Mathf.Lerp(lockChargeStartAlpha, lockChargeEndAlpha, progress);
        image.color = color;
    }
}
