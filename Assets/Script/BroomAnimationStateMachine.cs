using System;

// Presentation only. This machine never gates input, moves the character or applies damage.
public enum BroomMotion
{
    ArmIdle, DisarmedIdle, FastIdle, Disarm, Equip, ToFast, FromFast,
    TakeStick, Takeoff, AttackStart, AttackLoop, AttackEnd, Hit,
    DeathStart, DeathLoop, DeathEnd, ParryStart, ParryLoop, ParryEnd
}

public struct BroomAnimationSnapshot
{
    public bool Alive, Intro, Playing, TopSpeed, Disarmed, Attacking, Parrying, Respawning;
    public int WeaponKey, CastSequence, HitSequence, DeathSequence, ParryStartSequence;
}

public sealed class BroomAnimationStateMachine
{
    private readonly Func<BroomMotion, float> duration;
    private BroomAnimationSnapshot previous;
    private bool initialized, pendingEquip;
    private float elapsed, attackPulse;
    public BroomMotion Motion { get; private set; }
    public int Revision { get; private set; }
    public bool UsesBothHands => Motion == BroomMotion.Disarm || Motion == BroomMotion.DisarmedIdle ||
        Motion == BroomMotion.ToFast || Motion == BroomMotion.FastIdle || Motion == BroomMotion.FromFast ||
        Motion == BroomMotion.TakeStick || Motion == BroomMotion.Takeoff;

    public BroomAnimationStateMachine(Func<BroomMotion, float> duration) => this.duration = duration;

    public void Step(BroomAnimationSnapshot input, float deltaTime)
    {
        deltaTime = Math.Max(0f, deltaTime);
        bool first = !initialized;
        BroomAnimationSnapshot old = previous;
        previous = input;
        initialized = true;
        elapsed += deltaTime;
        attackPulse = Math.Max(0f, attackPulse - deltaTime);

        if (first)
        {
            // A late-joining observer must not replay old casts/hits/deaths.
            Enter(!input.Alive ? BroomMotion.DeathLoop : input.Intro ? BroomMotion.TakeStick :
                input.Respawning && input.Playing ? BroomMotion.DeathEnd : Idle(input));
            return;
        }
        if (!input.Alive)
        {
            pendingEquip = false;
            attackPulse = 0f;
            if (old.Alive || old.DeathSequence != input.DeathSequence) Enter(BroomMotion.DeathStart);
            else if (Motion == BroomMotion.DeathStart && Finished) Enter(BroomMotion.DeathLoop);
            return;
        }
        if (input.Intro)
        {
            pendingEquip = false;
            attackPulse = 0f;
            if (!old.Intro) Enter(BroomMotion.TakeStick);
            else if (Finished && Motion == BroomMotion.TakeStick) Enter(BroomMotion.Takeoff);
            else if (Finished && Motion == BroomMotion.Takeoff) Enter(Idle(input));
            return;
        }
        if (!input.Playing)
        {
            pendingEquip = false;
            attackPulse = 0f;
            // An unusually short VS intro cannot spill its takeoff into 3/2/1.
            if (Motion != Idle(input)) Enter(Idle(input));
            return;
        }
        if (!old.Alive)
        {
            pendingEquip = false;
            Enter(BroomMotion.DeathEnd);
            return;
        }

        bool cast = input.CastSequence != old.CastSequence;
        bool newParry = input.ParryStartSequence != old.ParryStartSequence || (input.Parrying && !old.Parrying);
        bool hit = input.HitSequence != old.HitSequence;
        if (input.WeaponKey != old.WeaponKey) pendingEquip = !input.Disarmed;
        if (cast) attackPulse = 0.12f;

        // Gameplay priority: death > parry > hit > attack > weapon/flight presentation.
        if (newParry)
        {
            attackPulse = 0f;
            Enter(BroomMotion.ParryStart);
        }
        else if (hit && !input.Parrying && !IsParry(Motion))
            Enter(BroomMotion.Hit);
        else if (!IsParry(Motion) && !input.Parrying && Motion != BroomMotion.Hit &&
                 (cast || input.Attacking) && (!IsAttack(Motion) || Motion == BroomMotion.AttackEnd))
            Enter(BroomMotion.AttackStart);

        switch (Motion)
        {
            case BroomMotion.ParryStart:
                if (Finished) Enter(BroomMotion.ParryLoop);
                return;
            case BroomMotion.ParryLoop:
                if (!input.Parrying) Enter(BroomMotion.ParryEnd);
                return;
            case BroomMotion.ParryEnd:
                if (Finished) Enter(BroomMotion.ArmIdle);
                return;
            case BroomMotion.Hit:
            case BroomMotion.DeathEnd:
                if (Finished) Enter(BroomMotion.ArmIdle);
                return;
            case BroomMotion.AttackStart:
                if (Finished) Enter(BroomMotion.AttackLoop);
                return;
            case BroomMotion.AttackLoop:
                if (!input.Attacking && attackPulse <= 0f && elapsed >= 0.08f) Enter(BroomMotion.AttackEnd);
                return;
            case BroomMotion.AttackEnd:
                if (Finished) Enter(BroomMotion.ArmIdle);
                return;
            case BroomMotion.Equip:
                if (Finished) Enter(BroomMotion.ArmIdle);
                return;
        }

        if (pendingEquip)
        {
            pendingEquip = false;
            Enter(BroomMotion.Equip);
            return;
        }
        switch (Motion)
        {
            case BroomMotion.ArmIdle:
                if (input.TopSpeed || input.Disarmed) Enter(BroomMotion.Disarm);
                break;
            case BroomMotion.Disarm:
                if (Finished) Enter(input.TopSpeed ? BroomMotion.ToFast : Idle(input));
                break;
            case BroomMotion.DisarmedIdle:
                if (input.TopSpeed) Enter(BroomMotion.ToFast);
                else if (!input.Disarmed) Enter(BroomMotion.Equip);
                break;
            case BroomMotion.ToFast:
                if (!input.TopSpeed) Enter(BroomMotion.FromFast);
                else if (Finished) Enter(BroomMotion.FastIdle);
                break;
            case BroomMotion.FastIdle:
                if (!input.TopSpeed) Enter(BroomMotion.FromFast);
                break;
            case BroomMotion.FromFast:
                if (input.TopSpeed) Enter(BroomMotion.ToFast);
                else if (Finished) Enter(Idle(input));
                break;
            default:
                Enter(Idle(input));
                break;
        }
    }

    private bool Finished => elapsed >= Math.Max(0.01f, duration(Motion));
    private static BroomMotion Idle(BroomAnimationSnapshot input) => input.TopSpeed ? BroomMotion.FastIdle :
        input.Disarmed ? BroomMotion.DisarmedIdle : BroomMotion.ArmIdle;
    private static bool IsAttack(BroomMotion motion) => motion == BroomMotion.AttackStart ||
        motion == BroomMotion.AttackLoop || motion == BroomMotion.AttackEnd;
    private static bool IsParry(BroomMotion motion) => motion == BroomMotion.ParryStart ||
        motion == BroomMotion.ParryLoop || motion == BroomMotion.ParryEnd;

    private void Enter(BroomMotion motion)
    {
        Motion = motion;
        elapsed = 0f;
        Revision++;
    }
}
