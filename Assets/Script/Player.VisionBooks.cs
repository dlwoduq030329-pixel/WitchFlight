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
            s.effectDuration *= 1.5f; s.summonChannelHealth *= 2f;
        }
        if(s.effect == MagicEffectKind.MagicStorm) s.stormDuration = .75f;
    }
}

// Restore authored values when a pooled effect is released.
public sealed class VisionEmpoweredVisual : MonoBehaviour
{
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
    private static Gradient PurpleGradient(Gradient original)
    {
        var g=new Gradient(); g.SetKeys(new[] {new GradientColorKey(VisionEmpowerment.Color,0),new GradientColorKey(new Color(.4f,.25f,1f),1)},original.alphaKeys); return g;
    }
    private static ParticleSystem.MinMaxGradient PurpleParticles(ParticleSystem.MinMaxGradient original)
    {
        if(original.mode==ParticleSystemGradientMode.Gradient || original.mode==ParticleSystemGradientMode.RandomColor)
            return new ParticleSystem.MinMaxGradient(PurpleGradient(original.gradient));
        if(original.mode==ParticleSystemGradientMode.TwoGradients)
            return new ParticleSystem.MinMaxGradient(PurpleGradient(original.gradientMin),PurpleGradient(original.gradientMax));
        Color first=VisionEmpowerment.Color, second=VisionEmpowerment.Color;
        first.a=original.colorMin.a; second.a=original.colorMax.a;
        return original.mode==ParticleSystemGradientMode.TwoColors ? new ParticleSystem.MinMaxGradient(first,second) : new ParticleSystem.MinMaxGradient(second);
    }
    public static void Restore(GameObject go) { if(go!=null) go.GetComponent<VisionEmpoweredVisual>()?.OnDisable(); }
    public static void Apply(GameObject go, float scale = 1.5f)
    {
        if(go==null) return;
        var v=go.GetComponent<VisionEmpoweredVisual>();
        if(v==null) v=go.AddComponent<VisionEmpoweredVisual>();
        if(v.applied) return;
        v.applied=true; v.originalScale=go.transform.localScale; go.transform.localScale*=scale;
        foreach(var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var saved=new MaterialPropertyBlock(); r.GetPropertyBlock(saved); v.renderers.Add(r); v.blocks.Add(saved);
            var tint=new MaterialPropertyBlock(); r.GetPropertyBlock(tint);
            var mat=r.sharedMaterial;
            foreach(string property in new[] {"_Color","_BaseColor","_TintColor","_MainColor","_EmissionColor"})
            {
                int id=Shader.PropertyToID(property);
                if(mat==null || !mat.HasProperty(id)) continue;
                Color authored=saved.HasColor(id) ? saved.GetColor(id) : mat.GetColor(id);
                Color color=VisionEmpowerment.Color*Mathf.Max(authored.r,Mathf.Max(authored.g,authored.b));
                color.a=authored.a; tint.SetColor(id,color);
            }
            r.SetPropertyBlock(tint);
        }
        foreach(var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main=ps.main; v.particles.Add(ps); v.colors.Add(main.startColor); main.startColor=PurpleParticles(main.startColor);
            var lifetime=ps.colorOverLifetime; v.lifetimeColors.Add(lifetime.color);
            if(lifetime.enabled) lifetime.color=PurpleParticles(lifetime.color);
        }
        foreach(var trail in go.GetComponentsInChildren<TrailRenderer>(true))
        { v.trails.Add(trail); v.trailColors.Add(trail.colorGradient); trail.colorGradient=PurpleGradient(trail.colorGradient); }
        foreach(var line in go.GetComponentsInChildren<LineRenderer>(true))
        {
            v.lines.Add(line); v.gradients.Add(line.colorGradient);
            var g=new Gradient(); g.SetKeys(new[] { new GradientColorKey(VisionEmpowerment.Color,0),new GradientColorKey(new Color(.4f,.25f,1f),1)},line.colorGradient.alphaKeys); line.colorGradient=g;
        }
    }
    private void OnDisable()
    {
        if(!applied) return;
        transform.localScale=originalScale;
        for(int i=0;i<renderers.Count;i++) if(renderers[i]!=null) renderers[i].SetPropertyBlock(blocks[i]);
        for(int i=0;i<particles.Count;i++) if(particles[i]!=null) { var main=particles[i].main; main.startColor=colors[i]; var lifetime=particles[i].colorOverLifetime; lifetime.color=lifetimeColors[i]; }
        for(int i=0;i<lines.Count;i++) if(lines[i]!=null) lines[i].colorGradient=gradients[i];
        for(int i=0;i<trails.Count;i++) if(trails[i]!=null) trails[i].colorGradient=trailColors[i];
        renderers.Clear();blocks.Clear();particles.Clear();colors.Clear();lifetimeColors.Clear();trails.Clear();trailColors.Clear();lines.Clear();gradients.Clear();applied=false;
    }
}
