using UnityEngine;

// Authored body/telegraphs and shared player-spell presentation for the raid.
public sealed partial class GiantMeowBossView : MonoBehaviour
{
    [SerializeField] private Transform body, head;
    [SerializeField] private Animator animator;
    [SerializeField] private WitchCatAppearance appearance;
    [SerializeField] private Transform[] gates, corridors, targetWarnings, breathWarnings;
    [SerializeField] private Transform absorbShield, absorbRange, burst;
    [SerializeField] private Transform paperPlane;
    private readonly MagicAreaVisual[] thunderAreas = new MagicAreaVisual[4];
    [SerializeField] private GameObject thunderBurstPrefab;
    private MagicStatEntry thunderStats;
    [SerializeField] private RaidSpellPresentation spellPool;
    private bool thunderWasActive;
    private CombatMagicVisual breathVisual;
    private Renderer[] bodyRenderers;
    private Vector3 fullScale, fullPosition;
    private Quaternion headOriginal;
    private ParticleSystem[][] gateParticles;
    private bool poseApplied;
    private float shownGrowth = -1f;
    private GiantMeowBoss boss;
    private int idleState, castState;
    private float castSeconds, nextCastAnimation;

    public void Initialize(GiantMeowBoss owner)
    {
        boss = owner;
        foreach (var collider in body.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        appearance?.ApplyAccountIndices(1, 1);
        body.localPosition = Vector3.zero;
        body.localScale = Vector3.one;
        body.localRotation = Quaternion.Euler(CatCompanionTable.Default.catEuler);
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            idleState = Animator.StringToHash(CatCompanionTable.Default.GetAnimation(CatAnimation.SittingIdle).stateName);
            var cheer = CatCompanionTable.Default.GetAnimation(CatAnimation.Cheer);
            castState = Animator.StringToHash(cheer.stateName);
            castSeconds = Mathf.Max(.2f, cheer.seconds);
            if (!animator.HasState(0, castState)) castState = idleState;
            animator.Play(idleState, 0, 0);
            animator.Update(0);
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }
        bodyRenderers = body.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = default;
        bool found = false;
        foreach (var renderer in bodyRenderers)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            Bounds world = renderer.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 sign = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                Vector3 point = transform.InverseTransformPoint(world.center + Vector3.Scale(world.extents, sign));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
        float scale = boss.Settings.height / Mathf.Max(.01f, bounds.size.y);
        fullScale = body.localScale * scale;
        fullPosition = -bounds.center * scale;
        body.localScale = fullScale;
        body.localPosition = fullPosition;
        thunderStats = MagicStatTable.Default.GetStats(MagicType.Thunder);
        thunderStats.radius = boss.ThunderRadius;
        thunderStats.thunderBurstVfxPrefab = thunderBurstPrefab;
        spellPool.Initialize(boss, thunderStats);
        breathVisual = CombatMagicVisual.Beam(MagicType.Fire, MagicStatTable.Default.GetStats(MagicType.Fire).projectileVfxPrefab);
        breathVisual.transform.SetParent(transform, false);
        breathVisual.gameObject.SetActive(false);
        for (int i = 0; i < thunderAreas.Length; i++)
        {
            thunderAreas[i] = MagicAreaVisual.Create(thunderStats.areaWarningVfxPrefab, "Raid thunder area");
            thunderAreas[i].transform.SetParent(transform, false);
            thunderAreas[i].gameObject.SetActive(false);
        }
        if (paperPlane != null)
        {
            var mesh = paperPlane.GetComponent<MeshFilter>();
            if (mesh != null && mesh.sharedMesh != null)
            {
                Bounds planeBounds = mesh.sharedMesh.bounds;
                float planeScale = boss.Settings.height * .9f / Mathf.Max(.01f, Mathf.Max(planeBounds.size.x, planeBounds.size.z));
                paperPlane.localScale = Vector3.one * planeScale;
                paperPlane.localPosition = new Vector3(-planeBounds.center.x * planeScale,
                    -boss.Settings.height * .5f - planeBounds.max.y * planeScale, -planeBounds.center.z * planeScale);
            }
            paperPlane.gameObject.SetActive(false);
        }
        gateParticles = new ParticleSystem[gates.Length][];
        for (int i = 0; i < gates.Length; i++)
            gateParticles[i] = gates[i].GetComponentsInChildren<ParticleSystem>(true);
        Present(boss);
    }
    public void PresentFireImpact(Vector3 point) => spellPool.FireImpact(point);

    public void PresentVision(MagicStatEntry stats, Vector3 origin, Vector3 end, Quaternion rotation) =>
        spellPool.Vision(stats, origin, end, rotation);

