using UnityEngine;
using UnityEngine.SceneManagement;

// Sample after the character's LateUpdate bone pose. History stays in world space.
[DefaultExecutionOrder(10000)]
public sealed class BladeSlashVisual : MonoBehaviour
{
    const int Segments = 48;
    const int HistoryCapacity = 128;
    static RaycastHit[] wallHits = new RaycastHit[32];
    readonly Vector3[] roots = new Vector3[HistoryCapacity];
    readonly Vector3[] tips = new Vector3[HistoryCapacity];
    Mesh mesh;
    Mesh swordMesh;
    Material swordMaterial;
    MeshRenderer swordRenderer;
    Transform swordTransform;
    Material material, edgeMaterial;
    MeshRenderer ribbon;
    LineRenderer blade, rim;
    Vector3[] vertices;
    Player owner;
    int samples;
    float age, duration, radius;
    bool chargePreview;
    Vector3 sweepOrigin;
    Quaternion sweepFrame;
    float sweepAngle;
    public void SetSweep(Vector3 origin, Vector3 aim, float angle)
    {
        sweepOrigin=origin;
        sweepFrame=Quaternion.LookRotation(aim.sqrMagnitude>.001f?aim:owner.transform.forward);
        sweepAngle=angle;
    }
    public static Vector3 SweepPoint(Player player, Vector3 origin, Quaternion frame, float angle, float range, float progress)
    {
        float a=Mathf.Lerp(angle*.5f,-angle*.5f,progress)*Mathf.Deg2Rad;
        Vector3 endpoint=origin+frame*new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*range*player.BladeVisualScale.z;
        return ProjectToSurface(player,origin,endpoint);
    }
    public static BladeSlashVisual Play(Player owner, float radius, float duration, Texture2D texture = null, bool chargePreview = false)
    {
        if (owner == null) return null;
        var root = new GameObject("Wand blade trail");
        var e = root.AddComponent<BladeSlashVisual>();
        e.chargePreview=chargePreview;
        e.owner=owner; e.duration=Mathf.Max(.2f,duration); e.radius=radius;
        e.vertices=new Vector3[(Segments+1)*2];
        var uv=new Vector2[e.vertices.Length];
        var triangles=new int[Segments*6];
        for(int i=0;i<=Segments;i++)
        {
            uv[i*2]=new Vector2(i/(float)Segments,0); uv[i*2+1]=new Vector2(i/(float)Segments,1);
            if(i==Segments) continue;
            int v=i*2,k=i*6;
            triangles[k]=v; triangles[k+1]=v+2; triangles[k+2]=v+1;
            triangles[k+3]=v+1; triangles[k+4]=v+2; triangles[k+5]=v+3;
        }
        e.mesh=new Mesh {name="Wand sampled slash"};
        e.mesh.MarkDynamic(); e.mesh.vertices=e.vertices; e.mesh.uv=uv; e.mesh.triangles=triangles;
        root.AddComponent<MeshFilter>().sharedMesh=e.mesh;
        e.ribbon=root.AddComponent<MeshRenderer>();
        Shader shader=Resources.Load<Shader>("BladeSlashTrail");
        e.material=shader!=null?new Material(shader):CombatPresentation.CreateEffectMaterial(Color.white);
        if(texture!=null)e.material.SetTexture("_MainTex",texture);
        e.ribbon.sharedMaterial=e.material;
        e.ribbon.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; e.ribbon.receiveShadows=false;
        e.edgeMaterial=CombatPresentation.CreateEffectMaterial(new Color(.9f,.65f,1f,.95f));
        e.blade=e.Line("Wand blade",2,Mathf.Max(.3f,radius*.065f));
        e.CreateSword();
        e.rim=e.Line("Surface slash contour",Segments+1,Mathf.Max(.12f,radius*.012f));
        e.ribbon.enabled=e.blade.enabled=e.rim.enabled=false;
        return e;
    }
    void CreateSword()
    {
        Shader shader=Resources.Load<Shader>("BladeRuneSword");
        if(shader==null)return;
        var root=new GameObject("Runic greatsword");
        root.transform.SetParent(transform,false);
        swordTransform=root.transform;
        // Cross sections: broad flat faces, bevelled edges and a flared spear tip.
        float[] lengths={0f,.08f,.76f,.82f,1f};
        float[] widths={.28f,.5f,.5f,.65f,0f};
        var v=new Vector3[40]; var uv=new Vector2[40];
        var indices=new int[4*8*6];
        for(int ring=0;ring<5;ring++)
        {
            float w=widths[ring];
            Vector2[] section={new Vector2(-w,0),new Vector2(-w*.72f,.5f),new Vector2(w*.72f,.5f),new Vector2(w,0),
                new Vector2(w,-.01f),new Vector2(w*.72f,-.5f),new Vector2(-w*.72f,-.5f),new Vector2(-w,-.01f)};
            for(int j=0;j<8;j++)
            {
                int k=ring*8+j;
                v[k]=new Vector3(section[j].x,section[j].y,lengths[ring]);
                uv[k]=new Vector2(section[j].x+.5f,lengths[ring]);
                if(ring==4)continue;
                int next=ring*8+(j+1)%8, t=(ring*8+j)*6;
                indices[t]=k;indices[t+1]=next;indices[t+2]=k+8;
                indices[t+3]=next;indices[t+4]=next+8;indices[t+5]=k+8;
            }
        }
        swordMesh=new Mesh{name="Bevelled runic greatsword"};
        swordMesh.vertices=v;swordMesh.uv=uv;swordMesh.triangles=indices;
        swordMesh.RecalculateNormals();swordMesh.RecalculateBounds();
        root.AddComponent<MeshFilter>().sharedMesh=swordMesh;
        swordMaterial=new Material(shader);
        swordRenderer=root.AddComponent<MeshRenderer>();
        swordRenderer.sharedMaterial=swordMaterial;
        swordRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        swordRenderer.receiveShadows=false;swordRenderer.enabled=false;
    }
    LineRenderer Line(string name,int count,float width)
    {
        var root=new GameObject(name); root.transform.SetParent(transform,false);
        var line=root.AddComponent<LineRenderer>();
        line.sharedMaterial=edgeMaterial; line.useWorldSpace=true; line.positionCount=count;
        line.startWidth=width; line.endWidth=width*.15f;
        line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows=false;
        return line;
    }
    public static Vector3 ProjectToSurface(Player owner,Vector3 origin,Vector3 endpoint)
    {
        Vector3 delta=endpoint-origin;
        float distance=delta.magnitude;
        if(owner==null || distance<.001f) return endpoint;
        PhysicsScene physics=owner.gameObject.scene.GetPhysicsScene();
        int count;
        while(true)
        {
            count=physics.Raycast(origin,delta/distance,wallHits,distance,owner.BladeSurfaceMask,QueryTriggerInteraction.Ignore);
            if(count<wallHits.Length)break;
            wallHits=new RaycastHit[wallHits.Length*2];
        }
        float nearest=distance;
        Vector3 result=endpoint;
        for(int i=0;i<count;i++)
        {
            var hit=wallHits[i];
            if(hit.collider==null || hit.distance>nearest || hit.collider.GetComponentInParent<Player>()!=null ||
                hit.collider.GetComponentInParent<MagicProjectile>()!=null)continue;
            nearest=hit.distance;
            result=hit.point+hit.normal*.04f;
        }
        return result;
    }
    void LateUpdate()
    {
        if(chargePreview)
        {
            if(owner==null || !owner.isActiveAndEnabled || !owner.IsAlive || !owner.BladeCharging) {Destroy(gameObject);return;}
            radius=owner.BladeChargedVisualRange;
            SetSweep(owner.LockAimPoint,owner.BladePreviewAim,owner.BladeVisualAngle);
            age=0;
        }
        else age+=Time.deltaTime;
        float t=age/duration;
        if(owner==null || !owner.isActiveAndEnabled || !owner.IsAlive || t>=1.5f) {Destroy(gameObject);return;}
        bool cutting=chargePreview || t<.43f;
        blade.enabled=cutting && swordRenderer==null;
        if(swordRenderer!=null)swordRenderer.enabled=cutting;
        if(cutting)
        {
            Vector3 start=owner.BladeSlashAnchor.position;
            Vector3 tip=start+owner.BladeSlashDirection*radius*owner.BladeVisualScale.z;
            tip=ProjectToSurface(owner,start,tip);
            if(swordTransform!=null)
            {
                Vector3 direction=tip-start;
                swordRenderer.enabled=direction.sqrMagnitude>.001f;
                if(swordRenderer.enabled)
                {
                    swordTransform.SetPositionAndRotation(start,Quaternion.LookRotation(direction,Vector3.up));
                    swordTransform.localScale=new Vector3(radius*owner.BladeVisualScale.x,radius*owner.BladeVisualScale.y,direction.magnitude);
                }
            }
            if(chargePreview)return;
            if(samples==roots.Length)
            {
                System.Array.Copy(roots,1,roots,0,HistoryCapacity-1); System.Array.Copy(tips,1,tips,0,HistoryCapacity-1); samples--;
            }
            roots[samples]=start; tips[samples]=tip; samples++;
            blade.SetPosition(0,start);blade.SetPosition(1,tip);
        }
        if(samples==0)return;
        ribbon.enabled=rim.enabled=samples>1;
        float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,1.5f,t));
        material.color=new Color(.7f,.25f,1f,.85f*fade);edgeMaterial.color=new Color(.95f,.8f,1f,fade);
        for(int i=0;i<=Segments;i++)
        {
            float index=i/(float)Segments*(samples-1);
            int lo=Mathf.FloorToInt(index),hi=Mathf.Min(lo+1,samples-1);
            Vector3 start=Vector3.Lerp(roots[lo],roots[hi],index-lo);
            Vector3 tip=Vector3.Lerp(tips[lo],tips[hi],index-lo);
            // Reproject interpolated points too, so a connecting segment follows wall corners.
            tip=ProjectToSurface(owner,start,tip);
            vertices[i*2]=tip; vertices[i*2+1]=Vector3.Lerp(start,tip,.35f);
            rim.SetPosition(i,tip);
        }
        mesh.vertices=vertices;mesh.RecalculateBounds();
    }
    void OnDestroy()
    {
        if(mesh!=null)Destroy(mesh);
        if(material!=null)Destroy(material);
        if(edgeMaterial!=null)Destroy(edgeMaterial);
        if(swordMesh!=null)Destroy(swordMesh);
        if(swordMaterial!=null)Destroy(swordMaterial);
    }
}
