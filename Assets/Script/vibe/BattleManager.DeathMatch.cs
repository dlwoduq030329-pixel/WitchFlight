using Fusion;
using UnityEngine;

public partial class BattleManager
{
    [Header("Deathmatch spawn (only used in Deathmatch)")]
    [SerializeField, Min(1f)] private float deathMatchSpawnClearance = 3f;
    [SerializeField, Min(0f)] private float deathMatchSpawnHeightVariation = 20f;
    [SerializeField, Min(1f)] private float deathMatchPlayerSeparation = 15f;

    private float deathMatchSpawnWarningAt;

    public void AdmitDeathMatchPlayer(PlayerData data)
    {
        if (!IsDeathMatch || !isInitialized || runner == null || !runner.IsServer || data == null ||
            data.Runner != runner || !data.IsLoadoutInitialized || spawnedPlayers.ContainsKey(data.ParticipantRef)) return;
        foreach (var pending in pendingRespawns)
            if (pending.PlayerRef == data.ParticipantRef) return;
        if (!NetworkGameManager.Instance.IsRestoredDeathMatchPlayer(data.ParticipantRef))
        { data.ResetMatchPerks(); data.ResetBattleStats(); }
        SpawnBattlePlayer(data.ParticipantRef, data, playerPrefab);
    }

    private void SpawnDeathMatchPlayer(PlayerRef participant, PlayerData data, NetworkPrefabRef prefab, bool respawn)
    {
        if (data == null || !data.IsLoadoutInitialized || !IsConnected(participant) ||
            (spawnedPlayers.TryGetValue(participant, out var existing) && existing != null && existing.IsValid)) return;
        if (!TryDeathMatchSpawn(out var position))
        {
            if (Time.unscaledTime >= deathMatchSpawnWarningAt)
            {
                deathMatchSpawnWarningAt = Time.unscaledTime + 5f;
                Debug.LogWarning("[DeathMatch] 안전한 스폰 공간을 찾지 못했습니다. 맵 경계와 스폰 높이를 확인해주세요.", this);
            }
            return;
        }
        var obj = runner.Spawn(prefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), participant);
        if (obj == null) return;
        spawnedPlayers[participant] = obj;
        runner.SetPlayerObject(participant, obj);
        obj.GetComponent<Player>().InitPlayer(data, respawn);
    }

    public void RestoreDeathMatchRespawns()
    {
        if (!IsDeathMatch || !isInitialized || runner == null || !runner.IsServer) return;
        foreach (var pair in spawnedPlayers)
        {
            if (pair.Value == null || !pair.Value.IsValid) continue;
            var pilot = pair.Value.GetComponent<Player>();
            if (pilot == null || pilot.IsAlive) continue;
            bool scheduled = false;
            foreach (var pending in pendingRespawns) if (pending.PlayerRef == pair.Key) { scheduled = true; break; }
            if (scheduled) continue;
            float remaining = pilot.RespawnTimer.RemainingTime(runner) ?? respawnDelaySeconds;
            pendingRespawns.Add(new PendingRespawn { PlayerRef = pair.Key, DeadObject = pair.Value,
                DespawnAt = TickTimer.CreateFromSeconds(runner, Mathf.Min(deathDespawnDelay, remaining)),
                RespawnAt = TickTimer.CreateFromSeconds(runner, Mathf.Max(.1f, remaining)) });
        }
    }

    private bool TryDeathMatchSpawn(out Vector3 position)
    {
        Vector3 origin = playerSpawnA != null ? playerSpawnA.position : transform.position;
        Bounds bounds = new Bounds(origin, new Vector3(160f, 40f, 160f));
        if (mapBoundaryTable != null && mapBoundaryTable.TryGetBotFlightBounds(out var authored)) bounds = authored;
        float minY = Mathf.Max(bounds.min.y, origin.y - deathMatchSpawnHeightVariation);
        float maxY = Mathf.Min(bounds.max.y, origin.y + deathMatchSpawnHeightVariation);
        if (maxY < minY) minY = maxY = bounds.center.y;
        float margin = Mathf.Min(deathMatchSpawnClearance, Mathf.Min(bounds.extents.x, bounds.extents.z) * .5f);
        for (int attempt = 0; attempt < 96; attempt++)
        {
            position = new Vector3(Random.Range(bounds.min.x + margin, bounds.max.x - margin),
                Random.Range(minY, maxY), Random.Range(bounds.min.z + margin, bounds.max.z - margin));
            if (Physics.CheckSphere(position, deathMatchSpawnClearance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)) continue;
            bool occupied = false;
            foreach (var obj in spawnedPlayers.Values)
                if (obj != null && obj.IsValid && (obj.transform.position - position).sqrMagnitude <
                    deathMatchPlayerSeparation * deathMatchPlayerSeparation) { occupied = true; break; }
            if (!occupied) return true;
        }
        position = default;
        return false;
    }
}
