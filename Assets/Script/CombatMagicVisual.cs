using UnityEngine;

// Pure local VFX. Persistent beams are reused throughout a channel, not spawned every tick.
public sealed class CombatMagicVisual : MonoBehaviour
{
    private LineRenderer line;
    private Transform stretched;
    private Vector3 authoredScale;
    private Material ownedMaterial;
    private Player follow;
    private float expires;
    private float duration;

    public static CombatMagicVisual Beam(MagicType magic, GameObject prefab)
    {
        var root = new GameObject(magic + " beam VFX");
        var effect = root.AddComponent<CombatMagicVisual>();
        GameObject view = CombatPresentation.InstantiateVfx(prefab, Vector3.zero, Quaternion.identity, root.transform);
        if (view != null)
        {
            effect.line = view.GetComponentInChildren<LineRenderer>(true);
            effect.stretched = view.transform;
            effect.authoredScale = view.transform.localScale;
        }
        else
        {
            effect.line = root.AddComponent<LineRenderer>();
            effect.ownedMaterial = CombatPresentation.CreateEffectMaterial(CombatPresentation.MagicColor(magic));
            effect.line.sharedMaterial = effect.ownedMaterial;
            effect.line.startWidth = 0.13f;
            effect.line.endWidth = 0.08f;
            effect.line.numCapVertices = 4;
        }
        if (effect.line != null) { effect.line.useWorldSpace = true; effect.line.positionCount = 2; }
        return effect;
    }

    public void SetEndpoints(Vector3 origin, Vector3 end)
    {
        if (line != null) { line.SetPosition(0, origin); line.SetPosition(1, end); }
        else if (stretched != null)
        {
            Vector3 delta = end - origin;
            stretched.SetPositionAndRotation(origin, delta.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(delta) : Quaternion.identity);
            stretched.localScale = new Vector3(authoredScale.x, authoredScale.y, authoredScale.z * delta.magnitude);
        }
    }

    public static void Shield(Player player, GameObject prefab, float seconds)
    {
        var root = new GameObject("Successful parry shield");
        var effect = root.AddComponent<CombatMagicVisual>();
        effect.follow = player;
        effect.duration = Mathf.Max(0.05f, seconds);
        effect.expires = Time.unscaledTime + effect.duration;
        root.transform.position = player.LockAimPoint;
        GameObject view = CombatPresentation.InstantiateVfx(prefab, player.LockAimPoint, Quaternion.identity, root.transform);
        if (view == null)
        {
            view = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Collider collider = view.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            view.transform.SetParent(root.transform, false);
            view.transform.localScale = Vector3.one * 2.5f;
            effect.ownedMaterial = CombatPresentation.CreateEffectMaterial(new Color(0.2f, 0.85f, 1f, 0.25f));
            view.GetComponent<Renderer>().sharedMaterial = effect.ownedMaterial;
        }
    }

    private void LateUpdate()
    {
        if (expires <= 0f) return;
        if (follow == null || follow.Object == null || !follow.Object.IsValid || Time.unscaledTime >= expires)
        { Destroy(gameObject); return; }
        transform.position = follow.LockAimPoint;
        if (ownedMaterial != null)
        {
            Color color = ownedMaterial.color;
            color.a = 0.25f * Mathf.Clamp01((expires - Time.unscaledTime) / duration);
            ownedMaterial.color = color;
        }
    }

    private void OnDestroy() { if (ownedMaterial != null) Destroy(ownedMaterial); }
}
