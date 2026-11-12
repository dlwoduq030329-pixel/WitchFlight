using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Pure Unity test actor. No Player, NetworkObject, NetworkBehaviour or RPC.
public sealed class MagicBalanceLabActor : MonoBehaviour
{
    public BroomType Broom;
    public HatType Hat;
    public float NowHp, NowAp, Speed;
    public Vector3 Velocity { get; private set; }
    public Vector3 LockAimPoint => transform.position + transform.up * 1.6f;
    public Vector3 CastPoint => LockAimPoint + transform.forward * 1.4f;
    public int DashCharges { get; private set; } = 3;
    public int MaximumDashCharges => broom.DashChargeCapacity;
    public bool IsBoosting => boost > 0f;
    public bool CanManeuver => bound <= 0f;
    public float RechargeProgress => DashCharges < MaximumDashCharges ? Mathf.Clamp01(recharge / RechargeSeconds) : 0;
    private float RechargeSeconds => Mathf.Max(.05f, FlightCombatTable.Default != null ? FlightCombatTable.Default.dashRechargeSeconds : 8);
    private CharacterController controller;
    private BroomStatEntry broom;
    private HatStatEntry hat;
    private float turnRate, boost, recharge, glide, glideStart, slowed, bound, slowFactor = 1;
    private readonly Dictionary<Transform, Transform> visualBones = new();
    private Transform visual;
    private PlayableGraph poseGraph;

    public void GetCollisionCapsule(out Vector3 center, out Vector3 halfAxis, out float radius)
    {
        center = transform.TransformPoint(controller.center);
        radius = controller.radius;
        halfAxis = transform.up * Mathf.Max(0f, controller.height * .5f - radius);
    }

