# Executes production award selection, host praise validation and activity accounting with transport doubles.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = @'
using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using Random=UnityEngine.Random;
namespace UnityEngine {
 public class MonoBehaviour { public static T FindFirstObjectByType<T>() where T:class => Suimono.Core.SuimonoModule.Instance as T; }
 public struct Vector3 {public float y;}
 public class Transform {public Vector3 position;}
 public static class Time {public static float unscaledTime;}
 public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static int Max(int a,int b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);public static int FloorToInt(float v)=>(int)Math.Floor(v);public static int RoundToInt(float v)=>(int)Math.Round(v);}
 public static class Random {static System.Random rng=new(437);public static int Range(int a,int b)=>rng.Next(a,b);}
}
namespace Suimono.Core {public class SuimonoModule {public static SuimonoModule Instance=new();public bool isActiveAndEnabled=true;public bool TryGetSurfaceHeight(Vector3 p,out float y){y=0;return true;}}}
namespace Fusion {
 public class NetworkedAttribute:Attribute {} public class CapacityAttribute:Attribute {public CapacityAttribute(int n){}}
 public enum RpcSources {All} public enum RpcTargets {StateAuthority}
 public class RpcAttribute:Attribute {public RpcAttribute(RpcSources s,RpcTargets t){}}
 public struct RpcInfo {public PlayerRef Source;}
 public interface INetworkStruct {} public struct _32 {}
 public struct NetworkString<T> {string value;public override string ToString()=>value;public static implicit operator NetworkString<T>(string s)=>new(){value=s};}
 public struct PlayerRef:IEquatable<PlayerRef> {public int RawEncoded;public static PlayerRef None=>default;public static implicit operator PlayerRef(int i)=>new(){RawEncoded=i};public bool Equals(PlayerRef o)=>RawEncoded==o.RawEncoded;public override bool Equals(object o)=>o is PlayerRef r&&Equals(r);public override int GetHashCode()=>RawEncoded;public static bool operator==(PlayerRef a,PlayerRef b)=>a.Equals(b);public static bool operator!=(PlayerRef a,PlayerRef b)=>!a.Equals(b);}
 public struct NetworkId {public int Value;}
 public class NetworkObject {public bool IsValid=true,HasStateAuthority=true;}
 public class NetworkRunner {public double SimulationTime;public float DeltaTime=.25f;public PlayerRef LocalPlayer=1;}
 public class NetworkArray<T> {T[] data=new T[4];public int Length=>data.Length;public T this[int i]=>data[i];public void Set(int i,T value)=>data[i]=value;}
}
public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
public class Player {public bool IsAlive=true,IsBoosting;public Transform transform=new();public static Dictionary<PlayerRef,Player> players=new();public static Player FindInRunner(NetworkRunner r,PlayerRef p)=>players.TryGetValue(p,out var player)?player:null;}
public partial class PlayerData:MonoBehaviour {
 public NetworkObject Object=new();public NetworkRunner Runner;public PlayerRef ParticipantRef;public int teamIndex;public bool IsLoadoutInitialized=true,IsMatchBot;
 public NetworkString<_32> playerName;
 public static List<PlayerData> PerkPlayers=new();
 public static PlayerData FindPerkData(NetworkRunner runner,PlayerRef p)=>PerkPlayers.Find(d=>d.Runner==runner&&d.ParticipantRef==p);
}
public partial class BattleFlag {
 public static BattleFlag Instance;public NetworkObject Object=new();public NetworkRunner Runner;public bool HasEnded;public PlayerRef Carrier;
 public bool IsCarriedBy(NetworkRunner r,PlayerRef p)=>r==Runner&&Carrier==p;
}
'@
foreach ($path in 'Assets/Script/PlayerData.BattleStats.cs','Assets/Script/BattleFlag.Results.cs') {
    $code = (Get-Content $path -Raw) -replace '(?m)^using [^;]+;\r?\n',''
    $code = $code.Replace('private void TickBattleActivity()', 'public void TickBattleActivity()').Replace('private void SelectMatchAwards()', 'public void SelectMatchAwards()').Replace('private void RPC_RequestPraise(', 'public void RPC_RequestPraise(')
    $code = $code.Replace('Awards => default;', 'Awards {get;} = new();').Replace('PraiseSequences => default;', 'PraiseSequences {get;} = new();')
    $code = $code.Replace('PraiseVoters => default;', 'PraiseVoters {get;} = new();')
    $source += "`n" + $code
}
$source += @'
public static class Checks {
 static int n; static void Check(bool ok,string m){if(!ok)throw new Exception(m);n++;}
 public static int Run(){
  var runner=new NetworkRunner();var flag=new BattleFlag{Runner=runner};BattleFlag.Instance=flag;
  var a=new PlayerData{Runner=runner,ParticipantRef=1,teamIndex=1,playerName="A"};
  var b=new PlayerData{Runner=runner,ParticipantRef=2,teamIndex=2,playerName="B"};
  PlayerData.PerkPlayers.AddRange(new[]{b,a});
  Player.players[1]=new(){IsBoosting=true};Player.players[1].transform.position=new(){y=-2};flag.Carrier=1;
  a.TickBattleActivity();Check(a.BattleFlagSeconds==.25f&&a.BattleBoostSeconds==.25f&&a.BattleUnderwaterSeconds==.25f,"host records all three real activity durations");
  Player.players[1].IsAlive=false;a.TickBattleActivity();Check(a.BattleUnderwaterSeconds==.25f,"death does not accumulate underwater time");Player.players[1].IsAlive=true;
  a.Object.HasStateAuthority=false;a.TickBattleActivity();a.RecordWallCrash();Check(a.BattleWallCrashes==0&&a.BattleFlagSeconds==.25f,"proxy cannot write activity");a.Object.HasStateAuthority=true;
  BattleManager.Instance.IsGameplayActive=false;a.TickBattleActivity();a.RecordWallCrash();Check(a.BattleWallCrashes==0&&a.BattleBoostSeconds==.25f,"intro/intermission/results do not count");BattleManager.Instance.IsGameplayActive=true;
  a.RecordWallCrash();b.RecordWallCrash();b.RecordWallCrash();
  b.RecordBattleDamage(1,50);a.RecordBattleDamage(2,20);b.RecordBattleDeath(new NetworkId{Value=1},a);b.RecordBattleDeath(new NetworkId{Value=1},a);
  Check(a.BattleDamageDealt==50&&b.BattleDamageTaken==50&&a.BattleKills==1&&b.BattleDeaths==1,"real damage and unique death totals drive awards");
  var seen=new HashSet<BattleAward>();
  for(int run=0;run<100;run++){
   flag=new BattleFlag{Runner=runner};flag.SelectMatchAwards();var distinct=new HashSet<BattleAward>();
   for(int i=0;i<4;i++){
    var award=flag.Awards[i];distinct.Add(award.Category);seen.Add(award.Category);
    var winner=PlayerData.FindPerkData(runner,award.Winner);
    Check(award.Value==BattleAwardRules.Value(winner,award.Category),"selected count matches category");
    Check(award.Value>=BattleAwardRules.Value(a,award.Category)&&award.Value>=BattleAwardRules.Value(b,award.Category),"highest value wins");
   }
   Check(distinct.Count==4,"four distinct categories");var saved=flag.Awards[0];flag.SelectMatchAwards();Check(flag.Awards[0].Category==saved.Category&&flag.Awards[0].Winner==saved.Winner,"results freeze after one draw");
  }
  Check(seen.Count==8,"all eight categories can be selected");
  flag.HasEnded=true;
  for(int i=0;i<4;i++)flag.Awards.Set(i,new BattleAwardResult{Winner=i<2?1:2});
  flag.RPC_RequestPraise(0,new RpcInfo{Source=1});
  Check(flag.PraiseSequences[0]==1&&flag.PraiseSequences[1]==1&&flag.HasPraised(1),"vote is replicated on every card of its recipient");
  flag.RPC_RequestPraise(0,new RpcInfo{Source=1});Check(flag.PraiseSequences[0]==1,"rapid duplicate ignored");
  flag.RPC_RequestPraise(2,new RpcInfo{Source=99});Check(flag.PraiseSequences[2]==0,"spectator cannot praise");
  runner.SimulationTime=200;flag.RPC_RequestPraise(2,new RpcInfo{Source=1});Check(flag.PraiseSequences[2]==0,"same voter cannot vote on another panel later");
  flag.RPC_RequestPraise(-1,new RpcInfo{Source=2});Check(!flag.HasPraised(2),"invalid panel does not consume a vote");
  flag.RPC_RequestPraise(1,new RpcInfo{Source=2});Check(flag.PraiseSequences[0]==2&&flag.PraiseSequences[1]==2,"distinct voters accumulate on the same player");
  flag.HasEnded=false;runner.SimulationTime=400;flag.RPC_RequestPraise(2,new RpcInfo{Source=1});Check(flag.PraiseSequences[2]==0,"praise unavailable before final result");
  Check(BattleAwardRules.Count(BattleAward.Seal,125.99f)=="02:05"&&BattleAwardRules.Unit(BattleAward.Seal)=="초","duration mm:ss and requested seconds unit");
  a.ResetBattleStats();b.ResetBattleStats();flag=new BattleFlag{Runner=runner};flag.SelectMatchAwards();
  int aSeats=0;for(int i=0;i<4;i++)if(flag.Awards[i].Winner==a.ParticipantRef)aSeats++;
  Check(aSeats==2,"two tied players split the four seats evenly");
  var c=new PlayerData{Runner=runner,ParticipantRef=3,teamIndex=1,playerName="Bot C",IsMatchBot=true};
  var d=new PlayerData{Runner=runner,ParticipantRef=4,teamIndex=2,playerName="Bot D",IsMatchBot=true};
  PlayerData.PerkPlayers.AddRange(new[]{c,d});
  flag=new BattleFlag{Runner=runner};flag.SelectMatchAwards();var seats=new HashSet<PlayerRef>();
  Check(!flag.HasPraised(1)&&!flag.HasPraised(2),"a new match has no previous votes");
  flag.HasEnded=true;flag.RPC_RequestPraise(0,new RpcInfo{Source=3});Check(!flag.HasPraised(3),"bots cannot cast votes");
  for(int i=0;i<4;i++)seats.Add(flag.Awards[i].Winner);
  Check(seats.Count==4&&seats.Contains(c.ParticipantRef)&&seats.Contains(d.ParticipantRef),"tied humans and bots all get one seat when possible");
  Check(a.BattleFlagSeconds==0&&a.BattleUnderwaterSeconds==0&&a.BattleBoostSeconds==0&&a.BattleWallCrashes==0&&a.BattleKills==0,"new match clears every total");
  return n;
 }
}
'@
Add-Type -TypeDefinition $source
"Battle award ranking, random categories, activity accounting and praise validation checks passed: $([Checks]::Run())"
