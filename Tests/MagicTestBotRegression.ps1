# Execute the actual dummy AI against lightweight Fusion/Unity stubs.
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$bot=Get-Content (Join-Path $repo 'Assets/Script/MagicTestBot.cs') -Raw
$stubs=@'
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public class MonoBehaviour {}
 public class DisallowMultipleComponent:Attribute {}
 public class RequireComponent:Attribute {public RequireComponent(Type t){}}
 public class SerializeField:Attribute {}
 public class TooltipAttribute:Attribute {public TooltipAttribute(string s){}}
 public class MinAttribute:Attribute {public MinAttribute(float v){}}
 public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static int Max(int a,int b)=>Math.Max(a,b);}
 public struct Vector3 {public float z;public float sqrMagnitude=>z*z;public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3{z=a.z-b.z};}
}
namespace Fusion {
 public class Runner {public float Time;}
 public struct TickTimer {
  public bool IsRunning;public float End;public static TickTimer None=>default;
  public bool Expired(Runner r)=>IsRunning&&r.Time>=End;
  public static TickTimer CreateFromSeconds(Runner r,float t)=>new TickTimer{End=r.Time+t,IsRunning=true};
 }
}
namespace BotChecks {
using UnityEngine;
using Fusion;
public enum MagicType {None,Fire,Ice,Vision,Binding}
public class Obj {public bool HasStateAuthority=true;}
public class Player {
 public static List<Player> ActiveCombatants=new();
 public Obj Object=new();public Runner Runner=new();
 public bool IsAlive=true,IsTestBot,Visible=true;
 public int Team,HitSequence,Shots;public float MaxHp=200,Hp=200;public MagicType LastShot;
 public Vector3 LockAimPoint;
 public bool IsTargetableBy(Player p)=>p!=this&&Team!=p.Team&&IsAlive;
 public bool FireTestBotMagic(MagicType magic,Player target){Shots++;LastShot=magic;return true;}
 public void RestoreHealth(float amount){Hp=Math.Min(MaxHp,Hp+amount);}
}
public static class MagicProjectile {public static bool HasBlastSight(Vector3 origin,Player p)=>p.Visible;}
}
'@
$tests=@'
namespace BotChecks {
using System.Reflection;
public static class Tests {
 static int count;
 static void Check(bool b,string name){if(!b)throw new Exception(name);count++;}
 static void Set(MagicTestBot b,string key,object value)=>typeof(MagicTestBot).GetField(key,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(b,value);
 public static int Run(){
  var bot=new MagicTestBot();var self=new Player{IsTestBot=true,Team=99};var enemy=new Player{Team=1,Runner=self.Runner};
  enemy.LockAimPoint=new UnityEngine.Vector3{z=20};
  Player.ActiveCombatants=new(){self,enemy};
  bot.Simulate(self);self.Runner.Time=10;bot.Simulate(self);Check(self.Shots==0,"passive never attacks");
  Set(bot,"attacks",true);self.Runner.Time=0;bot.Simulate(self);
  self.Runner.Time=1.99f;bot.Simulate(self);Check(self.Shots==0,"first shot waits two seconds");
  self.Runner.Time=2;bot.Simulate(self);Check(self.Shots==1,"first shot at two seconds");
  self.Runner.Time=3.99f;bot.Simulate(self);Check(self.Shots==1,"no early second shot");
  self.Runner.Time=4;bot.Simulate(self);Check(self.Shots==2,"second shot at four seconds");
  enemy.LockAimPoint=new UnityEngine.Vector3{z=100};self.Runner.Time=5;bot.Simulate(self);
  self.Runner.Time=20;bot.Simulate(self);Check(self.Shots==2,"outside distance stops fire");
  enemy.LockAimPoint=new UnityEngine.Vector3{z=20};bot.Simulate(self);
  self.Runner.Time=21;bot.Simulate(self);Check(self.Shots==2,"reenter waits full interval");
  self.Runner.Time=22;bot.Simulate(self);Check(self.Shots==3,"reenter fires after interval");
  enemy.Visible=false;self.Runner.Time=24;bot.Simulate(self);Check(self.Shots==3,"wall blocks bot");
  enemy.Visible=true;enemy.IsTestBot=true;bot.Simulate(self);self.Runner.Time=30;bot.Simulate(self);Check(self.Shots==3,"does not attack other dummies");
  enemy.IsTestBot=false;enemy.Team=99;bot.Simulate(self);Check(self.Shots==3,"does not attack same team");
  enemy.Team=1;enemy.Runner=new Fusion.Runner();bot.Simulate(self);Check(self.Shots==3,"does not attack another runner");
  enemy.Runner=self.Runner;Set(bot,"attackInterval",.5f);Set(bot,"attackMagic",MagicType.Binding);
  bot.Simulate(self);self.Runner.Time=30.5f;bot.Simulate(self);Check(self.Shots==4&&self.LastShot==MagicType.Binding,"editable interval and homing type");
  Set(bot,"attackMagic",MagicType.Vision);Check(bot.AttackMagic==MagicType.Fire,"invalid non-lock spell defaults to Fire");
  self.Object.HasStateAuthority=false;self.Runner.Time=40;bot.Simulate(self);Check(self.Shots==4,"only state authority shoots");
  self.Object.HasStateAuthority=true;self.IsAlive=false;bot.Simulate(self);Check(self.Shots==4,"dead bot cannot shoot");
  self.IsAlive=true;Set(bot,"attacks",false);self.Hp=20;self.HitSequence++;bot.Simulate(self);
  self.Runner.Time=44.99f;bot.Simulate(self);Check(self.Hp==20,"health reset waits configured delay");
  self.HitSequence++;bot.Simulate(self);self.Runner.Time=45;bot.Simulate(self);Check(self.Hp==20,"new damage extends reset delay");
  self.Runner.Time=50;bot.Simulate(self);Check(self.Hp==200,"dummy health resets after last hit");
  Set(bot,"resetHealthAfterSeconds",0f);self.Hp=1;self.HitSequence++;bot.Simulate(self);self.Runner.Time=100;bot.Simulate(self);
  Check(self.Hp==1,"zero disables auto healing");
  return count;
 }
}}
'@
$nl=[Environment]::NewLine
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition ($stubs+$nl+'namespace BotChecks {'+$nl+$bot+$nl+'}'+$nl+$tests)
"Dummy behavior checks passed: $([BotChecks.Tests]::Run())"
foreach($name in @('MagicDummy_Immortal','MagicDummy_LockOnAttacker')) {
 $path=Join-Path $repo ("Assets/Prefab/"+$name+".prefab")
 $text=Get-Content $path -Raw
 foreach($guid in @('6aab7e08f334a45428600445ff5f2532','c439dfec4e304d56b2d91e5c5b6a4219','d81c29d2f62042b1944c5e42544d9636')) {
  if(!$text.Contains($guid)){throw "Missing required reference in $name"}
 }
 if($text -notmatch 'RawGuidValue: 1af9a38d86554be0a519edc0452ebc9f'){throw "Missing projectile in $name"}
 if($text -notmatch 'attackInterval: 2'){throw "Default interval incorrect"}
 if($text -match 'guid: 31321ba15b8f8eb4c954353edc038b1d'){throw "Old missing material reference"}
 if((Get-Content ($path+'.meta') -Raw) -notmatch 'FusionPrefab'){throw "Missing Fusion prefab label"}
 if($name -eq 'MagicDummy_Immortal' -and ($text -notmatch 'immortal: 1' -or $text -notmatch 'attacks: 0')) {throw "Passive config wrong"}
 if($name -eq 'MagicDummy_LockOnAttacker' -and $text -notmatch 'attacks: 1') {throw "Attacker config wrong"}
}
'Dummy prefab references/configuration checked.'
