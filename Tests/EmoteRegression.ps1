# Real input gate + catalog + RPC method bodies with minimal Unity/Fusion doubles.
# This does not emulate Fusion transport/weaving or render the Unity Canvas.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$gate = Get-Content Assets/Script/EmoteInputGate.cs -Raw
$table = Get-Content Assets/Script/EmoteTable.cs -Raw
$network = Get-Content Assets/Script/Player.Emotes.cs -Raw
$stubs = @'
using System;
using System.Reflection;
using UnityEngine;
using Fusion;
namespace UnityEngine {
 [AttributeUsage(AttributeTargets.Field)] public class SerializeField:Attribute {}
 [AttributeUsage(AttributeTargets.Field)] public class MinAttribute:Attribute {public MinAttribute(float n){}}
 [AttributeUsage(AttributeTargets.Field)] public class TooltipAttribute:Attribute {public TooltipAttribute(string s){}}
 [AttributeUsage(AttributeTargets.Class)] public class CreateAssetMenuAttribute:Attribute {public string fileName,menuName;}
 public class ScriptableObject {} public class Sprite {}
 public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);}
 public static class Resources {public static object Value;public static T Load<T>(string s) where T:class=>Value as T;}
 public static class Debug {public static void LogWarning(string s,object o){}}
}
namespace Fusion {
 public enum RpcSources {InputAuthority,StateAuthority}
 public enum RpcTargets {StateAuthority,All}
 public enum RpcChannel {Reliable}
 [AttributeUsage(AttributeTargets.Method)] public class RpcAttribute:Attribute {
  public RpcSources Sources;public RpcTargets Targets;public RpcChannel Channel;
  public RpcAttribute(RpcSources s,RpcTargets t){Sources=s;Targets=t;}
 }
 public enum PlayerRef {None,One,Two}
 public class NetworkObject {public bool IsValid=true,HasInputAuthority=true;public PlayerRef InputAuthority=PlayerRef.One;}
 public class NetworkRunner {public bool IsRunning=true;public float Time;}
 public struct TickTimer {
  public bool IsRunning;public float End;
  public static TickTimer CreateFromSeconds(NetworkRunner runner,float duration)=>new TickTimer{IsRunning=true,End=runner.Time+duration};
 }
}
public class BattleManager {public static BattleManager Instance=new BattleManager();public bool IsGameplayActive=true;}
public static class BattleEmoteUI {public static int Count,Last;public static void Present(Player p,int id){Count++;Last=id;}}
public partial class Player {
 public NetworkObject Object=new NetworkObject();public NetworkRunner Runner=new NetworkRunner();
 public bool IsAlive=true,IsReturningToMap;
 private bool TimerIsActive(TickTimer timer)=>timer.IsRunning&&Runner.Time<timer.End;
 public void HostRequest(int id)=>RPC_RequestEmote(id);
}
'@
$source = $stubs + ($gate -replace '(?m)^using [^;]+;\r?\n','') +
    ($table -replace '(?m)^using [^;]+;\r?\n','') + ($network -replace '(?m)^using [^;]+;\r?\n','')
