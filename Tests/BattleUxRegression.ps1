# Runs production math and death-volume transitions against deterministic stubs, not Unity Play Mode.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$hud = Get-Content Assets/Script/BattleHud.cs -Raw
$shot = Get-Content Assets/Script/MagicProjectile.cs -Raw
$prediction = Get-Content Assets/Script/ProjectilePredictionView.cs -Raw
$camera = Get-Content Assets/Script/Camera/SpeedCameraEffects.cs -Raw
$player = Get-Content Assets/Script/Player.cs -Raw
$intro = Get-Content Assets/Script/BattleIntroPresentation.cs -Raw
function Method([string]$source, [string]$name) {
    $m = [regex]::Match($source, '(?ms)^    (?:private|public) (?:static )?[\w<>]+ ' + $name + '\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing production method: $name" }
    $m.Value -replace '^    private ', '    public '
}
$stubs = @'
using System;
using System.Collections.Generic;
namespace UxChecks {
public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;}
public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;}public static Color black=>new(0,0,0);public static Color white=>new(1,1,1);public static Color Lerp(Color a,Color b,float t)=>new(Mathf.Lerp(a.r,b.r,t),Mathf.Lerp(a.g,b.g,t),Mathf.Lerp(a.b,b.b,t));}
public static class Mathf {
 public static float Max(float a,float b)=>Math.Max(a,b);public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);public static float Clamp01(float x)=>Math.Clamp(x,0,1);
 public static int RoundToInt(float x)=>(int)Math.Round(x);public static float Sqrt(float x)=>(float)Math.Sqrt(x);public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
 public static float InverseLerp(float a,float b,float x)=>Clamp01((x-a)/(b-a));public static float SmoothStep(float a,float b,float x){x=Clamp01(x);return Lerp(a,b,x*x*(3-2*x));}
}
public abstract class Parameter {public bool overrideState;public abstract void SetValue(Parameter other);}
public class ValueParameter<T>:Parameter {public T value;public void Override(T v){value=v;overrideState=true;}public override void SetValue(Parameter p){value=((ValueParameter<T>)p).value;}}
public class VolumeComponent {public bool active=true;public List<Parameter> parameters=new();}
public class Vignette:VolumeComponent {public ValueParameter<Color> color=new(){value=Color.black};public ValueParameter<float> intensity=new(),smoothness=new();public Vignette(){parameters.AddRange(new Parameter[]{color,intensity,smoothness});}}
public class ColorAdjustments:VolumeComponent {public ValueParameter<Color> colorFilter=new(){value=Color.white};public ValueParameter<float> saturation=new();public ColorAdjustments(){parameters.AddRange(new Parameter[]{colorFilter,saturation});}}
public class Obj {public bool IsValid=true,HasInputAuthority=true;}
public class Transform {public Vector3 position;}
public class Player {public Obj Object=new();public bool IsAlive=true;public Transform transform=new();}
public class MapBoundaryTable {public float GetAltitudeDarkness(float y)=>Mathf.Clamp01(y);}
public class BattleManager {public bool IsGameplayActive=true;public MapBoundaryTable MapBoundary=new();}
public class NetworkGameManager {public static NetworkGameManager Instance=new();public bool IsMatching=true;}
'@
$math = 'public static class MathProbe {' + (Method $hud 'ManaPercent') + (Method $hud 'HitMarkerAlpha') + (Method $hud 'IsParryCueTiming') +
    (Method $shot 'TryEstimateImpactTime') + (Method $shot 'IsFiniteEstimateVector') + (Method $prediction 'BoundedLead') + '}'
