using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class RobbyInteractionZone : MonoBehaviour
{
    public enum Action { Drink, Pet, Sit }
    [SerializeField] private int zoneId;
    [SerializeField] private Action action;
    [Tooltip("고양이의 손을 댈 위치(머리 등)를 지정합니다.")]
    [SerializeField] private Transform catTarget;
    [Tooltip("앉을 때 플레이어 루트/발 기준 위치와 바라볼 방향. 의자 방석 중심이 아닙니다.")]
    [SerializeField] private Transform seatTarget;
    private Collider volume;
    private readonly HashSet<Collider> localContacts = new();
    private static readonly Dictionary<int, RobbyInteractionZone> zones = new();
    internal static readonly List<RobbyInteractionZone> Nearby = new();
    public int Id => zoneId;
    public Action Kind => action;
    public Transform CatTarget => catTarget;
    public Transform SeatTarget => seatTarget;
    public string Label => action == Action.Drink ? "마시기" : action == Action.Pet ? "쓰다듬기" : "앉기";
    public bool IsOccupiedByOther(PlayerRobby requester)
    {
        // Checked on interaction requests, not polled by every trigger every frame.
        foreach (var player in PlayerRobby.ActivePlayers)
        {
            if (player == null || player == requester || !player.InteractionBusy) continue;
            var occupied = Get(player.OccupiedInteractionZone);
            if (occupied == this || (occupied != null &&
                ((catTarget != null && occupied.catTarget == catTarget) ||
                 (seatTarget != null && occupied.seatTarget == seatTarget)))) return true;
        }
        return false;
    }
    public static RobbyInteractionZone Get(int id) => zones.TryGetValue(id, out var zone) ? zone : null;
    private void Awake() { volume = GetComponent<Collider>(); volume.isTrigger = true; }
    private void OnEnable()
    {
        if (zones.ContainsKey(zoneId)) Debug.LogError("Duplicate lobby interaction zone ID: " + zoneId, this);
        else zones.Add(zoneId, this);
    }
    private void OnDisable()
    {
        if (Get(zoneId) == this) zones.Remove(zoneId);
        Nearby.Remove(this); localContacts.Clear();
    }
    public bool Contains(Vector3 point) => isActiveAndEnabled && volume != null && volume.enabled &&
        (volume.ClosestPoint(point) - point).sqrMagnitude <= 1f;
    private void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponentInParent<PlayerRobby>();
        if (player == null || player != PlayerRobby.LocalPlayer) return;
        localContacts.Add(other);
        if (!Nearby.Contains(this)) Nearby.Add(this);
    }
    private void OnTriggerExit(Collider other)
    {
        localContacts.Remove(other);
        if (localContacts.Count == 0) Nearby.Remove(this);
    }
}
