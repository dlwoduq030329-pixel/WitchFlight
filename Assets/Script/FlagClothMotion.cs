using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FlagClothMotion : MonoBehaviour
{
    public MeshFilter cloth;
    public AudioClip flutterClip;
    [Min(1f)] public float fullWindSpeed=60f;
    [Min(0f)] public float airDrag=1.2f;
    [Min(0f)] public float shapeStiffness=25f;
    private const int Columns=13, Rows=9;
    private Mesh mesh, sourceMesh;
    private Vector3[] rest, vertices, gridRest, points, previous, anchors;
    private Vector2[] uv;
    private AudioSource sound;
    private float phase, wind;
    private Vector3 lastPosition,lastScale;
    private Quaternion lastRotation;
    private bool sampled;
    private Transform torso;
    private readonly List<Link> links=new();
    private struct Link { public int a,b; public Link(int a,int b){this.a=a;this.b=b;} }
    public Vector3 MeanWorldOffset { get; private set; }
    public float MeasuredSpeed { get; private set; }

    private void Awake()=>Initialize();
    public void Initialize()
    {
        if(mesh!=null || cloth==null || cloth.sharedMesh==null)return;
        sourceMesh=cloth.sharedMesh;mesh=Instantiate(sourceMesh);mesh.name="Flag inertial cloth";mesh.MarkDynamic();
        cloth.sharedMesh=mesh;rest=mesh.vertices;vertices=new Vector3[rest.Length];uv=mesh.uv;
        gridRest=new Vector3[Columns*Rows];points=new Vector3[gridRest.Length];previous=new Vector3[gridRest.Length];anchors=new Vector3[gridRest.Length];
        for(int y=0;y<Rows;y++)for(int x=0;x<Columns;x++)
        {
            int index=y*Columns+x;Vector2 target=new Vector2(x/(float)(Columns-1),y/(float)(Rows-1));
            float best=float.PositiveInfinity;
            for(int v=0;v<uv.Length;v++){float d=(uv[v]-target).sqrMagnitude;if(d<best){best=d;gridRest[index]=rest[v];}}
            if(x>0)links.Add(new Link(index,index-1));if(y>0)links.Add(new Link(index,index-Columns));
            if(x>0 && y>0){links.Add(new Link(index,index-Columns-1));links.Add(new Link(index-1,index-Columns));}
            // Two-point bending links stop the fabric collapsing into a rope.
            if(x>1)links.Add(new Link(index,index-2));if(y>1)links.Add(new Link(index,index-2*Columns));
        }
        sound=GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        sound.playOnAwake=false;sound.loop=true;sound.clip=flutterClip;sound.spatialBlend=1;sound.dopplerLevel=0;
        sound.minDistance=2;sound.maxDistance=22;sound.rolloffMode=AudioRolloffMode.Logarithmic;
        sound.outputAudioMixerGroup=GameAudioSettings.Group(GameAudioCategory.SFX);
        ResetMotion();
    }
    public void SetTorso(Transform bone)=>torso=bone;
    public void ResetMotion()
    {
        sampled=false;wind=0;MeasuredSpeed=0;MeanWorldOffset=Vector3.zero;
        if(sound!=null)sound.Stop();
    }
    // Compatibility for the original asset check; runtime uses the measured attachment trajectory.
    public void Animate(float unusedSpeed,bool carried,bool audible,float dt)=>AnimateMotion(carried,audible,dt);
    public void AnimateMotion(bool carried,bool audible,float dt)
    {
        Initialize();if(mesh==null || dt<=0 || float.IsNaN(dt) || float.IsInfinity(dt))return;
        Vector3 pos=cloth.transform.position,scale=cloth.transform.lossyScale;Quaternion rot=cloth.transform.rotation;
        bool discontinuity=!sampled || dt>.1f || (pos-lastPosition).magnitude>Mathf.Max(5f,fullWindSpeed*dt*3f) || (scale-lastScale).sqrMagnitude>.001f;
        if(discontinuity)
        {
            for(int i=0;i<points.Length;i++)points[i]=previous[i]=cloth.transform.TransformPoint(gridRest[i]);
            lastPosition=pos;lastRotation=rot;lastScale=scale;sampled=true;MeasuredSpeed=0;
        }
        float frameTime=Mathf.Min(dt,.0667f);
        MeasuredSpeed=(pos-lastPosition).magnitude/dt;
        wind=Mathf.Lerp(wind,carried?Mathf.Clamp01(MeasuredSpeed/Mathf.Max(1,fullWindSpeed)):0,1-Mathf.Exp(-8*frameTime));
        int steps=Mathf.Clamp(Mathf.CeilToInt(frameTime*120),1,8);float h=frameTime/steps;
        // Fixed-size substeps with velocity rescaling avoid render-frame-rate dependence.
        for(int step=0;step<steps;step++)
        {
            float t=(step+1f)/steps;
            Matrix4x4 frame=Matrix4x4.TRS(Vector3.Lerp(lastPosition,pos,t),Quaternion.Slerp(lastRotation,rot,t),scale);
            for(int i=0;i<points.Length;i++)anchors[i]=frame.MultiplyPoint3x4(gridRest[i]);
            phase+=h*(2+12*wind);
            Vector3 normal=frame.MultiplyVector(Vector3.Cross(gridRest[Columns-1]-gridRest[0],gridRest[(Rows-1)*Columns]-gridRest[0])).normalized;
            for(int i=0;i<points.Length;i++)
            {
                if(i%Columns==0){points[i]=previous[i]=anchors[i];continue;}
                Vector3 velocity=(points[i]-previous[i])/Mathf.Max(lastStep,.0001f);
                velocity=Vector3.ClampMagnitude(velocity,180);
                Vector3 air=Vector3.right*.35f-velocity;
                float u=(i%Columns)/(float)(Columns-1);
                Vector3 acceleration=Physics.gravity+air*airDrag+(anchors[i]-points[i])*shapeStiffness;
                acceleration+=normal*(Mathf.Sin(phase-u*8+i*.13f)*(1+25*wind)*u);
                Vector3 old=points[i];points[i]+=velocity*h+Vector3.ClampMagnitude(acceleration,200)*h*h;previous[i]=old;
            }
            for(int pass=0;pass<10;pass++)
            {
                foreach(var link in links)
                {
                    Vector3 delta=points[link.b]-points[link.a];float length=delta.magnitude;
                    if(length<.000001f)continue;
                    float restLength=(anchors[link.b]-anchors[link.a]).magnitude;
                    float wa=link.a%Columns==0?0:1,wb=link.b%Columns==0?0:1;
                    if(wa+wb==0)continue;
                    Vector3 correction=delta*((length-restLength)/length/(wa+wb));
                    points[link.a]+=correction*wa;points[link.b]-=correction*wb;
                }
                for(int i=0;i<points.Length;i++)
                {
                    int pin=i/Columns*Columns;
                    if(i==pin){points[i]=anchors[i];continue;}
                    float reach=(anchors[i]-anchors[pin]).magnitude*1.02f;
                    points[i]=anchors[pin]+Vector3.ClampMagnitude(points[i]-anchors[pin],reach);
                    // A small torso exclusion keeps loose cloth out of the character's chest.
                    if(torso!=null)
                    {
                        Vector3 away=points[i]-torso.position;
                        if(away.sqrMagnitude<.0324f && away.sqrMagnitude>.000001f)points[i]=torso.position+away.normalized*.18f;
                    }
                }
            }
            lastStep=h;
        }
        lastPosition=pos;lastRotation=rot;lastScale=scale;
        MeanWorldOffset=Vector3.zero;
        for(int i=0;i<points.Length;i++)MeanWorldOffset+=points[i]-anchors[i];
        MeanWorldOffset/=points.Length;
        for(int i=0;i<vertices.Length;i++)
        {
            float gx=Mathf.Clamp01(uv[i].x)*(Columns-1),gy=Mathf.Clamp01(uv[i].y)*(Rows-1);
            int x=Mathf.Min(Columns-2,Mathf.FloorToInt(gx)),y=Mathf.Min(Rows-2,Mathf.FloorToInt(gy));int a=y*Columns+x;
            Vector3 world=Vector3.Lerp(Vector3.Lerp(points[a],points[a+1],gx-x),Vector3.Lerp(points[a+Columns],points[a+Columns+1],gx-x),gy-y);
            vertices[i]=cloth.transform.InverseTransformPoint(world);
        }
        mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();
        sound.volume=audible && carried?wind*.28f:0;sound.pitch=Mathf.Lerp(.75f,1.8f,wind);
        if(sound.volume>.005f && sound.clip!=null){if(!sound.isPlaying)sound.Play();}else if(sound.isPlaying)sound.Stop();
    }
    private float lastStep=1f/120f;
    private void OnDisable(){ResetMotion();}
    private void OnDestroy()
    {
        if(mesh==null)return;if(cloth!=null)cloth.sharedMesh=sourceMesh;
        if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);
    }
}
