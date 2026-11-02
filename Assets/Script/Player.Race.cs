using Fusion;
using UnityEngine;

public partial class Player
{
    private PlayerData raceData;
    private int raceBotCheckpoint = -1, raceBotStage;
    private Vector3 raceBotEntry, raceBotExit, raceBotNormal;
    private float raceBotRecovery;
    private PlayerData RaceData
    {
        get
        {
            if (raceData == null || raceData.Object == null || !raceData.Object.IsValid)
                raceData = PlayerData.FindPerkData(Runner, ParticipantRef);
            return raceData;
        }
    }

    private bool RaceFinished => RaceCourse.Instance != null && RaceData != null && RaceData.RaceRank > 0;

    private bool BuildRaceInput(out NetworkInputData input)
    {
        input = default;
        if (!Object.HasStateAuthority) return false;
        RaceCourse course = RaceCourse.Instance;
        PlayerData data = RaceData;
        RaceRing ring = course.NextRing(data);
        input.portalExitSequence = PortalExitSequence;
        input.aimUp = Vector3.up;
        input.suppressCombat = true;
        if (ring == null || !IsAlive) return true;
        if (raceBotCheckpoint != data.RacePassed)
        {
            raceBotCheckpoint = data.RacePassed;
            raceBotNormal = ring.Normal;
            if (Vector3.Dot(ring.Center - transform.position, raceBotNormal) < 0) raceBotNormal = -raceBotNormal;
            // One random decision per checkpoint, not per frame. A miss requires a real return flight.
            bool miss = Random.value < course.MissChanceFor(data);
            float lateral = miss ? ring.Radius * 1.65f : Random.Range(-.25f, .25f) * ring.Radius;
            Vector3 offset = ring.Right * lateral;
            float approach = Mathf.Max(12f, ring.Radius * 1.5f);
            raceBotEntry = ring.Center - raceBotNormal * approach + offset;
            raceBotExit = ring.Center + raceBotNormal * approach + offset;
            raceBotStage = 0;
            raceBotRecovery = 0;
        }
        Vector3 destination = raceBotStage == 0 ? raceBotEntry : raceBotExit;
        float near = Mathf.Max(2f, Mathf.Min(6f, ring.Radius * .4f));
        if (raceBotStage == 0 && Vector3.Distance(transform.position, raceBotEntry) < near) raceBotStage = 1;
        float side = Vector3.Dot(transform.position - ring.Center, raceBotNormal);
        if (raceBotStage == 1 && side > Mathf.Max(3f, ring.Radius * .4f))
        {
            // Still targeting the same ring means it was missed; approach it again from the original side.
            raceBotEntry = ring.Center - raceBotNormal * Mathf.Max(12f, ring.Radius * 1.5f);
            raceBotExit = ring.Center + raceBotNormal * Mathf.Max(12f, ring.Radius * 1.5f);
            raceBotStage = 0;
            raceBotRecovery = course.BotRecoverySeconds;
        }
        destination = raceBotStage == 0 ? raceBotEntry : raceBotExit;
        Vector3 direction = (destination - transform.position).normalized;
        if (direction.sqrMagnitude < .01f) direction = transform.forward;
        direction = BotRouteDirection(destination, direction, MatchBotSettings.Default, out bool avoiding, true, Mathf.Max(.5f, ring.Radius * .2f));
        float alignment = Vector3.Dot(transform.forward, direction);
        raceBotRecovery = Mathf.Max(0, raceBotRecovery - Runner.DeltaTime);
        int desiredStage = avoiding || alignment < .7f || raceBotRecovery > 0 ? 1 : 3;
        input.aimDirection = direction;
        input.steerToAim = true;
        botSpeedPulse = !botSpeedPulse;
        if (botSpeedPulse && SpeedStage < desiredStage) input.buttons.Set(PlayerInputButton.Accelerate, true);
        if (botSpeedPulse && SpeedStage > desiredStage) input.buttons.Set(PlayerInputButton.Decelerate, true);
        if (desiredStage == 3 && alignment > .97f && Vector3.Distance(transform.position, destination) > 25)
            input.buttons.Set(PlayerInputButton.Boost, true);
        return true;
    }
}
