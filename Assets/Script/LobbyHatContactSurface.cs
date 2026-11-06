using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

// Uses the real linear skin weights, not an assumed nearest-bone surface.
// Constructed once for the readable lobby-only rig; no BakeMesh or allocations in Solve.
internal sealed class LobbyHatContactSurface
{
    private struct Sample { public int a, b, c; }
    private readonly Vector3[] vertices;
    private readonly BoneWeight[] weights;
    private readonly Matrix4x4[] bind, matrices;
    private readonly Transform[] skin;
    private readonly Sample[][] samples;
    private readonly Vector3[] axes;
    private readonly float[] angles;
    private readonly LobbyHatSoftBone contact;
    private readonly Quaternion[] baseRotations;
    private struct PreparedSample { public Vector3 own, other; public float weight; }
    private readonly PreparedSample[] prepared = new PreparedSample[24];
    private static readonly ProfilerMarker SolveMarker = new ProfilerMarker("WitchFlight.HatContact.Solve");

    public LobbyHatContactSurface(SkinnedMeshRenderer renderer, LobbyHatSoftBone contact)
    {
        this.contact = contact;
        var mesh = renderer.sharedMesh;
        vertices = mesh.vertices; weights = mesh.boneWeights; bind = mesh.bindposes; skin = renderer.bones;
        matrices = new Matrix4x4[skin.Length]; samples = new Sample[skin.Length][];
        axes = new Vector3[skin.Length]; angles = new float[skin.Length]; baseRotations = new Quaternion[skin.Length];
        var candidates = new List<Sample>[skin.Length];
        for (int i = 0; i < skin.Length; i++)
        {
            candidates[i] = new List<Sample>();
            if (skin[i] == null || skin[i].childCount == 0) continue;
            Vector3 radial = skin[i].GetChild(0).position - skin[i].position;
            axes[i] = skin[i].parent.InverseTransformDirection(Vector3.Cross(radial, contact.motionRoot.up).normalized);
        }
        for (int i = 0; i < vertices.Length; i++) Add(new Sample { a = i, b = i, c = i }, candidates);
        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
            Add(new Sample { a = triangles[i], b = triangles[i + 1], c = triangles[i + 2] }, candidates);
        for (int bone = 1; bone < skin.Length; bone++)
        {
            var list = candidates[bone];
            int count = Mathf.Min(24, list.Count); if (count == 0) continue;
            var selected = new Sample[count]; var distances = new float[list.Count];
            for (int j = 0; j < distances.Length; j++) distances[j] = float.PositiveInfinity;
            int next = 0;
            for (int n = 0; n < count; n++)
            {
                selected[n] = list[next]; Vector3 p = RestPoint(list[next]); float farthest = -1f;
                for (int j = 0; j < list.Count; j++)
                {
                    distances[j] = Mathf.Min(distances[j], (RestPoint(list[j]) - p).sqrMagnitude);
                    if (distances[j] > farthest) { farthest = distances[j]; next = j; }
                }
            }
            samples[bone] = selected;
        }
    }
    private Vector3 RestPoint(Sample s) => (vertices[s.a] + vertices[s.b] + vertices[s.c]) / 3f;
    private void Add(Sample s, List<Sample>[] candidates)
    {
        // Include a seam in BOTH of its deforming sectors.
        AddWeights(weights[s.a], s, candidates);
        if (s.b != s.a) AddWeights(weights[s.b], s, candidates);
        if (s.c != s.a) AddWeights(weights[s.c], s, candidates);
    }
    private void AddWeights(BoneWeight w, Sample s, List<Sample>[] candidates)
    {
        if (w.boneIndex0 > 0 && w.weight0 > .15f) candidates[w.boneIndex0].Add(s);
        if (w.boneIndex1 > 0 && w.weight1 > .15f) candidates[w.boneIndex1].Add(s);
        if (w.boneIndex2 > 0 && w.weight2 > .15f) candidates[w.boneIndex2].Add(s);
        if (w.boneIndex3 > 0 && w.weight3 > .15f) candidates[w.boneIndex3].Add(s);
    }
    private void Accumulate(int vertex, int movingBone, ref PreparedSample result)
    {
        Vector3 p = vertices[vertex]; BoneWeight w = weights[vertex];
        AccumulateInfluence(p, w.boneIndex0, w.weight0 / 3f, movingBone, ref result);
        AccumulateInfluence(p, w.boneIndex1, w.weight1 / 3f, movingBone, ref result);
        AccumulateInfluence(p, w.boneIndex2, w.weight2 / 3f, movingBone, ref result);
        AccumulateInfluence(p, w.boneIndex3, w.weight3 / 3f, movingBone, ref result);
    }
    private void AccumulateInfluence(Vector3 p, int bone, float weight, int movingBone, ref PreparedSample result)
    {
        if (weight <= 0f) return;
        if (bone == movingBone) { result.own += p * weight; result.weight += weight; }
        else result.other += matrices[bone].MultiplyPoint3x4(p) * weight;
    }
    private void Prepare(int bone)
    {
        var list = samples[bone];
        for (int i = 0; i < list.Length; i++)
        {
            PreparedSample p = default;
            Accumulate(list[i].a, bone, ref p); Accumulate(list[i].b, bone, ref p); Accumulate(list[i].c, bone, ref p);
            prepared[i] = p;
        }
    }
    private float Cost(int bone, float padding)
    {
        float sum = 0;
        Matrix4x4 matrix = matrices[bone];
        Vector3 translation = matrix.GetColumn(3);
        for (int i = 0; i < samples[bone].Length; i++)
        {
            var s = prepared[i];
            Vector3 p = matrix.MultiplyVector(s.own) + translation * s.weight + s.other, target = p;
            contact.Collide(ref target, padding);
            sum += (target - p).sqrMagnitude;
        }
        return sum;
    }
    private void SetAngle(int i, float angle)
    {
        skin[i].localRotation = Quaternion.AngleAxis(angle, axes[i]) * baseRotations[i];
        matrices[i] = skin[i].localToWorldMatrix * bind[i];
    }
    public void Solve(float maxAngle, float padding)
    {
        using var profile = SolveMarker.Auto();
        for (int i = 0; i < skin.Length; i++)
        {
            matrices[i] = skin[i].localToWorldMatrix * bind[i];
            baseRotations[i] = skin[i].localRotation;
        }
        // Start from last frame's fold, relax gently, then resolve current contacts.
        for (int i = 1; i < skin.Length; i++)
        {
            if (samples[i] == null) continue;
            angles[i] = Mathf.MoveTowards(angles[i], 0f, 55f * Time.deltaTime);
            SetAngle(i, angles[i]);
        }
        for (int pass = 0; pass < 2; pass++)
            for (int i = 1; i < skin.Length; i++)
            {
                if (samples[i] == null || axes[i].sqrMagnitude < .1f) continue;
                // Other bones stay fixed throughout the angle search. Cache their
                // exact weighted contribution once instead of skinning 3 vertices
                // (up to 12 matrix products) again for each of the 15 candidates.
                Prepare(i);
                float bestCost = Cost(i, padding), bestAngle = angles[i];
                if (bestCost < .0000001f) continue;
                // Search both folding directions. Radial capsule pushes alone can
                // have zero rotational leverage on a broad horizontal brim.
                for (int step = -7; step <= 7; step++)
                {
                    float angle = maxAngle * step / 7f; SetAngle(i, angle);
                    float cost = Cost(i, padding);
                    if (cost < bestCost - .00000001f ||
                        (Mathf.Abs(cost - bestCost) < .00000001f && Mathf.Abs(angle) < Mathf.Abs(bestAngle)))
                    { bestCost = cost; bestAngle = angle; }
                }
                angles[i] = bestAngle; SetAngle(i, bestAngle);
            }
    }
}
