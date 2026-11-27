using System.Collections.Generic;
using UnityEngine;

// Cosmetic cloud banks. Authored child transforms are editable bank centers; no physics/network objects.
[ExecuteAlways]
public sealed class HighAltitudeClouds : MonoBehaviour
{
    [SerializeField] private Material cloudAsset, ringAsset;
    [Tooltip("플레이어가 통과할 때 생기는 구멍의 반지름"), Min(.1f)] public float holeRadius = 18;
    [Tooltip("구멍이 완전히 메워지는 시간(초). 작을수록 빨리 복구"), Min(.2f)] public float refillSeconds = 2;
    [Tooltip("구름 불투명도. 0은 투명, 1은 진하게"), Range(0,1)] public float opacity = .65f;
    [Min(.1f)] public float ringRadius = 2.5f;
    [Min(.1f)] public float magicHoleRadius = 10f;
    private static readonly HashSet<HighAltitudeClouds> active = new();

    private sealed class Track { public Vector3 previous; public float lastSeen, nextEmission; }
    private readonly Dictionary<Transform,Track> tracks = new();
    private readonly List<Transform> expired = new();
    private MeshRenderer[] cloudRenderers;
    private MaterialPropertyBlock previewBlock;
    private float appliedOpacity = -1;
    private readonly Vector4[] holesA = new Vector4[16], holesB = new Vector4[16];
    private readonly float[] born = new float[16], radii = new float[16];
    private readonly Vector3[] direction = new Vector3[16];
    private readonly LineRenderer[] rings = new LineRenderer[16];
    private Material cloudMaterial, ringMaterial;

