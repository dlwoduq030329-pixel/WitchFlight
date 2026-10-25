# Actual audio table/runtime with engine doubles. Does not claim audible or multiplayer validation.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = @'
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Fusion;
namespace UnityEngine {
 public class RangeAttribute:Attribute {public RangeAttribute(float a,float b){}}
 public class MinAttribute:Attribute {public MinAttribute(float a){}}
 public class TooltipAttribute:Attribute {public TooltipAttribute(string a){}}
 public class CreateAssetMenuAttribute:Attribute {public string menuName;}
 public enum RuntimeInitializeLoadType {SubsystemRegistration}
 public class RuntimeInitializeOnLoadMethodAttribute:Attribute {public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t){}}
 public class ScriptableObject {}
 public class Component {public GameObject gameObject; public Transform transform=>gameObject.transform;}
 public class MonoBehaviour:Component {}
 public class Transform {static int counter; int id=++counter; public GameObject gameObject; public Vector3 position;public void SetParent(Transform t){}public int GetInstanceID()=>id;}
 public class GameObject {public Transform transform; public bool activeInHierarchy=true; public GameObject(string name){transform=new Transform{gameObject=this};}public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};if(c is AudioSource a)AudioSource.All.Add(a);return c;}}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 zero=>default;}
 public static class Mathf {public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);public static int Clamp(int x,int a,int b)=>Math.Clamp(x,a,b);public static float Clamp01(float x)=>Math.Clamp(x,0,1);public static float Max(float a,float b)=>Math.Max(a,b);public static int CeilToInt(float x)=>(int)Math.Ceiling(x);}
 public static class Time {public static float unscaledTime;}
 public static class Resources {public static object Value; public static T Load<T>(string path) where T:class=>Value as T;}
 public class AudioClip {}
 public enum AudioRolloffMode {Linear}
 public class AudioSource:Component {
  public static List<AudioSource> All=new();public static int Plays;
  public bool playOnAwake,isPlaying,loop;public float volume,pitch,spatialBlend,minDistance,maxDistance,dopplerLevel;
  public AudioClip clip;public AudioRolloffMode rolloffMode;public AudioMixerGroup outputAudioMixerGroup;
  public void Stop(){isPlaying=false;}public void Play(){isPlaying=true;Plays++;}
 }
}
namespace UnityEngine.Audio {public class AudioMixerGroup{}}
namespace Fusion {public struct PlayerRef {public int value;public static PlayerRef None=>default;public static bool operator==(PlayerRef a,PlayerRef b)=>a.value==b.value;public static bool operator!=(PlayerRef a,PlayerRef b)=>a.value!=b.value;public override bool Equals(object o)=>o is PlayerRef p&&p==this;public override int GetHashCode()=>value;}}
public enum MagicType {None,Fire,Ice,Vision,Thunder,Healing,Binding,Dark,Curse,Mine,Razier,Flare,Smoke}
public enum BattleStartPhase {WaitingForPlayers,Intro,Countdown,Playing,Ended,Intermission}
public class NetObject {public bool IsValid=true;}
public class BattleFlag {public static BattleFlag Instance;public NetObject Object=new();public BattleStartPhase Phase;public PlayerRef Carrier;public int WinningTeam,LastRoundWinner;public float PhaseRemainingSeconds;}
public class Player {public static Player LocalPlayer;public NetObject Object=new();public int TeamIndex;}
'@
foreach ($file in @('BattleAudioTable','BattleAudio')) {
    $source += (Get-Content "Assets/Script/$file.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n',''
}
$source += @'
public static class AudioChecks {
 static int count;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
 static BattleAudio Instance=>(BattleAudio)typeof(BattleAudio).GetField("instance",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetValue(null);
 static void Frame()=>typeof(BattleAudio).GetMethod("LateUpdate",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(Instance,null);
 static AudioSource LastPlaying()=>AudioSource.All.FindLast(a=>a.isPlaying);
 public static int Run(){
  var cue=new BattleSoundCue{clip=new AudioClip(),minimumInterval=.12f};
  var magic=new MagicSoundEntry{magic=MagicType.Ice,impact=new BattleSoundCue{clip=new AudioClip()}};
  var events=new List<BattleSoundEntry>();foreach(BattleSound s in Enum.GetValues(typeof(BattleSound)))events.Add(new BattleSoundEntry{sound=s,audio=new BattleSoundCue{clip=new AudioClip(),minimumInterval=.12f}});
  events[0].audio=cue;
  var table=new BattleAudioTable{maxVoices=8,commonSounds=events.ToArray(),magicSounds=new[]{magic}};
  Resources.Value=table;
  Check(table.Get(MagicType.Ice,MagicSound.Impact)==magic.impact,"magic ID/stage resolves exact cue");
  Check(table.Get(MagicType.Fire,MagicSound.Impact)==null,"unconfigured spell stays silent");
  BattleAudio.PlayMagic(MagicType.Fire,MagicSound.Cast,default);
  Check(AudioSource.Plays==0&&AudioSource.All.Count==0,"missing clips allocate no voices");
  BattleAudio.Play(BattleSound.ParryReady,local:true);
  Check(AudioSource.Plays==1&&LastPlaying().spatialBlend==0,"local timing cue is 2D");
  BattleAudio.Play(BattleSound.ParryReady,local:true);
  Check(AudioSource.Plays==1,"simultaneous projectile cues coalesce");
  Time.unscaledTime=.13f;BattleAudio.Play(BattleSound.ParryReady,local:true);
  Check(AudioSource.Plays==2,"later incoming projectile can beep");
  foreach(var a in AudioSource.All)a.Stop();
  var caster=new GameObject("Caster");
  var loop=BattleAudio.StartLoop(BattleSound.BoostLoop,caster.transform);
  var voice=LastPlaying();caster.transform.position=new Vector3(5,0,0);Frame();
  Check(voice.loop&&voice.transform.position.x==5,"loop follows emitter");
  loop.Stop();Check(!voice.isPlaying,"explicit stop ends loop");
  var replacement=BattleAudio.StartLoop(BattleSound.BoostLoop,caster.transform);loop.Stop();
  Check(voice.isPlaying,"stale handle cannot stop reused voice");
  caster.activeInHierarchy=false;Frame();Check(!voice.isPlaying,"disabled emitter ends loop");caster.activeInHierarchy=true;
  var loops=new List<BattleAudio.Loop>();for(int i=0;i<8;i++)loops.Add(BattleAudio.StartLoop(BattleSound.BoostLoop,caster.transform));
  int plays=AudioSource.Plays;Time.unscaledTime=1;BattleAudio.Play(BattleSound.DamageReceived);
  Check(AudioSource.All.Count==8&&AudioSource.Plays==plays,"voice limit never steals persistent loops");
  var first=loops[0];first.Stop();BattleAudio.Play(BattleSound.DamageReceived);
  Check(AudioSource.Plays==plays+1,"stopped loop slot is reusable");
  foreach(var a in AudioSource.All)a.Stop();
  BattleFlag.Instance=new BattleFlag{Phase=BattleStartPhase.Playing};plays=AudioSource.Plays;Frame();
  Check(AudioSource.Plays==plays,"initial snapshot never replays old start");
  BattleFlag.Instance.Phase=BattleStartPhase.Countdown;BattleFlag.Instance.PhaseRemainingSeconds=3;Frame();Frame();
  Check(AudioSource.Plays==plays+1,"countdown beeps once per numeral");
  Time.unscaledTime=2;BattleFlag.Instance.PhaseRemainingSeconds=2;Frame();
  Check(AudioSource.Plays==plays+2,"next countdown numeral beeps");
  Time.unscaledTime=3;BattleFlag.Instance.Phase=BattleStartPhase.Playing;Frame();Frame();
  Check(AudioSource.Plays==plays+3,"round start fires only on transition");
  Time.unscaledTime=4;BattleFlag.Instance.Carrier=new PlayerRef{value=1};Frame();Frame();
  Check(AudioSource.Plays==plays+4,"flag pickup fires only on change");
  Time.unscaledTime=5;BattleFlag.Instance.Carrier=PlayerRef.None;Frame();
  Check(AudioSource.Plays==plays+5,"flag drop fires once");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Audio behavior checks passed: $([AudioChecks]::Run())"
$asset = Get-Content Assets/Resources/BattleAudioTable.asset -Raw
if (([regex]::Matches($asset,'(?m)^    sound: ').Count) -ne 38) { throw 'Common sound catalog incomplete' }
$ids = [regex]::Matches($asset,'(?m)^    magic: (\d+)') | ForEach-Object { [int]$_.Groups[1].Value }
if (($ids -join ',') -ne '1,2,3,4,5,6,7,8,9,10,11,12') { throw 'Magic sound IDs do not match gameplay IDs' }
"Audio asset catalog: 38 common events and 12 unique magic IDs verified."
