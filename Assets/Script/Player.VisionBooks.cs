using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] public int VisionOpenBooks { get; private set; }
    public bool HasVisionEquipped => Magic1 == MagicType.Vision || Magic2 == MagicType.Vision;
    public bool VisionReady => HasVisionEquipped && VisionOpenBooks >= Mathf.Clamp(VisionBookSettings.Default.bookCount,1,12);
    private float VisionHealthCost(MagicStatEntry stats) => stats.magic == MagicType.Dark && (VisionReady || stats.visionBooksEmpowered) ? 0f : stats.healthCost;
    private bool PrepareVisionBooks(ref MagicStatEntry stats)
    {
        if (stats.visionBooksEmpowered) return true;
        if (!VisionReady) return false;
        VisionEmpowerment.Apply(ref stats);
        VisionOpenBooks = 0;
        return true;
    }
    [Networked] private TickTimer EmpoweredBladeSlowTimer { get; set; }
    [Networked] private int EmpoweredBladeSlowStacks { get; set; }
    private PlayerRef empoweredBindingAttacker;
    private NetworkId empoweredBindingId;
    private bool empoweredBinding;
    private TickTimer empoweredBindingTick;
    private void ResetVisionEffects()
    {
        empoweredBinding=false; empoweredBindingTick=TickTimer.None;
        EmpoweredBladeSlowStacks=0; EmpoweredBladeSlowTimer=TickTimer.None;
    }
    private float EmpoweredMovementMultiplier =>
        (TimerIsActive(EmpoweredBladeSlowTimer) ? 1f-.1f*EmpoweredBladeSlowStacks : 1f) *
        MagicProjectile.EmpoweredThunderMovement(Runner,LockAimPoint,TeamIndex);
    private void TickVisionEffects()
    {
        if(!Object.HasStateAuthority || !IsAlive) return;
        if(!empoweredBinding) return;
        if(!BindingTimer.IsRunning || !empoweredBindingTick.IsRunning || empoweredBindingTick.TargetTick>BindingTimer.TargetTick)
        { empoweredBinding=false; return; }
        if(empoweredBindingTick.Expired(Runner))
        {
            TakeDamage(20f,empoweredBindingAttacker,MagicType.Binding,false,0,0,empoweredBindingId);
            empoweredBindingTick=TickTimer.CreateFromSeconds(Runner,1f);
        }
    }
    private void CompleteVisionBooks(MagicStatEntry stats, bool empowered, bool accepted)
    {
        if (stats.magic != MagicType.Vision || empowered || !HasVisionEquipped) return;
        VisionOpenBooks = Mathf.Clamp(VisionOpenBooks + (accepted ? 1 : -1), 0,
            Mathf.Clamp(VisionBookSettings.Default.bookCount, 1, 12));
    }
}

public static class VisionEmpowerment
{
    public static readonly Color Color = new Color(.65f,.18f,1f,1f);
    public static void Apply(ref MagicStatEntry s)
    {
        if(s.visionBooksEmpowered) return;
        s.visionBooksEmpowered = true;
        var settings = VisionBookSettings.Default;
        if(s.magic == MagicType.Razier) { s.damage *= 2f; s.healing *= 2f; }
        else if(s.magic == MagicType.BladeRain) s.damage *= 1.15f;
        else
        {
            if(s.damage>0) s.damage += Mathf.Max(s.damage*settings.empoweredDamageBonus,settings.minimumDamageBonus);
            if(s.damagePerSecond>0) s.damagePerSecond += Mathf.Max(s.damagePerSecond*settings.empoweredDamageBonus,settings.minimumDamageBonus);
        }
        s.projectileSpeed *= 2f; s.projectileTurnSpeed *= 2f;
        s.projectileRadius *= 1.5f; s.hitscanRadius *= 1.5f; s.allyHitscanRadius *= 1.5f;
        s.radius *= 1.5f; s.flareInterceptRadius*=1.5f; s.flareVfxScale*=1.5f; s.flareVfxSpeed*=2f;
        if(s.magic == MagicType.Fire || s.magic == MagicType.Ice) s.radius = 15f;
        if(s.magic == MagicType.Dark) s.healthCost = 0f;
        if(s.magic == MagicType.Curse && s.weaknessExplosionDamage>0)
            s.weaknessExplosionDamage += Mathf.Max(s.weaknessExplosionDamage*settings.empoweredDamageBonus,settings.minimumDamageBonus);
        if(s.effect == MagicEffectKind.PursuitSummon)
        {
            // Summon attack uses the common damage increase; duration and channel durability scale separately.
            s.effectDuration *= 1.5f;
            if (s.perkFirst?.id != PerkId.MineArming) s.summonChannelHealth *= 2f;
        }
        if(s.effect == MagicEffectKind.MagicStorm) s.stormDuration = .75f;
    }
}

