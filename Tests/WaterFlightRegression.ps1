# Real scheduling and wake-math methods with small doubles; not GPU/physics/visual validation.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Extract-Method($path,$name) {
 $text=Get-Content $path -Raw
 $match=[regex]::Match($text,'(?ms)^    (?:internal|public) static bool '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$match.Success){throw "Missing $name"}
 $match.Value.Replace('internal static','public static')
}
$source=@'
using System;
namespace WaterChecks {
 public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Clamp01(float x)=>Math.Clamp(x,0,1);}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);public void Normalize(){float m=magnitude;x/=m;y/=m;z/=m;}}
 public static class Logic {
'@
$source+=(Extract-Method Assets/Script/WaterCaptureSettings.cs NeedsRefresh)
$source+=(Extract-Method Assets/Script/BattleWaterWake.cs TryWake)
$source+=@'
 }
 public static class Checks {
  static int count;static void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
  public static int Run(){
   Check(Logic.NeedsRefresh(false,0,30,0,.25f,0,.5f,false),"first capture renders immediately");
   Check(!Logic.NeedsRefresh(true,.01f,30,0,.25f,0,.5f,false),"stationary duplicate frame reuses texture");
   Check(Logic.NeedsRefresh(true,.034f,30,0,.25f,0,.5f,false),"animated water refreshes on schedule even while stationary");
   Check(Logic.NeedsRefresh(true,.001f,30,.25f*.25f,.25f,0,.5f,false),"translation forces immediate refresh");
   Check(Logic.NeedsRefresh(true,.001f,30,0,.25f,.6f,.5f,false),"turning refreshes screen-aligned textures");
   Check(Logic.NeedsRefresh(true,0,30,0,.25f,0,.5f,true),"camera/projection/size/underwater changes invalidate cache");
   Check(!Logic.NeedsRefresh(true,.01f,30,1f,2f,2f,3f,false),"reflection uses independent relaxed motion threshold");
   Check(Logic.TryWake(new(0,0,3),.1f,1,10,2,30,out var forward,out float near),"low forward flight emits wake");
   Check(forward.x==0&&forward.z==1,"forward motion orientation");
   Check(Logic.TryWake(new(0,0,-3),.1f,1,10,2,30,out var reverse,out _)&&reverse.z==-1,"reverse uses actual movement, not body forward");
   Check(Logic.TryWake(new(3,0,0),.1f,1,10,2,30,out var side,out _)&&side.x==1,"sideways flight turns wake");
   Check(Logic.TryWake(new(0,-1,3),.1f,1,10,2,30,out var angled,out _)&&angled.y==0,"pitch does not tilt surface ripple");
   Check(Logic.TryWake(new(0,0,3),.1f,8,10,2,30,out _,out float high)&&high<near,"height fades strength");
   Check(Logic.TryWake(new(0,0,.5f),.1f,1,10,2,30,out _,out float slow)&&slow<near,"slower flight is gentler");
   Check(!Logic.TryWake(new(0,0,3),.1f,10,10,2,30,out _,out _),"high flight emits nothing");
   Check(!Logic.TryWake(new(0,0,3),.1f,-1,10,2,30,out _,out _),"submerged character does not make aerial wake");
   Check(!Logic.TryWake(new(0,3,0),.1f,1,10,2,30,out _,out _),"vertical-only movement has no invented horizontal heading");
   Check(!Logic.TryWake(new(0,0,.01f),.1f,1,10,2,30,out _,out _),"stationary jitter does not emit");
   Check(!Logic.TryWake(new(0,0,3),0,1,10,2,30,out _,out _),"zero sample time rejected");
   return count;
  }
 }
}
'@
Add-Type -TypeDefinition $source
"Capture scheduling/wake behavior checks passed: $([WaterChecks.Checks]::Run())"
$scene=Get-Content Assets/Scenes/Battle.unity -Raw
foreach($name in @('WaterCaptureSettings','BattleWaterWake')) {
 $guid=[regex]::Match((Get-Content "Assets/Script/$name.cs.meta" -Raw),'guid: (\w+)').Groups[1].Value
 if(([regex]::Matches($scene,"m_Script: \{fileID: 11500000, guid: $guid,").Count) -ne 1){throw "Missing or duplicated $name scene binding"}
}
$captureGuid=[regex]::Match((Get-Content Assets/Settings/WaterCapture_Renderer.asset.meta -Raw),'guid: (\w+)').Groups[1].Value
foreach($pipeline in @('PC_RPAsset','Mobile_RPAsset')) {
 $text=Get-Content "Assets/Settings/$pipeline.asset" -Raw
 $list=[regex]::Match($text,'(?ms)  m_RendererDataList:\r?\n(.*?)  m_DefaultRendererIndex:').Groups[1].Value
 $guids=[regex]::Matches($list,'guid: (\w+)')
 if($guids.Count -lt 2 -or $guids[1].Groups[1].Value -ne $captureGuid -or $text -notmatch 'm_DefaultRendererIndex: 0') {throw "Capture renderer registration incorrect: $pipeline"}
}
$renderer=Get-Content Assets/Settings/WaterCapture_Renderer.asset -Raw
if($renderer -notmatch 'm_Name: Suimono URP' -or $renderer -match 'PeripheralSpeedBlur|ScreenSpaceAmbientOcclusion|GlobalVolumeFeature') {throw 'Capture renderer contains unrelated postprocessing'}
$wake=Get-Content Assets/Script/BattleWaterWake.cs -Raw
foreach($contract in @('IsHiddenBySmokeFor(local)','previous.portal != player.PortalExitSequence','QueryTriggerInteraction.Ignore','system.particleCount >= main.maxParticles')) {
 if(!$wake.Contains($contract)){throw "Missing wake safeguard: $contract"}
}
$module=[regex]::Match($scene,'(?ms)^--- !u!114 &1925800940\r?\n.*?(?=^--- !u!|\z)').Value
if($module -notmatch 'enableCausticsBlending: 0' -or $module -notmatch 'enableAdvancedDistort: 1' -or $module -notmatch 'enableTransparency: 1') {throw 'Water appearance/ripple prerequisites changed'}
$fx=[regex]::Match($scene,'(?ms)^--- !u!114 &1925800938\r?\n.*?(?=^--- !u!|\z)').Value
$list=[regex]::Match($fx,'(?ms)  effectsSystems:\r?\n(.*?)  systemClampType:').Groups[1].Value
$effects=[regex]::Matches($list,'guid: (\w+)')
if($effects[1].Groups[1].Value -ne '00dc73c91633d20418744bc987256fac' -or $effects[5].Groups[1].Value -ne 'edf84a7488a31c34481354c7db9f43ee') {throw 'Wake particle indices no longer match authored effects'}
"Scene bindings, capture-only renderer, native ripple/wake effects and concealment/teleport safeguards verified."
