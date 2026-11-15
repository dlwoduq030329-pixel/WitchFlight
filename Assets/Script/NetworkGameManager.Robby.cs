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
    private readonly List<UnityEngine.Object> retainedRobbyAssets = new();
    private bool robbyResourcesReady;

    private async System.Threading.Tasks.Task PrepareRobbyAssets(System.Threading.CancellationToken cancellation)
    {
        // Allow the existing loading overlay to render before asset deserialization.
        await Awaitable.NextFrameAsync(cancellation);
        await Awaitable.NextFrameAsync(cancellation);
        if (!robbyResourcesReady)
        {
            retainedRobbyAssets.Clear();
            string[] resources = { "MagicStatTable", "BasicAttackSettings", "EquipmentStatTable",
                "BattleAudioTable", "EmoteTable", "ProfileImageTable", "EyeTexturePresetTable", "MeowFestivalVisual" };
            foreach (string path in resources)
            {
                var request = Resources.LoadAsync<UnityEngine.Object>(path);
                while (!request.isDone) await Awaitable.NextFrameAsync(cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (request.asset == null) throw new System.InvalidOperationException("Missing lobby resource: " + path);
                retainedRobbyAssets.Add(request.asset);
            }
            robbyResourcesReady = true;
        }
        // Load the same registered prefab Fusion will spawn, including its referenced
        // meshes/materials/animations. Do not instantiate a second network avatar.
        var table = NetworkProjectConfig.Global.PrefabTable;
        var id = table.GetId((NetworkObjectGuid)robbyPlayerPrefab);
        float deadline = Time.realtimeSinceStartup + 60f;
        while (table.Load(id, isSynchronous: false) == null)
        {
            if (Time.realtimeSinceStartup >= deadline)
                throw new System.TimeoutException("Lobby prefab loading timed out.");
            await Awaitable.NextFrameAsync(cancellation);
        }
        cancellation.ThrowIfCancellationRequested();
    }

    private async System.Threading.Tasks.Task WaitForRobbyAvatar(NetworkRunner runner, System.Threading.CancellationToken cancellation)
    {
        float deadline = Time.realtimeSinceStartup + 30f;
        while (PlayerRobby.LocalPlayer == null || PlayerRobby.LocalPlayer.Runner != runner ||
            PlayerRobby.LocalPlayer.Object == null || !PlayerRobby.LocalPlayer.Object.IsValid)
        {
            cancellation.ThrowIfCancellationRequested();
            if (runner == null || !runner.IsRunning || Time.realtimeSinceStartup >= deadline)
                throw new System.InvalidOperationException("Lobby avatar initialization did not complete.");
            await Awaitable.NextFrameAsync(cancellation);
        }
        // Give Spawned/Start, appearance and animation graph initialization their frames.
        await Awaitable.NextFrameAsync(cancellation);
        await Awaitable.NextFrameAsync(cancellation);
    }

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
