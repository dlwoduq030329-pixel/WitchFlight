# Headless balance scenarios using production flight math and serialized stats.
# Constant speed, horizontal steering with the player's turn-rate response, swept sphere contact.
# Does not replace Unity physics / Fusion multiplayer testing.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content Assets/Script/MagicProjectile.cs -Raw
$asset = Get-Content Assets/Resources/MagicStatTable.asset -Raw
$equipment = Get-Content Assets/Resources/EquipmentStatTable.asset -Raw
$tableSource = Get-Content Assets/Script/MagicStatTable.cs -Raw
function Method([string]$name) {
    $m = [regex]::Match($source, '(?ms)^    public static [\w<>]+ ' + $name + '\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing production method: $name" }
    $m.Value
}
function Number([string]$text, [string]$name) {
    $m = [regex]::Match($text, '(?m)^\s+' + $name + ': ([\d.eE+-]+)\s*$')
    if (!$m.Success) { throw "Missing serialized field: $name" }
    $m.Groups[1].Value
}
$fields = 'homingTravelDistanceMultiplier','homingLifetimeMultiplier','homingLeadSeconds',
    'homingSpeedLossPerSecond','homingSpeedLossPerDegree','homingFallThreshold','homingTurnSpeed',
    'homingTurnReferenceSpeed','homingTurnSpeedExponent','homingMaxTurnLossPerSecond'
$stubs = @'
using System;
namespace HomingChecks {
public enum MagicType {Fire=1,Ice=2,Binding=6,Blade=13,MagicStorm=14,BladeRain=17}
public enum MagicEffectKind {DirectDamage,Mine}
public class MagicStatTable {
 public bool homingExhaustion=true;
 public bool useIndividualHomingTurnSpeeds;
 public float homingTurnSpeed;
 public float homingTravelDistanceMultiplier,homingLifetimeMultiplier,homingLeadSeconds;
 public float homingSpeedLossPerSecond,homingSpeedLossPerDegree,homingFallThreshold;
 public float homingTurnReferenceSpeed,homingTurnSpeedExponent,homingMaxTurnLossPerSecond;
 __TURN_SETTINGS__
}
public struct MagicStatEntry {
 public MagicType magic;public MagicEffectKind effect;public bool requiresTarget;
 public float range,placementDistance,projectileSpeed,projectileTurnSpeed,projectileLifetime,projectileRadius;
}
public static class Mathf {
 public const float Deg2Rad=(float)(Math.PI/180);
 public const float Rad2Deg=(float)(180/Math.PI);
 public static float Atan2(float y,float x)=>(float)Math.Atan2(y,x);
 public static float Max(float a,float b)=>Math.Max(a,b);
 public static float Min(float a,float b)=>Math.Min(a,b);
 public static float Pow(float a,float b)=>(float)Math.Pow(a,b);
 public static float Abs(float x)=>Math.Abs(x);
 public static float Sqrt(float x)=>(float)Math.Sqrt(x);
 public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);
 public static float Clamp01(float x)=>Math.Clamp(x,0,1);
}
public struct Vector3 {
 public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public float sqrMagnitude=>x*x+y*y+z*z;
 public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
 public Vector3 normalized=>magnitude>1e-6f?this*(1/magnitude):default;
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator-(Vector3 a)=>new(-a.x,-a.y,-a.z);
 public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*Math.Clamp(t,0,1);
 public static Vector3 operator*(Vector3 a,float s)=>new(a.x*s,a.y*s,a.z*s);
 public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
 public static Vector3 Cross(Vector3 a,Vector3 b)=>new(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
 public static float Angle(Vector3 a,Vector3 b){
  double dot=(double)a.x*b.x+(double)a.y*b.y+(double)a.z*b.z;
  double aa=(double)a.x*a.x+(double)a.y*a.y+(double)a.z*a.z;
  double bb=(double)b.x*b.x+(double)b.y*b.y+(double)b.z*b.z;
  return aa*bb<1e-15?0:(float)(Math.Acos(Math.Clamp(dot/Math.Sqrt(aa*bb),-1,1))*180/Math.PI);
 }
 public static Vector3 RotateTowards(Vector3 a,Vector3 b,float radians,float unused){
  double angle=Angle(a,b)*Math.PI/180;
  if(angle<1e-6||angle<=radians)return b.normalized;
  // Scenarios never require an exactly opposite instantaneous direction.
  double t=radians/angle;
  return (a.normalized*(float)(Math.Sin((1-t)*angle)/Math.Sin(angle))+
          b.normalized*(float)(Math.Sin(t*angle)/Math.Sin(angle))).normalized;
 }
}
public static class Flight {
'@
$methods = (Method FlightDistance) + (Method FlightLifetime) + (Method HomingAimDelta) + (Method ResolveHomingDirection) + (Method DrainFlightSpeed) + (Method FlightTurnSpeed)
$methods += Method TrySweptTargetContact
$flagSource = Get-Content Assets/Script/BattleFlag.cs -Raw
$segment = [regex]::Match($flagSource, '(?ms)^    internal static float SegmentDistanceSquared\([^{}]*?\)\r?\n    \{.*?^    \}').Value
if (!$segment) { throw 'Missing capsule segment math' }
$sweepMethod = [regex]::Match($source, '(?ms)^    private bool TryGetSweptTargetContact\([^{}]*?\)\r?\n    \{.*?^    \}').Value.Replace('private bool', 'public bool')
if (!$sweepMethod) { throw 'Missing actual-pose contact integration' }
$sweepStubs = @'
public class Player {
 public Vector3 center,halfAxis=new(0,.76f,0),ParryHintVelocity;public float radius=.44f;
 public int PortalExitSequence;
}
public class ProbeRunner { public float DeltaTime=1f/30; }
public class ProbeTransform { public Vector3 position; }
public class SweepProbe {
 public bool HasFlightExhaustion=true,IsMine,wallBlocked;
 public MagicStatEntry stats=new(){projectileRadius=.12f};
 public ProbeRunner Runner=new();public ProbeTransform transform=new();
 private Player sweptTarget;private Vector3 sweptTargetCenter,sweptProjectileStart;private int sweptPortalSequence;
 private bool HasBlastSight(Vector3 point,Player target)=>!wallBlocked;
 private bool TrySweptTargetContact(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 axis,float r,out Vector3 p)
  =>Flight.TrySweptTargetContact(a,b,c,d,axis,r,out p);
 __SWEEP_METHOD__
}
'@
$capsuleStub = 'public static void GetCapsule(Player p,out Vector3 center,out Vector3 axis,out float radius){center=p.center;axis=p.halfAxis;radius=p.radius;}'
$stubs = $stubs.Replace('public static class Flight {', $sweepStubs.Replace('__SWEEP_METHOD__',$sweepMethod) + "`npublic static class BattleFlag { $segment $capsuleStub }`npublic static class Flight {")
$turnSettings = [regex]::Match($tableSource, '(?ms)^    public MagicStatEntry ApplyHomingTurnSettings\([^{}]*?\)\r?\n    \{.*?^    \}').Value
if (!$turnSettings) { throw 'Missing production turn setting resolver' }
$stubs = $stubs.Replace('__TURN_SETTINGS__', $turnSettings)
foreach ($name in 'UsesFlightExhaustion') {
    $m = [regex]::Match($source, '(?ms)^    public static [^\r\n]+ ' + $name + '\([^;]+;')
    if (!$m.Success) { throw "Missing production expression: $name" }
    $methods += $m.Value
}
$checks = @'
}
public struct Broom {public int id;public float speed,rate,accel;}
public static class Tests {
 static int count;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
 static Vector3 Heading(float degrees)=>new((float)Math.Cos(degrees*Math.PI/180),0,(float)Math.Sin(degrees*Math.PI/180));
 static bool Hit(MagicStatEntry e,MagicStatTable table,Broom broom,float initialDistance,float heading,
                 float dt,float trigger=0,float arc=0){
  e=table.ApplyHomingTurnSettings(e);
  Vector3 projectile=default,target=new(initialDistance,0,0);
  Vector3 previousProjectile=projectile,previousTarget=target;
  Vector3 halfAxis=new(0,.76f,0);
  Vector3 direction=Flight.HomingAimDelta(target,Heading(heading)*broom.speed,e.projectileSpeed,table.homingLeadSeconds).normalized;
  float fraction=1,travelled=0,turned=0,rate=0;bool turning=false;
  float limit=Flight.FlightDistance(e,table),life=Flight.FlightLifetime(e,table);
  for(float time=0;time<life;time+=dt){
   if(Flight.TrySweptTargetContact(previousProjectile,projectile,previousTarget,target,halfAxis,.44f+e.projectileRadius,out _))return true;
   Vector3 offset=target-projectile;
   if(trigger>0&&offset.magnitude<=trigger)turning=true;
   if(turning&&turned<arc){
    rate+=(broom.rate-rate)*(1-(float)Math.Exp(-broom.accel*dt));
    float step=Math.Min(rate*dt,arc-turned);turned+=step;heading+=step;
   }
   Vector3 velocity=Heading(heading)*broom.speed;
   Vector3 desired=Flight.HomingAimDelta(offset,velocity,e.projectileSpeed*fraction,table.homingLeadSeconds);
   Vector3 before=direction;
   direction=Flight.ResolveHomingDirection(true,direction,desired,Flight.FlightTurnSpeed(e.projectileTurnSpeed,e.projectileSpeed*fraction,table),dt);
   fraction=Flight.DrainFlightSpeed(fraction,before,direction,dt,table);
   if(fraction<=table.homingFallThreshold)return false;
   float distance=Math.Min(e.projectileSpeed*fraction*dt,limit-travelled);
   // Static collider sweep, then next tick's actual relative-motion crossing check.
   if(Flight.TrySweptTargetContact(projectile,projectile+direction*distance,target,target,halfAxis,.44f+e.projectileRadius,out _))return true;
   previousProjectile=projectile;previousTarget=target;
   projectile+=direction*distance;target+=velocity*dt;travelled+=distance;
   if(travelled>=limit-.001f)return false;
  }
  return false;
 }
 public static int Run(MagicStatTable table,MagicStatEntry[] spells,Broom[] brooms){
  foreach(var e in spells){
   Check(Flight.FlightDistance(e,table)>e.range,"flight budget is independent of lock-on range");
   foreach(var broom in brooms)foreach(float dt in new[]{1f/30,1f/60,1f/120}){
    foreach(float distance in new[]{2f,3f,5f,10f,20f})for(int heading=0;heading<360;heading+=15)
     Check(Hit(e,table,broom,distance,heading,dt),$"close pass miss: {e.magic}, broom {broom.id}, distance {distance}, heading {heading}, dt {dt}");
    foreach(float fraction in new[]{.5f,.9f,1f})for(int heading=0;heading<360;heading+=15)
     Check(Hit(e,table,broom,e.range*fraction,heading,dt),$"straight miss: {e.magic}, broom {broom.id}, range {fraction}, heading {heading}, dt {dt}");
    if(broom.id==2||broom.id==3){
     int escapes=0;
     foreach(float trigger in new[]{15f,25f,40f,60f,80f})foreach(float arc in new[]{90f,180f})
      if(!Hit(e,table,broom,e.range*.95f,0,dt,trigger,arc))escapes++;
     Check(escapes>0,$"no timed turn can evade {e.magic} / broom {broom.id} / dt {dt}");
     Check(Hit(e,table,broom,e.range*.95f,0,dt,3,90),"turning too late is not a guaranteed dodge");
    }
   }
  }
  var fire=spells[0];
  Check(!table.useIndividualHomingTurnSpeeds,"balance scenarios run with the shared setting enabled");
  foreach(var e in spells){
   Check(table.ApplyHomingTurnSettings(e).projectileTurnSpeed==table.homingTurnSpeed,"all three spells use shared turning");
   table.useIndividualHomingTurnSpeeds=true;
   Check(table.ApplyHomingTurnSettings(e).projectileTurnSpeed==e.projectileTurnSpeed,"individual setting restores authored turning");
   table.useIndividualHomingTurnSpeeds=false;
  }
  float shared=table.homingTurnSpeed;table.homingTurnSpeed=0;
  Check(table.ApplyHomingTurnSettings(fire).projectileTurnSpeed==0,"shared zero disables steering");
  table.homingTurnSpeed=shared;
  foreach(var magic in new[]{MagicType.Blade,MagicType.MagicStorm,MagicType.BladeRain}){
   var other=fire;other.magic=magic;
   Check(Flight.FlightDistance(other,table)==other.range&&Flight.FlightLifetime(other,table)==other.projectileLifetime,"other spell budgets unchanged");
   Check(table.ApplyHomingTurnSettings(other).projectileTurnSpeed==other.projectileTurnSpeed,"other spells keep individual turning");
  }
  var mine=fire;mine.effect=MagicEffectKind.Mine;mine.placementDistance=2;
  Check(Flight.FlightDistance(mine,table)==2&&Flight.FlightLifetime(mine,table)==mine.projectileLifetime,"mine unaffected");
  table.homingExhaustion=false;
  Check(Flight.FlightDistance(fire,table)==fire.range&&Flight.FlightLifetime(fire,table)==fire.projectileLifetime,"disabled settings preserve legacy budget");
  table.homingExhaustion=true;
  var delta=new Vector3(20,0,0);
  Check((Flight.HomingAimDelta(delta,new(10,0,0),50,0)-delta).sqrMagnitude==0,"zero lead disables prediction");
  Check((Flight.HomingAimDelta(delta,new(100,0,0),50,3)-delta).sqrMagnitude==0,"unreachable target does not produce invalid intercept");
  Check(Flight.HomingAimDelta(delta,new(-50,0,0),50,3).x>0,"equal-speed approach handles linear intercept");
  Check(Flight.DrainFlightSpeed(.21f,Heading(0),Heading(180),1,table)==0,"exhaustion cannot produce negative speed");
  Check(Math.Abs(Flight.DrainFlightSpeed(1,Heading(37),Heading(37),1,table)-(1-table.homingSpeedLossPerSecond))<1e-6,"identical headings incur no turn loss");
  Check(Flight.FlightTurnSpeed(600,0,table)==0,"no steering when speed is zero");
  float full=Flight.FlightTurnSpeed(600,table.homingTurnReferenceSpeed,table);
  Check(Math.Abs(full-600)<.001f,"reference speed has reference steering");
  Check(Flight.FlightTurnSpeed(600,table.homingTurnReferenceSpeed*.5f,table)<full*.51f,"low speed weakens steering");
  foreach(float dt in new[]{1f/30,1f/60,1f/120}){
   float remaining=Flight.DrainFlightSpeed(1,Heading(0),Heading(179),dt,table);
   float maxLoss=(table.homingSpeedLossPerSecond+table.homingMaxTurnLossPerSecond)*dt;
   Check(remaining>=1-maxLoss-.000001f,"large turn cannot drain energy multiple times in one tick");
   float energy=1;
   for(int n=0;n<(int)Math.Round(1/dt);n++)energy=Flight.DrainFlightSpeed(energy,Heading(0),Heading(179),dt,table);
   Check(Math.Abs(energy-Math.Max(0,1-table.homingSpeedLossPerSecond-table.homingMaxTurnLossPerSecond))<.00001f,"turn loss cap is per second across tick rates");
  }
  Check(Flight.DrainFlightSpeed(.8f,Heading(0),Heading(179),0,table)==.8f,"zero simulation time cannot drain energy");
  Vector3 origin=default,endpoint=new(2,0,0),targetStart=new(3,0,0),targetEnd=new(1,0,0);
  Check(!Flight.TrySweptTargetContact(origin,endpoint,targetStart,targetStart,default,.56f,out _),"static sweep misses target which has yet to cross");
  Check(Flight.TrySweptTargetContact(origin,endpoint,targetStart,targetEnd,default,.56f,out var contact),"actual head-on crossing is caught between ticks");
  Check(contact.x>=0&&contact.x<=2,"contact is on the actual shot segment");
  Check(!Flight.TrySweptTargetContact(origin,endpoint,new(3,0,2),new(1,0,2),new(0,.76f,0),.56f,out _),"nearby pass outside capsule is still a miss");
  Check(!Flight.TrySweptTargetContact(origin,origin,targetStart,targetStart,default,.56f,out _),"stationary separated objects do not hit");
  Check(Flight.TrySweptTargetContact(origin,origin,origin,origin,default,.56f,out _),"stationary overlap has no divide-by-zero");
  Check(Flight.TrySweptTargetContact(new(0,1,0),new(2,1,0),new(3,.4f,0),new(1,.4f,0),new(0,.76f,0),.56f,out _),"target capsule height participates in crossing contact");
  foreach(int mode in new[]{0,1,2,3,4,5,6}){
   var probe=new SweepProbe();var victim=new Player{center=new(3,0,0)};
   Check(!probe.TryGetSweptTargetContact(victim,out _),"first target sample cannot invent a previous path");
   probe.transform.position=new(2,0,0);victim.center=new(1,0,0);
   if(mode==1)victim.PortalExitSequence++;
   if(mode==2)probe.wallBlocked=true;
   if(mode==3)probe.HasFlightExhaustion=false;
   if(mode==4)victim=new Player{center=new(1,0,0)};
   if(mode==5)victim.center=new(-20,0,0);
   if(mode==6){probe.TryGetSweptTargetContact(null,out _);}
   Check(probe.TryGetSweptTargetContact(victim,out _)==(mode==0),$"crossing guard: mode {mode} (normal / portal / wall / disabled / retarget / relocation / lock lost)");
  }
  Console.WriteLine("Close-pass tests: 2/3/5/10/20m, all headings, static collider sweep plus actual-pose crossing check.");
  Console.WriteLine("Straight tests: all five stock craft, 50/90/100% lock range, 24 headings, 30/60/120 Hz.");
  Console.WriteLine("Turn tests: middle/fast craft have evasive 90/180-degree maneuvers; point-blank late turns still hit.");
  return count;
 }
}}
'@
Add-Type -TypeDefinition ($stubs + $methods + $checks)
$table = [HomingChecks.MagicStatTable]::new()
$table.useIndividualHomingTurnSpeeds = (Number $asset 'useIndividualHomingTurnSpeeds') -ne '0'
foreach ($field in $fields) { $table.$field = [float]::Parse((Number $asset $field),[Globalization.CultureInfo]::InvariantCulture) }
$spellValues = foreach ($id in 1,2,6) {
    $block = [regex]::Match($asset, '(?ms)^  - magic: ' + $id + '\r?\n.*?(?=^  - magic:|\z)').Value
    $entry = [HomingChecks.MagicStatEntry]::new(); $entry.magic = $id; $entry.requiresTarget = $true
    foreach ($field in 'range','projectileSpeed','projectileTurnSpeed','projectileLifetime','projectileRadius') {
        $entry.$field = [float]::Parse((Number $block $field),[Globalization.CultureInfo]::InvariantCulture)
    }
    $entry
}
$broomValues = foreach ($block in ([regex]::Split($equipment, '(?m)^  brooms:\r?\n')[1] -split '(?m)^  - displayName:')) {
    if ($block -notmatch '(?m)^    broom:') { continue }
    $broom = [HomingChecks.Broom]::new(); $broom.id = [int](Number $block 'broom')
    $broom.speed = [float]::Parse((Number $block 'maxSpeed'),[Globalization.CultureInfo]::InvariantCulture) * 2 / 3
    $broom.rate = [float]::Parse((Number $block 'turnSpeed'),[Globalization.CultureInfo]::InvariantCulture)
    $broom.accel = [float]::Parse((Number $block 'turnAcceleration'),[Globalization.CultureInfo]::InvariantCulture)
    $broom
}
"Homing flight checks passed: $([HomingChecks.Tests]::Run($table,$spellValues,$broomValues))"
