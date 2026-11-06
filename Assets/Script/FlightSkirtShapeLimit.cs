using UnityEngine;
using EZhex1991.EZSoftBone;

[DefaultExecutionOrder(15300)]
public sealed class FlightSkirtShapeLimit : MonoBehaviour
{
    public EZSoftBone solver;
    public Transform[] chainRoots;
    [Range(0f, 40f)] public float maxJointBend = 32f;
    [Range(0f, .2f)] public float maxModelDisplacement = .13f;
    [Range(0f, 30f)] public float flutterDegrees = 18f;
    [Range(0f, 16f)] public float baseFlutterDegrees = 5f;
    [Tooltip("Minimum moving flutter strength, reached smoothly at low speed so normal cruise is visibly animated.")]
    [Range(0f, 1f)] public float cruiseFlutterFloor = .65f;
    [Range(1f, 2f)] public float boostFlutterMultiplier = 1.5f;
    private LobbySkirtSoftBone wind;
    private struct Joint
    {
        public Transform bone, anchor;
        public Vector3 position, anchorPosition;
        public Quaternion rotation;
        public Vector3 flutterAxis;
        public float phase;
    }
    private Joint[] joints;
    private void Awake()
    {
        var list = new System.Collections.Generic.List<Joint>();
        wind = GetComponent<LobbySkirtSoftBone>();
        if (chainRoots != null)
            foreach (var root in chainRoots)
            {
                if (root == null) continue;
                // Only the configured 001..004 chain, never ribbon branches.
                Transform current = root;
                for (int depth = 0; depth < 4 && current != null; depth++)
                {
                    if (depth >= 2) list.Add(new Joint { bone = current, anchor = root,
                        position = current.localPosition, rotation = current.localRotation,
                        anchorPosition = root.InverseTransformPoint(current.position),
                        flutterAxis = current.parent.InverseTransformDirection(root.right).normalized,
                        phase = list.Count * .7f });
                    current = current.childCount > 0 ? current.GetChild(0) : null;
                }
            }
        joints = list.ToArray();
    }
    private void LateUpdate()
    {
        if (solver == null || !solver.enabled) return;
        float boost = wind != null ? wind.FlightBoostAmount : 0f;
        float amplitude = Mathf.Lerp(1f, boostFlutterMultiplier, boost);
        float bendLimit = Mathf.Min(40f, maxJointBend * Mathf.Lerp(1f, 1.25f, boost));
        float displacementLimit = maxModelDisplacement * Mathf.Lerp(1f, 1.15f, boost);
        foreach (var j in joints)
        {
            float wave = Mathf.Sin(Time.time * 11f + j.phase) * .7f + Mathf.Sin(Time.time * 17f + j.phase) * .3f;
            float speed = wind != null ? wind.FlightFlutterAmount : 0f;
            float movingFlutter = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(speed / .15f)) * Mathf.Lerp(cruiseFlutterFloor, 1f, speed);
            float angle = wave * (baseFlutterDegrees + flutterDegrees * movingFlutter) * amplitude;
            Quaternion pose = Quaternion.AngleAxis(angle, j.flutterAxis) * j.bone.localRotation;
            j.bone.localRotation = Quaternion.RotateTowards(j.rotation, pose, bendLimit);
            j.bone.localPosition = j.position;
            Vector3 world = j.bone.position;
            // The pose limiter must not simply undo the solver's broom response.
            if (wind != null) wind.ResolveBroomContact(ref world, .008f * Mathf.Abs(j.anchor.lossyScale.y));
            Vector3 p = j.anchor.InverseTransformPoint(world);
            j.bone.position = j.anchor.TransformPoint(j.anchorPosition +
                Vector3.ClampMagnitude(p - j.anchorPosition, displacementLimit));
        }
    }
}