    public void Present(GiantMeowBoss owner)
    {
        if (boss == null) return;
        var settings = boss.Settings;
        PresentMechanics();
        bool waiting = boss.Phase == BattleStartPhase.WaitingForPlayers;
        float growth = boss.Phase == BattleStartPhase.Intro ? Mathf.SmoothStep(.03f, 1f, boss.StageProgress) : waiting ? .03f : 1f;
        if (body.gameObject.activeSelf == waiting)
        {
            body.gameObject.SetActive(!waiting);
            shownGrowth = -1f;
            if (!waiting && animator != null) animator.Play(idleState, 0, 0);
        }
        if (!Mathf.Approximately(shownGrowth, growth))
        {
            shownGrowth = growth;
            body.localScale = fullScale * growth;
            body.localPosition = fullPosition * growth - Vector3.up * (settings.height * .5f * (1 - growth));
        }
        bool playing = boss.Phase == BattleStartPhase.Playing;
        if (playing && !boss.Exposed && animator != null && Time.time >= nextCastAnimation)
        {
            animator.CrossFadeInFixedTime(castState, .12f, 0, 0f);
            nextCastAnimation = Time.time + castSeconds;
        }
        bool warning = playing && boss.Stage == RaidPatternStage.Warning;
        bool active = playing && boss.Stage == RaidPatternStage.Active;
        bool recovery = playing && boss.Stage == RaidPatternStage.Recovery;
        if (paperPlane != null) paperPlane.gameObject.SetActive(playing && boss.Retreating);
        bool thunder = playing && boss.Pattern == RaidPattern.Thunder;
        for (int i = 0; i < gates.Length; i++)
        {
            var portal = boss.VisionPortals[i];
            bool show = playing && boss.Pattern == RaidPattern.VisionCorridors && portal.State == 1;
            corridors[i].gameObject.SetActive(show);
            if (show)
            {
                gates[i].SetPositionAndRotation(portal.Origin, Quaternion.LookRotation(portal.Direction));
                gates[i].localScale = Vector3.one * boss.VisionRadius * 3;
                Cylinder(corridors[i], portal.Origin, portal.Direction, boss.VisionRange, boss.VisionRadius);
            }
            if (gates[i].gameObject.activeSelf != show)
            {
                gates[i].gameObject.SetActive(show);
                foreach (var particles in gateParticles[i])
                {
                    particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    if (show) particles.Play(false);
                }
            }
        }
        for (int i = 0; i < targetWarnings.Length; i++)
        {
            bool marker = warning && i < boss.WarningCount && (thunder || boss.Pattern == RaidPattern.FireVolley);
            targetWarnings[i].gameObject.SetActive(marker);
            if (marker)
            {
                targetWarnings[i].position = boss.Origins[i];
                targetWarnings[i].localScale = Vector3.one * (thunder ? boss.ThunderRadius * 2 : 5);
            }
            bool strike = active && thunder && i < boss.WarningCount;
            thunderAreas[i].gameObject.SetActive(marker && thunder);
            if (marker && thunder)
                thunderAreas[i].SetArea(boss.Origins[i], boss.ThunderRadius, new Color(1f, 0f, 0f, .3f), boss.StageProgress);
            if (strike && !thunderWasActive)
                spellPool.Thunder(i, boss.Origins[i]);
        }
        thunderWasActive = active && thunder;
        absorbShield.gameObject.SetActive(playing && boss.Absorbing);
        absorbRange.gameObject.SetActive(playing && boss.Absorbing);
        if (boss.Absorbing)
        {
            absorbShield.position = absorbRange.position = transform.position;
            absorbShield.localScale = new Vector3(settings.hitRadius * 2.4f, settings.height * 1.1f, settings.hitRadius * 2.4f);
            absorbRange.localScale = Vector3.one * boss.AbsorbBlastRadius * 2;
        }
        bool blast = recovery && boss.Pattern == RaidPattern.AbsorbBurst && boss.DetonationRadius > 0f && !boss.Exposed && boss.StageProgress < .4f;
        burst.gameObject.SetActive(blast);
        if (blast)
        {
            burst.position = boss.DetonationPoint;
            burst.localScale = Vector3.one * boss.DetonationRadius * 2 * Mathf.Clamp01(boss.StageProgress * 5);
        }
        bool breath = playing && boss.Pattern == RaidPattern.FlameBreath;
        for (int i = 0; i < breathWarnings.Length; i++)
        {
            breathWarnings[i].gameObject.SetActive(breath && warning);
            if (breath && warning)
            {
                float angle = Mathf.Lerp(-settings.breathSweepDegrees * .5f, settings.breathSweepDegrees * .5f, i / (float)(breathWarnings.Length - 1));
                Cylinder(breathWarnings[i], boss.Mouth, Quaternion.AngleAxis(angle, Vector3.up) * boss.Aim, settings.breathLength, settings.breathRadius);
            }
        }
        if (breath && active) breathVisual.SetEndpoints(boss.Mouth, boss.Mouth + boss.BreathDirection * settings.breathLength);
        breathVisual.gameObject.SetActive(breath && active);
    }
    private void OnDestroy()
    {
        if (breathVisual != null) MagicVfxPool.Release(breathVisual.gameObject);
        foreach (var area in thunderAreas)
            if (area != null) MagicVfxPool.Release(area.gameObject);
    }
    private static void Cylinder(Transform visual, Vector3 origin, Vector3 direction, float length, float radius)
    {
        visual.SetPositionAndRotation(origin + direction * length * .5f, Quaternion.FromToRotation(Vector3.up, direction));
        visual.localScale = new Vector3(radius * 2, length * .5f, radius * 2);
    }
    private void Update()
    {
        if (poseApplied && head != null) head.localRotation = headOriginal;
        poseApplied = false;
    }
    private void LateUpdate()
    {
        if (boss == null || boss.Object == null || !boss.Object.IsValid || head == null ||
            boss.Phase != BattleStartPhase.Playing || boss.Pattern != RaidPattern.FlameBreath ||
            boss.Stage != RaidPatternStage.Active) return;
        headOriginal = head.localRotation;
        poseApplied = true;
        head.rotation = Quaternion.FromToRotation(transform.forward, boss.BreathDirection) * head.rotation;
    }
}
