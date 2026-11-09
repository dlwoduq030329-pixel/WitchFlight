using System.Collections.Generic;
using UnityEngine;
using EZhex1991.EZSoftBone;

// Runs after EZSoftBone. The natural hairstyle remains the forward limit, while
// backward and sideways motion stays free. All chains are cached once.
[DefaultExecutionOrder(15310)]
public sealed class LongHairSwayLimit : MonoBehaviour
{
    [SerializeField] private EZSoftBone solver;
    [SerializeField] private LongHairSoftBone bodyContact;
    [SerializeField] private Transform motionRoot;
    [Header("Travelling wave while moving (model metres)")]
    [SerializeField, Range(0f, .12f)] private float movingSideFlutter = .045f;
    [SerializeField, Range(0f, .1f)] private float movingBackFlutter = .03f;
    [SerializeField, Range(1f, 6f)] private float flutterFrequency = 3.2f;
    [Header("Wind lift while moving (model metres)")]
    [SerializeField, Range(0f, .5f)] private float movingLift = .24f;
    [SerializeField, Range(0f, .2f)] private float movingVerticalFlutter = .08f;
    [Header("Irregular gusts")]
    [SerializeField, Range(0f, 1f)] private float irregularity = .7f;
    [Tooltip("Maximum additional droop below the animated hairstyle, in model metres.")]
    [SerializeField, Range(0f, .1f)] private float maxDownwardOffset = .012f;
    private struct Joint
    {
        public Transform bone, anchor;
        public Vector3 restInAnchor;
        public int child;
        public int chain;
        public float waveWeight, wavePhase;
    }
    private Joint[] joints;
    private Vector3[] positions;
    private Vector2[] chainWind;
    private float noiseSeed;

    private void Awake()
    {
        if (solver == null || motionRoot == null) { enabled = false; return; }
        var list = new List<Joint>();
        chainWind = new Vector2[solver.rootBones.Count];
        noiseSeed = Mathf.Abs(GetInstanceID() % 997) * .137f;
        for (int i = 0; i < solver.rootBones.Count; i++)
        {
            var root = solver.rootBones[i];
            if (root != null) Cache(root, root, 0, i, list);
        }
        joints = list.ToArray();
        positions = new Vector3[joints.Length];
    }
    private int Cache(Transform bone, Transform anchor, int depth, int chain, List<Joint> list)
    {
        int index = -1;
        if (depth > solver.startDepth)
        {
            index = list.Count;
            float length = Vector3.Distance(anchor.position, bone.position) / Mathf.Max(.001f, Mathf.Abs(motionRoot.lossyScale.y));
            list.Add(new Joint { bone = bone, anchor = anchor,
                restInAnchor = anchor.InverseTransformPoint(bone.position), child = -1,
                chain = chain,
                waveWeight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(length / .4f)),
                wavePhase = chain * .7f - length * 8f });
        }
        if (solver.endBones.Contains(bone)) return index;
        for (int i = 0; i < bone.childCount; i++)
        {
            var child = bone.GetChild(i);
            if (!child.gameObject.activeSelf) continue;
            int childIndex = Cache(child, anchor, depth + 1, chain, list);
            if (index >= 0 && bone.childCount == 1)
            { var joint = list[index]; joint.child = childIndex; list[index] = joint; }
        }
        return index;
    }
    private void LateUpdate()
    {
        if (solver == null || !solver.enabled || joints == null) return;
        Vector3 forward = motionRoot.forward;
        Vector3 up = motionRoot.up;
        float amount = bodyContact != null ? bodyContact.MovingFlutterAmount : 0f;
        float phase = bodyContact != null ? bodyContact.FlutterTime * flutterFrequency * Mathf.PI * 2f : 0f;
        float scale = Mathf.Abs(motionRoot.lossyScale.y);
        if (amount > .0001f)
        {
            float time = bodyContact.FlutterTime;
            // Two smooth noise samples per strand, shared by its joints. No per-frame
            // Random calls or per-vertex work; neighbouring joints stay coherent.
            for (int i = 0; i < chainWind.Length; i++)
            {
                float seed = noiseSeed + i * 13.17f;
                float drift = (Mathf.PerlinNoise(seed, time * .8f) * 2f - 1f) * irregularity * 2f;
                float gust = 1f + (Mathf.PerlinNoise(seed + 37f, time * 1.25f) * 2f - 1f) * irregularity * .5f;
                chainWind[i] = new Vector2(phase * (1f + irregularity * .06f * (i % 3 - 1)) + drift, gust);
            }
        }
        for (int i = 0; i < joints.Length; i++)
        {
            var j = joints[i];
            Vector3 rest = j.anchor.TransformPoint(j.restInAnchor);
            Vector3 p = j.bone.position;
            if (amount > .0001f)
            {
                Vector2 wind = chainWind[j.chain];
                float jointPhase = wind.x + j.wavePhase;
                float wave = Mathf.Sin(jointPhase);
                float strength = j.waveWeight * amount * scale;
                // Offset along the strand gives a travelling ripple, not a rigid sway.
                // Backward wave never pulls forward; collision/forward limits run after it.
                p += motionRoot.right * (wave * movingSideFlutter * strength) -
                    forward * ((.5f + .5f * Mathf.Sin(jointPhase + .8f)) * movingBackFlutter * wind.y * strength) +
                    up * (Mathf.Max(0f, movingLift + Mathf.Sin(jointPhase + 1.5f) * movingVerticalFlutter * wind.y) * strength);
            }
            // Alternating projections retain body clearance at the forward limit.
            for (int pass = 0; pass < 8; pass++)
            {
                p -= forward * Mathf.Max(0f, Vector3.Dot(p - rest, forward));
                p += up * Mathf.Max(0f, -maxDownwardOffset * scale - Vector3.Dot(p - rest, up));
                if (bodyContact != null) bodyContact.Collide(ref p, solver.globalRadius);
                if (Vector3.Dot(p - rest, forward) <= .000001f &&
                    Vector3.Dot(p - rest, up) >= -maxDownwardOffset * scale - .000001f) break;
            }
            positions[i] = p;
        }
        for (int i = 0; i < joints.Length; i++)
        {
            var j = joints[i];
            j.bone.position = positions[i];
            if (j.child < 0) continue;
            Vector3 from = joints[j.child].bone.position - positions[i];
            Vector3 to = positions[j.child] - positions[i];
            if (from.sqrMagnitude > .0000001f && to.sqrMagnitude > .0000001f)
                j.bone.rotation = Quaternion.FromToRotation(from, to) * j.bone.rotation;
        }
    }
}
