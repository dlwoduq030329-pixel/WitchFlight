#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Read-only project validation. Does not save scenes, alter accounts or start matchmaking.
public static class WitchFlightPerkValidation
{
    private static int checks;
    private static void Check(bool ok,string label) { if(!ok)throw new Exception("Perk validation: "+label);checks++; }
    private static bool Near(float a,float b) => Mathf.Abs(a-b)<.001f;
    [MenuItem("Tools/WitchFlight/Validate Perk System")]
    public static void Run()
    {
        checks=0;
        try
        {
            ValidateRules(); ValidateEffects(); ValidateBindings();
            Debug.Log($"PERK_VALIDATION_PASSED: {checks} checks. No scene or asset was saved.");
        }
        catch(Exception e) { Debug.LogException(e); if(Application.isBatchMode)EditorApplication.Exit(1); else throw; }
    }
    private static void ValidateRules()
    {
        Check(PerkRules.RequiredXp(100,1.5f,0)==100,"first threshold");
        Check(PerkRules.RequiredXp(100,1.5f,1)==150,"second threshold");
        Check(PerkRules.RequiredXp(100,1.5f,2)==225,"third threshold");
        Check(PerkRules.RequiredXp(100,1.5f,3)==338,"fourth threshold rounds upward");
        Check(PerkRules.Candidate(MagicType.Healing,0,0,PerkId.GuardianKnot)==PerkId.HealingPower,"fixed healing stage 1");
        Check(PerkRules.Candidate(MagicType.Healing,1,0,PerkId.GuardianKnot)==PerkId.HealingCleanse,"fixed healing stage 2");
        Check(PerkRules.Candidate(MagicType.Fire,2,0,PerkId.GuardianKnot)==PerkId.GuardianKnot,"mastered slot offers common");
        Check(PerkRules.Candidate(MagicType.Ice,0,2,PerkId.GuardianKnot)==PerkId.IceSlow,"other slot still offers stage 1");
        Check(PerkRules.Candidate(MagicType.Ice,1,2,PerkId.DeepMana)==PerkId.DeepMana,"2/1 both offer common");
        Check(!PerkRules.CanUpgrade(1,2)&&PerkRules.CanUpgrade(0,2)&&PerkRules.CanUpgrade(1,1),"one mastery limit");
        Check(PerkRules.Candidate(MagicType.None,0,0,PerkId.DeepMana)==PerkId.None,"empty slots cannot be upgraded");
        var left=PerkRules.CommonCandidate(0,0);var right=PerkRules.CommonCandidate(0,5,left);
        Check(left!=right&&PerkRules.Common(left)&&PerkRules.Common(right),"distinct common candidates");
        Check(PerkRules.CommonCandidate(PerkRules.Bit(left),0)!=left,"no already owned common perk");
        var xp=new PerkXpLedger();
        Check(xp.Damage(1,49,200,.25f,4,6)==0,"damage XP needs actual quota");
        Check(xp.Damage(1,1,200,.25f,4,6)==6,"partial damage accumulates");
        Check(xp.Damage(1,500,200,.25f,4,6)==18,"overkill/healing cannot exceed four quotas");
        Check(xp.Damage(1,500,200,.25f,4,6)==0,"same life cannot be farmed");
        Check(xp.Damage(2,50,200,.25f,4,6)==6,"new life has new quota");
        Check(xp.Cap(24,60,30)==24&&xp.Cap(12,60,30)==6&&xp.Cap(4,60,30)==0,"shared action cap");
        xp.Advance(59);Check(xp.Cap(4,60,30)==0,"cap remains before expiry");
        xp.Advance(1);Check(xp.Cap(4,60,30)==4,"rolling combat time cap expires");
        Check(xp.RewardReady(0,20)&&!xp.RewardReady(0,20),"kill reward cooldown");
        Check(xp.Kill(1)&&!xp.Kill(1),"kill deduplication");
        Check(xp.Flag(1)&&!xp.Flag(1)&&xp.Flag(2),"flag first pickup per round");
        Check(xp.RoundBonus(1)&&!xp.RoundBonus(1)&&xp.RoundBonus(2),"round bonus once");
        var g=new PerkInputGate();
        g.Sample(true,false,false,false);g.Sample(true,true,false,false);
        Check(g.Showing&&g.BlocksCombat&&g.Choice<0,"Space shows cards without cursor/combat");
        g.Sample(true,true,true,false);Check(g.Choice==0,"left click chooses left immediately");
        g.Sample(true,true,true,false);Check(g.Choice<0,"held button does not repeat");
        g.Sample(true,false,true,false);Check(!g.Showing&&g.BlocksCombat&&g.Choice<0,"Space release wins and held selection cannot fire");
        g.Sample(true,false,false,false);g.Sample(true,false,true,false);Check(!g.BlocksCombat,"fresh combat input after release");
        g.Sample(true,true,true,false);Check(g.Choice<0,"pre-held mouse cannot choose");
        g.Sample(true,true,false,false);g.Sample(true,true,true,true);Check(g.Choice<0,"simultaneous clicks rejected");
        g.Sample(false,true,false,false);Check(!g.Showing&&g.Choice<0,"not allowed/focus lost hides input");
    }
    private static void ValidateEffects()
    {
        var table=AssetDatabase.LoadAssetAtPath<PerkStatTable>("Assets/Resources/PerkStatTable.asset");
        Check(table!=null&&table.perks.Length==44,"real balancing asset has 44 perks");
        var ids=new HashSet<PerkId>();foreach(var p in table.perks)Check(p!=null&&ids.Add(p.id),"unique stable perk ID");
        var magic=MagicStatTable.Default;Check(magic!=null,"shared MagicStatTable exists");
        MagicStatEntry S(MagicType m,int level,int common=0)=>PerkEffects.Apply(magic.GetStats(m),1,level,common,100,table);
        float A(PerkId id)=>table.Amount(id);
        for(int i=1;i<=(int)MagicType.BladeRain;i++)
        {
            var m=(MagicType)i;var a=S(m,2);var b=S(m,0);
            Check(a.perkFirst!=null&&a.perkSecond!=null,"stage 2 retains both effects "+m);
            Check(b.perkFirst==null&&b.perkSecond==null,"unupgraded duplicate slot unaffected "+m);
        }
        Check(S(MagicType.Blade,1).bladeCharges==5,"blade has five attacks");
        Check(Near(S(MagicType.Blade,2).woundHealingMultiplier,.25f),"wound reduces healing by 75 percent");
        Check(S(MagicType.MagicStorm,1).stormShotCount==40,"storm has forty shots");
        Check(Near(S(MagicType.MagicStorm,2).stormSpreadAngle,magic.GetStats(MagicType.MagicStorm).stormSpreadAngle*.5f),"storm spread halved");
        Check(Near(S(MagicType.BladeRain,1).rainManaPerSecond,20f),"rain charge costs twenty mana per second");
        var focus = S(MagicType.BladeRain,2);
        Check(focus.focusedBlade && !focus.requiresTarget && !focus.requiresFullLock && focus.apCost==0f && focus.projectileTurnSpeed==0f,"focus replaces lock with free release");
        Check(Near(focus.projectileSpeed,250f) && Near(focus.rainManaPerSecond,20f),"focus fast projectile and five second full charge at 200 MP");
        var empty = PerkEffects.FocusBladeShot(focus,0f,100f);
        var half = PerkEffects.FocusBladeShot(focus,50f,100f);
        var full = PerkEffects.FocusBladeShot(focus,100f,100f);
        Check(Near(empty.damage,40f) && Near(half.damage,80f) && Near(full.damage,120f),"focus damage uses actual spent MP plus forty");
        Check(Near(full.damage * full.rainFocusHeadMultiplier,240f),"full focus head hit multiplier");
        Check(Near(PerkEffects.FocusBladeShot(focus,150f,150f).damage,160f),"120 is not a damage cap with larger MP hat");
        Check(Near(empty.projectileRadius,.3f) && Near(half.projectileRadius,1.35f) && Near(full.projectileRadius,2.4f),"focus collision grows with charge");
        Check(Near(full.focusedBladeCharge,1f) && full.effect==MagicEffectKind.DirectDamage,"focus is one direct projectile");
        Check(!S(MagicType.BladeRain,1).focusedBlade && S(MagicType.BladeRain,1).requiresTarget,"first stage keeps normal lock on rain");
        Check(Near(S(MagicType.TimeStop,1).radius,magic.GetStats(MagicType.TimeStop).radius*1.3f),"time radius increased");
        Check(Near(S(MagicType.TimeStop,2).timeHealingPerSecond,10f),"time healing rate");
        Check(Near(S(MagicType.MeowFestival,1).festivalSpeedBonus,.3f),"festival movement bonus");
        Check(S(MagicType.MeowFestival,1).festivalGuardCharges==0&&S(MagicType.MeowFestival,2).festivalGuardCharges==1,"festival second stage guard");
        Check(Near(S(MagicType.Fire,2).lockChargeSeconds,magic.GetStats(MagicType.Fire).lockChargeSeconds*(1-Mathf.Clamp01(A(PerkId.FireIgnition)))),"fire lock follows table");
        Check(Near(S(MagicType.Fire,2).apCost,magic.GetStats(MagicType.Fire).apCost*Mathf.Max(1-table.manaDiscountCap,1-table.Get(PerkId.FireIgnition).secondaryAmount)),"fire mana follows table");
        Check(S(MagicType.Ice,1).movementMultiplier<magic.GetStats(MagicType.Ice).movementMultiplier,"ice stronger slow");
        Check(Near(S(MagicType.Vision,1).hitscanRadius,magic.GetStats(MagicType.Vision).hitscanRadius*(1+A(PerkId.VisionAim))),"vision radius");
        var thunder=S(MagicType.Thunder,2);
        Check(Near(PerkEffects.AreaHit(thunder,0).damage,thunder.damage*(1+A(PerkId.ThunderCore)))&&Near(PerkEffects.AreaHit(thunder,thunder.radius+.01f).damage,thunder.damage),"thunder core not outside area");
        Check(Near(S(MagicType.Healing,1).healing,magic.GetStats(MagicType.Healing).healing*(1+A(PerkId.HealingPower))),"healing amount");
        Check(Near(S(MagicType.Binding,2).apCost,magic.GetStats(MagicType.Binding).apCost),"binding mana unchanged");
        Check(Near(S(MagicType.Binding,2).effectDuration,magic.GetStats(MagicType.Binding).effectDuration+2f),"binding duration plus two");
        Check(Near(S(MagicType.Binding,1).lockChargeSeconds,magic.GetStats(MagicType.Binding).lockChargeSeconds),"binding lock time unchanged");
        Check(Near(S(MagicType.Dark,1).healthCost,magic.GetStats(MagicType.Dark).healthCost) &&
            Near(S(MagicType.Dark,1).healOnHit,magic.GetStats(MagicType.Dark).healOnHit*1.25f),"dark healing bonus only");
        Check(Near(S(MagicType.Dark,2).damage,magic.GetStats(MagicType.Dark).damage*1.3f),"dark damage bonus");
        Check(Near(S(MagicType.Curse,2).damagePerSecond,magic.GetStats(MagicType.Curse).damagePerSecond),"curse slows without DPS boost");
        foreach(var m in new[]{MagicType.Curse,MagicType.Razier})
            Check(Near(PerkEffects.HitManaRefund(S(m,2),200,A(PerkId.ManaRemnant)),0),"channel cannot generate mana via remnant "+m);
        Check(Near(PerkEffects.HitManaRefund(S(MagicType.Fire,2),200,A(PerkId.ManaRemnant)),200*A(PerkId.ManaRemnant)),"single-hit spells retain remnant refund");
        Check(Near(S(MagicType.Mine,2).lockChargeSeconds,magic.GetStats(MagicType.Mine).lockChargeSeconds*(1-Mathf.Clamp01(A(PerkId.MineArming)))),"summon lock");
        Check(Near(S(MagicType.Mine,2).summonAttackInterval,Mathf.Max(.1f,magic.GetStats(MagicType.Mine).summonAttackInterval*(1-Mathf.Clamp01(table.Get(PerkId.MineArming).secondaryAmount)))),"summon attack interval follows perk");
        Check(Near(S(MagicType.Mine,2).range,magic.GetStats(MagicType.Mine).range) &&
            Near(S(MagicType.Mine,2).summonAttackRange,magic.GetStats(MagicType.Mine).summonAttackRange),"dual summon does not change range");
        Check(Near(S(MagicType.Razier,1).hitscanRadius,magic.GetStats(MagicType.Razier).hitscanRadius*1.5f),"beam width");
        Check(Near(S(MagicType.Razier,2).damagePerSecond,magic.GetStats(MagicType.Razier).damagePerSecond*1.2f),"beam base DPS increased once");
        Check(Near(table.Get(PerkId.BeamFocus).condition,2f) && Near(table.Get(PerkId.BeamFocus).amount,16f) && Near(table.Get(PerkId.BeamFocus).seconds,2f),"continuous beam burn settings");
        Check(Near(S(MagicType.Smoke,2).radius,magic.GetStats(MagicType.Smoke).radius*(1+A(PerkId.SmokeRadius)))&&Near(S(MagicType.Smoke,2).effectDuration,magic.GetStats(MagicType.Smoke).effectDuration),"smoke area retained and duration unchanged");
        Check(Near(S(MagicType.Flare,2).effectDuration,magic.GetStats(MagicType.Flare).effectDuration+1f),"flare lasts an additional second");
        Check(Near(magic.GetStats(MagicType.Flare).flareSpreadAngle,30f),"flare fans rearward at thirty degrees each side");
        Check(Near(magic.GetStats(MagicType.Flare).flareParticleExtraLifetime,1f),"legacy flare particles last one extra second");
        Check(Near(S(MagicType.Flare,2).apCost,magic.GetStats(MagicType.Flare).apCost),"flare has no refund or discount");
        float stackedMana=Mathf.Max(1-table.manaDiscountCap,1-Mathf.Clamp01(A(PerkId.FrugalRune)));
        Check(Near(S(MagicType.Binding,2,PerkRules.Bit(PerkId.FrugalRune)).apCost,magic.GetStats(MagicType.Binding).apCost*stackedMana),"multiplicative mana discount respects editable cap");
        Check(Near(S(MagicType.Fire,2,PerkRules.Bit(PerkId.EchoSpell)).cooldownSeconds,magic.GetStats(MagicType.Fire).cooldownSeconds*Mathf.Max(1-table.cooldownDiscountCap,1-A(PerkId.EchoSpell))),"common cooldown");
        foreach(var p in table.perks)Check(!BattlePerkUI.Description(p).Contains("{"),"description tokens expanded "+p.id);
    }
    private static void ValidateBindings()
    {
        // Only open in batch mode; interactive validation never replaces the user's current scene.
        if(!Application.isBatchMode)return;
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Battle.unity",OpenSceneMode.Single);
        var manager=UnityEngine.Object.FindFirstObjectByType<BattleManager>();
        Check(manager!=null,"battle manager exists");
        var ui=manager.GetComponent<BattlePerkUI>();Check(ui!=null,"Inspector component attached in Battle scene");
        var so=new SerializedObject(ui);
        Check(so.FindProperty("experienceAnchor").objectReferenceValue!=null,"XP anchored to actual HP bar");
        Check(so.FindProperty("font").objectReferenceValue!=null,"project font assigned");
        Check(new SerializedObject(manager).FindProperty("perkStatTable").objectReferenceValue!=null,"perk asset wired");
        // Build the fallback in memory, exercise its layout, and close without saving.
        typeof(BattlePerkUI).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,null);
        var canvas=GameObject.Find("GeneratedPerkHUD");Check(canvas!=null,"fallback canvas built");
        Check(canvas.GetComponent<GraphicRaycaster>()==null,"fallback cannot capture combat cursor");
        var slider=canvas.GetComponentInChildren<Slider>(true);slider.SetValueWithoutNotify(.5f);Canvas.ForceUpdateCanvases();
        Check(!slider.interactable&&slider.fillRect.sizeDelta==Vector2.zero,"XP is read-only and fill has no oversize offset");
        foreach(var g in canvas.GetComponentsInChildren<Graphic>(true))Check(!g.raycastTarget,"all fallback graphics ignore raycasts");
        UnityEngine.Object.DestroyImmediate(canvas);
    }
}
#endif
