$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$fog = Get-Content 'Assets/SUIMONO - WATER SYSTEM 2/SCRIPTS/Suimono_UnderwaterFog.cs' -Raw
$module = Get-Content 'Assets/SUIMONO - WATER SYSTEM 2/SCRIPTS/SuimonoModule.cs' -Raw
$clear = [regex]::Match($fog, '(?ms)^        public void ClearWaterTransition\(\).*?^        \}').Value
$reset = [regex]::Match($module, '(?ms)^        public void ResetCameraWaterAfterRespawn\(\).*?^        \}').Value
if (!$clear -or !$reset) { throw 'Missing production reset methods' }
$source = @'
using System;
public struct Vector2 {public static Vector2 zero=>new();}
public class Suimono_UnderwaterFog {
 public bool doTransition=true,cancelTransition=true;
 public float trans1Time=.1f,trans2Time=.1f;public Vector2 dropOff;
'@ + $clear + @'
}
public class Surface {public string useShader="under",shader_Surface="surface";}
public class Sound {public int stops;public void Stop(){stops++;}}
public class Camera {public Suimono_UnderwaterFog fog=new();public T GetComponent<T>() where T:class=>fog as T;}
public class Water {
 public bool doWaterTransition=true,cameraIsSubmerged=true,isUnderwater=true;
 public Surface cameraWaterSurface=new();public Sound underSoundComponent=new();
 public Camera setCamera=new();public Suimono_UnderwaterFog underwaterObject;
 public bool spawnUnderwater,wasArmedAtSample;public int samples,audioUpdates;
 // Record ordering at the water-query boundary; rendering and physics are not simulated here.
 void StoreSurfaceHeight(){samples++;wasArmedAtSample=doWaterTransition;cameraIsSubmerged=isUnderwater=spawnUnderwater;if(spawnUnderwater)doWaterTransition=true;}
 void PlayUnderwaterSound(){audioUpdates++;}
'@ + $reset + @'
}
public static class Checks {
 static void Require(bool ok,string name){if(!ok)throw new Exception(name);}
 public static void Run(){
  foreach(bool submerged in new[]{false,true}){
   var water=new Water{spawnUnderwater=submerged};var previousSurface=water.cameraWaterSurface;
   water.ResetCameraWaterAfterRespawn();
   Require(!water.wasArmedAtSample,"clear old exit trigger before querying new camera position");
   Require(previousSurface.useShader=="surface","release previous underwater surface");
   Require(!water.setCamera.fog.doTransition&&!water.setCamera.fog.cancelTransition&&water.setCamera.fog.trans1Time==1.1f&&water.setCamera.fog.trans2Time==1.1f,"clear active droplets immediately");
   Require(water.cameraIsSubmerged==submerged&&water.isUnderwater==submerged,"new water state follows new position");
   Require(water.doWaterTransition==submerged,"new underwater spawn can still leave water normally later");
   Require(water.underSoundComponent.stops==1&&water.samples==1&&water.audioUpdates==1,"stop stale audio and refresh once");
  }
 }
}
'@
Add-Type -TypeDefinition $source
[Checks]::Run()
'Respawn water reset: 12 ordering/state checks passed. Full rendering requires Unity play testing.'
