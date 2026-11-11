# Executes the production bounded trail-history tick with lightweight engine doubles.
# Does not render Unity particles or replace multiplayer Play Mode testing.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source=Get-Content Assets/Script/MagicProjectile.Flare.cs -Raw
$tick=[regex]::Match($source,'(?ms)^    private void TickFlareField\(\).*?^    \}').Value
$point=[regex]::Match($source,'(?ms)^    internal static Vector3 FlareTrailPoint\(.*?;').Value
if (!$tick -or !$point) {throw 'Missing production trail methods'}
$code=@'
using System;
namespace FlareTrailChecks {
public struct Vector3 {
 public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public static Vector3 zero=>default;public static Vector3 one=>new(1,1,1);public static Vector3 forward=>new(0,0,1);
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
}
public struct Quaternion {
 public float angle;
 public static Quaternion Euler(float x,float y,float z)=>new(){angle=y};
 public static Quaternion operator*(Quaternion a,Quaternion b)=>new(){angle=a.angle+b.angle};
 public static Vector3 operator*(Quaternion q,Vector3 v){float r=q.angle*(float)Math.PI/180;return new(v.x*(float)Math.Cos(r)+v.z*(float)Math.Sin(r),v.y,-v.x*(float)Math.Sin(r)+v.z*(float)Math.Cos(r));}
}
public struct Bounds {public Bounds(Vector3 p,Vector3 s){}public void Encapsulate(Vector3 p){}}
public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static int Min(int a,int b)=>Math.Min(a,b);}
public class NetObject {public bool IsValid=true;}
public class Caster {public NetObject Object=new(); public bool IsAlive=true; public int PortalExitSequence; public Vector3 position;}
public class Transform {public Vector3 position;public Quaternion rotation;}
public class Clock {public float now;public bool despawned;public void Despawn(NetObject obj){despawned=true;}}
public class Timer {public float end=5.1f;public bool Expired(Clock clock)=>clock.now>=end;}
public class Field {
 public Clock Runner=new(); public Timer Lifetime=new(); public NetObject Object=new();
 public float FlareElapsed=>Runner.now;public bool FlareEmitting=>!FlareEmissionStopped&&Runner.now<duration;
 public float duration=3f,FlareParticleLifetime=2.1f,FlareSpeed=14f,FlareSpread=30f,ConstructRadius=2f;
 public bool FlareEmissionStopped;
 public Caster flareCaster=new();public Transform transform=new();
 private const int FlareHistoryCapacity=128;
 private struct FlareSample {public Vector3 position;public Quaternion rotation;public float at;public int portal;}
 private struct FlareSegment {public Vector3 start,end,origin;public float radius;}
 private FlareSample[] flareHistory=new FlareSample[128];private FlareSegment[] flareSegments=new FlareSegment[384];
 private int flareHistoryHead,flareHistoryCount,flareSegmentCount;private float nextFlareSample;private Bounds flareBounds;
 private void FollowFlareCaster(){transform.position=flareCaster.position;}
 public void Tick(float now){Runner.now=now;TickFlareField();}
 public int Samples=>flareHistoryCount;public int Segments=>flareSegmentCount;
 public bool HasOldOrigin=>flareSegmentCount>0&&flareSegments[flareSegmentCount-1].origin.x==0;
 public float LargestJump(){float result=0;for(int i=0;i<flareSegmentCount;i++)result=Math.Max(result,Math.Abs(flareSegments[i].start.x-flareSegments[i].end.x));return result;}
'@+$tick+$point+@'
}
public static class Tests {
 static int checks;static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
 public static int Run(){
  var field=new Field();field.Tick(0);
  Check(field.Samples==1&&field.Segments==3,"initial pulse has three branches");
  field.flareCaster.position=new Vector3(10,0,0);field.Tick(.1f);
  Check(field.Segments==6&&field.HasOldOrigin,"moving emitter preserves previous world-space origins");
  field.flareCaster.position=new Vector3(1000,0,0);field.flareCaster.PortalExitSequence++;field.Tick(.2f);
  Check(field.LargestJump()<100,"portal teleport does not draw an interception ribbon through the map");
  field.flareCaster.IsAlive=false;field.Tick(.3f);
  Check(field.FlareEmissionStopped&&field.Segments>0,"death stops emission but leaves existing trails");
  field.Tick(2.31f);Check(field.Segments==0,"old trails expire after extended particle lifetime");
  var tail=new Field();for(int i=0;i<60;i++)tail.Tick(i*.05f);
  int before=tail.Samples;tail.Tick(3.1f);
  Check(tail.Samples==before&&tail.Segments>0&&!tail.Runner.despawned,"emission ends before lingering trails disappear");
  tail.Tick(5.11f);Check(tail.Runner.despawned,"pooled field releases after the last particles expire");
  var longCast=new Field();longCast.duration=20;longCast.Lifetime.end=22.1f;
  for(int i=0;i<1000;i++)longCast.Tick(i*.02f);
  Check(longCast.Samples<=128&&longCast.Segments<=384,"long casts use bounded history and segment storage");
  var p=Field.FlareTrailPoint(new Vector3(5,0,0),Vector3.forward,14,1);
  Check(Math.Abs(p.x-5)<.001&&Math.Abs(p.z-11.55f)<.001,"trail follows emitted direction independently of current caster pose");
  return checks;
 }
}
}
'@
Add-Type -TypeDefinition $code
Write-Output ("Flare trail simulation passed: {0} checks" -f [FlareTrailChecks.Tests]::Run())
