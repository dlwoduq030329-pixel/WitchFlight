using Fusion;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public struct RobbyInputData : INetworkInput
{
    public Vector2 move;
    public float yaw;
    public NetworkBool hasHeading;
    public NetworkBool sprint;
}

// Lobby-only movement. Uses its own input type, never the battle Player/input data.
[DisallowMultipleComponent]
public sealed class PlayerRobby : NetworkBehaviour
{
    internal static readonly System.Collections.Generic.List<PlayerRobby> ActivePlayers = new();
    public static PlayerRobby LocalPlayer { get; private set; }
    [Networked] public Vector3 SimulationPosition { get; private set; }
    [Networked] public float SimulationYaw { get; private set; }
    [Networked] private Vector3 SimulationVelocity { get; set; }
    [SerializeField] private Rigidbody movementBody;
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
    [SerializeField, Min(0.01f)] private float animationBlendSeconds = 0.12f;
    [Networked] private NetworkBool IsWalking { get; set; }
    [Networked] private NetworkBool IsRunning { get; set; }
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
        // Absolute view heading survives missing input ticks and rollback. Never
        // derive the next input heading from a physics/Animator presentation pose.
        if (input.hasHeading) SimulationYaw = Mathf.Repeat(input.yaw, 360f);
        Quaternion rotation = Quaternion.Euler(0f, SimulationYaw, 0f);
        Vector2 move = Vector2.ClampMagnitude(input.move, 1f);
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
        animationMixer = AnimationMixerPlayable.Create(animationGraph, 4);
        idlePlayable = AnimationClipPlayable.Create(animationGraph, idleClip);
        walkPlayable = AnimationClipPlayable.Create(animationGraph, walkCompatible ? walkClip : idleClip);
        kickPlayable = AnimationClipPlayable.Create(animationGraph, kickCompatible ? kickClip : idleClip);
        runPlayable = AnimationClipPlayable.Create(animationGraph, runCompatible ? runClip : idleClip);
        animationGraph.Connect(idlePlayable, 0, animationMixer, 0);
        animationGraph.Connect(walkPlayable, 0, animationMixer, 1);
        animationGraph.Connect(kickPlayable, 0, animationMixer, 2);
        animationGraph.Connect(runPlayable, 0, animationMixer, 3);
        animationMixer.SetInputWeight(0, 1f);
        var output = AnimationPlayableOutput.Create(animationGraph, "Lobby pose", avatarAnimator);
        output.SetSourcePlayable(animationMixer);
        animationGraph.Play();
    }

    public override void Render()
    {
        if (!animationGraph.IsValid()) return;
        // Walk's source clip is not marked Loop; wrap this instance without editing the shared asset.
        LoopClip(idlePlayable, idleClip);
        LoopClip(walkPlayable, walkCompatible ? walkClip : idleClip);
        LoopClip(runPlayable, runCompatible ? runClip : idleClip);
        if (kickPlaying && kickPlayable.GetTime() >= kickClip.length) kickPlaying = false;
        float step = Time.deltaTime / Mathf.Max(0.01f, animationBlendSeconds);
        walkWeight = Mathf.MoveTowards(walkWeight, IsWalking && walkCompatible ? 1f : 0f, step);
        kickWeight = Mathf.MoveTowards(kickWeight, kickPlaying ? 1f : 0f, step);
        runWeight = Mathf.MoveTowards(runWeight, IsRunning && runCompatible ? 1f : 0f, step);
        animationMixer.SetInputWeight(0, (1f - walkWeight) * (1f - kickWeight));
        animationMixer.SetInputWeight(1, walkWeight * (1f - runWeight) * (1f - kickWeight));
        animationMixer.SetInputWeight(2, kickWeight);
        animationMixer.SetInputWeight(3, walkWeight * runWeight * (1f - kickWeight));
    }

    private static void LoopClip(AnimationClipPlayable playable, AnimationClip clip)
    {
        if (clip.length > 0f && playable.GetTime() >= clip.length)
            playable.SetTime(playable.GetTime() % clip.length);
    }

    private void StopAnimation()
    {
        if (animationGraph.IsValid()) animationGraph.Destroy();
        kickPlaying = false;
        walkWeight = kickWeight = runWeight = 0f;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
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
