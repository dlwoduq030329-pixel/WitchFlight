using System.Collections.Generic;
using Suimono.Core;
using UnityEngine;

// Viewer-local effects use rendered player motion. No additional network RPCs or spawned objects.
[DisallowMultipleComponent]
public sealed class BattleWaterWake : MonoBehaviour
{
    [SerializeField] private SuimonoModule water;
    [Header("Flight above water")]
    [SerializeField, Min(.1f)] private float maximumHeight = 10f;
    [SerializeField, Min(.1f)] private float minimumSpeed = 2f;
    [SerializeField, Min(1f)] private float fullStrengthSpeed = 30f;
    [SerializeField, Range(.04f, .5f)] private float sampleInterval = .1f;
    [SerializeField, Min(10f)] private float maximumViewDistance = 180f;
    [SerializeField, Min(.1f)] private float wakeSpacing = 2f;
    [SerializeField, Min(1f)] private float teleportDistance = 20f;
    [Header("Existing Suimono particles (zero-based Effects Systems index)")]
    [SerializeField] private int rippleSystemIndex = 1;
    [SerializeField] private int directionalWakeSystemIndex = 5;
    [SerializeField, Min(.1f)] private float rippleSize = 4f;
    [SerializeField, Min(.1f)] private float directionalWakeSize = 5f;
    [SerializeField, Min(.1f)] private float lifetime = 2f;
    [SerializeField, Min(0f)] private float rearOffset = 1f;
    [SerializeField] private float particleYawOffset;
    [SerializeField, Range(0f, 1f)] private float opacity = .65f;
    [Tooltip("캐릭터와 수면 사이에 바닥이나 벽이 있으면 파문을 만들지 않습니다.")]
    [SerializeField] private LayerMask obstructionLayers = ~0;
    private struct Sample { public Vector3 position; public float time; public int portal; public bool alive; }
    private readonly Dictionary<Player, Sample> samples = new();
    private readonly List<Player> stale = new();
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private float nextSample;

    private void Awake() { if (water == null) water = GetComponent<SuimonoModule>(); }
    private void LateUpdate()
    {
        if (Time.time < nextSample) return;
        nextSample = Time.time + Mathf.Max(.04f, sampleInterval);
        Player local = Player.LocalPlayer;
        if (water == null || !water.isActiveAndEnabled || !water.enableAdvancedDistort ||
            local == null || local.Object == null || !local.Object.IsValid ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
        { samples.Clear(); return; }
        var library = water.suimonoModuleLibrary;
        var systems = library != null && library.fxObject != null ? library.fxObject.fxParticles : null;
        if (systems == null) return;
        Vector3 viewer = water.setCamera != null ? water.setCamera.position : local.transform.position;
        foreach (Player player in Player.ActiveCombatants)
        {
            if (player == null || player.Object == null || !player.Object.IsValid || player.Runner != local.Runner) continue;
            Vector3 position = player.transform.position;
            bool hadPrevious = samples.TryGetValue(player, out Sample previous);
            samples[player] = new Sample { position = position, time = Time.time, portal = player.PortalExitSequence, alive = player.IsAlive };
            if (!hadPrevious || !previous.alive || previous.portal != player.PortalExitSequence ||
                !player.IsAlive || player.IsReturningToMap || player.IsHiddenBySmokeFor(local) ||
                (viewer-position).sqrMagnitude > maximumViewDistance*maximumViewDistance) continue;
            float elapsed = Time.time-previous.time;
            Vector3 delta = position-previous.position;
            float allowedJump = Mathf.Max(teleportDistance, Mathf.Abs(player.CurrentSpeed)*elapsed*2f+2f);
            if (elapsed <= 0f || elapsed > .5f || delta.sqrMagnitude > allowedJump*allowedJump) continue;
            if (!water.TryGetSurfaceHeight(position, out float surfaceY)) continue;
            if (!TryWake(delta, elapsed, position.y-surfaceY, maximumHeight, minimumSpeed, fullStrengthSpeed,
                out Vector3 direction, out float strength)) continue;
            Vector3 point = new Vector3(position.x, surfaceY+.08f, position.z);
            if (Obstructed(player, position, point)) continue;
            // Place a bounded number of samples along motion, without long teleport trails.
            int count = Mathf.Clamp(Mathf.CeilToInt(new Vector2(delta.x,delta.z).magnitude/Mathf.Max(.1f,wakeSpacing)),1,3);
            for (int i=0; i<count; i++)
            {
                Vector3 at = point - direction*(rearOffset+i*Mathf.Min(wakeSpacing,delta.magnitude/count));
                float angle = Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg + particleYawOffset;
                Emit(systems, directionalWakeSystemIndex, at, angle, directionalWakeSize, strength);
                if (i==0) Emit(systems, rippleSystemIndex, at, angle, rippleSize, strength*.65f);
            }
        }
        stale.Clear();
        foreach (var entry in samples)
            if (entry.Key == null || entry.Key.Object == null || !entry.Key.Object.IsValid || Time.time-entry.Value.time > 1f)
                stale.Add(entry.Key);
        foreach (Player player in stale) samples.Remove(player);
    }

    internal static bool TryWake(Vector3 movement, float seconds, float height, float maxHeight,
        float minSpeed, float fullSpeed, out Vector3 direction, out float strength)
    {
        direction = new Vector3(movement.x,0f,movement.z);
        strength = 0f;
        if (seconds <= 0f || height < 0f || height >= maxHeight || maxHeight <= 0f) return false;
        float speed = direction.magnitude/seconds;
        if (speed < minSpeed || direction.sqrMagnitude < .0001f) return false;
        direction.Normalize();
        strength = (1f-Mathf.Clamp01(height/maxHeight))*Mathf.Clamp01(speed/Mathf.Max(minSpeed,fullSpeed));
        return strength > .01f;
    }

    private bool Obstructed(Player player, Vector3 from, Vector3 to)
    {
        Vector3 ray = to-from;
        int count = Physics.RaycastNonAlloc(from, ray.normalized, hits, ray.magnitude, obstructionLayers, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return true;
        for (int i=0;i<count;i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null || collider.gameObject.layer == water.layerWaterNum ||
                collider.transform.IsChildOf(player.transform) || collider.GetComponentInParent<Player>() != null ||
                collider.GetComponentInParent<MagicProjectile>() != null) continue;
            return true;
        }
        return false;
    }

    private void Emit(ParticleSystem[] systems, int index, Vector3 point, float angle, float size, float strength)
    {
        if (index < 0 || index >= systems.Length || systems[index] == null) return;
        ParticleSystem system = systems[index];
        var main = system.main;
        if (system.particleCount >= main.maxParticles) return;
        Transform space = main.simulationSpace == ParticleSystemSimulationSpace.Local ? system.transform :
            main.simulationSpace == ParticleSystemSimulationSpace.Custom ? main.customSimulationSpace : null;
        Color color = main.startColor.Evaluate(.5f);
        color.a *= Mathf.Clamp01(strength*opacity);
        system.Emit(new ParticleSystem.EmitParams {
            position = space != null ? space.InverseTransformPoint(point) : point,
            velocity = Vector3.zero, rotation = angle, startSize = size,
            startLifetime = Mathf.Max(.1f,lifetime), startColor = color, applyShapeToPosition = false
        },1);
        if (!system.isPlaying) system.Play();
    }
    private void OnDisable() { samples.Clear(); nextSample=0f; }
}
