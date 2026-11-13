# Run production deadline update/release methods. Rendering and lease storage are doubles.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$pool=Get-Content Assets/Script/MagicVfxPool.cs -Raw
function Method([string]$name) {
 $m=[regex]::Match($pool,'(?ms)^    (?:private|public) (?:static )?void '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value -replace '^    private ','    public '
}
$source=@'
using System;
using System.Collections.Generic;
namespace DeadlineChecks {
public static class Time {public static float time,deltaTime;}
public class TimeVfxSpeed {public float Multiplier=1;}
public class Transform {
 public Transform parent;
 public bool IsChildOf(Transform root){for(var p=this;p!=null;p=p.parent)if(p==root)return true;return false;}
}
public class GameObject {
 public Transform transform=new();public bool destroyed;public TimeVfxSpeed clock;
 public T GetComponent<T>() where T:class=>clock as T;
 public void GetComponentsInChildren<T>(bool includeInactive,List<T> result){result.Clear();}
}
public class MagicVfxLease {public GameObject root;public bool returned;}
public class Probe {
 private struct Deadline {public GameObject root;public float at;public TimeVfxSpeed clock;}
 private static Probe instance;
 private readonly List<Deadline> deadlines=new();
 private readonly Dictionary<GameObject,MagicVfxLease> leases=new();
 private readonly List<MagicVfxLease> releaseBuffer=new();
 public int returns;
 public Probe(){instance=this;}
 private static Probe Ensure()=>instance;
 private static void Destroy(GameObject root){root.destroyed=true;}
 private void Return(MagicVfxLease lease){lease.returned=true;returns++;}
 public void Add(GameObject root,float at,bool leased=false){
  deadlines.Add(new(){root=root,at=at,clock=root?.clock});
  if(leased)leases[root]=new(){root=root};
 }
 public int Count=>deadlines.Count;
 public float At(GameObject root)=>deadlines.Find(d=>d.root==root).at;
'@ + (Method 'Update') + (Method 'Release') + @'
}
public static class Checks {
 static int n;static void Check(bool ok,string m){if(!ok)throw new Exception(m);n++;}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.00001f;
 public static int Run(){
  Time.time=1;Time.deltaTime=.1f;
  var pool=new Probe();var parent=new GameObject();var child=new GameObject();child.transform.parent=parent.transform;
  var other=new GameObject{clock=new(){Multiplier=.2f}};
  pool.Add(child,100);pool.Add(parent,0);pool.Add(other,10,true);pool.Update();
  Check(parent.destroyed&&pool.Count==1,"expired parent cancels nested deadlines");
  Check(Near(pool.At(other),10.08f),"unrelated slowed effect advances exactly once despite index shifts");
  pool.Update();Check(Near(pool.At(other),10.16f),"next frame advances exactly once");
  pool=new Probe();var fast=new GameObject{clock=new(){Multiplier=1.3f}};
  pool.Add(fast,2,true);pool.Update();Check(Near(pool.At(fast),1.97f),"friendly accelerated clock preserved");
  Time.time=2;pool.Update();Check(pool.Count==0&&pool.returns==1,"accelerated deadline returns lease once");
  pool=new Probe();var normal=new GameObject();pool.Add(normal,5,true);pool.Add(null,0);
  pool.Update();Check(pool.Count==1&&pool.At(normal)==5,"normal deadline unchanged and missing root removed");
  Time.time=5;pool.Update();Check(pool.Count==0&&pool.returns==1,"exact deadline expires once");
  pool.Update();Check(pool.returns==1,"empty pool cannot return again");
  pool=new Probe();normal=new GameObject();Time.time=0;
  Probe.Release(normal,3);Probe.Release(normal,7);
  Check(pool.Count==1&&pool.At(normal)==7,"rescheduling replaces prior deadline");
  Probe.Release(normal);Check(pool.Count==0&&normal.destroyed,"immediate release removes scheduled expiry");
  // Reverse ordering must behave identically: parent before child in the list.
  pool=new Probe();parent=new GameObject();child=new GameObject();child.transform.parent=parent.transform;
  other=new GameObject{clock=new(){Multiplier=.2f}};Time.time=1;
  pool.Add(parent,0);pool.Add(child,100);pool.Add(other,10);pool.Update();
  Check(pool.Count==1&&Near(pool.At(other),10.08f),"hierarchy ordering does not change unrelated lifetime");
  return n;
 }
}}
'@
Add-Type -TypeDefinition $source
"VFX deadline regression checks passed: $([DeadlineChecks.Checks]::Run())"
