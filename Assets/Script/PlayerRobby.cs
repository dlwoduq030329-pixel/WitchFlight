using Fusion;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public struct RobbyInputData : INetworkInput
{
    public Vector2 move;
    public float yaw;
}

// Lobby-only movement. Uses its own input type, never the battle Player/input data.
[DisallowMultipleComponent]
public sealed class PlayerRobby : NetworkBehaviour
{
    public static PlayerRobby LocalPlayer { get; private set; }
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [Header("Lobby animation")]
    [SerializeField] private Animator avatarAnimator;
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip walkClip;
    [SerializeField] private AnimationClip kickClip;
    [SerializeField, Min(0.01f)] private float animationBlendSeconds = 0.12f;
    [Networked] private NetworkBool IsWalking { get; set; }
    private PlayableGraph animationGraph;
    private AnimationMixerPlayable animationMixer;
    private AnimationClipPlayable idlePlayable, walkPlayable, kickPlayable;
    private float walkWeight, kickWeight;
    private bool kickPlaying;
    private bool walkCompatible, kickCompatible;

    public override void FixedUpdateNetwork()
    {
        // Host owns movement; NetworkTransform interpolates it for other clients.
        if (!Object.HasStateAuthority) return;
        IsWalking = false;
        if (!GetInput(out RobbyInputData input)) return;
        var manager = NetworkGameManager.Instance;
        if (manager == null || !manager.IsWaitingInCodeRoom) return;
        transform.Rotate(0f, input.yaw, 0f, Space.World);
        Vector2 move = Vector2.ClampMagnitude(input.move, 1f);
        IsWalking = move.sqrMagnitude > 0.001f && moveSpeed > 0f;
        Vector3 direction = transform.right * move.x + transform.forward * move.y;
        transform.position += direction * (moveSpeed * Runner.DeltaTime);
    }

    public override void Spawned()
    {
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
        if (!kickCompatible || !kickPlayable.IsValid()) return;
        kickPlayable.SetTime(0);
        kickPlaying = true;
    }

    private void StartAnimation()
    {
        if (animationGraph.IsValid() || avatarAnimator == null || idleClip == null || walkClip == null || kickClip == null) return;
        // A generic Mixamo-path clip cannot drive this character's humanoid rig.
        // Blending it with a humanoid idle removes the valid body pose and can sink
        // the model below its movement/collider origin. Preserve idle until retargeted.
        walkCompatible = !avatarAnimator.isHuman || walkClip.isHumanMotion;
        kickCompatible = !avatarAnimator.isHuman || kickClip.isHumanMotion;
        if (!walkCompatible || !kickCompatible)
            Debug.LogWarning("[PlayerRobby] Walk/Kick must be Humanoid clips retargeted from their source rig. Incompatible clips use Idle to prevent the character sinking.", this);
        avatarAnimator.applyRootMotion = false;
        animationGraph = PlayableGraph.Create("Robby Idle Walk Kick");
        animationMixer = AnimationMixerPlayable.Create(animationGraph, 3);
        idlePlayable = AnimationClipPlayable.Create(animationGraph, idleClip);
        walkPlayable = AnimationClipPlayable.Create(animationGraph, walkCompatible ? walkClip : idleClip);
        kickPlayable = AnimationClipPlayable.Create(animationGraph, kickCompatible ? kickClip : idleClip);
        animationGraph.Connect(idlePlayable, 0, animationMixer, 0);
        animationGraph.Connect(walkPlayable, 0, animationMixer, 1);
        animationGraph.Connect(kickPlayable, 0, animationMixer, 2);
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
        if (kickPlaying && kickPlayable.GetTime() >= kickClip.length) kickPlaying = false;
        float step = Time.deltaTime / Mathf.Max(0.01f, animationBlendSeconds);
        walkWeight = Mathf.MoveTowards(walkWeight, IsWalking && walkCompatible ? 1f : 0f, step);
        kickWeight = Mathf.MoveTowards(kickWeight, kickPlaying ? 1f : 0f, step);
        animationMixer.SetInputWeight(0, (1f - walkWeight) * (1f - kickWeight));
        animationMixer.SetInputWeight(1, walkWeight * (1f - kickWeight));
        animationMixer.SetInputWeight(2, kickWeight);
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
        walkWeight = kickWeight = 0f;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (LocalPlayer == this) LocalPlayer = null;
        StopAnimation();
    }

    private void OnDestroy()
    {
        if (LocalPlayer == this) LocalPlayer = null;
        StopAnimation();
    }
}
