using Fusion;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public struct RobbyInputData : INetworkInput
{
    public Vector2 move;
    public float yaw;
    public float pitch;
    public NetworkBool hasHeading;
    public NetworkBool sprint;
    public NetworkBool standRequested;
    public NetworkBool interactionCameraReady;
}

// Lobby-only movement. Uses its own input type, never the battle Player/input data.
[DisallowMultipleComponent]
public sealed partial class PlayerRobby : NetworkBehaviour
{
    internal static readonly System.Collections.Generic.List<PlayerRobby> ActivePlayers = new();
    public static PlayerRobby LocalPlayer { get; private set; }
    // Lobby presentation events only: never enter battle damage or projectile code.
    public void SendPracticeEvent(byte kind, MagicType magic, Vector3 origin, Vector3 direction, bool guided = false)
    {
        if (Object != null && Object.IsValid && Object.HasInputAuthority)
            RPC_PracticeEvent(kind, (int)magic, origin, direction, guided);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
    private void RPC_PracticeEvent(byte kind, int magic, Vector3 origin, Vector3 direction, NetworkBool guided)
    {
        if (Object.HasInputAuthority) return; // The sender already presented this event.
        RobbyMagicPractice.Instance?.ReceivePracticeEvent(this, kind, (MagicType)magic, origin, direction, guided);
    }
    [Networked] public Vector3 SimulationPosition { get; private set; }
    [Networked] public float SimulationYaw { get; private set; }
    [Networked] private float LookPitch { get; set; }
    [Networked] private Vector3 SimulationVelocity { get; set; }
    [SerializeField] private Rigidbody movementBody;
    [Header("Lobby practice only (existing staff objects)")]
    [SerializeField] private GameObject[] practiceStaffs;
    [SerializeField] private Transform practiceMagicRoot;
    public Transform PracticeMagicRoot => practiceMagicRoot;
    public void SelectPracticeStaff(MagicType magic)
    {
        if (practiceStaffs == null) return;
        for (int i = 0; i < practiceStaffs.Length; i++)
            if (practiceStaffs[i] != null) practiceStaffs[i].SetActive(false);
        int index = (int)magic;
        if (index >= practiceStaffs.Length) index = 1; // New magic shares the default staff model.
        if (index > 0 && index < practiceStaffs.Length && practiceStaffs[index] != null)
            practiceStaffs[index].SetActive(true);
    }
    private RobbyPhysicsSimulation physicsSimulation;
    private bool physicsRegistered;
    private CapsuleCollider contactCapsule;
    private float predictedKickUntil;
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(0f)] private float runSpeed = 8f;
    [Header("Lobby animation")]
    [SerializeField] private Animator avatarAnimator;
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip walkClip;
    [SerializeField] private AnimationClip runClip;
    [SerializeField] private AnimationClip kickClip;
    [SerializeField] private AnimationClip rightStrafeClip, leftStrafeClip, backwardClip;
    [Header("Copied battle action clips, upper body only")]
    [SerializeField] private AnimationClip practiceEquip, practiceStart, practiceLoop, practiceEnd;
    [SerializeField, Min(.01f)] private float practiceRaiseSeconds = .25f;
    public float PracticeRaiseRemaining => !practiceMixer.IsValid() || practiceAction == 2 ? 0f :
        practiceAction == 1 ? Mathf.Max(0, practiceRaiseSeconds - practiceElapsed) : practiceRaiseSeconds;
    public void PreparePracticeRelease()
    {
        practiceHolding = true;
        if (practiceAction != 1 && practiceAction != 2) SetPracticeAction(1);
    }
    public void FinishPracticeRelease() { practiceHolding = false; SetPracticeAction(3); }
    [SerializeField, Range(0f, 65f)] private float headPitchLimit = 40f;
    private AnimationLayerMixerPlayable practiceLayers;
    private AnimationMixerPlayable practiceMixer;
    private readonly AnimationClipPlayable[] practiceClips = new AnimationClipPlayable[4];
    private AvatarMask practiceMask;
    private int practiceAction = -1;
    private float practiceElapsed, practiceWeight;
    private bool practiceHolding;
    private Transform lookHead;
    private Quaternion headBase;
    private bool headApplied;
    private float localLookPitch, smoothedLookPitch;
    public void PresentLocalLookPitch(float pitch) => localLookPitch = pitch;
    public void SetPracticeCasting(bool holding)
    {
        if (practiceHolding == holding) return;
        practiceHolding = holding;
        SetPracticeAction(holding ? 1 : 3);
    }
    public void PlayPracticeCast() { practiceHolding = false; SetPracticeAction(1); }
    public void PlayPracticeEquip() { practiceHolding = false; SetPracticeAction(0); }
    private void SetPracticeAction(int action)
    {
        if (!practiceMixer.IsValid()) return;
        practiceAction = action; practiceElapsed = 0;
        practiceClips[action].SetTime(0);
        for (int i = 0; i < 4; i++) practiceMixer.SetInputWeight(i, i == action ? 1 : 0);
    }
    [SerializeField, Min(0.01f)] private float animationBlendSeconds = 0.12f;
    [Networked] private NetworkBool IsWalking { get; set; }
    [Networked] private NetworkBool IsRunning { get; set; }
    [Networked] private Vector2 LocalMove { get; set; }
    private readonly AnimationClipPlayable[] directionalPlayables = new AnimationClipPlayable[3];
    private readonly AnimationClip[] directionalClips = new AnimationClip[3];
    private Vector4 directionWeights = new Vector4(1, 0, 0, 0);
    private PlayableGraph animationGraph;
    private AnimationMixerPlayable animationMixer;
    private AnimationClipPlayable idlePlayable, walkPlayable, kickPlayable, runPlayable;
    private float walkWeight, kickWeight, runWeight;
    private bool kickPlaying;
    private bool walkCompatible, kickCompatible, runCompatible;

