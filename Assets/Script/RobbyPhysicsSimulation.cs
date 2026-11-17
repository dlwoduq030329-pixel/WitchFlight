using System.Collections.Generic;
using Fusion;
using UnityEngine;

// Exists only while lobby avatars exist. Physics runs once after each Fusion tick,
// including resimulation; the battle Runner and project physics settings are untouched.
public sealed class RobbyPhysicsSimulation : SimulationBehaviour, IAfterTick
{
    private readonly List<PlayerRobby> avatars = new();
    private static int defaultSceneUsers;
    private static SimulationMode previousMode;
    private bool ownsDefaultScene;
    private int measuredFrame = -1, forwardTicks, replayTicks;
    private double physicsMilliseconds;
    private static readonly Unity.Profiling.ProfilerMarker PhysicsProfile = new("WitchFlight.LobbyPhysics.AfterTick");

    public void Register(PlayerRobby avatar)
    {
        if (avatars.Contains(avatar)) return;
        if (avatars.Count == 0 && Runner.GetPhysicsScene().Equals(Physics.defaultPhysicsScene))
        {
            if (defaultSceneUsers++ == 0)
            {
                previousMode = Physics.simulationMode;
                Physics.simulationMode = SimulationMode.Script;
            }
            ownsDefaultScene = true;
        }
        avatars.Add(avatar);
    }

    public void Unregister(PlayerRobby avatar)
    {
        avatars.Remove(avatar);
        if (avatars.Count != 0) return;
        ReleaseDefaultScene();
        enabled = false;
        if (Runner != null) Runner.RemoveGlobal(this);
        Destroy(this);
    }

    public void AfterTick()
    {
        if (avatars.Count == 0) return;
        PhysicsScene scene = Runner.GetPhysicsScene();
        if (!scene.IsValid()) return;
        if (measuredFrame != Time.frameCount)
        {
            measuredFrame = Time.frameCount;
            forwardTicks = replayTicks = 0; physicsMilliseconds = 0;
        }
        if (Runner.IsForward) forwardTicks++; else replayTicks++;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var profile = PhysicsProfile.Auto();
        Physics.SyncTransforms();
        scene.Simulate(Runner.DeltaTime);
        foreach (PlayerRobby avatar in avatars)
            if (avatar != null) avatar.CaptureRobbyPhysics();
        physicsMilliseconds += (System.Diagnostics.Stopwatch.GetTimestamp() - started) *
            1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    private void LateUpdate()
    {
        if (measuredFrame != Time.frameCount || physicsMilliseconds < 8) return;
        Debug.LogWarning($"[LobbyPhysics] frame={measuredFrame} server={Runner.IsServer} forwardTicks={forwardTicks} replayTicks={replayTicks} total={physicsMilliseconds:F2}ms avatars={avatars.Count} defaultScene={ownsDefaultScene}");
    }

    private void ReleaseDefaultScene()
    {
        if (!ownsDefaultScene) return;
        ownsDefaultScene = false;
        if (--defaultSceneUsers == 0) Physics.simulationMode = previousMode;
    }

    private void OnDestroy() => ReleaseDefaultScene();
}