// Restore authored values when a pooled effect is released.
public sealed class VisionEmpoweredVisual : MonoBehaviour
{
    [Header("Empowered spell electricity")]
    [Range(0f, 2f)] public float electricityIntensity = 1f;
    [Min(.001f)] public float electricityWidth = .025f;
    private Transform electricityRoot;
    private Material electricityMaterial;
    private readonly LineRenderer[] electricArcs = new LineRenderer[3];
    private readonly float[] nextArc = new float[3], arcEnd = new float[3];
    private readonly System.Random electricRandom = new System.Random();
    private Bounds electricBounds;
    private LineRenderer electricBeam;
    private float RandomElectric(float min, float max) => Mathf.Lerp(min, max, (float)electricRandom.NextDouble());
    private bool IsElectric(Transform candidate) => electricityRoot != null && candidate.IsChildOf(electricityRoot);

    private void PrepareElectricity()
    {
        electricBeam = null;
        foreach (var line in lines)
            if (line != null && line.positionCount > 1) { electricBeam = line; break; }
        electricBounds = new Bounds(Vector3.zero, Vector3.one * .6f);
        bool found = false;
        foreach (var renderer in renderers)
        {
            // Particle bounds can contain an entire old trail. Use mesh geometry for the spell body.
            if (renderer == null || renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer) continue;
            var b = renderer.localBounds;
            for (int corner = 0; corner < 8; corner++)
            {
                var p = b.center + Vector3.Scale(b.extents, new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                p = transform.InverseTransformPoint(renderer.transform.TransformPoint(p));
                if (!found) { electricBounds = new Bounds(p, Vector3.zero); found = true; }
                else electricBounds.Encapsulate(p);
            }
        }
        if (electricityRoot == null)
        {
            var shader = Resources.Load<Shader>("VisionPageEnergy");
            if (shader == null) return;
            electricityMaterial = new Material(shader);
            electricityMaterial.SetFloat("_Steady", 1f);
            electricityRoot = new GameObject("Empowered spell electricity").transform;
            electricityRoot.SetParent(transform, false);
            for (int i = 0; i < electricArcs.Length; i++)
            {
                var child = new GameObject("Arc " + i); child.transform.SetParent(electricityRoot, false);
                var line = child.AddComponent<LineRenderer>(); electricArcs[i] = line;
                line.sharedMaterial = electricityMaterial; line.useWorldSpace = false; line.positionCount = 13;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false; line.numCapVertices = 2;
                line.widthCurve = AnimationCurve.EaseInOut(0, .2f, .5f, 1f);
            }
        }
        electricityRoot.gameObject.SetActive(true);
        for (int i = 0; i < electricArcs.Length; i++) { nextArc[i] = Time.time + i * .035f; arcEnd[i] = 0; electricArcs[i].enabled = false; }
    }

    private void LateUpdate()
    {
        if (!applied || electricityRoot == null) return;
        for (int i = 0; i < electricArcs.Length; i++)
        {
            var line = electricArcs[i];
            if (Time.time >= nextArc[i])
            {
                float duration = RandomElectric(.07f, .16f);
                arcEnd[i] = Time.time + duration;
                nextArc[i] = arcEnd[i] + RandomElectric(.035f, .24f);
                float phase = RandomElectric(0, Mathf.PI*2), sweep = RandomElectric(1.1f, 2.8f);
                var axis = Quaternion.Euler(RandomElectric(0,360), RandomElectric(0,360), RandomElectric(0,360));
                float start = RandomElectric(0, .65f);
                for (int p = 0; p < 13; p++)
                {
                    float t = p/12f;
                    Vector3 point;
                    if (electricBeam != null && electricBeam.enabled && electricBeam.positionCount > 1)
                    {
                        float sample = (start + t*.35f)*(electricBeam.positionCount-1);
                        int a = Mathf.Min(Mathf.FloorToInt(sample), electricBeam.positionCount-2);
                        point = Vector3.Lerp(electricBeam.GetPosition(a), electricBeam.GetPosition(a+1), sample-a);
                        if (!electricBeam.useWorldSpace) point = electricBeam.transform.TransformPoint(point);
                        point = transform.InverseTransformPoint(point);
                        point += axis * new Vector3(0,RandomElectric(-.12f,.12f),RandomElectric(-.12f,.12f));
                    }
                    else
                    {
                        float angle = phase + t*sweep;
                        Vector3 radial = axis * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), RandomElectric(-.16f,.16f));
                        point = electricBounds.center + Vector3.Scale(radial, electricBounds.extents) * RandomElectric(1.02f,1.2f);
                    }
                    line.SetPosition(p, point);
                }
                line.widthMultiplier = electricityWidth;
            }
            line.enabled = electricityIntensity > 0 && Time.time < arcEnd[i];
            if (!line.enabled) continue;
            float alpha = Mathf.Clamp01((arcEnd[i]-Time.time)/.04f)*electricityIntensity;
            line.startColor = poisonPalette ? new Color(.9f,1.2f,.3f,alpha*.65f) : new Color(1.7f,.75f,2.7f,alpha);
            line.endColor = poisonPalette ? new Color(.3f,.5f,.08f,alpha*.45f) : new Color(.6f,.25f,2f,alpha*.7f);
        }
    }
    private void OnDestroy() { if (electricityMaterial != null) Destroy(electricityMaterial); }
    private Vector3 originalScale;
    private bool applied;
    private readonly System.Collections.Generic.List<Renderer> renderers = new();
    private readonly System.Collections.Generic.List<MaterialPropertyBlock> blocks = new();
    private readonly System.Collections.Generic.List<ParticleSystem> particles = new();
    private readonly System.Collections.Generic.List<ParticleSystem.MinMaxGradient> colors = new();
    private readonly System.Collections.Generic.List<LineRenderer> lines = new();
    private readonly System.Collections.Generic.List<Gradient> gradients = new();
    private readonly System.Collections.Generic.List<ParticleSystem.MinMaxGradient> lifetimeColors = new();
    private readonly System.Collections.Generic.List<TrailRenderer> trails = new();
    private readonly System.Collections.Generic.List<Gradient> trailColors = new();
    private Color effectTint = VisionEmpowerment.Color;
    private Color effectEdge = new Color(.4f,.25f,1f);
    private bool poisonPalette;
    private Gradient PurpleGradient(Gradient original)
    {
        var g=new Gradient(); g.SetKeys(new[] {new GradientColorKey(effectTint,0),new GradientColorKey(effectEdge,1)},original.alphaKeys); return g;
    }
    private ParticleSystem.MinMaxGradient PurpleParticles(ParticleSystem.MinMaxGradient original)
    {
        if(original.mode==ParticleSystemGradientMode.Gradient || original.mode==ParticleSystemGradientMode.RandomColor)
            return new ParticleSystem.MinMaxGradient(PurpleGradient(original.gradient));
        if(original.mode==ParticleSystemGradientMode.TwoGradients)
            return new ParticleSystem.MinMaxGradient(PurpleGradient(original.gradientMin),PurpleGradient(original.gradientMax));
        Color first=effectTint, second=effectTint;
        first.a=original.colorMin.a; second.a=original.colorMax.a;
        return original.mode==ParticleSystemGradientMode.TwoColors ? new ParticleSystem.MinMaxGradient(first,second) : new ParticleSystem.MinMaxGradient(second);
    }
    public static void Restore(GameObject go) { if(go!=null) go.GetComponent<VisionEmpoweredVisual>()?.OnDisable(); }
    public static void Apply(GameObject go, float scale = 1.5f, bool poison = false)
    {
        if(go==null) return;
        var v=go.GetComponent<VisionEmpoweredVisual>();
        if(v==null) v=go.AddComponent<VisionEmpoweredVisual>();
        if(v.applied) return;
        v.poisonPalette=poison;
        v.effectTint=poison ? CombatSmoke.PoisonColor : VisionEmpowerment.Color;
        v.effectEdge=poison ? new Color(.18f,.29f,.07f) : new Color(.4f,.25f,1f);
        v.applied=true; v.originalScale=go.transform.localScale; go.transform.localScale*=scale;
        foreach(var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if(v.IsElectric(r.transform)) continue;
            var saved=new MaterialPropertyBlock(); r.GetPropertyBlock(saved); v.renderers.Add(r); v.blocks.Add(saved);
            var tint=new MaterialPropertyBlock(); r.GetPropertyBlock(tint);
            var mat=r.sharedMaterial;
            foreach(string property in new[] {"_Color","_BaseColor","_TintColor","_MainColor","_EmissionColor"})
            {
                int id=Shader.PropertyToID(property);
                if(mat==null || !mat.HasProperty(id)) continue;
                Color authored=saved.HasColor(id) ? saved.GetColor(id) : mat.GetColor(id);
                float brightness=Mathf.Max(authored.r,Mathf.Max(authored.g,authored.b));
                Color color=v.effectTint*(poison ? Mathf.Min(brightness,1f) : brightness);
                color.a=authored.a; tint.SetColor(id,color);
            }
            r.SetPropertyBlock(tint);
        }
        foreach(var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main=ps.main; v.particles.Add(ps); v.colors.Add(main.startColor); main.startColor=v.PurpleParticles(main.startColor);
            var lifetime=ps.colorOverLifetime; v.lifetimeColors.Add(lifetime.color);
            if(lifetime.enabled) lifetime.color=v.PurpleParticles(lifetime.color);
        }
        foreach(var trail in go.GetComponentsInChildren<TrailRenderer>(true))
        { v.trails.Add(trail); v.trailColors.Add(trail.colorGradient); trail.colorGradient=v.PurpleGradient(trail.colorGradient); }
        foreach(var line in go.GetComponentsInChildren<LineRenderer>(true))
        {
            if(v.IsElectric(line.transform)) continue;
            v.lines.Add(line); v.gradients.Add(line.colorGradient);
            line.colorGradient=v.PurpleGradient(line.colorGradient);
        }
        v.PrepareElectricity();
    }
    private void OnDisable()
    {
        if(!applied) return;
        if(electricityRoot != null) electricityRoot.gameObject.SetActive(false);
        transform.localScale=originalScale;
        for(int i=0;i<renderers.Count;i++) if(renderers[i]!=null) renderers[i].SetPropertyBlock(blocks[i]);
        for(int i=0;i<particles.Count;i++) if(particles[i]!=null) { var main=particles[i].main; main.startColor=colors[i]; var lifetime=particles[i].colorOverLifetime; lifetime.color=lifetimeColors[i]; }
        for(int i=0;i<lines.Count;i++) if(lines[i]!=null) lines[i].colorGradient=gradients[i];
        for(int i=0;i<trails.Count;i++) if(trails[i]!=null) trails[i].colorGradient=trailColors[i];
        renderers.Clear();blocks.Clear();particles.Clear();colors.Clear();lifetimeColors.Clear();trails.Clear();trailColors.Clear();lines.Clear();gradients.Clear();applied=false;
    }
}
