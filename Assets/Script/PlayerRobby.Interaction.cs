using Fusion;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public sealed partial class PlayerRobby
{
    [Header("Lobby interactions (full body)")]
    [SerializeField] private AnimationClip drinkingClip, chocolateClip, pettingClip, standToSitClip, sitIdleClip, sitToStandClip;
    [SerializeField, Min(.1f)] private float seatAlignSeconds = .35f;
    [SerializeField, Min(.1f)] private float drinkTurnSeconds = .45f;
    [Networked] private int InteractionState { get; set; } // 1 drink, 2 chocolate, 3 pet, 4 sit down, 5 seated, 6 stand, 7 camera return, 8 turn after drink
    [Networked] private TickTimer InteractionTimer { get; set; }
    [Networked] private float InteractionStarted { get; set; }
    [Networked] private Vector3 InteractionPoint { get; set; }
    [Networked] private Vector3 InteractionOrigin { get; set; }
    [Networked] private Vector3 PetPoint { get; set; }
    [Networked] private float InteractionYaw { get; set; }
    [Networked] private int InteractionZoneId { get; set; }
    public bool InteractionFrontCamera => InteractionBusy && InteractionState >= 4 && InteractionState <= 6;
    public bool InteractionCameraReturning => InteractionBusy && InteractionState == 7;
    public bool InteractionFreeCamera => InteractionBusy && (InteractionState <= 3 || InteractionState == 8);
    public bool InteractionSeated => InteractionBusy && (InteractionState == 4 || InteractionState == 5);
    public int OccupiedInteractionZone => InteractionBusy ? InteractionZoneId : -1;
    public bool InteractionBusy => Object != null && Object.IsValid && InteractionState != 0;
    private AnimationMixerPlayable interactionMixer;
    private AnimationLayerMixerPlayable interactionLayers;
    private AnimationClip[] interactionClips;
    private readonly AnimationClipPlayable[] interactionPlayables = new AnimationClipPlayable[6];
    private int shownInteraction;
    private float interactionWeight;
    private Transform petUpper, petLower, petHand;

    public void RequestInteraction(int zoneId)
    {
        if (Object != null && Object.IsValid && Object.HasInputAuthority && !InteractionBusy)
            RPC_RequestInteraction(zoneId);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestInteraction(int zoneId)
    {
        if (InteractionBusy || NetworkGameManager.Instance == null || !NetworkGameManager.Instance.IsWaitingInCodeRoom) return;
        var zone = RobbyInteractionZone.Get(zoneId);
        if (zone == null || zone.IsOccupiedByOther(this) || !zone.Contains(SimulationPosition + Vector3.up * .8f)) return;
        int state = zone.Kind == RobbyInteractionZone.Action.Drink ? 1 : zone.Kind == RobbyInteractionZone.Action.Pet ? 3 : 4;
        if (interactionClips == null || interactionClips[state - 1] == null) return;
        if (state == 3 && zone.CatTarget == null || state == 4 && zone.SeatTarget == null) return;
        InteractionZoneId = zoneId;
        InteractionOrigin = InteractionPoint = SimulationPosition;
        InteractionYaw = SimulationYaw;
        if (state == 4)
        {
            // SeatTarget is the avatar's ROOT/feet anchor, not the chair cushion centre.
            InteractionPoint = zone.SeatTarget.position;
            InteractionYaw = zone.SeatTarget.eulerAngles.y;
        }
        if (state == 3)
        {
            PetPoint = zone.CatTarget.position;
            Vector3 facing = PetPoint - SimulationPosition; facing.y = 0;
            if (facing.sqrMagnitude > .001f) InteractionYaw = Quaternion.LookRotation(facing).eulerAngles.y;
        }
        ChangeInteraction(state);
    }

    private void ChangeInteraction(int state)
    {
        InteractionState = state;
        InteractionStarted = (float)Runner.SimulationTime;
        InteractionTimer = state == 0 || state == 3 || state == 5 || state == 7 ? TickTimer.None :
            TickTimer.CreateFromSeconds(Runner, state == 8 ? Mathf.Max(.1f, drinkTurnSeconds) :
                Mathf.Max(.1f, interactionClips[state - 1] != null ? interactionClips[state - 1].length : .5f));
    }

    private bool TickInteraction(RobbyInputData input)
    {
        if (InteractionState == 0) return false;
        if (Object.HasStateAuthority)
        {
            if (InteractionState == 7 && input.interactionCameraReady) ChangeInteraction(0);
            else if (InteractionState == 3 && input.standRequested) ChangeInteraction(7);
            else if (InteractionState == 5 && input.standRequested) ChangeInteraction(6);
            else if (InteractionTimer.Expired(Runner))
            {
                if (InteractionState == 8)
                {
                    InteractionYaw = Mathf.Repeat(InteractionYaw + 180f, 360f);
                    ChangeInteraction(chocolateClip != null ? 2 : 7);
                }
                else ChangeInteraction(InteractionState == 1 ? 8 : InteractionState == 4 ? 5 : 7);
            }
        }
        if (InteractionState == 0) return false;
        IsWalking = IsRunning = false; LocalMove = Vector2.zero;
        SimulationYaw = InteractionYaw;
        if (InteractionState == 8)
        {
            float turn = Mathf.Clamp01(((float)Runner.SimulationTime - InteractionStarted) / Mathf.Max(.1f, drinkTurnSeconds));
            SimulationYaw = Mathf.Repeat(InteractionYaw + 180f * Mathf.SmoothStep(0, 1, turn), 360f);
        }
        float progress = InteractionState == 4 ? Mathf.Clamp01(((float)Runner.SimulationTime - InteractionStarted) / seatAlignSeconds) : 1;
        SimulationPosition = Vector3.Lerp(InteractionOrigin, InteractionPoint, Mathf.SmoothStep(0, 1, progress));
        SimulationVelocity = Vector3.zero;
        movementBody.useGravity = false;
        movementBody.constraints = RigidbodyConstraints.FreezeAll;
        movementBody.position = SimulationPosition;
        movementBody.rotation = Quaternion.Euler(0, SimulationYaw, 0);
        movementBody.linearVelocity = Vector3.zero;
        return true;
    }

    private void BuildInteractionGraph(AnimationPlayableOutput output)
    {
        interactionClips = new[] { drinkingClip, chocolateClip, pettingClip, standToSitClip, sitIdleClip, sitToStandClip };
        interactionMixer = AnimationMixerPlayable.Create(animationGraph, 6);
        for (int i = 0; i < 6; i++)
        {
            var clip = interactionClips[i];
            if (clip == null || (avatarAnimator.isHuman && !clip.isHumanMotion))
            {
                Debug.LogWarning("[Robby interaction] Missing/incompatible Humanoid clip at slot " + i, this);
                clip = idleClip;
            }
            interactionPlayables[i] = AnimationClipPlayable.Create(animationGraph, clip);
            interactionPlayables[i].SetSpeed(0); // Time comes from replicated state, including late joiners.
            animationGraph.Connect(interactionPlayables[i], 0, interactionMixer, i);
        }
        interactionLayers = AnimationLayerMixerPlayable.Create(animationGraph, 2);
        animationGraph.Connect(output.GetSourcePlayable(), 0, interactionLayers, 0);
        animationGraph.Connect(interactionMixer, 0, interactionLayers, 1);
        interactionLayers.SetInputWeight(0, 1);
        output.SetSourcePlayable(interactionLayers);
        shownInteraction = 0; interactionWeight = 0;
        if (avatarAnimator.isHuman)
        {
            petUpper = avatarAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            petLower = avatarAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            petHand = avatarAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        }
    }

    private void RenderInteraction()
    {
        if (!interactionLayers.IsValid()) return;
        int state = InteractionState;
        if (state > 0 && state <= 6)
        {
            shownInteraction = state;
            float duration = interactionClips[state - 1] != null ? interactionClips[state - 1].length : 1;
            float elapsed = Mathf.Max(0, (float)Runner.SimulationTime - InteractionStarted);
            interactionPlayables[state - 1].SetTime(state == 5 || state == 3 ? elapsed % Mathf.Max(.01f, duration) : Mathf.Min(elapsed, duration));
        }
        for (int i = 0; i < 6; i++) interactionMixer.SetInputWeight(i, i == shownInteraction - 1 ? 1 : 0);
        interactionWeight = Mathf.MoveTowards(interactionWeight, state == 0 || state >= 7 ? 0 : 1, Time.deltaTime / .12f);
        interactionLayers.SetInputWeight(1, interactionWeight);
    }

    private void ApplyPetReach()
    {
        if (!InteractionBusy || InteractionState != 3 || petHand == null || petLower == null || petUpper == null) return;
        // Two-bone reach: rotations only; never stretch bones or move the whole avatar into the cat.
        Vector3 shoulder = petUpper.position;
        float upper = Vector3.Distance(shoulder, petLower.position), lower = Vector3.Distance(petLower.position, petHand.position);
        Vector3 delta = PetPoint - shoulder;
        if (upper < .001f || lower < .001f || delta.sqrMagnitude < .001f) return;
        float elapsed = Mathf.Max(0, (float)Runner.SimulationTime - InteractionStarted);
        Vector3 goal = PetPoint + Vector3.up * (Mathf.Sin(elapsed * 7) * .025f);
        delta = goal - shoulder;
        float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(upper - lower) + .001f, upper + lower - .001f);
        Vector3 forward = delta.normalized;
        Vector3 bend = Vector3.ProjectOnPlane(petLower.position - shoulder, forward).normalized;
        if (bend.sqrMagnitude < .001f) bend = Vector3.ProjectOnPlane(-transform.up, forward).normalized;
        float along = (upper * upper + distance * distance - lower * lower) / (2 * distance);
        Vector3 elbow = shoulder + forward * along + bend * Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
        float reachWeight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / .25f));
        petUpper.rotation = Quaternion.Slerp(petUpper.rotation,
            Quaternion.FromToRotation(petLower.position - shoulder, elbow - shoulder) * petUpper.rotation, reachWeight);
        petLower.rotation = Quaternion.Slerp(petLower.rotation,
            Quaternion.FromToRotation(petHand.position - petLower.position, shoulder + forward * distance - petLower.position) * petLower.rotation, reachWeight);
    }
}
