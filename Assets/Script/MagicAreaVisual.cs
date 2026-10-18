using UnityEngine;

// Display only. Radius is the actual spherical damage radius, including at flight altitude.
public sealed class MagicAreaVisual : MonoBehaviour
{
    private Material material;
    private LineRenderer countdown;
    private Vector3 authoredScale = Vector3.one;

    public static MagicAreaVisual Create(GameObject prefab, string label)
    {
        var root = new GameObject(label);
        var visual = root.AddComponent<MagicAreaVisual>();
        var custom = CombatPresentation.InstantiateVfx(prefab,Vector3.zero,Quaternion.identity,root.transform);
        if (custom != null) { visual.authoredScale = root.transform.localScale; return visual; }
        visual.material = CombatPresentation.CreateEffectMaterial(Color.cyan);
        Ring(root.transform,visual.material,Quaternion.identity);
        Ring(root.transform,visual.material,Quaternion.Euler(90,0,0));
        Ring(root.transform,visual.material,Quaternion.Euler(0,0,90));
        visual.countdown = Ring(root.transform,visual.material,Quaternion.identity);
        return visual;
    }

    public static LineRenderer Ring(Transform parent, Material material, Quaternion rotation)
    {
        var obj = new GameObject("Magic ring");
        obj.transform.SetParent(parent,false);
        obj.transform.localRotation = rotation;
        var ring = obj.AddComponent<LineRenderer>();
        ring.sharedMaterial = material; ring.useWorldSpace = false; ring.loop = true;
        ring.positionCount = 64; ring.widthMultiplier = .14f;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i=0;i<64;i++)
        {
            float angle = i * Mathf.PI * 2f / 64;
            ring.SetPosition(i,new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)));
        }
        return ring;
    }

    public void SetArea(Vector3 center, float radius, Color color, float progress)
    {
        transform.position = center;
        transform.localScale = authoredScale * Mathf.Max(.1f,radius);
        if (material != null) material.color = color;
        if (countdown != null)
        {
            countdown.gameObject.SetActive(progress > 0f);
            countdown.transform.localScale = Vector3.one * Mathf.Max(.02f,1f-progress);
        }
    }

    public static void Lightning(Vector3 point, MagicStatEntry stats)
    {
        Vector3 top = point + Vector3.up * Mathf.Max(15f,stats.radius*2f);
        if (stats.beamVfxPrefab != null)
        {
            var beam = CombatMagicVisual.Beam(MagicType.Thunder,stats.beamVfxPrefab);
            beam.SetEndpoints(top,point);
            Destroy(beam.gameObject,Mathf.Max(.1f,stats.vfxLifetime));
            return;
        }
        var root = new GameObject("Thunder strike");
        Material material = CombatPresentation.CreateEffectMaterial(new Color(.8f,.9f,1f,1f));
        var line = root.AddComponent<LineRenderer>();
        line.sharedMaterial=material; line.useWorldSpace=true; line.positionCount=9;
        line.startWidth=.6f; line.endWidth=.18f;
        for (int i=0;i<9;i++)
        {
            Vector3 p = Vector3.Lerp(top,point,i/8f);
            if (i>0 && i<8) p += new Vector3((i%2==0?1f:-1f)*1.1f,0,Mathf.Sin(i*2f)*.8f);
            line.SetPosition(i,p);
        }
        root.AddComponent<CombatTransientEffect>().Initialize(.35f,Vector3.one,Vector3.one,new[]{material});
    }

    private void OnDestroy() { if (material != null) Destroy(material); }
}
