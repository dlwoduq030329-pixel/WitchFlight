# Runs production input selection and authoritative cast dispatch with deterministic Unity/Fusion doubles.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$player = Get-Content Assets/Script/Player.cs -Raw
function Method([string]$name) {
    $m = [regex]::Match($player, '(?ms)^    private void ' + $name + '\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing method $name" }
    $m.Value
}
$source = @'
using System;
using Fusion;
using UnityEngine;
namespace UnityEngine { public struct Vector2{} public struct Vector3{} }
namespace Fusion {
 public interface INetworkInput{}
 public struct NetworkBool {bool value;public static implicit operator bool(NetworkBool b)=>b.value;public static implicit operator NetworkBool(bool b)=>new(){value=b};}
 public struct NetworkId {public int value;}
 public struct NetworkButtons {
  int bits; public bool IsSet(PlayerInputButton b)=>(bits&(1<<(int)b))!=0;
  public void Set(PlayerInputButton b,bool value){if(value)bits|=1<<(int)b;else bits&=~(1<<(int)b);}
  public bool WasPressed(NetworkButtons old,PlayerInputButton b)=>IsSet(b)&&!old.IsSet(b);
 }
}
public enum MagicType {None,Instant,Fire,Thunder,Beam,Blade,BladeRain,MagicStorm,Heal,BasicAttack}
public enum MagicEffectKind {None,DelayedArea}
public struct MagicStatEntry {public MagicType magic;public MagicEffectKind effect;public bool TargetsAllies,IsChanneled,requiresTarget;}
public class Authority {public bool HasStateAuthority=true;}
public class TestRunner {public TestTick Tick=new();public float DeltaTime=.02f;}
public class TestTick {public int Raw;}
public class Marker {public IDisposable Auto()=>new Scope();class Scope:IDisposable{public void Dispose(){}}}
public class Player {
 public Authority Object=new(); public TestRunner Runner=new();Marker combatMarker=new();
 NetworkButtons previousButtons; MagicSelectionInput selection;int lastMagicSelectionRevision;
 public int CurrentMagicSlot=0, BasicAttackCharges=3, CastSequence, LockFailureSequence, BladeCharges, ParryCount, LastCastSlot, CancelRainCount;
 public MagicStatEntry First,Second;
 public MagicStatEntry SelectedMagicStats=>CurrentMagicSlot==0?new(){magic=MagicType.BasicAttack}:CurrentMagicSlot==1?First:Second;
 public MagicType GetMagicInSlot(int slot)=>slot==1?First.magic:slot==2?Second.magic:MagicType.None;
 void FireBasicAttack(){if(BasicAttackCharges>0){BasicAttackCharges--;CastSequence++;LastCastSlot=0;}}
 public bool areaAimArmed, channelNeedsRelease, predictedRaisePending, RainFiring, IsHitStunned, IsMagicStormActive, IsMatchBot;
 public bool ChannelActive, LastHeld, LastReleased;public int LockTicks,LastTarget;
 float weaponRaiseHeldSeconds;float TimeFieldSpeed=1;int castInputTick;Vector3 castAimDirection;MagicType pendingMagic;
 void ClearBladeAbility(){BladeCharges=0;}
 void CancelBladeRain(){CancelRainCount++;}
 void StopChannel(){ChannelActive=false;}
 void ClearLockTargetInternal(bool report=true){LockTicks=0;}
 void UpdateMagicStormAim(Vector3 aim){}
 void TryStartParry(){ParryCount++;}
 void FireBlade(){CastSequence++;BladeCharges--;LastCastSlot=CurrentMagicSlot;}
 void UpdateHealingTarget(NetworkId target){LastTarget=target.value;}
 void SetInputLockTarget(NetworkId target){LastTarget=target.value;}
 void UpdateLockCharge(){LockTicks++;}
 void TryCastSelectedMagic(){if(!SelectedMagicStats.requiresTarget||LockTicks>0){CastSequence++;LastCastSlot=CurrentMagicSlot;}}
 void UpdateChannel(MagicStatEntry s,NetworkId target,Vector3 dir){ChannelActive=true;}
 void ProcessAreaAim(NetworkInputData data,bool held,bool pressed,bool released){if(pressed)areaAimArmed=true;if(released&&areaAimArmed){CastSequence++;LastCastSlot=CurrentMagicSlot;areaAimArmed=false;}}
 void ProcessBladeRain(NetworkInputData data,bool held,bool released){LastHeld=held;LastReleased=released;}
 public void Input(bool fire,int toggle=0){
  if(toggle!=0)selection.Toggle(toggle,fire);
  NetworkInputData data=new(){usesToggleSelection=true,selectedMagicSlot=selection.Slot,magicSelectionRevision=selection.Revision};
  data.buttons.Set(PlayerInputButton.Lock,selection.SampleCast(fire));data.lockTarget=new(){value=42};Tick(data);
 }
 public void Tick(NetworkInputData data){SelectInputMagicSlot(data);ProcessAuthoritativeCombat(data,data.buttons);previousButtons=data.buttons;}
'@
$network = (Get-Content Assets/Script/NetworkInputData.cs -Raw) -replace '(?m)^using [^;]+;\r?\n',''
$source += (Method 'SelectInputMagicSlot') + (Method 'SelectMagicSlot') + (Method 'ProcessAuthoritativeCombat') + "`n}`n" + $network
$source += @'
public static class Checks {
 static int count;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
 static MagicStatEntry Spell(MagicType m)=>new(){magic=m,requiresTarget=m==MagicType.Fire,IsChanneled=m==MagicType.Beam,TargetsAllies=m==MagicType.Heal,effect=m==MagicType.Thunder?MagicEffectKind.DelayedArea:MagicEffectKind.None};
 static Player Make(MagicType a,MagicType b)=>new(){First=Spell(a),Second=Spell(b)};
 public static int Run(){
  var selection=new MagicSelectionInput();selection.Toggle(1,false);int usedRevision=selection.Revision;
  Check(selection.ReturnToBasic(usedRevision,true)&&selection.Slot==0,"completed skill returns to basic mode");
  Check(!selection.SampleCast(true),"held cast cannot leak into an automatic basic attack");
  selection.SampleCast(false);Check(selection.SampleCast(true),"fresh click after completion attacks normally");
  selection.Toggle(2,false);int newerRevision=selection.Revision;
  Check(!selection.ReturnToBasic(usedRevision,false)&&selection.Slot==2,"late completion preserves newer E selection");
  Check(selection.ReturnToBasic(newerRevision,false)&&!selection.ReturnToBasic(newerRevision,false),"duplicate completion is idempotent");
  var p=Make(MagicType.Instant,MagicType.Fire);
  p.Input(false,1);Check(p.CurrentMagicSlot==1&&p.CastSequence==0,"Q selects without firing");
  p.Input(true);Check(p.CastSequence==1&&p.LastCastSlot==1,"LMB casts selected instant spell");
  p.Input(true);p.Input(false);Check(p.CastSequence==1,"hold/release does not repeat instant spell");
  p.Input(false,1);Check(p.CurrentMagicSlot==0,"same key toggles selection off");
  p.Input(true);Check(p.BasicAttackCharges==2&&p.LastCastSlot==0,"unselected LMB consumes a basic charge");
  p.Input(false,2);p.Input(true);Check(p.LockTicks==1&&p.LastTarget==42,"selected E spell locks with LMB");
  p.Input(false);Check(p.CastSequence==3&&p.LastCastSlot==2,"release fires lock spell");
  p.Input(true);p.Input(true,1);Check(p.CastSequence==3&&p.LockTicks==0,"switch while charging cancels without a phantom release cast");
  p.Input(true);Check(p.CastSequence==3,"holding fire across switch waits for a new click");
  p.Input(false);p.Input(true);Check(p.CastSequence==4&&p.LastCastSlot==1,"fresh click after switch fires");
  p=Make(MagicType.Blade,MagicType.Instant);p.Input(false,1);p.BladeCharges=3;
  p.Input(true);p.Input(false);p.Input(false,1);p.Input(true);
  Check(p.BladeCharges==1&&p.BasicAttackCharges==3&&p.CurrentMagicSlot==0,"blade overrides basic mode without consuming basic charges");
  p.Input(false);p.Input(true);Check(p.BladeCharges==0&&p.BasicAttackCharges==3,"last blade still leaves basic charges intact");
  p.Input(false);p.Input(true);Check(p.BasicAttackCharges==2,"next click after last blade is basic attack");
  p=Make(MagicType.Blade,MagicType.Instant);p.Input(false,1);p.BladeCharges=3;p.Input(false,2);
  Check(p.BladeCharges==0&&p.CastSequence==0,"different magic clears blade without firing");
  p=Make(MagicType.Instant,MagicType.Thunder);p.Input(false,2);p.Input(true);
  Check(p.areaAimArmed&&p.CastSequence==0,"area arms on left click");p.Input(false);
  Check(p.CastSequence==1&&p.LastCastSlot==2,"area fires on left release");
  p=Make(MagicType.Instant,MagicType.BladeRain);p.Input(false,2);p.Input(true);Check(p.LastHeld&&!p.LastReleased,"rain charges with left click");
  p.Input(false);Check(!p.LastHeld&&p.LastReleased,"rain releases with left click");
  p=Make(MagicType.Instant,MagicType.Beam);p.Input(false,2);p.Input(true);Check(p.ChannelActive,"beam runs while LMB held");
  p.Input(false);Check(!p.ChannelActive,"beam stops on left release");
  p=Make(MagicType.Instant,MagicType.Instant);NetworkInputData bot=new();bot.buttons.Set(PlayerInputButton.MagicSlot2,true);bot.buttons.Set(PlayerInputButton.Lock,true);p.Tick(bot);
  Check(p.LastCastSlot==2&&p.CastSequence==1,"legacy bot input remains supported");
  bot.buttons=default;bot.buttons.Set(PlayerInputButton.Parry,true);p.Tick(bot);Check(p.ParryCount==1,"parry remains independent");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source -CompilerOptions "/nowarn:0414"
"Direct magic input / authoritative dispatch checks passed: $([Checks]::Run())"