    public void Initialize(GameObject visualSource)
    {
        controller = gameObject.AddComponent<CharacterController>();
        controller.radius = .44f; controller.height = 2.4f; controller.center = Vector3.up * 1.2f;
        controller.minMoveDistance = 0;
        // Read the asset, copying only transforms and renderers. Never Instantiate the
        // combat prefab: even disabling its scripts would still permit their Awake calls.
        visual = CopyTransform(visualSource.transform, transform);
        visual.gameObject.SetActive(true);
        visual.localPosition = Vector3.zero;
        visual.localScale *= 2f;
        foreach (var source in visualSource.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = visualBones[source.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
            mesh.sharedMesh = source.sharedMesh; mesh.sharedMaterials = source.sharedMaterials;
            mesh.bones = System.Array.ConvertAll(source.bones, bone => bone != null && visualBones.TryGetValue(bone, out var target) ? target : null);
            mesh.rootBone = source.rootBone != null && visualBones.TryGetValue(source.rootBone, out var root) ? root : visual;
            mesh.localBounds = source.localBounds; mesh.updateWhenOffscreen = true; mesh.enabled = source.enabled;
            if (source.sharedMesh != null)
                for (int i = 0; i < source.sharedMesh.blendShapeCount; i++) mesh.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i));
        }
        foreach (var source in visualSource.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = source.GetComponent<MeshRenderer>();
            if (renderer == null) continue;
            var target = visualBones[source.transform].gameObject;
            target.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            var mesh = target.AddComponent<MeshRenderer>(); mesh.sharedMaterials = renderer.sharedMaterials; mesh.enabled = renderer.enabled;
        }
        foreach (var animator in visualSource.GetComponentsInChildren<Animator>(true))
        {
            if (animator.runtimeAnimatorController == null) continue;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                if (clip != null && clip.name == "AS_Broom_SitFly_Arm_Idle")
                {
                    var localAnimator = visualBones[animator.transform].gameObject.AddComponent<Animator>();
                    localAnimator.avatar = animator.avatar;
                    localAnimator.applyRootMotion = false;
                    // Sample a native humanoid pose without a gameplay controller or its behaviours.
                    poseGraph = PlayableGraph.Create("Offline flight pose");
                    poseGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var output = AnimationPlayableOutput.Create(poseGraph, "Pose", localAnimator);
                    output.SetSourcePlayable(AnimationClipPlayable.Create(poseGraph, clip));
                    poseGraph.Evaluate(0);
                    return;
                }
        }
    }

    private Transform CopyTransform(Transform source, Transform parent)
    {
        var copy = new GameObject(source.name).transform;
        copy.SetParent(parent, false); copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation; copy.localScale = source.localScale;
        visualBones.Add(source, copy);
        for (int i = 0; i < source.childCount; i++) CopyTransform(source.GetChild(i), copy);
        copy.gameObject.SetActive(source.gameObject.activeSelf);
        return copy;
    }

    public void RefreshBalanceLabEquipment(EquipmentStatTable table, bool fill = false)
    {
        float hpRatio = NowHp / Mathf.Max(1, broom.maxHp), mpRatio = NowAp / Mathf.Max(1, hat.maxAp);
        broom = table.GetBroomStats(Broom); hat = table.GetHatStats(Hat);
        DashCharges = fill ? MaximumDashCharges : Mathf.Min(DashCharges, MaximumDashCharges);
        if (DashCharges >= MaximumDashCharges) recharge = 0;
        NowHp = fill ? broom.maxHp : Mathf.Max(1, broom.maxHp * hpRatio);
        NowAp = fill ? hat.maxAp : hat.maxAp * mpRatio;
        foreach (var pair in visualBones)
        {
            string name = pair.Key.name;
            if (name == "StandardBroom") pair.Value.gameObject.SetActive(Broom == BroomType.Standard);
            else if (name == "SlowBroom") pair.Value.gameObject.SetActive(Broom == BroomType.Slow);
            else if (name == "FastBroom") pair.Value.gameObject.SetActive(Broom == BroomType.Speed);
            else if (name == "RobotCleaning") pair.Value.gameObject.SetActive(Broom == BroomType.Robot);
            else if (name.Equals("Cleaner", System.StringComparison.OrdinalIgnoreCase) || name.Equals("CleanerWand", System.StringComparison.OrdinalIgnoreCase)) pair.Value.gameObject.SetActive(Broom == BroomType.Cleaner);
            else if (name == "StandardHat") pair.Value.gameObject.SetActive(Hat == HatType.Classic);
            else if (name == "SortingHat") pair.Value.gameObject.SetActive(Hat == HatType.Twisted);
            else if (name == "AsHat") pair.Value.gameObject.SetActive(Hat == HatType.Elemental);
        }
    }

    public void ResetPose(Vector3 position, Quaternion rotation)
    {
        controller.enabled = false; transform.SetPositionAndRotation(position, rotation); controller.enabled = true;
        Speed = turnRate = boost = recharge = glide = slowed = bound = 0; Velocity = Vector3.zero; DashCharges = MaximumDashCharges;
    }

    public void Simulate(Quaternion desired, bool stationary, bool braking, bool dash, bool infiniteMana, float dt)
    {
        if (DashCharges < MaximumDashCharges) { recharge += dt; if (recharge >= RechargeSeconds) { recharge -= RechargeSeconds; DashCharges++; } }
        if (DashCharges == MaximumDashCharges) recharge = 0;
        boost = Mathf.Max(0, boost - dt); slowed = Mathf.Max(0, slowed - dt); bound = Mathf.Max(0, bound - dt);
        NowAp = infiniteMana ? hat.maxAp : Mathf.Min(hat.maxAp, NowAp + hat.apRecoveryPerSecond * dt);
        if (dash && !braking && boost <= 0 && DashCharges > 0 && bound <= 0) { DashCharges--; boost = 2; }
        float multiplier = slowed > 0 ? slowFactor : 1;
        float targetRate = Quaternion.Angle(transform.rotation, desired) > .01f ? broom.turnSpeed * multiplier : 0;
        float response = targetRate > turnRate ? broom.turnAcceleration : broom.turnReturnSpeed;
        turnRate = Mathf.Lerp(turnRate, targetRate, 1 - Mathf.Exp(-Mathf.Max(.01f, response) * dt));
        if (!stationary && bound <= 0) transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnRate * dt);
        if (braking)
        {
            if (glide <= 0) glideStart = Speed;
            boost = 0; glide += dt; Speed = Mathf.Min(Speed, glideStart * (1 - Mathf.Clamp01(glide / 1.5f)));
        }
        else
        {
            glide = 0;
            float target = stationary || bound > 0 ? 0 : broom.maxSpeed * (boost > 0 ? broom.boostMultiplier : 2f / 3) * multiplier;
            float acceleration = target < Speed ? broom.brakeSpeed : broom.speedStageTransitionSpeed * multiplier * (boost > 0 ? 4 : 1);
            Speed = Mathf.MoveTowards(Speed, target, Mathf.Max(.01f, acceleration) * dt);
        }
        Vector3 before = transform.position;
        if (!stationary) controller.Move(transform.forward * Speed * dt);
        else Speed = 0;
        Velocity = (transform.position - before) / Mathf.Max(.0001f, dt);
    }

    public void Hit(MagicStatEntry stats)
    {
        NowHp = Mathf.Max(1, NowHp - stats.damage);
        if (stats.effect == MagicEffectKind.SlowDamage) { slowed = stats.effectDuration; slowFactor = Mathf.Clamp(stats.movementMultiplier, .05f, 1); }
        if (stats.effect == MagicEffectKind.Binding) bound = stats.effectDuration;
    }

    private void OnDestroy() { if (poseGraph.IsValid()) poseGraph.Destroy(); }
}
