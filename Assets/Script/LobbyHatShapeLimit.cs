using UnityEngine;
using EZhex1991.EZSoftBone;
using System.Collections.Generic;

// Keeps arm contacts from pulling the hat off the head or sharply folding it.
[DefaultExecutionOrder(15200)]
public sealed class LobbyHatShapeLimit : MonoBehaviour
{
    public EZSoftBone solver;
    public Transform[] bones;
    [Range(0f, 40f)] public float maxBend = 12f;
    [Range(0f, .05f)] public float maxDisplacement = .012f;
    [Tooltip("Maximum brim bend allowed specifically for arm contact, in degrees.")]
    [Range(0f, 75f)] public float contactBend = 45f;
    [Range(0f, .06f)] public float contactPadding = .035f;
    private Vector3[] positions;
    private Quaternion[] rotations;
    private LobbyHatSoftBone contact;
    private Vector3[][] surfacePoints;
    private LobbyHatContactSurface weightedSurface;
    private bool surfaceInitialized;
    private Vector3[] flutterAxes;
    private void Awake()
    {
        if (bones == null) { enabled = false; return; }
        positions = new Vector3[bones.Length]; rotations = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null) { positions[i] = bones[i].localPosition; rotations[i] = bones[i].localRotation; }
        contact = GetComponent<LobbyHatSoftBone>();
        flutterAxes = new Vector3[bones.Length];
        if (contact != null && contact.motionRoot != null)
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone == null || bone.parent == null || bone.childCount == 0) continue;
                Vector3 axis = Vector3.Cross(bone.GetChild(0).position - bone.position, contact.motionRoot.up);
                if (axis.sqrMagnitude < .000001f) axis = contact.motionRoot.right;
                flutterAxes[i] = bone.parent.InverseTransformDirection(axis.normalized);
            }
    }
    private void InitializeSurface()
    {
        surfaceInitialized = true;
        if (contact != null && contact.motionRoot != null && contact.hatRenderer is SkinnedMeshRenderer skinned &&
            skinned.sharedMesh != null && skinned.sharedMesh.isReadable && skinned.sharedMesh.name == "LobbyDefaultHatRig")
        {
            weightedSurface = new LobbyHatContactSurface(skinned, contact);
            return;
        }
        CacheSurfacePoints();
    }
    private void CacheSurfacePoints()
    {
        surfacePoints = new Vector3[bones.Length][];
        if (contact == null || !(contact.hatRenderer is SkinnedMeshRenderer renderer)) return;
        // One bake at spawn, never per frame. Native bone collisions alone leave
        // large gaps across a wide hat brim, particularly the three-bone default hat.
        var baked = new Mesh();
        renderer.BakeMesh(baked);
        Vector3[] vertices = baked.vertices;
        int[] triangles = baked.triangles;
        var coverage = new List<Vector3>(vertices);
        // Include face interiors: an arm can cross a large triangle without
        // touching any of its vertices. Generated once, not during simulation.
        for (int t = 0; t < triangles.Length; t += 3)
        {
            Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
            coverage.Add((a + b + c) / 3f);
            coverage.Add((a + b) * .5f);
            coverage.Add((b + c) * .5f);
            coverage.Add((c + a) * .5f);
        }
        Destroy(baked);
        var candidates = new List<Vector3>[bones.Length];
        for (int i = 0; i < bones.Length; i++) candidates[i] = new List<Vector3>();
        foreach (Vector3 vertex in coverage)
        {
            Vector3 world = renderer.transform.TransformPoint(vertex);
            int closest = -1; float best = float.PositiveInfinity;
            for (int i = 0; i < bones.Length; i++)
            {
                Transform b = bones[i];
                if (b == null || b.childCount == 0) continue;
                string name = b.name;
                if (!name.StartsWith("Hat_Brim_") && !name.StartsWith("AsHatBrim_") && !name.StartsWith("SortingHatBone")) continue;
                Vector3 axis = b.GetChild(0).position - b.position;
                float t = axis.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector3.Dot(world - b.position, axis) / axis.sqrMagnitude) : 0f;
                float distance = (world - b.position - axis * t).sqrMagnitude;
                if (distance < best) { best = distance; closest = i; }
            }
            if (closest >= 0) candidates[closest].Add(world);
        }
        // Farthest-point selection spreads samples over the surface instead of
        // concentrating on duplicate UV seam vertices. Limited to 64 per bone.
        for (int i = 0; i < bones.Length; i++)
        {
            var points = candidates[i]; if (points.Count == 0) continue;
            int count = Mathf.Min(64, points.Count);
            var chosen = new Vector3[count]; var distances = new float[points.Count];
            for (int j = 0; j < distances.Length; j++) distances[j] = float.PositiveInfinity;
            int next = 0;
            for (int n = 0; n < count; n++)
            {
                Vector3 selected = points[next]; chosen[n] = bones[i].InverseTransformPoint(selected);
                float farthest = -1f;
                for (int j = 0; j < points.Count; j++)
                {
                    distances[j] = Mathf.Min(distances[j], (points[j] - selected).sqrMagnitude);
                    if (distances[j] > farthest) { farthest = distances[j]; next = j; }
                }
            }
            surfacePoints[i] = chosen;
        }
    }
    private void LateUpdate()
    {
        if (solver == null || !solver.enabled) return;
        // Remote/hidden hats never need the expensive contact sample cache.
        if (!surfaceInitialized) InitializeSurface();
        float boost = contact != null && contact.flightMode ? contact.FlightBoostAmount : 0f;
        float bendLimit = boost > 0f ? Mathf.Min(40f, maxBend * Mathf.Lerp(1f, 1.25f, boost)) : maxBend;
        float displacementLimit = maxDisplacement * Mathf.Lerp(1f, 1.15f, boost);
        for (int i = 0; i < bones.Length; i++)
        {
            Transform bone = bones[i]; if (bone == null) continue;
            bone.localPosition = positions[i] + Vector3.ClampMagnitude(bone.localPosition - positions[i], displacementLimit);
            Quaternion pose = bone.localRotation;
            if (contact != null && contact.flightMode && flutterAxes[i].sqrMagnitude > .1f)
            {
                float wave = Mathf.Sin(Time.time * 9f + i * .65f) * .7f + Mathf.Sin(Time.time * 14f + i * .4f) * .3f;
                float speed = contact.FlightFlutterAmount;
                float moving = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(speed / .15f)) * Mathf.Lerp(contact.flightCruiseFlutterFloor, 1f, speed);
                float angle = wave * (contact.flightBaseFlutterDegrees + contact.flightFlutterDegrees * moving) *
                    Mathf.Lerp(1f, contact.flightBoostFlutterMultiplier, boost);
                pose = Quaternion.AngleAxis(angle, flutterAxes[i]) * pose;
            }
            bone.localRotation = Quaternion.RotateTowards(rotations[i], pose, bendLimit);
        }
        if (contact == null || contact.motionRoot == null) return;
        float padding = contactPadding * contact.ContactScale;
        if (weightedSurface != null)
        {
            weightedSurface.Solve(contactBend, padding);
            return;
        }
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = bones[i]; var samples = surfacePoints[i];
                if (bone == null || samples == null) continue;
                for (int j = 0; j < samples.Length; j++)
                {
                    Vector3 from = bone.TransformPoint(samples[j]), target = from;
                    contact.Collide(ref target, padding);
                    if ((target - from).sqrMagnitude < .000001f) continue;
                    Vector3 a = from - bone.position, b = target - bone.position;
                    if (a.sqrMagnitude < .000001f || b.sqrMagnitude < .000001f) continue;
                    bone.rotation = Quaternion.FromToRotation(a, b) * bone.rotation;
                    // Apply the contact allowance AFTER the ordinary shape limiter.
                    bone.localRotation = Quaternion.RotateTowards(rotations[i], bone.localRotation, Mathf.Max(bendLimit, contactBend));
                }
            }
    }
}