$source += @'
public static class EmoteChecks {
 static int count;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
 static void Step(EmoteInputGate gate,bool held,float x=0,float y=0,bool allowed=true,bool cancel=false,bool left=false,bool right=false)
  =>gate.Sample(allowed,held,cancel,x,y,left,right,28,140);
 public static int Run(){
  for(int slot=0;slot<4;slot++) {
   var g=new EmoteInputGate();float x=slot==1?80:slot==3?-80:0,y=slot==0?80:slot==2?-80:0;
   Step(g,true,x,y);Check(g.Showing&&g.CapturesMouse&&g.BlocksCombat&&g.SelectedSlot==slot,"hold/select direction "+slot);
   Step(g,false);Check(!g.Showing&&g.Choice==slot&&g.CapturesMouse&&g.BlocksCombat,"release commits once "+slot);
   Step(g,false);Check(g.Choice==-1&&!g.BlocksCombat&&!g.CapturesMouse,"next frame clears "+slot);
  }
  var gate=new EmoteInputGate();Step(gate,true);Step(gate,false);
  Check(gate.Choice==-1,"tap at centre cancels");
  Step(gate,true,0,80);Step(gate,true,0,-80);Step(gate,false);
  Check(gate.Choice==-1,"move back to centre cancels");
  Step(gate,true,80,0);Step(gate,true,-80,90);Step(gate,false);
  Check(gate.Choice==0,"change candidate before release");
  Step(gate,true,0,0);Step(gate,false,-70,0);
  Check(gate.Choice==3,"release-frame mouse motion included");
  Step(gate,true,10000,2000);
  Check(Math.Abs(Math.Sqrt(gate.X*gate.X+gate.Y*gate.Y)-140)<.01&&gate.SelectedSlot==1,"cursor radius capped");
  Step(gate,false);Step(gate,true,0,0);Step(gate,true,20,10);Step(gate,false);
  Check(gate.Choice==-1,"dead zone ignores small motion");
  foreach(bool esc in new[]{false,true}) {
   gate=new EmoteInputGate();Step(gate,true,0,80);
   Step(gate,true,allowed:esc,cancel:esc);
   Check(!gate.Showing&&gate.Choice==-1&&gate.CapturesMouse,"focus/phase/Esc cancels without send");
   Step(gate,true);Check(!gate.Showing,"still-held T does not reopen after cancel");
   Step(gate,false);Check(gate.Choice==-1,"cancelled hold release does not send");
   Step(gate,true,0,80);Check(gate.Showing,"fresh T opens after cancellation");
  }
  gate=new EmoteInputGate();gate.Reset(true);Step(gate,true,0,80);
  Check(!gate.Showing,"component enabled while T held waits for fresh press");
  Step(gate,false);Step(gate,true,80,0,left:true,right:true);Step(gate,false,left:true,right:true);
  Check(gate.Choice==1&&gate.BlocksCombat,"clicks do not choose or release into combat");
  Step(gate,false,left:true);Check(gate.BlocksCombat&&!gate.CapturesMouse,"held combat click stays quarantined");
  Step(gate,false);Check(gate.BlocksCombat,"mouse release frame still blocked");
  Step(gate,false,left:true);Check(!gate.BlocksCombat,"fresh combat click after release allowed");
  gate=new EmoteInputGate();Step(gate,true,allowed:false);Step(gate,true);
  Check(!gate.Showing,"cannot open by holding T through intermission");
  Step(gate,false);Step(gate,true,float.NaN,float.PositiveInfinity);
  Check(gate.SelectedSlot==-1&&gate.X==0&&gate.Y==0,"invalid delta ignored");

  var table=new EmoteTable();Resources.Value=table;
  for(int id=1;id<=4;id++) Check(table.TryGet(id,out var e)&&e.id==id&&!string.IsNullOrEmpty(e.displayName),"catalog default "+id);
  Check(!table.TryGet(0,out _)&&!table.TryGet(-1,out _)&&!table.TryGet(1000,out _),"catalog rejects unknown IDs");
  Check(table.MinimumInterval>=.1f,"rate limit has safe floor");
  for(int id=1;id<=4;id++) {
   var p=new Player();int before=BattleEmoteUI.Count;
   Check(p.RequestEmote(id)&&BattleEmoteUI.Count==before+1&&BattleEmoteUI.Last==id,"owner ID reaches presentation "+id);
   p.HostRequest(id);Check(BattleEmoteUI.Count==before+1,"host rate limit "+id);
   p.Runner.Time=table.MinimumInterval;p.HostRequest(id);
   Check(BattleEmoteUI.Count==before+2,"rate limit expires "+id);
  }
  foreach(int id in new[]{-1,0,999}) {
   var p=new Player();int before=BattleEmoteUI.Count;
   Check(!p.RequestEmote(id),"local rejects invalid ID "+id);p.HostRequest(id);
   Check(BattleEmoteUI.Count==before,"host rejects invalid ID "+id);
  }
  var other=new Player();other.Object.HasInputAuthority=false;
  Check(!other.RequestEmote(1),"cannot request on somebody else's Player");
  var dead=new Player{IsAlive=false};int n=BattleEmoteUI.Count;
  Check(!dead.RequestEmote(1),"dead owner cannot send");dead.HostRequest(1);Check(BattleEmoteUI.Count==n,"dead actor rejected at host");
  var returning=new Player{IsReturningToMap=true};returning.HostRequest(1);
  Check(BattleEmoteUI.Count==n,"map-return actor rejected at host");
  var bot=new Player();bot.Object.InputAuthority=PlayerRef.None;bot.HostRequest(1);
  Check(BattleEmoteUI.Count==n,"unowned dummy cannot broadcast");
  BattleManager.Instance.IsGameplayActive=false;var outside=new Player();
  Check(!outside.RequestEmote(1),"cannot send in intermission");outside.HostRequest(1);
  Check(BattleEmoteUI.Count==n,"host rejects outside battle");
  BattleManager.Instance.IsGameplayActive=true;
  var request=typeof(Player).GetMethod("RPC_RequestEmote",BindingFlags.NonPublic|BindingFlags.Instance).GetCustomAttribute<RpcAttribute>();
  var present=typeof(Player).GetMethod("RPC_PresentEmote",BindingFlags.NonPublic|BindingFlags.Instance).GetCustomAttribute<RpcAttribute>();
  Check(request.Sources==RpcSources.InputAuthority&&request.Targets==RpcTargets.StateAuthority,"RPC only owner to host");
  Check(present.Sources==RpcSources.StateAuthority&&present.Targets==RpcTargets.All,"only host broadcasts to both peers");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
Write-Output ("Emote behaviour checks passed: " + [EmoteChecks]::Run())

function Require([bool]$ok,[string]$name) { if (!$ok) { throw $name } }
$scene = Get-Content Assets/Scenes/Battle.unity -Raw
$ui = Get-Content Assets/Script/BattleEmoteUI.cs -Raw
$net = Get-Content Assets/Script/NetworkGameManager.cs -Raw
$camera = Get-Content Assets/Script/Camera/CameraFollow.cs -Raw
$lock = Get-Content Assets/Script/enemyLockOn.cs -Raw
$perks = Get-Content Assets/Script/BattlePerkUI.cs -Raw
Require ($scene.Contains('- component: {fileID: 1515047386}') -and $scene.Contains('guid: b9d42e71640f4d2c8e8bb36c2944f3e4')) 'Battle scene must attach emote controller'
$component = [regex]::Match($scene,'(?s)--- !u!114 &1515047386\r?\n.*?(?=--- !u!)').Value
Require ($component.Contains('m_GameObject: {fileID: 1515047378}') -and $component.Contains('holdKey: 116')) 'Controller must live on active battlemanager with T key'
Require ($component.Contains('wheelRoot: {fileID: 0}') -and $component.Contains('localDisplay:') -and $component.Contains('opponentDisplay:')) 'Generated UI must be default with separate local/opponent channels'
Require ($net.Contains('data.suppressCombat = BattlePerkUI.BlocksCombatInput || BattleEmoteUI.BlocksCombatInput')) 'Network combat suppression missing'
Require ($net.Contains('data.look = BattleEmoteUI.CapturesMouse ? Vector2.zero : accumulatedLook') -and $net.Contains('if (BattleEmoteUI.CapturesMouse) accumulatedLook = Vector2.zero')) 'Mouse deltas must not queue into flight'
Require ($net.Contains('pendingEmoteCombatSuppression |= BattleEmoteUI.BlocksCombatInput') -and $net.Contains('BattleEmoteUI.BlocksCombatInput || pendingEmoteCombatSuppression')) 'Brief wheel opens between network ticks must still cancel pending casts'
Require ($camera.Contains('CombatPresentation.MenuOpen || BattleEmoteUI.CapturesMouse')) 'Camera must not rotate with selector'
Require (($lock | Select-String -Pattern 'BattleEmoteUI.BlocksCombatInput' -AllMatches).Matches.Count -eq 2) 'Both lock targeting paths must respect emote gate'
Require ((Get-Content Assets/Script/Player.AreaMagic.cs -Raw).Contains('!BattleEmoteUI.BlocksCombatInput')) 'Area preview must not open from wheel clicks'
Require ($perks.Contains('Allowed && !BattleEmoteUI.CapturesMouse')) 'Perk selection must not steal closing-frame clicks'
Require ($ui.Contains('sender.Object.HasInputAuthority') -and $ui.Contains('mine ? active.localDisplay : active.opponentDisplay')) 'Received emote must route by sender, not by client ID order'
Require (!$ui.Contains('Cursor.visible =') -and !$ui.Contains('Cursor.lockState = ')) 'Wheel must not unlock the cursor'
Require ($ui.Contains('public bool SetSlotEmote(int slot, int emoteId)')) 'Future customization needs validated slot API'
Require ($ui.Contains('view.generated && !view.overridePosition') -and $ui.Contains('rect.anchoredPosition = view.position')) 'Display positions must be adjustable without forcing custom UI layout'
$asset = Get-Content Assets/Resources/EmoteTable.asset -Raw
Require (($asset | Select-String -Pattern '(?m)^  - id: [1-4]$' -AllMatches).Matches.Count -eq 4) 'Table must contain four stable IDs'
Write-Output 'Emote scene/input integration contracts passed.'
