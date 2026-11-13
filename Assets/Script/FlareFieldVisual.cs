using UnityEngine;
using UnityEngine.Rendering;

// One retained core/smoke system per pooled field, not GameObjects per decorative spark.
public sealed class FlareFieldVisual : MonoBehaviour
{
    public const float SmokeLifetime = 1.8f;
    private readonly ParticleSystem.Particle[] heads = new ParticleSystem.Particle[96];
    private readonly GameObject[] customHeads = new GameObject[96];
    private readonly bool[] seen = new bool[96];
    private ParticleSystem cores, smoke;
    private Material coreMaterial, smokeMaterial;
    private MagicStatEntry stats;
    private int count, lastBurst;
    private float nextSmoke;
    private bool emitSmoke;

    public static FlareFieldVisual Create(Transform owner, MagicStatEntry stats)
    {
        var root=new GameObject("Three-way rear flare bursts");root.SetActive(false);root.transform.SetParent(owner,false);
        var view=root.AddComponent<FlareFieldVisual>();view.stats=stats;
        view.cores=view.CreateParticles("Bright decoy heads",true);
        view.smoke=view.CreateParticles("Lingering flare smoke",false);
        return view;
    }
    public void Configure(MagicStatEntry entry)
    {
        stats = entry;
        if (coreMaterial != null && coreMaterial.HasProperty("_Intensity"))
            coreMaterial.SetFloat("_Intensity", Mathf.Max(1f, stats.flareVfxBrightness) * 2f);
    }
    private ParticleSystem CreateParticles(string name,bool bright)
    {
        var go=new GameObject(name);go.transform.SetParent(transform,false);
        var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.loop=true;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.maxParticles=bright?96:2048;main.startLifetime=bright?6.2f:SmokeLifetime;
        main.startSpeed=0f;main.startSize=bright?.5f:1f;
        var emission=ps.emission;emission.enabled=false;
        var shape=ps.shape;shape.enabled=false;
        var renderer=ps.GetComponent<ParticleSystemRenderer>();
        var shader=Resources.Load<Shader>(bright?"FlareParticle":"UtilityMagicParticle");
        var material=shader!=null?new Material(shader):CombatPresentation.CreateEffectMaterial(Color.white);
        renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        if(bright)
        {
            coreMaterial=material;
            if(material.HasProperty("_Intensity"))material.SetFloat("_Intensity",Mathf.Max(1f,stats.flareVfxBrightness)*2f);
            renderer.renderMode=ParticleSystemRenderMode.Stretch;renderer.lengthScale=2.5f;renderer.velocityScale=.06f;
        }
        else
        {
            smokeMaterial=material;
            var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.Linear(0,.3f,1,2f));
            var color=ps.colorOverLifetime;color.enabled=true;
            var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.55f,.6f,.65f),1)},
                new[]{new GradientAlphaKey(.45f,0),new GradientAlphaKey(.25f,.6f),new GradientAlphaKey(0,1)});color.color=g;
        }
        ps.Play(false);return ps;
    }
    public void BeginFrame()
    {
        count=0;System.Array.Clear(seen,0,seen.Length);
        emitSmoke=Time.time>=nextSmoke;
        if(emitSmoke)nextSmoke=Time.time+.08f;
    }
    public void SetHead(int slot,Vector3 position,Vector3 velocity,float age,float lifetime)
    {
        seen[slot]=true;
        float scale=Mathf.Max(.1f,stats.flareVfxScale);
        heads[count++]=new ParticleSystem.Particle {
            position=position,velocity=velocity,startSize=.4f*scale,
            startColor=new Color(1f,.7f,.25f,Mathf.Clamp01((lifetime-age)*3)),
            startLifetime=lifetime,remainingLifetime=Mathf.Max(.01f,lifetime-age),randomSeed=(uint)(slot+1) };
        if(emitSmoke)
            smoke.Emit(new ParticleSystem.EmitParams {position=position,velocity=Vector3.up*.5f,
                startSize=.7f*scale,startColor=new Color(.85f,.88f,.9f,.7f)},1);
        // Optional authored replacement rents once per live head; the pool restarts it on reuse.
        if(stats.utilityVfxPrefab==null)return;
        if(customHeads[slot]==null)
        {
            customHeads[slot]=CombatPresentation.InstantiateVfx(stats.utilityVfxPrefab,position,Quaternion.identity,transform);
            if(customHeads[slot]!=null)customHeads[slot].transform.localScale*=scale;
        }
        var custom=customHeads[slot];if(custom==null)return;
        custom.transform.SetPositionAndRotation(position,velocity.sqrMagnitude>.001f?Quaternion.LookRotation(velocity):Quaternion.identity);
        if(!custom.activeSelf)custom.SetActive(true);
    }
    public void EndFrame(int burst)
    {
        cores.SetParticles(heads,stats.utilityVfxPrefab==null?count:0);
        for(int i=0;i<customHeads.Length;i++)
            if(!seen[i] && customHeads[i]!=null) ReleaseCustomHead(i);
        if(burst>lastBurst)
        {
            BattleAudio.PlayMagic(MagicType.Flare,MagicSound.EffectLoop,count>0?heads[0].position:transform.position);
            lastBurst=burst;
        }
    }
    private void OnEnable()
    {
        if(cores!=null)cores.Play(false);if(smoke!=null)smoke.Play(false);
    }
    private void OnDisable()
    {
        lastBurst=0;nextSmoke=0;
        if(cores!=null)cores.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        if(smoke!=null)smoke.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        for(int i=0;i<customHeads.Length;i++) ReleaseCustomHead(i);
    }
    private void ReleaseCustomHead(int index)
    {
        if (customHeads[index] == null) return;
        MagicVfxPool.Release(customHeads[index]);
        customHeads[index] = null;
    }
    private void OnDestroy()
    {
        for(int i=0;i<customHeads.Length;i++) ReleaseCustomHead(i);
        if(coreMaterial!=null)Destroy(coreMaterial);if(smokeMaterial!=null)Destroy(smokeMaterial);
    }
}
