# Tests production prediction registry/reconciliation; visuals/physics need Unity Play Mode.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content Assets/Script/ProjectilePredictionView.cs -Raw
function Method([string]$name) {
    $m = [regex]::Match($source, '(?ms)^    public static [\w<>]+ ' + $name + '\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing method: $name" }
    $m.Value
}
$stubs = @'
using System;
using System.Collections.Generic;
namespace PredictionChecks {
public enum MagicType {Fire,Ice}
public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 zero=>default;public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);}
public class Transform {public Vector3 position,forward=new(0,0,1);public Transform parent;public void SetParent(Transform p,bool world){parent=p;}}
public class GameObject {public bool activeSelf;public void SetActive(bool value){activeSelf=value;}}
public static class Time {public static float unscaledTime;}
public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);}
public struct NetworkId {public int value;}
public class NetworkRunner {public bool IsForward=true;public Player player;public bool TryFindObject(NetworkId id,out NetworkObject obj){obj=player?.Object;return obj!=null&&obj.Id.Equals(id);}}
public class NetworkObject {public bool HasInputAuthority=true,HasStateAuthority,IsValid=true;public NetworkId Id;public Player player;public T GetComponent<T>() where T:class=>player as T;}
public class MagicStatTable {}
public class MagicStatEntry {public MagicType magic;public float projectileSpeed=75,castSeconds;public bool requiresTarget=true;}
public class Player {public NetworkObject Object=new();public NetworkRunner Runner;public Transform transform=new();public Vector3 LockAimPoint=>transform.position;public Vector3 MagicCastPosition=new(2,3,4);public MagicStatTable MagicTable=new();}
public class MagicProjectile {public NetworkRunner Runner;public NetworkId ShooterId;public int PredictionInputTick;public MagicType Magic;public Transform transform=new();public static Vector3 ResolveLaunchDirection(bool a,Vector3 b,Vector3 c,Vector3 d)=>b;}
public class ProjectilePredictionView {
 public static HashSet<ProjectilePredictionView> pending=new();
 public Player owner,target;public MagicProjectile source;public MagicStatEntry stats;public MagicStatTable table;public NetworkRunner runner;public NetworkId shooterId;public int inputTick;
 public float createdAt,launchAt;public Vector3 direction,correction;public bool predictedStop,predictedImpact,hasSource;public Transform transform=new();public GameObject content=new();
 public static int Creates;
 static ProjectilePredictionView Create(MagicStatEntry entry,Vector3 position,Vector3 forward){Creates++;return new(){stats=entry,transform=new(){position=position,forward=forward}};}
 Vector3 GetConfirmedPosition()=>source.transform.position;
'@
$methods = (Method HasPending) + (Method Predict) + (Method Attach)
$checks = @'
}
public static class Tests {
 static int n;static void Check(bool ok,string msg){if(!ok)throw new Exception(msg);n++;}
 static Player Owner(int id){var p=new Player{Runner=new()};p.Object.Id=new(){value=id};p.Object.player=p;p.Runner.player=p;return p;}
 public static int Run(){
  var p=Owner(1);var stats=new MagicStatEntry();var other=Owner(2);
  p.Object.HasStateAuthority=true;ProjectilePredictionView.Predict(p,other,stats,1);Check(ProjectilePredictionView.pending.Count==0,"host does not duplicate authoritative visuals");
  p.Object.HasStateAuthority=false;p.Object.HasInputAuthority=false;ProjectilePredictionView.Predict(p,other,stats,1);Check(ProjectilePredictionView.pending.Count==0,"proxy cannot predict local casts");
  p.Object.HasInputAuthority=true;p.Runner.IsForward=false;ProjectilePredictionView.Predict(p,other,stats,1);Check(ProjectilePredictionView.pending.Count==0,"rollback never spawns visuals");
  p.Runner.IsForward=true;stats.projectileSpeed=0;ProjectilePredictionView.Predict(p,other,stats,1);Check(ProjectilePredictionView.pending.Count==0,"hitscan has no projectile ghost");
  stats.projectileSpeed=75;ProjectilePredictionView.Predict(p,other,stats,40);Check(ProjectilePredictionView.pending.Count==1,"local client predicts a projectile");
  var pending=new List<ProjectilePredictionView>(ProjectilePredictionView.pending)[0];Check(pending.inputTick==40&&pending.owner==p&&pending.content.activeSelf,"prediction records input identity and is immediately visible");
  Check(pending.transform.position.x==2&&pending.transform.position.y==3&&pending.transform.position.z==4,"predicted shot begins at the configured cast root, not LockAimPoint");
  ProjectilePredictionView.Predict(p,other,stats,41);Check(ProjectilePredictionView.pending.Count==1,"same player/spell is bounded to one unconfirmed shot");
  var server=new MagicProjectile{Runner=p.Runner,ShooterId=p.Object.Id,PredictionInputTick=39,Magic=MagicType.Fire};
  var wrongTick=ProjectilePredictionView.Attach(server,stats);Check(wrongTick!=pending&&ProjectilePredictionView.pending.Count==1,"previous input does not steal the new ghost");
  server.PredictionInputTick=40;server.ShooterId=other.Object.Id;Check(ProjectilePredictionView.Attach(server,stats)!=pending,"different shooter does not steal a ghost");
  server.ShooterId=p.Object.Id;server.Runner=other.Runner;Check(ProjectilePredictionView.Attach(server,stats)!=pending,"different runner cannot reconcile");
  server.Runner=p.Runner;server.Magic=MagicType.Ice;Check(ProjectilePredictionView.Attach(server,stats)!=pending,"different magic cannot reconcile");
  server.Magic=MagicType.Fire;pending.transform.position=new(8,0,0);server.transform.position=new(3,0,0);int creates=ProjectilePredictionView.Creates;
  var confirmed=ProjectilePredictionView.Attach(server,stats);Check(confirmed==pending&&ProjectilePredictionView.Creates==creates,"authoritative spawn reuses the same visual");
  Check(ProjectilePredictionView.pending.Count==0&&confirmed.hasSource&&confirmed.source==server,"confirmed visual leaves pending registry");
  Check(confirmed.transform.position.x==8&&confirmed.correction.x==5&&confirmed.transform.parent==server.transform,"reconciliation preserves visible position for smoothing");
  Time.unscaledTime=10;stats.castSeconds=.3f;ProjectilePredictionView.Predict(p,other,stats,60);pending=new List<ProjectilePredictionView>(ProjectilePredictionView.pending)[0];
  Check(!pending.content.activeSelf&&Math.Abs(pending.launchAt-10.3f)<.001,"cast delay is preserved");
  ProjectilePredictionView.pending.Clear();for(int i=0;i<32;i++)ProjectilePredictionView.Predict(Owner(i),other,stats,i);ProjectilePredictionView.Predict(Owner(99),other,stats,99);
  Check(ProjectilePredictionView.pending.Count==32,"unconfirmed registry is capped");
  return n;
 }
}}
'@
Add-Type -TypeDefinition ($stubs + $methods + $checks)
"Prediction ownership/reconciliation checks passed: $([PredictionChecks.Tests]::Run())"