    public override void FixedUpdateNetwork()
    {
        // Networked state is restored by Fusion before resimulation. Never accumulate
        // movement from an interpolated presentation Transform during rollback.
        if ((!Object.HasStateAuthority && !Object.HasInputAuthority) || movementBody == null) return;
        // These assignments restore the last network state for rollback, not locomotion.
        movementBody.position = SimulationPosition;
        movementBody.rotation = Quaternion.Euler(0f, SimulationYaw, 0f);
        movementBody.linearVelocity = SimulationVelocity;
        movementBody.angularVelocity = Vector3.zero;
        IsWalking = false;
        GetInput(out RobbyInputData input);
        var manager = NetworkGameManager.Instance;
        if (manager == null || !manager.IsWaitingInCodeRoom) input = default;
        if (TickInteraction(input)) return;
        movementBody.useGravity = true;
        // Absolute view heading survives missing input ticks and rollback. Never
        // derive the next input heading from a physics/Animator presentation pose.
        if (input.hasHeading) { SimulationYaw = Mathf.Repeat(input.yaw, 360f); LookPitch = Mathf.Clamp(input.pitch, -70f, 70f); }
        Quaternion rotation = Quaternion.Euler(0f, SimulationYaw, 0f);
        Vector2 move = Vector2.ClampMagnitude(input.move, 1f);
        LocalMove = move;
        IsWalking = move.sqrMagnitude > 0.001f && moveSpeed > 0f;
        Vector3 direction = rotation * new Vector3(move.x, 0f, move.y);
        bool moving = move.sqrMagnitude > 0.001f;
        IsRunning = moving && input.sprint;
        // Only prevent tipping. Yaw belongs to mouse input, not the physics solver.
        movementBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ |
            (moving ? RigidbodyConstraints.None : RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ);
        movementBody.rotation = rotation;
        Vector3 velocity = direction * (IsRunning ? Mathf.Max(moveSpeed, runSpeed) : moveSpeed);
        velocity.y = movementBody.linearVelocity.y;
        movementBody.linearVelocity = velocity;
    }

    internal void CaptureRobbyPhysics()
    {
        if (Object == null || !Object.IsValid || movementBody == null ||
            (!Object.HasStateAuthority && !Object.HasInputAuthority)) return;
        SimulationPosition = movementBody.position;
        // Contacts must not overwrite the input-owned yaw (or undo it after a tick).
        movementBody.rotation = Quaternion.Euler(0f, SimulationYaw, 0f);
        movementBody.angularVelocity = Vector3.zero;
        SimulationVelocity = movementBody.linearVelocity;
        IsWalking = new Vector2(SimulationVelocity.x, SimulationVelocity.z).sqrMagnitude > 0.01f;
    }