$cameraProbe = @'
public class CameraProbe {
 public Player altitudePlayer;public bool deathPresentationLatched;public float altitudeFullBlack;public bool deathGrayscale=true;
 public Vignette altitudeVignette=new(),originalAltitudeVignette=new();public ColorAdjustments altitudeColor=new(),originalAltitudeColor=new();
'@ + (Method $camera 'UpdateAltitudeEffects') + (Method $camera 'RestoreVolumeComponent') + '}'
$checks = @'
public static class Tests {
 static int n;static void Check(bool ok,string msg){if(!ok)throw new Exception(msg);n++;}static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
 public static int Run(){
  Check(MathProbe.ManaPercent(75,100)==75&&MathProbe.ManaPercent(150,200)==75,"mana uses maximum, not raw AP");
  Check(MathProbe.ManaPercent(-1,100)==0&&MathProbe.ManaPercent(500,100)==100,"mana bounds");
  Check(MathProbe.ManaPercent(1,0)==0&&MathProbe.ManaPercent(float.NaN,100)==0&&MathProbe.ManaPercent(10,float.PositiveInfinity)==0,"invalid mana safe");
  Check(MathProbe.HitMarkerAlpha(0,.04f,.3f)==1&&MathProbe.HitMarkerAlpha(.04f,.04f,.3f)==1,"Hit marker pops in immediately and holds briefly");
  Check(Near(MathProbe.HitMarkerAlpha(.19f,.04f,.3f),.5f),"Hit marker fades gradually after hold");
  Check(MathProbe.HitMarkerAlpha(.34f,.04f,.3f)==0&&MathProbe.HitMarkerAlpha(10,.04f,.3f)==0,"Hit marker disappears after fade");
  Check(MathProbe.HitMarkerAlpha(float.PositiveInfinity,.04f,.3f)==0&&MathProbe.HitMarkerAlpha(-1,.04f,.3f)==0,"Unstarted marker is hidden");
  Check(MathProbe.HitMarkerAlpha(0,0,0)==1&&MathProbe.HitMarkerAlpha(1,0,0)==0,"Zero configured duration is bounded safely");
  Check(Near(MathProbe.BoundedLead(.15f,.2f),.15f)&&Near(MathProbe.BoundedLead(2,.2f),.2f),"visual lead bounded by setting");
  Check(Near(MathProbe.BoundedLead(2,2),.3f)&&MathProbe.BoundedLead(-1,.2f)==0,"hard lead cap and no rewinding");
  Check(MathProbe.BoundedLead(float.NaN,.2f)==0&&MathProbe.BoundedLead(1,float.PositiveInfinity)==0,"invalid timing safe");
  Check(MathProbe.IsParryCueTiming(.4f,.5f,.08f)&&!MathProbe.IsParryCueTiming(.45f,.5f,.08f),"parry cue respects real window minus safety margin");
  Check(!MathProbe.IsParryCueTiming(-.1f,.5f,.08f)&&!MathProbe.IsParryCueTiming(.1f,0,.08f),"expired or disabled cue rejected");
  Check(MathProbe.IsParryCueTiming(.005f,.01f,.08f),"tiny configured windows still have a cue");
  Check(MathProbe.TryEstimateImpactTime(new(0,0,51),new(0,0,-100),1,false,out float time)&&Near(time,.5f),"straight approaching projectile ETA");
  Check(!MathProbe.TryEstimateImpactTime(new(20,0,51),new(0,0,-100),1,false,out _),"passing straight shot is not a parry cue");
  Check(!MathProbe.TryEstimateImpactTime(new(0,0,51),new(0,0,100),1,true,out _),"receding projectile is not a cue");
  Check(MathProbe.TryEstimateImpactTime(new(0,0,51),new(0,0,-50),1,true,out time)&&Near(time,1),"guided ETA uses radial closing speed");
  Check(MathProbe.TryEstimateImpactTime(new(0,0,.5f),new(0,0,-50),1,true,out time)&&time==0,"already touching is imminent");
  Check(!MathProbe.TryEstimateImpactTime(new(float.NaN,0,1),new(0,0,-50),1,true,out _),"invalid position rejected");
  var camera=new CameraProbe();var battle=new BattleManager();var player=new Player();player.transform.position=new(0,1,0);
  camera.UpdateAltitudeEffects(player,battle);Check(camera.altitudeFullBlack==1&&camera.altitudeColor.colorFilter.value.r==0,"high altitude is black while alive");
  player.IsAlive=false;camera.UpdateAltitudeEffects(player,battle);Check(camera.altitudeFullBlack==0&&camera.altitudeColor.colorFilter.value.r==1,"death removes altitude black filter");
  Check(camera.altitudeColor.saturation.value==-100&&camera.altitudeVignette.intensity.value==0,"death is grayscale, not a black vignette");
  camera.UpdateAltitudeEffects(null,battle);Check(camera.altitudeColor.saturation.value==-100,"grayscale survives corpse despawn");
  camera.UpdateAltitudeEffects(new Player(),battle);Check(camera.altitudeColor.saturation.value==0&&!camera.deathPresentationLatched,"new alive player restores normal color");
  player.transform.position=new(0,0,0);camera.UpdateAltitudeEffects(player,battle);Check(camera.altitudeColor.saturation.value==-100,"combat death also desaturates");
  battle.IsGameplayActive=false;camera.UpdateAltitudeEffects(null,battle);Check(camera.altitudeColor.saturation.value==0&&!camera.deathPresentationLatched,"match end resets death presentation");
  battle.IsGameplayActive=true;camera.deathGrayscale=false;camera.UpdateAltitudeEffects(player,battle);Check(camera.altitudeColor.saturation.value==0&&camera.altitudeFullBlack==0,"disabling grayscale still removes blackout on death");
  camera.deathGrayscale=true;camera.UpdateAltitudeEffects(player,battle);NetworkGameManager.Instance.IsMatching=false;camera.UpdateAltitudeEffects(null,battle);Check(!camera.deathPresentationLatched&&camera.altitudeColor.saturation.value==0,"disconnect clears presentation");
  return n;
 }
}}
'@
Add-Type -TypeDefinition ($stubs + $math + $cameraProbe + $checks)
"Battle UX behavior checks passed: $([UxChecks.Tests]::Run())"
foreach ($token in 'owner.RespawnTimer.RemainingTime(owner.Runner)', 'CreateCentralPrompt("Respawn countdown"', 'Mathf.CeilToInt(respawnAt - Time.unscaledTime)') {
    if (!$hud.Contains($token)) { throw "Missing respawn display: $token" }
}
foreach ($token in '[Networked] public TickTimer RespawnTimer', 'RespawnTimer = TickTimer.None;', 'TickTimer.CreateFromSeconds(Runner, BattleManager.Instance.RespawnDelaySeconds)') {
    if (!$player.Contains($token)) { throw "Missing replicated respawn deadline: $token" }
}
foreach ($token in 'candidate.runner == projectile.Runner', 'candidate.shooterId.Equals(projectile.ShooterId)',
    'candidate.inputTick == projectile.PredictionInputTick', 'pending.Remove(view)', 'pending.Remove(this)',
    'pending.Count >= 32', 'Time.unscaledTime - launchAt', 'Physics.SphereCastNonAlloc', 'Physics.OverlapSphereNonAlloc',
    'if (count == hits.Length)', 'if (count == overlaps.Length)', '!source.Object.HasInputAuthority || source.Object.HasStateAuthority') {
    if (!$prediction.Contains($token)) { throw "Missing prediction reconciliation guard: $token" }
}
if ($prediction -match '\b(?:Runner|runner)\.Spawn\s*\(|\b(?:TakeDamage|ReceiveMagicHit|TryConsumeAp)\s*\(') { throw 'Cosmetic prediction must not mutate gameplay' }
$scene = Get-Content Assets/Scenes/Battle.unity -Raw
foreach ($field in 'feedbackFont','countdownFont') {
    if ($scene -notmatch ($field + ': \{fileID: 11400000, guid: ad82f58d7f40a25448fe68d46ecb6f2c, type: 2\}')) { throw "DungGeunMo not bound: $field" }
}
if (!$intro.Contains('if (countdown) EnsureCountdownText();') -or !$intro.Contains('countdownText.font = countdownFont;')) { throw 'Countdown font / fallback missing' }
$prefab = Get-Content Assets/UI/Prefab/BattleUI.prefab -Raw
$ids = [regex]::Matches($prefab,'(?m)^--- !u!\d+ &(\d+)') | ForEach-Object {$_.Groups[1].Value}
if (@($ids | Group-Object | Where-Object Count -gt 1).Count) { throw 'Duplicate prefab object IDs' }
foreach ($pair in @(@('hpGridBar','2027782590','902100009900001'),@('hpText','2027782591','4180305955442932374'),@('maxHpText','2027782592','3549336188242143501'),@('manaPercentText','2027782593','3013086511338269479'))) {
    if (!$scene.Contains(($pair[0] + ': {fileID: ' + $pair[1] + '}'))) { throw "HUD binding missing: $($pair[0])" }
    if ($scene -notmatch ('(?s)--- !u!114 &' + $pair[1] + ' stripped\s+MonoBehaviour:\s+m_CorrespondingSourceObject: \{fileID: ' + $pair[2] + ',')) { throw "Wrong prefab source: $($pair[0])" }
    if ($prefab -notmatch ('(?m)^--- !u!114 &' + $pair[2] + '\b')) { throw "Missing prefab component: $($pair[0])" }
}
'Prediction authority/lifecycle, font, respawn deadline, HP/MP prefab and scene contracts passed.'
$hitRpc = [regex]::Match($player, '(?s)\[Rpc\(RpcSources.StateAuthority, RpcTargets.InputAuthority, Channel = RpcChannel.Reliable\)\]\s+private void RPC_ConfirmedMagicHit\(\).*?BattleHud.ShowHitMarker\(this\);')
if (!$hitRpc.Success) { throw 'Confirmed hit must go from authority to attacker only' }
foreach ($token in '[SerializeField] private Image hitMarkerImage;', '[SerializeField] private RectTransform hitMarkerCenter;',
    'Graphic next = hitMarkerImage;', 'if (next == null)', 'generatedHitMarker.HitMarker = true;',
    'attacker != Player.LocalPlayer', 'instance.hitMarkerStarted = Time.unscaledTime;',
    'hitMarkerCenter != null ? hitMarkerCenter : desiredAimMarker', 'if (show) PlaceHitMarker();') {
    if (!$hud.Contains($token)) { throw "Hit marker integration missing: $token" }
}
'Attacker-only reliable hit confirmation, custom Image priority, fallback geometry, reticle following and repeat-hit reset checked.'
