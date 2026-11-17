$ErrorActionPreference='Stop'
$source=Get-Content "$PSScriptRoot/../Assets/Script/PlayerData.Perks.cs" -Raw
$start=$source.IndexOf('    private static readonly Dictionary<(NetworkRunner, PlayerRef), PlayerData>')
$end=$source.IndexOf('    [Networked]', $start)
$body=$source.Substring($start,$end-$start)
$stub=@'
using System;
using System.Collections.Generic;
public class NetworkRunner {}
public struct PlayerRef {public int id; public static bool operator==(PlayerRef a,PlayerRef b)=>a.id==b.id; public static bool operator!=(PlayerRef a,PlayerRef b)=>a.id!=b.id; public override bool Equals(object o)=>o is PlayerRef p&&p==this; public override int GetHashCode()=>id;}
public static class Time {public static int frameCount;}
public class Obj {public bool IsValid=true;}
public class PlayerData {
 static readonly HashSet<PlayerData> perkPlayers=new();
 public Obj Object=new(); public NetworkRunner Runner; public PlayerRef ParticipantRef;
 public static void Run(){
  var r=new NetworkRunner();var p=new PlayerRef{id=1};
  if(FindPerkData(r,p)!=null)throw new Exception("Empty lookup");
  var a=new PlayerData{Runner=r,ParticipantRef=p};perkPlayers.Add(a);
  if(FindPerkData(r,p)!=a||FindPerkData(r,p)!=a)throw new Exception("Lookup/cache hit");
  a.ParticipantRef=new PlayerRef{id=2};
  if(FindPerkData(r,p)!=null)throw new Exception("Stale authority");
  var b=new PlayerData{Runner=r,ParticipantRef=p};perkPlayers.Add(b);
  if(FindPerkData(r,p)!=b)throw new Exception("Same-frame replacement");
  perkPlayers.Remove(b);
  if(FindPerkData(r,p)!=null)throw new Exception("Despawn retained");
  perkPlayers.Add(b);Time.frameCount++;
  if(FindPerkData(r,p)!=b||FindPerkData(new NetworkRunner(),p)!=null)throw new Exception("Frame/runner isolation");
  b.Object.IsValid=false;
  if(FindPerkData(r,p)!=null)throw new Exception("Invalid object retained");
 }
'@
Add-Type -TypeDefinition ($stub+$body+'}')
[PlayerData]::Run()
Write-Output 'PASS: cache hits, same-frame join/despawn/authority changes, invalidation, runner isolation.'
