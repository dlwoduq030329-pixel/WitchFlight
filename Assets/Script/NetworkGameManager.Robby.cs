using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

// Main-scene lobby spawning; separate from the battle avatar lifecycle.
public partial class NetworkGameManager
{
    [Header("Main room avatar (separate from battle)")]
    [SerializeField] private NetworkPrefabRef robbyPlayerPrefab;
    [SerializeField] private Transform robbySpawnPoint;
    private readonly Dictionary<PlayerRef, NetworkObject> robbyPlayers = new Dictionary<PlayerRef, NetworkObject>();

    private bool CanSpawnRobbyPlayer(NetworkRunner runner)
    {
        return runner == _runner && runner.IsServer && !IsTraining && !IsRandomMatch &&
            !IsDeathMatch && !isGameStarting && !cancelRequested &&
            SceneManager.GetActiveScene().path == "Assets/Scenes/Main.unity";
    }

    private void SpawnRobbyPlayer(NetworkRunner runner, PlayerRef player)
    {
        if (!CanSpawnRobbyPlayer(runner) || robbyPlayers.ContainsKey(player)) return;
        if (!robbyPlayerPrefab.IsValid || robbySpawnPoint == null)
        {
            Debug.LogWarning("Robby player prefab or Main spawnPoint is not assigned.", this);
            return;
        }
        NetworkObject avatar = runner.Spawn(robbyPlayerPrefab, robbySpawnPoint.position, robbySpawnPoint.rotation, player);
        if (avatar != null) robbyPlayers.Add(player, avatar);
    }

    private void SpawnRobbyPlayersAfterSceneLoad(NetworkRunner runner)
    {
        if (!CanSpawnRobbyPlayer(runner)) return;
        // The manager persists but the authored scene reference may have been unloaded.
        // Resolve once on scene load, never in Update or a network tick.
        if (robbySpawnPoint == null)
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == "spawnPoint") { robbySpawnPoint = child; break; }
        foreach (PlayerRef player in runner.ActivePlayers) SpawnRobbyPlayer(runner, player);
    }

    private void DespawnRobbyPlayer(NetworkRunner runner, PlayerRef player)
    {
        if (!robbyPlayers.TryGetValue(player, out NetworkObject avatar)) return;
        robbyPlayers.Remove(player);
        if (runner.IsServer && avatar != null && avatar.IsValid) runner.Despawn(avatar);
    }

    private void DespawnAllRobbyPlayers(NetworkRunner runner)
    {
        if (runner.IsServer)
            foreach (NetworkObject avatar in robbyPlayers.Values)
                if (avatar != null && avatar.IsValid) runner.Despawn(avatar);
        robbyPlayers.Clear();
    }
}
