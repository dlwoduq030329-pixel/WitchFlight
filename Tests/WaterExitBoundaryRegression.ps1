$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
# Exercise production boundary and submersion math without launching Unity/network clients.
$boundary = Get-Content Assets/Script/MapBoundaryTable.cs -Raw
$module = Get-Content 'Assets/SUIMONO - WATER SYSTEM 2/SCRIPTS/SuimonoModule.cs' -Raw
$submersion = [regex]::Match($module, '(?ms)        public static bool ShouldRemainSubmerged\(.*?^        \}').Value
if (!$submersion) { throw 'Submersion method not found' }
$ocean = Get-Content 'Assets/SUIMONO - WATER SYSTEM 2/SCRIPTS/SuimonoObject.cs' -Raw
$visibility = [regex]::Match($ocean, '(?ms)        private void UpdateInfiniteSurfaceVisibility\(\).*?^        \}').Value.Replace('private void', 'public void')
if (!$visibility) { throw 'Ocean visibility method not found' }
$source = @'
using System;
using UnityEngine;
namespace UnityEngine {
 public static class Application {public static bool isPlaying=true;}
 public class ScriptableObject {}
 public class CreateAssetMenuAttribute:Attribute {public string fileName,menuName;}
 public class HeaderAttribute:Attribute {public HeaderAttribute(string s){}}
 public class TooltipAttribute:Attribute {public TooltipAttribute(string s){}}
 public class MinAttribute:Attribute {public MinAttribute(float v){}}
 public static class Mathf {
  public const float PI=(float)Math.PI;
  public static float Clamp(float v,float a,float b)=>Math.Max(a,Math.Min(v,b));
  public static float Clamp01(float v)=>Clamp(v,0,1);
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Min(float a,float b)=>Math.Min(a,b);
  public static float Sin(float v)=>(float)Math.Sin(v);
  public static float InverseLerp(float a,float b,float v)=>Clamp01((v-a)/(b-a));
 }
 public struct Vector3 {
  public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 up=>new Vector3(0,1,0);
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator -(Vector3 a)=>new Vector3(-a.x,-a.y,-a.z);
  public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
  public static Vector3 operator *(float b,Vector3 a)=>a*b;
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
 }
}
'@
$source += $boundary.Replace('using UnityEngine;', '')
$source += "public static class WaterState { $submersion }"
$source += @'
public class OceanVisibilityHarness {
 public class Toggle {public bool enabled=true;}
 public class WaterModule {public bool isUnderwater;}
 public int typeIndex;
 public Toggle scaleRenderer=new Toggle(),scaleCollider=new Toggle();
 public WaterModule moduleObject=new WaterModule();
'@
$source += $visibility + "}"
$source += @'
public static class WaterExitChecks {
 static int count; static void Check(bool result,string reason){if(!result)throw new Exception(reason);count++;}
 public static int Run(){
  Check(WaterState.ShouldRemainSubmerged(false,true,2,.1f),"Enter underwater");
  Check(WaterState.ShouldRemainSubmerged(true,true,.05f,.1f),"Keep submerged while still below surface");
  Check(!WaterState.ShouldRemainSubmerged(true,true,0,.1f),"Exit at the surface");
  Check(!WaterState.ShouldRemainSubmerged(true,true,-.01f,.1f),"Exit immediately above surface");
  Check(!WaterState.ShouldRemainSubmerged(true,false,100,.1f),"Missing surface clears stale underwater state");
  Check(!WaterState.ShouldRemainSubmerged(false,true,.05f,.1f),"Small waves do not immediately retrigger entry");
  Check(!WaterState.ShouldRemainSubmerged(true,true,float.NaN,.1f),"Invalid depth cannot retain underwater state");
  var ocean=new OceanVisibilityHarness();
  bool stable=true;
  // Model a camera whose ray hits the outer ocean, using the real visibility method.
  // The old renderer/collider coupling loses the query on every second frame.
  for(int frame=0;frame<120;frame++) {
   ocean.UpdateInfiniteSurfaceVisibility();
   ocean.moduleObject.isUnderwater=WaterState.ShouldRemainSubmerged(ocean.moduleObject.isUnderwater,ocean.scaleCollider.enabled,5,.1f);
   stable &= ocean.moduleObject.isUnderwater;
  }
  Check(stable,"Submerged camera stays underwater for 120 consecutive frames");
  Check(!ocean.scaleRenderer.enabled&&ocean.scaleCollider.enabled,"Hidden far surface retains height queries");
  ocean.moduleObject.isUnderwater=WaterState.ShouldRemainSubmerged(true,ocean.scaleCollider.enabled,-1,.1f);
  ocean.UpdateInfiniteSurfaceVisibility();
  Check(!ocean.moduleObject.isUnderwater&&ocean.scaleRenderer.enabled,"Surfacing still immediately exits underwater");
  ocean.typeIndex=1; ocean.UpdateInfiniteSurfaceVisibility();
  Check(!ocean.scaleRenderer.enabled&&!ocean.scaleCollider.enabled,"Finite water has no infinite query surface");
  var table=new MapBoundaryTable {lowerLimitEnabled=true,ymin=-50,returnInset=2};
  Check(table.Contains(new Vector3(0,-50,0)),"Minimum Y is inclusive");
  Check(table.Contains(new Vector3(0,-50.01f,0)),"Below minimum never triggers a horizontal U-turn");
  Check(!table.Contains(new Vector3(201,0,0)),"Existing horizontal bounds retained");
  Check(table.ClampToLowerLimit(new Vector3(0,-60,0)).y==-50,"Lower floor clamps exactly at minimum without bounce/inset");
  var slide=table.ConstrainMovement(new Vector3(0,-49,0),new Vector3(3,-100,4));
  Check(slide.x==3&&slide.z==4&&slide.y==-1,"High speed descent stops at floor and preserves lateral motion");
  var blocked=table.ConstrainMovement(new Vector3(0,-50,0),new Vector3(0,-2,0));
  Check(blocked.y==0,"Repeated downward input cannot penetrate floor");
  Check(table.ConstrainMovement(new Vector3(0,-50,0),new Vector3(0,2,0)).y==2,"Upward movement freely leaves floor");
  Check(table.ConstrainMovement(new Vector3(0,-40,0),new Vector3(1,-2,3)).y==-2,"Normal descent above floor unchanged");
  var start=new Vector3(210,-60,0);
  table.CreateReturnPath(start,new Vector3(1,0,0),out var c1,out var c2,out var end);
  Check(table.Contains(end),"Combined horizontal and lower breach returns inside");
  MapBoundaryTable.EvaluateReturnPath(start,c1,c2,end,8,1,out var result,out var tangent);
  Check(Math.Abs(result.y-end.y)<.001f&&table.Contains(result),"Network return path ends safely inside");
  table.lowerLimitEnabled=false;
  Check(table.Contains(new Vector3(0,-500,0)),"Disabled lower boundary preserves legacy behavior");
  Check(table.ClampToLowerLimit(new Vector3(0,-500,0)).y==-500,"Disabled lower boundary preserves height");
  table.lowerLimitEnabled=true; table.ymin=float.NaN;
  Check(!table.HasValidLowerLimit&&table.Contains(new Vector3(0,-500,0)),"Invalid lower limit safely ignored");
  table.ymin=table.altitudeFadeStartY;
  Check(!table.HasValidLowerLimit,"Lower limit cannot overlap upper blackout");
  table.ymin=-50; table.boundaryEnabled=false;
  Check(table.ConstrainMovement(new Vector3(0,-50,0),new Vector3(0,-5,0)).y==-5,"Disabled boundary permits descent");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Water exit and boundary behavior checks passed: $([WaterExitChecks]::Run())"

if ($module.Contains('InvokeRepeating("StoreSurfaceHeight"')) { throw 'Camera state still samples on delayed timer' }
# Check the actual lifecycle method, not merely that a sampling call exists somewhere.
# A Start-only call freezes the state at spawn and breaks all later water entry/exit.
$lateUpdate = [regex]::Match($module, '(?ms)^\t\tvoid LateUpdate\s*\(\)\s*\{.*?^\t\t\}').Value
if (!$lateUpdate -or $lateUpdate -notmatch 'Suimono_CheckCamera\(\);[\s\S]*?StoreSurfaceHeight\(\);[\s\S]*?PlayUnderwaterSound\(\);') {
 throw 'Water state must be sampled in LateUpdate after camera selection and before ambient audio'
}
if ([regex]::Matches($lateUpdate, 'StoreSurfaceHeight\(\);').Count -ne 1) {
 throw 'Expected one water state sample per frame'
}
foreach ($contract in @('if (clip == null || underSoundComponent.clip != clip)',
 'underwaterObject.cancelTransition = false;', 'underwaterObject.doTransition = true;')) {
 if (!$module.Contains($contract)) { throw "Missing audio/exit transition safeguard: $contract" }
}
$feature=Get-Content 'Assets/SUIMONO - WATER SYSTEM 2/SCRIPTS/SuimonoUrpFeature.cs' -Raw
if (!$feature.Contains('if(!module.isUnderwater && !fog.UrpTransitionActive) return;')) {
 throw 'Exit water droplets must render even when no longer underwater'
}
$player=Get-Content Assets/Script/Player.cs -Raw
foreach ($contract in @('[Networked] private Vector3 mapReturnEnd', '[Networked] public bool IsReturningToMap',
 'public override void FixedUpdateNetwork()', 'if (UpdateMapBoundaryReturn())',
 'finally { EnforceMapFloor(); }', 'ConstrainMapMovement(KnockbackVelocity * Runner.DeltaTime)',
 'ConstrainMapMovement(transform.forward * Speed * Runner.DeltaTime)')) {
 if (!$player.Contains($contract)) { throw "Missing existing network boundary integration: $contract" }
}
"Audio, water-drop transition and network integration source checks passed."
