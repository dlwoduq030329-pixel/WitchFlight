$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$s=Get-Content Assets/Script/MagicProjectile.Flare.cs -Raw
$rule=[regex]::Match($s,'(?ms)^    internal static bool CanLureFlare.*?;').Value
$point=[regex]::Match($s,'(?ms)^    internal static Vector3 FlareTrailPoint.*?^    \}').Value
$code=@'
using System;
public struct Vector3 {
 public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public static Vector3 forward=>new(0,0,1);public static Vector3 down=>new(0,-1,0);
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
}
public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Exp(float a)=>(float)Math.Exp(a);}
public static class Flare {
'@
$code+=$rule+$point
$code+=@'
 public static int Run(){
  int n=0;
  for(int guided=0;guided<2;guided++)for(int owner=0;owner<2;owner++)for(int ally=0;ally<2;ally++)for(int tele=0;tele<2;tele++){
   bool expected=guided==1&&(owner==1||(tele==1&&ally==1));
   if(CanLureFlare(guided==1,owner==1,ally==1,tele==1)!=expected)throw new Exception("eligibility");n++;
  }
  var p0=FlareTrailPoint(default,Vector3.forward,14,0);
  var p1=FlareTrailPoint(default,Vector3.forward,14,1);
  var p2=FlareTrailPoint(default,Vector3.forward,14,2);
  if(p0.z!=0||p1.z<=0||p2.z-p1.z>=p1.z||p2.y>=p1.y||p1.y>=0)throw new Exception("decelerating, falling decoy");
  return n+1;
 }
}
'@
Add-Type -TypeDefinition $code
"Flare eligibility / trajectory checks passed: $([Flare]::Run())"
foreach($token in 'nextFlareSample=now+FlareBurstInterval','if(age>FlareParticleLifetime) break','sequence=sample.sequence,branch=branch','if (sequence <= lastFlareSequence) return','TargetId=default; // Spent decoy') {
 if(!$s.Contains($token)){throw "Missing burst lifecycle contract: $token"}
}
if($s.Contains('stats.projectileTurnSpeed =') -or $s.Contains('stats.requiresTarget =')){throw 'Flare must preserve incoming guidance stats'}