    internal void PresentLocalViewHeading(float yaw)
    {
        if (InteractionBusy) return;
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority) return;
        // Presentation only, after Animator/NetworkTransform have evaluated. Position
        // still comes exclusively from Rigidbody physics. The same absolute heading
        // is submitted through RobbyInputData for host validation and prediction.
        // Do not write Networked state here; rollback owns SimulationYaw.
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            SimulationPosition = transform.position;
            SimulationYaw = transform.eulerAngles.y;
        }
        contactCapsule = GetComponent<CapsuleCollider>();
        if (movementBody == null) movementBody = GetComponent<Rigidbody>();
        if (movementBody != null)
        {
            movementBody.isKinematic = !Object.HasStateAuthority && !Object.HasInputAuthority;
            movementBody.useGravity = !movementBody.isKinematic;
            movementBody.interpolation = RigidbodyInterpolation.None;
            movementBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            movementBody.collisionDetectionMode = movementBody.isKinematic
                ? CollisionDetectionMode.Discrete : CollisionDetectionMode.ContinuousDynamic;
            physicsSimulation = Runner.GetComponent<RobbyPhysicsSimulation>();
            if (physicsSimulation == null || !physicsSimulation.enabled)
            {
                physicsSimulation = Runner.gameObject.AddComponent<RobbyPhysicsSimulation>();
                Runner.AddGlobal(physicsSimulation);
            }
            physicsSimulation.Register(this);
            physicsRegistered = true;
        }
        if (!ActivePlayers.Contains(this)) ActivePlayers.Add(this);
        if (Object.HasInputAuthority) LocalPlayer = this;
        StartAnimation();
    }

    // Called only after an authoritative ball contact actually applied a kick.
    public void PlayBallKick()
    {
        if (Object != null && Object.IsValid && Object.HasStateAuthority) RPC_PlayBallKick();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayBallKick()
    {
        if (Object.HasInputAuthority && Time.unscaledTime < predictedKickUntil) return;
        StartKickVisual();
    }

    internal void PredictBallKick()
    {
        if (!Object.HasInputAuthority || Object.HasStateAuthority || !Runner.IsForward ||
            Time.unscaledTime < predictedKickUntil) return;
        predictedKickUntil = Time.unscaledTime + 0.5f;
        StartKickVisual();
    }

    private void StartKickVisual()
    {
        if (!kickCompatible || !kickPlayable.IsValid()) return;
        kickPlayable.SetTime(0);
        kickPlaying = true;
    }

    internal bool TouchesBall(Vector3 center, float ballRadius)
    {
        if (contactCapsule == null || !contactCapsule.enabled) return false;
        Vector3 scale = transform.lossyScale;
        float radius = contactCapsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        Vector3 capsuleCenter = SimulationPosition + Quaternion.Euler(0f, SimulationYaw, 0f) *
            Vector3.Scale(contactCapsule.center, scale);
        float halfSegment = Mathf.Max(0f, contactCapsule.height * Mathf.Abs(scale.y) * 0.5f - radius);
        capsuleCenter.y = Mathf.Clamp(center.y, capsuleCenter.y - halfSegment, capsuleCenter.y + halfSegment);
        float reach = radius + ballRadius;
        return (center - capsuleCenter).sqrMagnitude <= reach * reach;
    }

    private void StartAnimation()
    {
        if (animationGraph.IsValid() || avatarAnimator == null || idleClip == null || walkClip == null || kickClip == null) return;
        // A generic Mixamo-path clip cannot drive this character's humanoid rig.
        // Blending it with a humanoid idle removes the valid body pose and can sink
        // the model below its movement/collider origin. Preserve idle until retargeted.
        walkCompatible = !avatarAnimator.isHuman || walkClip.isHumanMotion;
        kickCompatible = !avatarAnimator.isHuman || kickClip.isHumanMotion;
        runCompatible = runClip != null && (!avatarAnimator.isHuman || runClip.isHumanMotion);
        if (!walkCompatible || !kickCompatible)
            Debug.LogWarning("[PlayerRobby] Walk/Kick must be Humanoid clips retargeted from their source rig. Incompatible clips use Idle to prevent the character sinking.", this);
        avatarAnimator.applyRootMotion = false;
        animationGraph = PlayableGraph.Create("Robby Idle Walk Run Kick");
        animationMixer = AnimationMixerPlayable.Create(animationGraph, 7);
        idlePlayable = AnimationClipPlayable.Create(animationGraph, idleClip);
        walkPlayable = AnimationClipPlayable.Create(animationGraph, walkCompatible ? walkClip : idleClip);
        kickPlayable = AnimationClipPlayable.Create(animationGraph, kickCompatible ? kickClip : idleClip);
        runPlayable = AnimationClipPlayable.Create(animationGraph, runCompatible ? runClip : idleClip);
        animationGraph.Connect(idlePlayable, 0, animationMixer, 0);
        animationGraph.Connect(walkPlayable, 0, animationMixer, 1);
        animationGraph.Connect(kickPlayable, 0, animationMixer, 2);
        animationGraph.Connect(runPlayable, 0, animationMixer, 3);
        directionalClips[0] = rightStrafeClip;
        directionalClips[1] = leftStrafeClip;
        directionalClips[2] = backwardClip;
        for (int i = 0; i < 3; i++)
        {
            var clip = directionalClips[i];
            if (clip == null || (avatarAnimator.isHuman && !clip.isHumanMotion))
                directionalClips[i] = walkCompatible ? walkClip : idleClip;
            directionalPlayables[i] = AnimationClipPlayable.Create(animationGraph, directionalClips[i]);
            animationGraph.Connect(directionalPlayables[i], 0, animationMixer, 4 + i);
        }
        animationMixer.SetInputWeight(0, 1f);
        var output = AnimationPlayableOutput.Create(animationGraph, "Lobby pose", avatarAnimator);
        output.SetSourcePlayable(animationMixer);
        lookHead = avatarAnimator.isHuman ? avatarAnimator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (practiceEquip != null && practiceStart != null && practiceLoop != null && practiceEnd != null)
        {
            practiceMask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) practiceMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            practiceMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            practiceMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            practiceMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            practiceMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            practiceMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            practiceMixer = AnimationMixerPlayable.Create(animationGraph, 4);
            var clips = new[] { practiceEquip, practiceStart, practiceLoop, practiceEnd };
            for (int i = 0; i < 4; i++)
            {
                practiceClips[i] = AnimationClipPlayable.Create(animationGraph, clips[i]);
                if (i != 2) practiceClips[i].SetSpeed(clips[i].length / (i == 1 ? Mathf.Max(.01f, practiceRaiseSeconds) : .25f));
                animationGraph.Connect(practiceClips[i], 0, practiceMixer, i);
            }
            practiceLayers = AnimationLayerMixerPlayable.Create(animationGraph, 2);
            animationGraph.Connect(animationMixer, 0, practiceLayers, 0);
            animationGraph.Connect(practiceMixer, 0, practiceLayers, 1);
            practiceLayers.SetLayerMaskFromAvatarMask(1, practiceMask);
            practiceLayers.SetInputWeight(0, 1);
            output.SetSourcePlayable(practiceLayers);
        }
        BuildInteractionGraph(output);
        animationGraph.Play();
    }

    public override void Render()
    {
        if (!animationGraph.IsValid()) return;
        RenderInteraction();
        // Walk's source clip is not marked Loop; wrap this instance without editing the shared asset.
        LoopClip(idlePlayable, idleClip);
        LoopClip(walkPlayable, walkCompatible ? walkClip : idleClip);
        LoopClip(runPlayable, runCompatible ? runClip : idleClip);
        for (int i = 0; i < 3; i++) LoopClip(directionalPlayables[i], directionalClips[i]);
        if (kickPlaying && kickPlayable.GetTime() >= kickClip.length) kickPlaying = false;
        float step = Time.deltaTime / Mathf.Max(0.01f, animationBlendSeconds);
        walkWeight = Mathf.MoveTowards(walkWeight, IsWalking && walkCompatible ? 1f : 0f, step);
        kickWeight = Mathf.MoveTowards(kickWeight, kickPlaying ? 1f : 0f, step);
        // Blend diagonal walking without rotating the body away from the camera heading.
        Vector4 desired = new Vector4(Mathf.Max(0, LocalMove.y), Mathf.Max(0, LocalMove.x),
            Mathf.Max(0, -LocalMove.x), Mathf.Max(0, -LocalMove.y));
        float sum = desired.x + desired.y + desired.z + desired.w;
        if (sum > .001f) directionWeights = Vector4.Lerp(directionWeights, desired / sum, Mathf.Clamp01(step));
        runWeight = Mathf.MoveTowards(runWeight, IsRunning && runCompatible && LocalMove.y > 0 ? 1f : 0f, step);
        float directionalWeight = walkWeight * (1f - runWeight) * (1f - kickWeight);
        animationMixer.SetInputWeight(0, (1f - walkWeight) * (1f - kickWeight));
        animationMixer.SetInputWeight(1, directionalWeight * directionWeights.x);
        animationMixer.SetInputWeight(2, kickWeight);
        animationMixer.SetInputWeight(3, walkWeight * runWeight * (1f - kickWeight));
        for (int i = 0; i < 3; i++)
        {
            animationMixer.SetInputWeight(4 + i, directionalWeight * directionWeights[i + 1]);
            directionalPlayables[i].SetSpeed(IsRunning ? Mathf.Max(1f, runSpeed / Mathf.Max(.01f, moveSpeed)) : 1f);
        }
        if (practiceLayers.IsValid())
        {
            practiceElapsed += Time.deltaTime;
            if (practiceAction == 2) LoopClip(practiceClips[2], practiceLoop);
            else if (practiceAction >= 0 && practiceElapsed >= (practiceAction == 1 ? practiceRaiseSeconds : .25f))
            {
                if (practiceAction == 1) SetPracticeAction(practiceHolding ? 2 : 3);
                else practiceAction = -1;
            }
            practiceWeight = Mathf.MoveTowards(practiceWeight, practiceAction >= 0 ? 1f : 0f, step);
            practiceLayers.SetInputWeight(1, practiceWeight * (1 - kickWeight));
        }
    }

    private void Update()
    {
        if (headApplied && lookHead != null) lookHead.localRotation = headBase;
        headApplied = false;
    }
    private void LateUpdate()
    {
        ApplyPetReach();
        if (InteractionBusy) return;
        if (lookHead == null || Object == null || !Object.IsValid) return;
        float pitch = Object.HasInputAuthority ? localLookPitch : LookPitch;
        smoothedLookPitch = Mathf.Lerp(smoothedLookPitch, Mathf.Clamp(pitch, -headPitchLimit, headPitchLimit), 1 - Mathf.Exp(-12 * Time.deltaTime));
        headBase = lookHead.localRotation;
        lookHead.rotation = Quaternion.AngleAxis(smoothedLookPitch, transform.right) * lookHead.rotation;
        headApplied = true;
    }

    private static void LoopClip(AnimationClipPlayable playable, AnimationClip clip)
    {
        if (clip.length > 0f && playable.GetTime() >= clip.length)
            playable.SetTime(playable.GetTime() % clip.length);
    }

    private void StopAnimation()
    {
        if (animationGraph.IsValid()) animationGraph.Destroy();
        if (practiceMask != null) Destroy(practiceMask);
        practiceMask = null; practiceAction = -1; practiceHolding = false; practiceWeight = 0;
        if (headApplied && lookHead != null) lookHead.localRotation = headBase;
        headApplied = false;
        kickPlaying = false;
        walkWeight = kickWeight = runWeight = 0f;
        directionWeights = new Vector4(1, 0, 0, 0);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        RobbyMagicPractice.Instance?.RemoveRemoteCaster(GetInstanceID());
        ReleasePhysics();
        ActivePlayers.Remove(this);
        if (LocalPlayer == this) LocalPlayer = null;
        StopAnimation();
    }

    private void OnDestroy()
    {
        ReleasePhysics();
        ActivePlayers.Remove(this);
        if (LocalPlayer == this) LocalPlayer = null;
        StopAnimation();
    }

    private void ReleasePhysics()
    {
        if (!physicsRegistered) return;
        physicsRegistered = false;
        if (movementBody != null) movementBody.isKinematic = true;
        if (physicsSimulation != null) physicsSimulation.Unregister(this);
    }
}
