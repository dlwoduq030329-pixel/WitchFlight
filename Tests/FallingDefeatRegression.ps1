$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$visual = Get-Content (Join-Path $repo 'Assets/Script/FallingDefeatVisual.cs') -Raw
function Method($name) {
 $match=[regex]::Match($visual,'(?ms)^    (?:private|internal) (?:static )?[\w<>]+ '+$name+'\([^{}]*\)\r?\n    \{.*?^    \}')
 if (!$match.Success) {throw "Missing $name"}
 $match.Value.Replace('private ','public ').Replace('internal ','public ')
}
$code=@'
using System;
using System.Collections.Generic;
public struct Vector3 {
 public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public float magnitude=>(float)Math.Sqrt(x*x+y*y+z*z);
 public static Vector3 zero=>new(); public static Vector3 down=>new(0,-1,0);
 public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
}
public static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); }
public class Player {} public class MagicProjectile {} public class SuimonoObject {}
public class Collider {
 public bool isTrigger; public Collider gameObject=>this; public int layer;
 public object parentComponent;
 public T GetComponentInParent<T>() where T:class=>parentComponent as T;
}
public class Water { public int layerWaterNum=4; }
public class Settings {public float deathWaterSinkSpeed=2.5f,deathJointRadius=.09f;}
public class JointParticle {public Vector3 position,previous; public float inverseMass=1;}
public struct BoneLink { public int a,b; public float length; }
public class Probe {
 public List<JointParticle> joints=new(); public List<BoneLink> links=new();
 public Settings settings=new(); public Water water=new(); public float gravity=16;
 public bool underwater,floor; public int contacts;
 public Vector3 Sweep(Vector3 from,Vector3 to,float radius) {
  if(floor&&to.y<radius){to.y=radius; contacts++;}return to;
 }
'@ + (Method 'LinkCorrection') + (Method 'StepBody') + (Method 'Solid') + @'
}
public static class FallingChecks {
 static int count;
 static void Check(bool b,string msg){if(!b)throw new Exception(msg);count++;}
 static JointParticle At(float x,float y)=>new(){position=new(x,y,0),previous=new(x,y,0)};
 public static int Run(){
  Check(Probe.LinkCorrection(Vector3.zero,1).magnitude==0,"Coincident joints cannot create NaN");
  var p=new Probe();p.joints.Add(At(0,20));p.joints.Add(At(1,20));p.links.Add(new(){a=0,b=1,length=1});
  p.joints[1].previous=new(1,20.03f,0);
  for(int i=0;i<60;i++)p.StepBody(1f/60);
  Check(p.joints[0].position.y<15,"Body actually falls under gravity");
  Check(Math.Abs((p.joints[1].position-p.joints[0].position).magnitude-1)<.001,"Joint lengths stay intact while falling");
  Check(Math.Abs(p.joints[1].position.y-p.joints[0].position.y)>.01,"Limb moves independently instead of rigid baked pose");
  p.floor=true;
  for(int i=0;i<240;i++)p.StepBody(1f/60);
  Check(p.contacts>0&&p.joints[0].position.y>=.089&&p.joints[1].position.y>=.089,"Solid terrain supports the body");
  var sink=new Probe{underwater=true};sink.joints.Add(At(0,-1));
  for(int i=0;i<120;i++)sink.StepBody(1f/60);
  Check(sink.joints[0].position.y< -2,"Underwater body continues sinking, does not float");
  Check((sink.joints[0].position-sink.joints[0].previous).magnitude*60<3,"Water slows the fall");
  Check(!p.Solid(new Collider{layer=4}),"Water sampling collider is not a floor");
  Check(!p.Solid(new Collider{parentComponent=new SuimonoObject()}),"Water on another layer is still ignored");
  Check(!p.Solid(new Collider{isTrigger=true}),"Portal/item triggers do not catch bodies");
  Check(!p.Solid(new Collider{parentComponent=new Player()}),"Live player cannot block dead visual");
  Check(!p.Solid(new Collider{parentComponent=new MagicProjectile()}),"Projectile cannot block dead visual");
  Check(p.Solid(new Collider()),"Building collider remains solid");
  p.water=null;Check(p.Solid(new Collider()),"Maps without water still have ground collision");
  return count;
 }
}
'@
Add-Type -TypeDefinition $code
"Falling body physics/water checks passed: $([FallingChecks]::Run())"
