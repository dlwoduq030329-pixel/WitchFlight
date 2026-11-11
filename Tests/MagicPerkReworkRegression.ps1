# Offline execution of the production rules, table, stat transform and input policy.
# Does not replace Fusion weaving or two-client Play Mode verification.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
function Method([string]$source,[string]$name) {
    $m=[regex]::Match($source,'(?ms)^    private static void '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if(!$m.Success){throw "Missing production validator $name"}; $m.Value
}
$combat=Get-Content Tests/MagicCombatRegression.ps1 -Raw
$stubs=[regex]::Match($combat,'(?ms)\$stubs = @''\r?\n(.*?)\r?\n''@').Groups[1].Value
$stubs=$stubs.Substring(0,$stubs.IndexOf('namespace CombatChecks'))
$stubs=$stubs.Replace('public class Object {}','public enum HideFlags {DontSave} public class Object {public HideFlags hideFlags;}')
$stubs=$stubs.Replace('public class ScriptableObject:Object {}','public class ScriptableObject:Object { public static T CreateInstance<T>() where T:new()=>new T(); }')
$stubs=$stubs.Replace('public class Object {','public class HideInInspectorAttribute:Attribute {} public class Object {')
$stubs=$stubs.Replace('where T:class=>null;','where T:class=>(T)Activator.CreateInstance(typeof(T));')
$stubs=$stubs.Replace('public static float Max(float a,float b)=>Math.Max(a,b);','public static float Max(float a,float b)=>Math.Max(a,b); public static float Abs(float v)=>Math.Abs(v);')
$enums=[regex]::Match((Get-Content Assets/Script/PlayerData.cs -Raw),'public enum MagicType\s*\{[^}]+\}').Value
$stubs=$stubs.Replace('public static class Mathf {','public static class Mathf { public static int Clamp(int v,int a,int b)=>Math.Clamp(v,a,b); public static int Max(int a,int b)=>Math.Max(a,b); public static int RoundToInt(float v)=>(int)Math.Round(v); public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t); public static float Min(params float[] v)=>System.Linq.Enumerable.Min(v);')
$code=$stubs+"`nnamespace Fusion {public enum PlayerRef {None,A,B}}`nnamespace PerkChecks { $enums }`n"
foreach($file in 'MagicStatTable','PerkStatTable','PerkRules','PerkEffects','PerkInputGate') {
    $code+="`nnamespace PerkChecks {`n"+(Get-Content "Assets/Script/$file.cs" -Raw)+"`n}`n"
}
$validation=Get-Content Assets/Editor/WitchFlightPerkValidation.cs -Raw
$ui=Get-Content Assets/Script/BattlePerkUI.cs -Raw
$description=[regex]::Match($ui,'(?ms)^    public static string Description\(.*?;').Value
$code+="`nnamespace PerkChecks { using System; using System.Collections.Generic; using UnityEngine;`n"+@'
public static class AssetDatabase {public static T LoadAssetAtPath<T>(string path) where T:new()=>new T();}
public static class BattlePerkUI {
'@ + $description + @'
}
public static class Tests {
 private static int checks;
 private static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
 private static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
 public static int Run(){ValidateRules();ValidateEffects();return checks;}
'@+(Method $validation 'ValidateRules')+(Method $validation 'ValidateEffects')+"`n}}"

$chargeSource=Get-Content Assets/Script/Player.BladeRain.cs -Raw
$chargeMethod=[regex]::Match($chargeSource,'(?ms)^    private void ProcessFocusedBlade\(.*?^    \}').Value
if (!$chargeMethod) {throw 'Missing production charge method'}
$code+="`nnamespace PerkChecks { using System; using UnityEngine;`n"+@'
public sealed class ChargeProbe {
 public class Clock { public float DeltaTime=.1f; }
 public class Prefab {public bool IsValid=true;}
 public Clock Runner=new Clock(); public Prefab magicProjectilePrefab=new Prefab();
 public MagicStatEntry SelectedMagicStats, LastShot;
 private MagicStatEntry rainStats;
 public float NowAp=200f,MaxAp=200f,RainManaSpent,RainChargeBudget,LockProgress;
 public bool RainCharging,RainFocused, rainFullCue;
 public int RainTargetCount, Cues, Shots;
 public bool[] MagicCooldowns=new bool[18];
 private bool TimerIsActive(bool value)=>value;
 private void ClearLockTargetInternal(bool value) {LockProgress=0f;}
 private void RPC_PresentLockComplete(MagicType magic){Cues++;}
 private void FireFocusedBlade(){LastShot=PerkEffects.FocusBladeShot(rainStats,RainManaSpent,RainChargeBudget);Shots++;RainCharging=false;MagicCooldowns[17]=true;}
 public void Step(bool held,bool released)=>ProcessFocusedBlade(held,released);
'@+$chargeMethod+@'
}
public static class ChargeTests {
 static int count;
 static void Check(bool value,string label){if(!value)throw new Exception(label);count++;}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.01f;
 static ChargeProbe Make(float dt=.1f) {
   var p=new ChargeProbe();p.Runner.DeltaTime=dt;
   p.SelectedMagicStats=PerkEffects.Apply(MagicStatTable.DefaultEntry(MagicType.BladeRain),1,2,0,200,new PerkStatTable());
   return p;
 }
 public static int Run() {
   foreach(float dt in new[]{.1f,.02f,1f/60f}) {
     var p=Make(dt);int steps=(int)Math.Round(5f/dt);
     for(int i=0;i<steps;i++)p.Step(true,false);
     Check(Near(p.RainManaSpent,100f)&&Near(p.NowAp,100f),"five seconds charges half max MP at tick "+dt);
     for(int i=0;i<steps;i++)p.Step(true,false);
     Check(Near(p.RainManaSpent,100f)&&Near(p.NowAp,100f)&&p.Cues==1&&p.Shots==0,"full charge holds without extra cost or auto firing");
     p.Step(false,true);
     Check(p.Shots==1&&Near(p.NowAp,100f)&&Near(p.LastShot.damage,120f),"release fires one 120 damage sword without extra mana");
     p.Step(false,false);p.Step(true,false);
     Check(p.Shots==1&&!p.RainCharging,"cooldown prevents repeat cast");
   }
   var low=Make();low.NowAp=30f;
   for(int i=0;i<80;i++)low.Step(true,false);
   Check(Near(low.NowAp,0f)&&Near(low.RainManaSpent,30f)&&low.Cues==0&&low.Shots==0,"empty MP holds partial charge without false full cue");
   low.Step(false,true);Check(Near(low.LastShot.damage,64f),"partial charge damage uses actual spend");
   var zero=Make();zero.NowAp=0f;zero.Step(true,false);zero.Step(false,true);
   Check(Near(zero.LastShot.damage,40f),"minimum damage forty with no release cost");
   var large=Make();large.MaxAp=300;large.NowAp=300;
   for(int i=0;i<75;i++)large.Step(true,false);
   Check(Near(large.RainManaSpent,150f)&&large.Cues==1,"large hat needs 7.5 seconds to max charge");
   large.Step(false,true);Check(Near(large.LastShot.damage,160f),"large hat damage scales beyond 120");
   var invalid=Make();invalid.magicProjectilePrefab.IsValid=false;invalid.Step(true,false);
   Check(!invalid.RainCharging&&Near(invalid.NowAp,200f),"missing prefab does not spend mana");
   return count;
 }
}
}
'@
Add-Type -TypeDefinition $code
Write-Output ("Perk rules/stat rework passed: {0} checks" -f [PerkChecks.Tests]::Run())
Write-Output ("Production focus charge passed: {0} checks" -f [PerkChecks.ChargeTests]::Run())
