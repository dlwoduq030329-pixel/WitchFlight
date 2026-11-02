using Fusion;

public partial class PlayerData
{
    [Networked] public bool RaceReady { get; private set; }
    [Networked] public bool RaceStarted { get; private set; }
    [Networked] public bool RaceClosed { get; private set; }
    [Networked] public TickTimer RaceCountdown { get; private set; }
    [Networked] public TickTimer RaceFinishWindow { get; private set; }
    [Networked] public double RaceStartTime { get; private set; }
    [Networked] public int RacePassed { get; private set; }
    [Networked] public int RaceCompetitorTeam { get; private set; }
    [Networked] public int RaceRank { get; private set; }
    [Networked] public bool RaceDidFinish { get; private set; }
    [Networked] public float RaceTime { get; private set; }
    [Networked] public int RaceVotes { get; private set; }
    [Networked] public bool RaceHasVoted { get; private set; }

    public void ResetRace()
    {
        if (!Object.HasStateAuthority) return;
        RaceReady = IsMatchBot;
        RaceCompetitorTeam = RaceCourse.Instance != null ? RaceCourse.Instance.CompetitorTeam(ParticipantRef) : 1;
        RaceStarted = RaceClosed = RaceDidFinish = RaceHasVoted = false;
        RaceCountdown = RaceFinishWindow = TickTimer.None;
        RacePassed = RaceRank = RaceVotes = 0;
        RaceTime = 0;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RaceReady()
    {
        if (RaceCourse.Instance != null && IsLoadoutInitialized) RaceReady = true;
    }

    public void StartRace(float countdown)
    {
        if (!Object.HasStateAuthority || RaceStarted) return;
        RaceStarted = true;
        RaceCountdown = TickTimer.CreateFromSeconds(Runner, countdown);
        RaceStartTime = Runner.SimulationTime + countdown;
    }

    public void PassRaceRing() { if (Object.HasStateAuthority && !RaceClosed && RaceRank == 0) RacePassed++; }
    public void SetRaceFinishWindow() { if (Object.HasStateAuthority) RaceFinishWindow = TickTimer.CreateFromSeconds(Runner, 30); }
    public void SetRaceResult(int rank, bool finished)
    {
        if (!Object.HasStateAuthority || RaceRank != 0) return;
        RaceRank = rank;
        RaceDidFinish = finished;
        RaceTime = (float)(Runner.SimulationTime - RaceStartTime);
    }
    public void CloseRace() { if (Object.HasStateAuthority) RaceClosed = true; }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RaceVote(PlayerRef target)
    {
        if (!RaceClosed || RaceHasVoted || IsMatchBot || RaceCourse.Instance == null) return;
        PlayerData recipient = FindPerkData(Runner, target);
        if (recipient == null || !recipient.RaceClosed || recipient.RaceRank <= 0) return;
        RaceHasVoted = true;
        recipient.RaceVotes++;
    }
}
