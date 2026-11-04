using System;
using UnityEngine;

[Serializable]
public sealed class PlayerBroomAnimation
{
    [Tooltip("표현만 제어합니다. 이동/판정/쿨타임/부활 시간은 변경하지 않습니다.")]
    [SerializeField] private bool enabled = true;
    [SerializeField, Min(0f)] private float blendSeconds = 0.06f;
    [Tooltip("공격 시작/종료, 무기 교체, 피격, 부활 모션의 최대 재생 시간입니다. 긴 원본 모션을 빠르게 재생하여 조작에 뒤처지지 않게 합니다.")]
    [SerializeField, Min(0.05f)] private float quickActionSeconds = 0.25f;
    [SerializeField, Min(0.01f)] private float parryStartSeconds = 0.08f;
    [SerializeField, Min(0.01f)] private float parryEndSeconds = 0.12f;
    [Tooltip("접수된 패링마다 같은 손을 몸 쪽으로 당기는 동작/위로 드는 동작을 교대로 재생합니다.")]
    [SerializeField] private bool alternateParryAnimations = true;
    [SerializeField, Min(0.05f)] private float speedTransitionSeconds = 0.35f;
    [Tooltip("3단계 선택 후 실제 최고속도의 이 비율에 도달하면 고속 자세로 전환합니다.")]
    [SerializeField, Range(.1f, 1f)] private float fastPoseEnterSpeedRatio = .72f;
    [SerializeField, Range(0f, .9f)] private float fastPoseExitSpeedRatio = .55f;
    [Tooltip("두 손으로 빗자루를 잡는 모션 동안 손의 마법 지팡이를 숨깁니다. 장착 데이터는 바꾸지 않습니다.")]
    [SerializeField] private bool hideStaffWhenDisarmed = true;

    private const string SpeedParameter = "BroomActionSpeed";
    private Animator animator;
    private BroomAnimationStateMachine machine;
    private int[] hashes;
    private int[] alternateHashes;
    private bool[] alternateAvailable;
    private float[] alternateLengths;
    private float[] lengths;
    private bool[] available;
    private bool hasSpeedParameter;
    private int speedHash, playedRevision = -1;
    private RuntimeAnimatorController controller;
    private bool fastPose;
    public bool UseFastPose(int stage, float speedRatio)
    {
        float enter = Mathf.Clamp(fastPoseEnterSpeedRatio, .1f, 1f);
        float exit = Mathf.Clamp(fastPoseExitSpeedRatio, 0f, enter);
        fastPose = Mathf.Abs(stage) >= 3 && speedRatio >= (fastPose ? exit : enter);
        return fastPose;
    }
    public bool StaffVisible => !enabled || !hideStaffWhenDisarmed || machine == null ||
        available == null || !available[(int)machine.Motion] || !machine.UsesBothHands;
    public float RecoverySeconds => Mathf.Max(0.05f, quickActionSeconds);

    public void Render(Animator target, BroomAnimationSnapshot snapshot, float deltaTime,
        string hitState, string deathState)
    {
        if (!enabled || target == null || target.runtimeAnimatorController == null || !target.isActiveAndEnabled)
            return;
        if (animator != target || controller != target.runtimeAnimatorController || machine == null)
            Initialize(target, hitState, deathState);
        machine.Step(snapshot, deltaTime);
        if (playedRevision == machine.Revision) return;
        playedRevision = machine.Revision;
        int index = (int)machine.Motion;
        if (!available[index]) return; // Do not synthesize missing states/clips.
        float duration = Duration(machine.Motion);
        if (hasSpeedParameter)
            animator.SetFloat(speedHash, ClipLength(index) / Mathf.Max(0.01f, duration));
        // Apply only to the Ingame base layer; the existing Face layer is untouched.
        int stateHash = UseAlternate(index) ? alternateHashes[index] : hashes[index];
        animator.CrossFadeInFixedTime(stateHash, Mathf.Min(blendSeconds, duration * 0.35f), 0, 0f);
    }

