# Execute production status queries/UI methods offline; no extra network messages.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$player = Get-Content Assets/Script/Player.cs -Raw
$hud = Get-Content Assets/Script/BattleHud.cs -Raw
function Method([string]$source, [string]$name) {
    $match = [regex]::Match($source, '(?ms)^    (?:private|public) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$match.Success) {throw "Missing method: $name"}
    $match.Value -replace '^    private ', '    public '
}
$binding = [regex]::Match($hud, '(?ms)^    private sealed class StatusEffectUIBinding\r?\n    \{.*?^    \}').Value -replace 'private sealed', 'public sealed'
if (!$binding) {throw 'Missing serialized status/UI binding'}
$visible = [regex]::Match($hud, '(?m)^    private static void Visible\(.*$').Value -replace 'private static', 'public static'
$source = @'
using System;
namespace StatusHudChecks {
public class TooltipAttribute:Attribute {public TooltipAttribute(string text){}}
public class Transform {
 public Transform parent;
 public bool IsChildOf(Transform other)=>this==other||(parent!=null&&parent.IsChildOf(other));
}
public class GameObject {
 public Transform transform=new();public bool activeSelf=true;public int changes;
 public void SetActive(bool active){activeSelf=active;changes++;}
}
public static class Debug {public static int warnings;public static void LogWarning(string message,object context){warnings++;}}
public class NetworkObject {public bool IsValid=true,HasInputAuthority=true;}
public class NetworkRunner {public bool IsRunning=true;public int tick;}
public struct TickTimer {
 public bool IsRunning;public int end;
 public TickTimer(int end){this.end=end;IsRunning=true;}
 public bool Expired(NetworkRunner runner)=>runner.tick>=end;
}
'@
$source += Get-Content Assets/Script/StatusEffectType.cs -Raw
$source += @'
public class Player {
 public static Player LocalPlayer;
 public NetworkObject Object=new();public NetworkRunner Runner=new();public bool IsAlive=true;
 public TickTimer BindingTimer,SlowTimer,HitStunTimer;
'@
$source += (Method $player 'HasStatusEffect') + "`n" + (Method $player 'TimerIsActive')
$source += "`n} public class Hud {public Player owner; public Transform transform=new();`n" + $binding
$source += "`npublic StatusEffectUIBinding[] statusEffectViews;`n" + $visible
$source += "`n" + (Method $hud 'ValidateStatusEffectViews') + "`n" + (Method $hud 'BindStatusEffectViews')
$source += @'
}
public static class Tests {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 public static int Run(){
  Check((int)StatusEffectType.None==0&&(int)StatusEffectType.Binding==1&&(int)StatusEffectType.Slow==2&&(int)StatusEffectType.HitStun==3,"stable serialized IDs");
  var me=new Player();Player.LocalPlayer=me;
  var bound=new GameObject();var slow=new GameObject();var stun=new GameObject();var unused=new GameObject();
  var hud=new Hud{owner=me,statusEffectViews=new[]{
   new Hud.StatusEffectUIBinding{status=StatusEffectType.Binding,uiObject=bound},
   new Hud.StatusEffectUIBinding{status=StatusEffectType.Slow,uiObject=slow},
   new Hud.StatusEffectUIBinding{status=StatusEffectType.HitStun,uiObject=stun},
   new Hud.StatusEffectUIBinding{status=StatusEffectType.None,uiObject=unused},null,new Hud.StatusEffectUIBinding()}};
  hud.ValidateStatusEffectViews();hud.BindStatusEffectViews(false);
  Check(!bound.activeSelf&&!slow.activeSelf&&!stun.activeSelf&&!unused.activeSelf,"startup clears authored active status UI");
  hud.BindStatusEffectViews(true);Check(!bound.activeSelf,"no binding timer means no status UI");
  me.BindingTimer=new(3);hud.BindStatusEffectViews(true);
  Check(bound.activeSelf&&!slow.activeSelf&&!stun.activeSelf&&!unused.activeSelf,"replicated Binding timer activates only Binding UI");
  int changes=bound.changes;for(int frame=0;frame<100;frame++)hud.BindStatusEffectViews(true);
  Check(bound.changes==changes,"unchanged status does not restart animations or SetActive");
  me.Runner.tick=2;hud.BindStatusEffectViews(true);Check(bound.activeSelf,"Binding lasts through remaining server ticks");
  me.BindingTimer=new(6);me.Runner.tick=3;hud.BindStatusEffectViews(true);
  Check(bound.activeSelf,"reapplied binding follows new server deadline, not a stale local duration");
  me.SlowTimer=new(10);me.HitStunTimer=new(4);hud.BindStatusEffectViews(true);
  Check(bound.activeSelf&&slow.activeSelf&&stun.activeSelf,"simultaneous statuses can display independently");
  me.Runner.tick=4;hud.BindStatusEffectViews(true);Check(bound.activeSelf&&slow.activeSelf&&!stun.activeSelf,"hit stun expires independently");
  me.Runner.tick=6;hud.BindStatusEffectViews(true);Check(!bound.activeSelf&&slow.activeSelf,"binding disappears at the exact expiry tick");
  me.SlowTimer=default;hud.BindStatusEffectViews(true);Check(!slow.activeSelf,"explicit status removal hides UI immediately");
  me.BindingTimer=new(20);hud.BindStatusEffectViews(false);Check(!bound.activeSelf,"menu/intermission/end hides statuses even with running timers");
  hud.BindStatusEffectViews(true);Check(bound.activeSelf,"resuming HUD re-evaluates current timer");
  me.IsAlive=false;hud.BindStatusEffectViews(true);Check(!bound.activeSelf,"death hides old timers before despawn");
  me.IsAlive=true;me.Object.IsValid=false;hud.BindStatusEffectViews(true);Check(!bound.activeSelf,"despawned network object is safe");
  Check(!me.HasStatusEffect(StatusEffectType.Binding),"invalid network query returns false");me.Object.IsValid=true;
  me.Object.HasInputAuthority=false;hud.BindStatusEffectViews(true);Check(!bound.activeSelf,"remote-authority UI is never displayed");me.Object.HasInputAuthority=true;
  hud.owner=new Player{BindingTimer=new(20)};hud.BindStatusEffectViews(true);Check(!bound.activeSelf,"enemy status cannot be mistaken for the local player's");
  hud.owner=null;hud.BindStatusEffectViews(true);Check(!bound.activeSelf,"between character spawns has no stale UI");hud.owner=me;
  me.Runner.IsRunning=false;hud.BindStatusEffectViews(true);Check(!bound.activeSelf,"disconnected runner clears UI");me.Runner.IsRunning=true;
  var runner=me.Runner;me.Runner=null;Check(!me.HasStatusEffect(StatusEffectType.Binding),"missing runner safe");me.Runner=runner;
  var obj=me.Object;me.Object=null;Check(!me.HasStatusEffect(StatusEffectType.Binding),"missing network object safe");me.Object=obj;
  Check(!me.HasStatusEffect(StatusEffectType.None)&&!me.HasStatusEffect((StatusEffectType)999),"None/unknown enum never activates an unrelated effect");
  Player.LocalPlayer=hud.owner=new Player();hud.BindStatusEffectViews(true);Check(!bound.activeSelf,"fresh respawn does not retain predecessor status");
  var invalidRoot=new GameObject();hud.transform.parent=invalidRoot.transform;
  var invalidSelf=new GameObject{transform=hud.transform};
  hud.statusEffectViews=new[]{new Hud.StatusEffectUIBinding{uiObject=invalidRoot},new Hud.StatusEffectUIBinding{uiObject=invalidSelf}};
  hud.ValidateStatusEffectViews();hud.BindStatusEffectViews(false);
  Check(Debug.warnings==2&&hud.statusEffectViews[0].uiObject==null&&hud.statusEffectViews[1].uiObject==null,"reject controller/ancestor assignments with an actionable warning");
  Check(invalidRoot.activeSelf&&invalidSelf.activeSelf,"cannot deactivate the controller and strand the HUD");
  hud.statusEffectViews=new[]{new Hud.StatusEffectUIBinding{uiObject=invalidSelf}};
  hud.BindStatusEffectViews(false);Check(invalidSelf.activeSelf,"runtime reassignment is also protected");
  hud.statusEffectViews=null;hud.ValidateStatusEffectViews();hud.BindStatusEffectViews(true);Check(true,"empty/null list safe");
  return count;
 }
}}
'@
Add-Type -TypeDefinition $source
"Status effect UI/timer behavior checks passed: $([StatusHudChecks.Tests]::Run())"

if (!(Method $hud 'Update').Contains('BindStatusEffectViews(valid && battle.IsGameplayActive && !MenuOpen);')) {throw 'Combat lifecycle must drive status UI'}
if (!(Method $hud 'HideViews').Contains('BindStatusEffectViews(false);')) {throw 'Disable/death/intermission must clear status UI'}
if (!(Method $hud 'Awake').Contains('ValidateStatusEffectViews();')) {throw 'Validate inspector assignments before hiding UI'}
$query = Method $player 'HasStatusEffect'
if ($query -match '\[Rpc|Request|CreateFromSeconds|BindingTimer\s*=(?!=)') {throw 'UI query must not send messages or change authoritative effects'}
$hit = Method $player 'ReceiveMagicHit'
if ($hit.IndexOf('if (IsParrying)') -gt $hit.IndexOf('if (stats.effect == MagicEffectKind.Binding)') -or
    !$hit.Contains('BindingTimer = TickTimer.CreateFromSeconds(Runner, StatusPerkDuration(stats.effectDuration));')) {throw 'Binding must remain authoritative, parryable and use the configured duration with cleanse resistance'}
$scene = Get-Content Assets/Scenes/Battle.unity -Raw
if ($scene -notmatch 'statusEffectViews:\s+- status: 1\s+uiObject: \{fileID: 2027782598\}' -or
    $scene -notmatch '(?s)--- !u!1 &2027782598 stripped\s+GameObject:\s+m_CorrespondingSourceObject: \{fileID: 3976024349891690280, guid: f85a690f8032072489dd7b498ab4110d, type: 3\}\s+m_PrefabInstance: \{fileID: 2027782578\}') {throw 'Battle must bind the existing BindingText object'}
$prefab = Get-Content Assets/UI/Prefab/BattleUI.prefab -Raw
if ($prefab -notmatch '(?s)--- !u!1 &3976024349891690280\s+GameObject:.*?m_Name: BindingText') {throw 'Wrong prefab UI target'}
'Local-only UI, no extra networking, existing Binding rules and saved scene binding passed.'
