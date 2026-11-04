# Exercise the production trailing-bar timing without launching Unity.
$ErrorActionPreference = 'Stop'
$hud = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets/Script/BattleHud.cs') -Raw
$method = [regex]::Match($hud, '(?ms)^    private static float AdvanceManaTrail\(.*?^    \}').Value
if (!$method) { throw 'Missing AdvanceManaTrail' }
$source = @'
using System;
public static class Mathf {
 public static float Clamp01(float v)=>Math.Clamp(v,0,1);
 public static float Max(float a,float b)=>Math.Max(a,b);
 public static float MoveTowards(float a,float b,float d)=>Math.Abs(b-a)<=d?b:a+Math.Sign(b-a)*d;
}
public static class ManaTrailChecks {
'@ + $method + @'
 static int count;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
 public static int Run(){
  bool active=false;float deadline=0,speed=0;
  float v=AdvanceManaTrail(1,.5f,0,.1f,.2f,.4f,ref active,ref deadline,ref speed);
  Check(v==1&&active,"loss preserves old MP initially");
  v=AdvanceManaTrail(v,.5f,.1f,.1f,.2f,.4f,ref active,ref deadline,ref speed);
  Check(v==1&&deadline==.2f,"hold lasts 0.2 seconds");
  for(int i=0;i<4;i++)v=AdvanceManaTrail(v,.5f,.2f+i*.1f,.1f,.2f,.4f,ref active,ref deadline,ref speed);
  Check(Math.Abs(v-.5f)<.0001f&&!active,"single loss catches up in 0.4 seconds");
  v=AdvanceManaTrail(v,.8f,1,.1f,.2f,.4f,ref active,ref deadline,ref speed);
  Check(v==.8f&&!active,"recovery immediately raises trail to current MP");
  active=false;v=1;
  for(int i=0;i<20;i++){
   float current=1-(i+1)*.02f;
   v=AdvanceManaTrail(v,current,2+i*.05f,.05f,.2f,.4f,ref active,ref deadline,ref speed);
   Check(v>=current&&v<=1,"continuous drain stays between current and original MP");
  }
  Check(v<.9f&&Math.Abs(deadline-2.2f)<.0001f,"continuous spending cannot postpone the hold deadline");
  for(int i=0;i<20;i++)v=AdvanceManaTrail(v,.6f,3+i*.05f,.05f,.2f,.4f,ref active,ref deadline,ref speed);
  Check(Math.Abs(v-.6f)<.0001f&&!active,"stopping continuous drain finishes the trail");
  active=false;v=AdvanceManaTrail(1,0,5,2,0,0,ref active,ref deadline,ref speed);
  Check(v==0&&!active,"frame stall and zero settings never overshoot");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"MP trail timing checks passed: $([ManaTrailChecks]::Run())"
