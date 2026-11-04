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
    private CatSummonPresentation catPresentation;
    private Quaternion shownRotation;
    private Vector3 previousFlightHeading;
    private float bankAngle;

    public static MagicSummonVisual Create(MagicStatEntry stats, Transform parent, int attackSequence, MagicProjectile source = null)
    {
        var root = new GameObject("Pursuing familiar visual");
        root.transform.SetParent(parent,false);
        var visual = root.AddComponent<MagicSummonVisual>();
        visual.shownRotation = parent.rotation;
        visual.previousFlightHeading = parent.forward;
        visual.material = CombatPresentation.CreateEffectMaterial(new Color(1f,.65f,.15f,.9f));
        visual.catPresentation = CatSummonPresentation.Create(source, root.transform, attackSequence);
        var authored = visual.catPresentation != null ? visual.catPresentation.gameObject :
            CombatPresentation.InstantiateVfx(stats.summonVfxPrefab,parent.position,parent.rotation,root.transform);
        visual.attackSequence = attackSequence;
        if (authored != null && visual.catPresentation == null)
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
        root.transform.localScale = Vector3.one * SummonFlightMotion.VisualScale;
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

    public void SetMotion(bool isMoving, int sequence) { moving = isMoving; attackSequence = sequence; catPresentation?.SetAttack(sequence); }

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
            MagicVfxPool.Release(gameObject, seconds);
        }
        else MagicVfxPool.Release(gameObject);
    }

    private void LateUpdate()
    {
        animationPlayer?.Render(moving, attackSequence, Time.deltaTime);
        if (retiring) return;
        bool validTarget = target != null && target.Object != null && target.Object.IsValid && target.IsAlive;
        Vector3 gaze = validTarget ? target.LockAimPoint - transform.position :
            (transform.parent != null ? transform.parent.forward : shownRotation * Vector3.forward);
        shownRotation = SummonFlightMotion.SmoothLook(shownRotation, gaze, Time.deltaTime);
        Vector3 flightHeading = transform.parent != null ? transform.parent.forward : previousFlightHeading;
        float yawRate = Time.deltaTime > .0001f ? Vector3.SignedAngle(previousFlightHeading,flightHeading,Vector3.up) / Time.deltaTime : 0f;
        float desiredBank = moving ? -Mathf.Clamp(yawRate / 120f,-1f,1f) * 25f : 0f;
        bankAngle = Mathf.Lerp(bankAngle,desiredBank,1f - Mathf.Exp(-5f * Time.deltaTime));
        previousFlightHeading = flightHeading;
        transform.rotation = shownRotation * Quaternion.Euler(0,0,bankAngle);
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