    private int cursor;
    private void InitializeVisuals()
    {
        cloudRenderers=GetComponentsInChildren<MeshRenderer>(true);
        var authoredRings=GetComponentsInChildren<LineRenderer>(true);
        for(int i=0;i<rings.Length;i++)
        {
            rings[i]=i<authoredRings.Length?authoredRings[i]:null;
            born[i]=-1000;holesA[i]=holesB[i]=Vector4.zero;
            if(rings[i]==null)continue;
            rings[i].useWorldSpace=true;rings[i].loop=true;rings[i].positionCount=48;
            rings[i].widthMultiplier=1.2f;rings[i].enabled=false;
        }
        if(Application.isPlaying && cloudAsset!=null && ringAsset!=null)
        {
            cloudMaterial=new Material(cloudAsset);ringMaterial=new Material(ringAsset);
            foreach(var renderer in cloudRenderers) renderer.sharedMaterial=cloudMaterial;
            foreach(var ring in rings)if(ring!=null)ring.sharedMaterial=ringMaterial;
        }
        appliedOpacity=-1;
        ApplyOpacity();
    }
    private void ApplyOpacity()
    {
        if(cloudRenderers==null || Mathf.Approximately(appliedOpacity,opacity))return;
        previewBlock ??= new MaterialPropertyBlock();
        foreach(var renderer in cloudRenderers)
        {
            if(renderer==null)continue;
            renderer.GetPropertyBlock(previewBlock);
            previewBlock.SetColor("_Color",new Color(.94f,.97f,1,opacity));
            renderer.SetPropertyBlock(previewBlock);previewBlock.Clear();
        }
        appliedOpacity=opacity;
    }
    private void Update()
    {
        ApplyOpacity();
        if(!Application.isPlaying || cloudMaterial==null)return;
        float now=Time.time;
        foreach(var player in Player.ActiveCombatants)
        {
            if(player==null||!player.isActiveAndEnabled||!player.IsAlive||player.gameObject.scene!=gameObject.scene)continue;
            TrackPassage(player.transform,holeRadius,now,false);
        }
        foreach(var projectile in MagicProjectile.ActiveProjectiles)
            if(projectile!=null && projectile.isActiveAndEnabled && projectile.gameObject.scene==gameObject.scene)
                TrackPassage(projectile.transform,magicHoleRadius,now,true);
        expired.Clear();foreach(var pair in tracks)if(pair.Key==null||now-pair.Value.lastSeen>1)expired.Add(pair.Key);
        foreach(var player in expired)tracks.Remove(player);
        for(int i=0;i<16;i++)
        {
            float age=now-born[i];float life=Mathf.Clamp01(age/Mathf.Max(.2f,refillSeconds));
            float open=Mathf.SmoothStep(0,1,age/.12f)*(1-Mathf.SmoothStep(.35f,1,life));
            holesA[i].w=radii[i]*open;holesB[i].w=open;
            var line=rings[i];if(line==null)continue;line.enabled=age>=0&&age<.8f;
            if(!line.enabled)continue;
            var normal=direction[i];var right=Vector3.Cross(normal,Mathf.Abs(normal.y)>.95f?Vector3.right:Vector3.up).normalized;var up=Vector3.Cross(normal,right);
            Vector3 center=((Vector3)holesA[i]+(Vector3)holesB[i])*.5f;
            float radius=ringRadius*(.65f+age*1.1f);
            for(int v=0;v<48;v++){float angle=v*Mathf.PI*2/48;float irregular=1+.04f*Mathf.Sin(v*3.1f+age*4);line.SetPosition(v,center+(right*Mathf.Cos(angle)+up*Mathf.Sin(angle))*radius*irregular);}
            var color=new Color(.9f,.96f,1,(1-age/.8f)*.5f);line.startColor=line.endColor=color;
        }
        cloudMaterial.SetVectorArray("_HolesA",holesA);cloudMaterial.SetVectorArray("_HolesB",holesB);
    }
    private void TrackPassage(Transform source,float radius,float now,bool magic)
    {
        Vector3 pos=source.position;
        if(!tracks.TryGetValue(source,out var track)){track=new Track{previous=pos};tracks.Add(source,track);}
        Vector3 delta=pos-track.previous;
        if(delta.sqrMagnitude>.01f && (magic || delta.sqrMagnitude<2500) && now>=track.nextEmission && CrossesCloud(track.previous,pos))
        { OpenPassage(track.previous,pos,radius);track.nextEmission=now+.3f; }
        track.previous=pos;track.lastSeen=now;
    }
    private void OpenPassage(Vector3 from,Vector3 to,float radius)
    {
        if((to-from).sqrMagnitude<.001f)return;
        int slot=cursor++%16;born[slot]=Time.time;radii[slot]=radius;direction[slot]=(to-from).normalized;
        Vector3 a=from-direction[slot]*12,b=to+direction[slot]*12;
        holesA[slot]=new Vector4(a.x,a.y,a.z,radius);holesB[slot]=new Vector4(b.x,b.y,b.z,1);
    }
    // Instant beams have no moving projectile; their presentation path reports the complete segment.
    public static void MagicPassage(Vector3 from,Vector3 to)
    {
        foreach(var clouds in active)
            if(clouds!=null && clouds.cloudMaterial!=null && clouds.CrossesCloud(from,to)) clouds.OpenPassage(from,to,clouds.magicHoleRadius);
    }
    private bool CrossesCloud(Vector3 a,Vector3 b)
    {
        if(cloudRenderers==null)return false;
        foreach(var renderer in cloudRenderers)
        {
            if(renderer==null || !renderer.gameObject.activeInHierarchy)continue;
            // Conservative billboard bounds follow edited transforms, including newly scaled puffs.
            float radius=renderer.transform.lossyScale.magnitude*.5f;
            var bounds=new Bounds(renderer.transform.position,Vector3.one*radius*2);
            Vector3 delta=b-a;
            if(bounds.Contains(a)||bounds.Contains(b))return true;
            if(delta.sqrMagnitude>.001f && bounds.IntersectRay(new Ray(a,delta.normalized),out float distance) && distance<=delta.magnitude)return true;
        }
        return false;
    }
    private void OnEnable()
    {
        InitializeVisuals();
        if(Application.isPlaying)active.Add(this);
    }
    private void OnDisable()
    {
        active.Remove(this);tracks.Clear();
        if(cloudRenderers!=null)foreach(var renderer in cloudRenderers)if(renderer!=null && cloudAsset!=null)renderer.sharedMaterial=cloudAsset;
        foreach(var ring in rings)if(ring!=null){ring.enabled=false;if(ringAsset!=null)ring.sharedMaterial=ringAsset;}
        if(cloudMaterial!=null){if(Application.isPlaying)Destroy(cloudMaterial);else DestroyImmediate(cloudMaterial);cloudMaterial=null;}
        if(ringMaterial!=null){if(Application.isPlaying)Destroy(ringMaterial);else DestroyImmediate(ringMaterial);ringMaterial=null;}
    }
    private void OnTransformChildrenChanged(){if(!Application.isPlaying)cloudRenderers=GetComponentsInChildren<MeshRenderer>(true);appliedOpacity=-1;}
}