    private void Initialize(Animator target, string hitState, string deathState)
    {
        animator = target;
        controller = target.runtimeAnimatorController;
        speedHash = Animator.StringToHash(SpeedParameter);
        hasSpeedParameter = false;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.nameHash == speedHash && parameter.type == AnimatorControllerParameterType.Float)
                hasSpeedParameter = true;
        int count = Enum.GetValues(typeof(BroomMotion)).Length;
        hashes = new int[count]; lengths = new float[count]; available = new bool[count];
        alternateHashes = new int[count]; alternateAvailable = new bool[count];
        alternateLengths = new float[count];
        AnimationClip[] clips = controller.animationClips;
        for (int i = 0; i < count; i++)
        {
            BroomMotion motion = (BroomMotion)i;
            string name = motion == BroomMotion.Hit ? hitState : motion == BroomMotion.DeathStart ? deathState : StateName(motion);
            hashes[i] = Animator.StringToHash("Ingame." + name);
            available[i] = !string.IsNullOrWhiteSpace(name) && animator.HasState(0, hashes[i]);
            // Support a replacement controller whose base layer has a different name.
            if (!available[i] && !string.IsNullOrWhiteSpace(name))
            {
                hashes[i] = Animator.StringToHash(name);
                available[i] = animator.HasState(0, hashes[i]);
            }
            lengths[i] = 0.1f;
            foreach (AnimationClip clip in clips)
                if (clip != null && clip.name == name) { lengths[i] = Mathf.Max(0.01f, clip.length); break; }
            if (motion == BroomMotion.ParryStart || motion == BroomMotion.ParryLoop || motion == BroomMotion.ParryEnd)
            {
                alternateHashes[i] = Animator.StringToHash("Ingame." + name + "_Raised");
                alternateAvailable[i] = animator.HasState(0, alternateHashes[i]);
                if (!alternateAvailable[i])
                {
                    alternateHashes[i] = Animator.StringToHash(name + "_Raised");
                    alternateAvailable[i] = animator.HasState(0, alternateHashes[i]);
                }
                string raisedClip = name.Replace("_Block1_", "_Attack1_");
                alternateLengths[i] = lengths[i];
                foreach (AnimationClip clip in clips)
                    if (clip != null && clip.name == raisedClip)
                    { alternateLengths[i] = Mathf.Max(0.01f, clip.length); break; }
            }
        }
        // Root motion must never fight Fusion's predicted CharacterController.
        animator.applyRootMotion = false;
        machine = new BroomAnimationStateMachine(Duration);
        playedRevision = -1;
    }

    private bool UseAlternate(int index) => alternateParryAnimations && machine != null &&
        machine.AlternateParry && alternateAvailable[index];

    private float ClipLength(int index) => UseAlternate(index) ? alternateLengths[index] : lengths[index];

    private float Duration(BroomMotion motion)
    {
        int index = (int)motion;
        if (!available[index]) return 0.01f;
        float length = ClipLength(index);
        if (!hasSpeedParameter) return length;
        float cap = motion switch
        {
            BroomMotion.ParryStart => parryStartSeconds,
            BroomMotion.ParryEnd => parryEndSeconds,
            BroomMotion.Disarm or BroomMotion.ToFast or BroomMotion.FromFast => speedTransitionSeconds,
            BroomMotion.AttackStart or BroomMotion.AttackEnd or BroomMotion.Equip or BroomMotion.Hit or BroomMotion.DeathEnd => quickActionSeconds,
            _ => length
        };
        return Mathf.Max(0.01f, Mathf.Min(length, cap));
    }

    public void Reset()
    {
        fastPose = false;
        if (animator != null && hasSpeedParameter) animator.SetFloat(speedHash, 1f);
        machine = null;
        playedRevision = -1;
    }

    public static string StateName(BroomMotion motion) => motion switch
    {
        BroomMotion.ArmIdle => "AS_Broom_SitFly_Arm_Idle",
        BroomMotion.DisarmedIdle => "AS_Broom_SitFly_Disrm_Idle",
        BroomMotion.FastIdle => "AS_Broom_SitFly_Disarm_Fast_Idle",
        BroomMotion.Disarm => "AS_Broom_SitFly_Disarm",
        BroomMotion.Equip => "AS_Broom_SitFly_Equip",
        BroomMotion.ToFast => "AS_Broom_SitFly_Disrm_Normal_To_Fast",
        BroomMotion.FromFast => "AS_Broom_SitFly_Disrm_Fast_To_Normal",
        BroomMotion.TakeStick => "AS_Broom_Disrm_Take_Stick",
        BroomMotion.Takeoff => "AS_Broom_Disrm_Start_Fly",
        BroomMotion.AttackStart => "AS_Broom_SitFly_Arm_Attack1_Start",
        BroomMotion.AttackLoop => "AS_Broom_SitFly_Arm_Attack1_Loop",
        BroomMotion.AttackEnd => "AS_Broom_SitFly_Arm_Attack1_End",
        BroomMotion.Hit => "AS_Broom_SitFly_Arm_Hit_React",
        BroomMotion.DeathStart => "AS_Broom_SitFly_Arm_Knock1_Start",
        BroomMotion.DeathLoop => "AS_Broom_SitFly_Arm_Knock1_Loop",
        BroomMotion.DeathEnd => "AS_Broom_SitFly_Arm_Knock1_End",
        BroomMotion.ParryStart => "AS_Broom_SitFly_Arm_Block1_Start",
        BroomMotion.ParryLoop => "AS_Broom_SitFly_Arm_Block1_Loop",
        _ => "AS_Broom_SitFly_Arm_Block1_End"
    };
}
