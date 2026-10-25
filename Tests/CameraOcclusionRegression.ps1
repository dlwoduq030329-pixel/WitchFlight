$ErrorActionPreference='Stop'
$sourceFile=Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets/Script/Camera/CameraFollow.Occlusion.cs'
$code=Get-Content $sourceFile -Raw
function Method($name){
 $m=[regex]::Match($code,'(?ms)^    private (?:static )?void '+$name+'\([^{}]*\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value.Replace('private ','public ')
}
$source=@'
using System;
using System.Collections.Generic;
public class Material {public bool destroyed;}
public class MaterialPropertyBlock {public bool isEmpty;public string color;}
public class Renderer {
 public Material[] sharedMaterials;public MaterialPropertyBlock global=new(){color="global original"};
 public MaterialPropertyBlock[] slots=new MaterialPropertyBlock[2];
 public void SetPropertyBlock(MaterialPropertyBlock b,int i){slots[i]=b;}
}
public class Probe {
 public class ObstructionFade {public Renderer renderer;public Material[] originals,transparent;public MaterialPropertyBlock[] saved;}
 public List<ObstructionFade> appliedObstructions=new();
 public Dictionary<Renderer,ObstructionFade> obstructionFades=new();
 public Dictionary<int,int> obstructionRenderers=new();public int obstructionFrame;
 public static void Destroy(Material m){m.destroyed=true;}
'@+(Method 'RestoreOcclusionMaterials')+(Method 'DisposeOcclusionMaterials')+(Method 'ClearOcclusionFade')+@'
}
public static class OcclusionChecks {
 static int n;static void Check(bool b,string text){if(!b)throw new Exception(text);n++;}
 public static int Run(){
  var original=new Material();var glass=new Material();var clone=new Material();
  var renderer=new Renderer();var block=new MaterialPropertyBlock{color="original slot color"};
  var state=new Probe.ObstructionFade{renderer=renderer,originals=new[]{original,glass},transparent=new[]{clone,glass},
   saved=new[]{new MaterialPropertyBlock{isEmpty=true},block}};
  renderer.sharedMaterials=state.transparent;renderer.slots[0]=new(){color="faded"};renderer.slots[1]=new(){color="faded"};
  var p=new Probe();p.appliedObstructions.Add(state);p.obstructionFades.Add(renderer,state);p.obstructionRenderers.Add(1,1);
  p.RestoreOcclusionMaterials();
  Check(ReferenceEquals(renderer.sharedMaterials,state.originals),"Original materials restored before another camera");
  Check(renderer.slots[0]==null,"Originally empty per-slot block removed");
  Check(ReferenceEquals(renderer.slots[1],block),"Authored per-slot overrides restored exactly");
  Check(renderer.global.color=="global original","Global overrides stay untouched");
  Check(p.appliedObstructions.Count==0,"Repeated end callback is harmless");
  p.RestoreOcclusionMaterials();Check(!clone.destroyed,"Fade clone retained between frames");
  p.ClearOcclusionFade();Check(clone.destroyed,"Owned clone released on camera disable or target swap");
  Check(!original.destroyed&&!glass.destroyed,"Never destroy shared map assets or transparent originals");
  Check(p.obstructionFades.Count==0&&p.obstructionRenderers.Count==0&&p.obstructionFrame==-1,"Target swap clears cached map queries");
  p.appliedObstructions.Add(new Probe.ObstructionFade{renderer=null});p.RestoreOcclusionMaterials();
  Check(p.appliedObstructions.Count==0,"Destroyed scene renderer is safe during cleanup");
  return n;
 }
}
'@
Add-Type -TypeDefinition $source
"Camera-local material lifecycle checks passed: $([OcclusionChecks]::Run())"
