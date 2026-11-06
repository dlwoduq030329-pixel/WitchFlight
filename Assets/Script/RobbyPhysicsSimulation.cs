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
        Physics.SyncTransforms();
        scene.Simulate(Runner.DeltaTime);
        foreach (PlayerRobby avatar in avatars)
            if (avatar != null) avatar.CaptureRobbyPhysics();
    }

    private void ReleaseDefaultScene()
    {
        if (!ownsDefaultScene) return;
        ownsDefaultScene = false;
        if (--defaultSceneUsers == 0) Physics.simulationMode = previousMode;
    }

    private void OnDestroy() => ReleaseDefaultScene();
}
