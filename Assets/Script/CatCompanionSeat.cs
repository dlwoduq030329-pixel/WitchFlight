using UnityEngine;

// Authored attachment point. Resolved once when equipment changes; no update loop.
[DisallowMultipleComponent]
[AddComponentMenu("WitchFlight/Cat Companion Seat")]
public sealed class CatCompanionSeat : MonoBehaviour
{
    [Header("Position = seat, blue (+Z) = forward, green (+Y) = up")]
    [SerializeField, Tooltip("Edit this object's Transform in the riding pose. Its name does not affect attachment.")]
    private bool showSeatGizmo = true;

    private void OnDrawGizmosSelected()
    {
        if (!showSeatGizmo) return;
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position, transform.forward * .2f);
        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position, transform.up * .2f);
    }
}
