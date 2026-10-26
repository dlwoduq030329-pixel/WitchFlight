$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$source=Get-Content (Join-Path $repo 'Assets/Script/GameAudioSettings.cs') -Raw
$source=$source.Substring(0,$source.IndexOf('// Covers authored'))
$stubs=@'
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public enum RuntimeInitializeLoadType { SubsystemRegistration }
 public class RuntimeInitializeOnLoadMethodAttribute:Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t){} }
 public static class Mathf {public static float Clamp01(float v)=>Math.Max(0,Math.Min(1,v));public static float Log10(float v)=>(float)Math.Log10(v);}
 public static class PlayerPrefs {static Dictionary<string,float> d=new();public static float GetFloat(string k,float f)=>d.TryGetValue(k,out var v)?v:f;public static void SetFloat(string k,float v)=>d[k]=v;}
 public static class Resources {public static T Load<T>(string s) where T:new()=>new T();}
}
namespace UnityEngine.Audio {
 public class AudioMixerGroup {public string name;}
 public class AudioMixer {public static Dictionary<string,float> values=new();public AudioMixerGroup[] FindMatchingGroups(string n)=>new[]{new AudioMixerGroup{name=n}};public bool SetFloat(string n,float v){values[n]=v;return true;}}
}
'@
Add-Type -TypeDefinition ("using System.Collections.Generic;`n"+$source+($stubs -replace 'using System;','' -replace 'using System.Collections.Generic;',''))
$count=0
function Check($condition,$label){if(!$condition){throw $label};$script:count++}
Check ([GameAudioSettings]::Master -eq 1) 'Default volume'
Check ([GameAudioSettings]::Decibels(0) -eq -80) 'Mute gain'
Check ([GameAudioSettings]::Decibels(1) -eq 0) 'Full gain'
Check ([Math]::Abs([GameAudioSettings]::Decibels(.5)+6.0206) -lt .001) 'Half linear volume'
[GameAudioSettings]::SetBGM(.3)
Check ([Math]::Abs([GameAudioSettings]::BGM-.3) -lt .001) 'Persist BGM'
Check ([GameAudioSettings]::SFX -eq 1) 'BGM independent of SFX'
[GameAudioSettings]::SetSFX(-1)
Check ([GameAudioSettings]::SFX -eq 0) 'Clamp negative'
Check ([UnityEngine.Audio.AudioMixer]::values['SFXVolume'] -eq -80) 'Apply live mute'
[GameAudioSettings]::SetMaster(2)
Check ([GameAudioSettings]::Master -eq 1) 'Clamp upper bound'
[GameAudioSettings]::SetBGM([float]::NaN)
Check ([Math]::Abs([GameAudioSettings]::BGM-.3) -lt .001) 'Reject invalid volume'
Check ([GameAudioSettings]::Group([GameAudioCategory]::Voice).name -eq 'Voice') 'Voice routed separately'
$mixer=Get-Content (Join-Path $repo 'Assets/Resources/GameAudioMixer.mixer') -Raw
foreach($name in @('Master','BGM','SFX','Voice')) {
 Check ($mixer.Contains("name: ${name}Volume")) "Exposed $name"
 Check ($mixer.Contains("m_Name: $name")) "Group $name"
}
foreach($path in @('Assets/UI/Prefab/SettingUI.prefab','Assets/UI/Prefab/IntroAndRobby.prefab')) {
 $prefab=Get-Content (Join-Path $repo $path) -Raw
 $guid=[regex]::Match((Get-Content (Join-Path $repo 'Assets/Script/SoundSettingsUI.cs.meta') -Raw),'guid: (\w+)').Groups[1].Value
 Check ($prefab.Contains($guid)) "Sound UI component $path"
 foreach($row in @('MasterVolume','BGM','SFX','Voice')) {Check ($prefab.Contains("m_Name: $row")) "Sound row $row"}
}
$main=Get-Content (Join-Path $repo 'Assets/Scenes/Main.unity') -Raw
foreach($clip in @('bc1feb4ca1cd56048a4a5e0017df61ef','1c03db3c5ce3737489258fed37e0eac1')) {
 $sources=[regex]::Matches($main,'(?ms)^--- !u!82 &\d+\r?\n.*?(?=^---|\z)')
 $music=$sources | Where-Object {$_.Value.Contains($clip)}
 Check (@($music).Count -eq 1) "BGM source exists $clip"
 Check ($music.Value.Contains('OutputAudioMixerGroup: {fileID: 24300001, guid: 03c9cbd9716843afa374d194799c9932, type: 2}')) "BGM explicit routing $clip"
}
"Audio settings checks passed: $count"
