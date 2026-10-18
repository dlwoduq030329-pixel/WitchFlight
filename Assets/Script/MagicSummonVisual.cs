using UnityEngine;

// Optional authored familiar/robot replaces only the body; the lock telegraph remains visible.
public sealed class MagicSummonVisual : MonoBehaviour
{
    private Material material;
    private Transform body;
    private LineRenderer targetRing;
    private Player target;
    private float progress;
    private SummonAnimationPlayer animationPlayer;
    private bool moving, retiring;
    private int attackSequence;

    public static MagicSummonVisual Create(MagicStatEntry stats, Transform parent, int attackSequence)
    {
        var root = new GameObject("Pursuing familiar visual");
        root.transform.SetParent(parent,false);
        var visual = root.AddComponent<MagicSummonVisual>();
        visual.material = CombatPresentation.CreateEffectMaterial(new Color(1f,.65f,.15f,.9f));
        var authored = CombatPresentation.InstantiateVfx(stats.summonVfxPrefab,parent.position,parent.rotation,root.transform);
        visual.attackSequence = attackSequence;
        if (authored != null)
        {
            var animator = authored.GetComponentInChildren<Animator>(true);
            visual.animationPlayer = new SummonAnimationPlayer(animator, stats, attackSequence);
        }
        if (authored == null)
        {
            var bodyRoot = new GameObject("Arcane familiar");
            bodyRoot.transform.SetParent(root.transform,false);
            visual.body=bodyRoot.transform;
            visual.AddOrb(bodyRoot.transform,Vector3.zero,Vector3.one*.8f);
            visual.AddOrb(bodyRoot.transform,new Vector3(0,0,.45f),new Vector3(.22f,.22f,.3f));
            MagicAreaVisual.Ring(bodyRoot.transform,visual.material,Quaternion.Euler(25,0,15));
            MagicAreaVisual.Ring(bodyRoot.transform,visual.material,Quaternion.Euler(65,0,-15));
        }
        visual.targetRing = MagicAreaVisual.Ring(null,visual.material,Quaternion.identity);
        visual.targetRing.gameObject.SetActive(false);
        return visual;
    }

    private void AddOrb(Transform parent,Vector3 offset,Vector3 scale)
    {
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.transform.SetParent(parent,false); orb.transform.localPosition=offset; orb.transform.localScale=scale;
        var collider = orb.GetComponent<Collider>(); collider.enabled=false; Destroy(collider);
        var renderer = orb.GetComponent<Renderer>(); renderer.sharedMaterial=material;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    public void SetLock(Player victim,float charge) { target=victim; progress=charge; }

    public void SetMotion(bool isMoving, int sequence) { moving = isMoving; attackSequence = sequence; }

    public void Retire()
    {
        if (retiring) return;
        retiring = true;
        target = null; progress = 0f;
        if (targetRing != null) targetRing.gameObject.SetActive(false);
        float seconds = animationPlayer?.PlayDeath() ?? 0f;
        if (seconds > 0f)
        {
            // The network object despawns after .2 s; keep only the non-colliding visual for the clip.
            transform.SetParent(null, true);
            Destroy(gameObject, seconds);
        }
        else Destroy(gameObject);
    }

    private void LateUpdate()
    {
        animationPlayer?.Render(moving, attackSequence, Time.deltaTime);
        if (retiring) return;
        if (body != null) body.localRotation=Quaternion.Euler(0,Time.time*60f,0);
        bool visible=target!=null && target.Object!=null && target.Object.IsValid && target.IsAlive && progress>0f &&
            !target.IsHiddenBySmokeFor(Player.LocalPlayer);
        targetRing.gameObject.SetActive(visible);
        if (!visible) return;
        Camera cam=Camera.main;
        targetRing.transform.position=target.LockAimPoint;
        if (cam!=null) targetRing.transform.rotation=cam.transform.rotation*Quaternion.Euler(90,0,0);
        targetRing.transform.localScale=Vector3.one*Mathf.Lerp(2.8f,.9f,progress);
    }

    private void OnDestroy()
    {
        animationPlayer?.Dispose();
        if (targetRing!=null) Destroy(targetRing.gameObject);
        if (material!=null) Destroy(material);
    }
}
