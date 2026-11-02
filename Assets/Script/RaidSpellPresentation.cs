using UnityEngine;

// Scene-owned pools, warmed before combat. Player spell presentation is untouched.
public sealed class RaidSpellPresentation : MonoBehaviour
{
    private readonly CombatMagicVisual[] beams = new CombatMagicVisual[20];
    private readonly float[] beamEnds = new float[20];
    private readonly Transform[] bursts = new Transform[4];
    private readonly MagicVfxLease[] burstLeases = new MagicVfxLease[4];
    private readonly Vector3[] burstPoints = new Vector3[4];
    private readonly float[] burstStarts = new float[4];
    private GiantMeowBoss boss;
    private MagicStatEntry thunder;
    private int beamCursor, castCursor, visionHitCursor, fireHitCursor;
    private readonly MagicVfxLease[] casts = new MagicVfxLease[20], visionHits = new MagicVfxLease[20], fireHits = new MagicVfxLease[64];
    private readonly float[] castEnds = new float[20], visionHitEnds = new float[20], fireHitEnds = new float[64];
    private MagicStatEntry fire;

    public void Initialize(GiantMeowBoss owner, MagicStatEntry thunderStats)
    {
        boss = owner;
        thunder = thunderStats;
        var vision = MagicStatTable.Default.GetStats(MagicType.Vision);
        for (int i = 0; i < beams.Length; i++)
        {
            beams[i] = CombatMagicVisual.Beam(MagicType.Vision, vision.beamVfxPrefab, vision.beamMaterial);
            beams[i].transform.SetParent(transform, false);
            beams[i].gameObject.SetActive(false);
        }
        for (int i = 0; i < bursts.Length; i++)
        {
            var root = new GameObject("Pooled raid thunder burst");
            root.SetActive(false);
            root.transform.SetParent(transform, false);
            bursts[i] = root.transform;
            var effect = MagicVfxPool.Rent(thunder.thunderBurstVfxPrefab, Vector3.zero, Quaternion.identity, root.transform);
            if (effect != null) burstLeases[i] = effect.GetComponent<MagicVfxLease>();
        }
        fire = MagicStatTable.Default.GetStats(MagicType.Fire);
        Warm(casts, vision.castVfxPrefab);
        Warm(visionHits, vision.impactVfxPrefab);
        Warm(fireHits, fire.impactVfxPrefab);
    }
    private void Warm(MagicVfxLease[] pool, GameObject prefab)
    {
        if (prefab == null) return;
        for (int i = 0; i < pool.Length; i++)
        {
            var effect = MagicVfxPool.Rent(prefab, transform.position, Quaternion.identity);
            if (effect == null) continue;
            pool[i] = effect.GetComponent<MagicVfxLease>();
            effect.SetActive(false);
        }
    }
    private static void Play(MagicVfxLease[] pool, float[] ends, ref int cursor, Vector3 point, Quaternion rotation, float seconds)
    {
        int slot = cursor++ % pool.Length;
        var effect = pool[slot];
        if (effect == null) return;
        effect.Restore();
        effect.transform.SetPositionAndRotation(point, rotation);
        effect.gameObject.SetActive(true);
        effect.Restart();
        ends[slot] = Time.time + Mathf.Max(.05f, seconds);
    }
    public void FireImpact(Vector3 point)
    {
        Play(fireHits, fireHitEnds, ref fireHitCursor, point, Quaternion.identity, fire.vfxLifetime);
        BattleAudio.PlayMagic(MagicType.Fire, MagicSound.Impact, point);
    }
    public void Vision(MagicStatEntry stats, Vector3 origin, Vector3 end, Quaternion rotation)
    {
        BattleAudio.PlayMagic(stats.magic, MagicSound.Cast, origin);
        Play(casts, castEnds, ref castCursor, origin, rotation, stats.vfxLifetime);
        int slot = beamCursor++ % beams.Length;
        beams[slot].SetEndpoints(origin, end);
        beams[slot].gameObject.SetActive(true);
        beamEnds[slot] = Time.time + .2f;
        Play(visionHits, visionHitEnds, ref visionHitCursor, end, Quaternion.identity, stats.vfxLifetime);
        BattleAudio.PlayMagic(stats.magic, MagicSound.Impact, end);
    }
    public void Thunder(int slot, Vector3 point)
    {
        burstPoints[slot] = point;
        burstStarts[slot] = Time.time;
        bursts[slot].SetPositionAndRotation(point, Quaternion.identity);
        bursts[slot].localScale = Vector3.one * thunder.radius / Mathf.Max(.01f, thunder.thunderVfxReferenceRadius) * thunder.thunderBurstStartScale;
        if (burstLeases[slot] != null) burstLeases[slot].Restore();
        bursts[slot].gameObject.SetActive(true);
        if (burstLeases[slot] != null) burstLeases[slot].Restart();
        BattleAudio.PlayMagic(MagicType.Thunder, MagicSound.Impact, point);
    }
    private void Update()
    {
        if (boss == null || boss.Object == null || !boss.Object.IsValid) return;
        bool playing = boss.IsTargetable;
        Retire(casts, castEnds, playing);
        Retire(visionHits, visionHitEnds, playing);
        Retire(fireHits, fireHitEnds, playing);
        for (int i = 0; i < beams.Length; i++)
            if (beams[i] != null && beams[i].gameObject.activeSelf && (!playing || Time.time >= beamEnds[i])) beams[i].gameObject.SetActive(false);
        for (int i = 0; i < bursts.Length; i++)
        {
            if (bursts[i] == null || !bursts[i].gameObject.activeSelf) continue;
            float age = Time.time - burstStarts[i];
            if (!playing || age >= thunder.vfxLifetime) { bursts[i].gameObject.SetActive(false); continue; }
            bursts[i].position = burstPoints[i];
            bursts[i].localScale = Vector3.one * thunder.radius / Mathf.Max(.01f, thunder.thunderVfxReferenceRadius) *
                MagicAreaVisual.BurstScale(age, thunder.thunderBurstExpandSeconds, thunder.thunderBurstStartScale);
        }
    }
    private static void Retire(MagicVfxLease[] pool, float[] ends, bool playing)
    {
        for (int i = 0; i < pool.Length; i++)
            if (pool[i] != null && pool[i].gameObject.activeSelf && (!playing || Time.time >= ends[i])) pool[i].gameObject.SetActive(false);
    }
    private static void Release(MagicVfxLease[] pool)
    {
        foreach (var item in pool) if (item != null) MagicVfxPool.Release(item.gameObject);
    }
    private void OnDestroy()
    {
        Release(casts); Release(visionHits); Release(fireHits);
        foreach (var beam in beams) if (beam != null) MagicVfxPool.Release(beam.gameObject);
        foreach (var burst in bursts) if (burst != null) MagicVfxPool.Release(burst.gameObject);
    }
}
