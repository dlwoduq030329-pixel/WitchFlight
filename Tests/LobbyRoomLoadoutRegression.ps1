$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$production = Get-Content Assets/Script/PlayerData.LobbyLoadout.cs -Raw
$stub = @'
namespace Fusion {
 public class NetworkedAttribute : System.Attribute {}
 public enum RpcSources { InputAuthority }
 public enum RpcTargets { StateAuthority }
 public class RpcAttribute : System.Attribute { public RpcAttribute(RpcSources s, RpcTargets t) {} }
}
public enum HatType { Classic=1, Twisted=2 }
public enum BroomType { Standard=2, Cleaner=5 }
public enum MagicType { Fire=1, Ice=2, Thunder=4, Blade=13 }
public class NetObject { public bool IsValid=true, HasInputAuthority=true; }
public class NetworkGameManager {
 public static NetworkGameManager Instance=new();
 public object Runner=new(); public bool IsWaitingInCodeRoom=true;
}
public class EmoteTable {
 public static EmoteTable Default=new();
 public bool TryGet(int id,out object entry) { entry=null; return id>=1 && id<=9; }
}
public static class DataConfig {
 public static event System.Action Changed;
 public static int hatIndex=1,broomIndex=2,magic1Index=1,magic2Index=2;
 public static int[] emotes={1,2,3,4};
 public static int GetEmote(int i)=>emotes[i];
 public static void Change()=>Changed?.Invoke();
}
public partial class PlayerData {
 public NetObject Object=new(); public object Runner=NetworkGameManager.Instance.Runner;
 public bool IsLoadoutInitialized=true,IsMatchBot;
 public HatType hat; public BroomType broom;
 public MagicType magic1,magic2,InitialMagic1,InitialMagic2;
 static HatType NormalizeHat(HatType h)=>h;
 static BroomType NormalizeBroom(BroomType b)=>b;
 static MagicType NormalizeMagic(MagicType m,MagicType f)=>m;
 static int checks;
 static void Check(bool pass,string name) { if(!pass)throw new System.Exception(name); checks++; }
 public static int RunChecks() {
  var p=new PlayerData(); p.StartLobbyLoadoutSync();
  Check(p.magic1==MagicType.Fire && p.InitialMagic2==MagicType.Ice,"initial waiting loadout");
  DataConfig.hatIndex=2;DataConfig.broomIndex=5;DataConfig.magic1Index=13;DataConfig.emotes[0]=8;DataConfig.Change();
  Check(p.hat==HatType.Twisted && p.broom==BroomType.Cleaner,"equipment sync");
  Check(p.magic1==MagicType.Blade && p.InitialMagic1==MagicType.Blade,"battle starts with new spells");
  Check(p.EmoteUpId==8 && p.EmoteLeftId==4,"emote direction preserved");
  p.magic1=MagicType.Thunder; DataConfig.Change();
  Check(p.magic1==MagicType.Thunder,"unrelated account event does not resend");
  NetworkGameManager.Instance.IsWaitingInCodeRoom=false;
  p.RPC_UpdateLobbyLoadout(HatType.Classic,BroomType.Standard,MagicType.Fire,MagicType.Ice,1,2,3,4);
  Check(p.magic1==MagicType.Thunder && p.EmoteUpId==8,"host rejects late request after battle starts");
  NetworkGameManager.Instance.IsWaitingInCodeRoom=true;p.Object.HasInputAuthority=false;
  DataConfig.magic1Index=4;DataConfig.emotes[0]=7;DataConfig.Change();
  Check(p.EmoteUpId==8,"remote object cannot send local account data");
  p.Object.HasInputAuthority=true;p.Object.IsValid=false;DataConfig.Change();
  Check(p.EmoteUpId==8,"despawned object cannot send");
  p.Object.IsValid=true;p.StopLobbyLoadoutSync();DataConfig.Change();
  Check(p.EmoteUpId==8,"unsubscribe on teardown");
  p.RPC_UpdateLobbyLoadout(HatType.Classic,BroomType.Standard,MagicType.Fire,MagicType.Ice,-1,200,3,4);
  Check(p.EmoteUpId==1 && p.EmoteRightId==2,"invalid emotes fallback by direction");
  p.IsMatchBot=true;p.RPC_UpdateLobbyLoadout(HatType.Twisted,BroomType.Cleaner,MagicType.Blade,MagicType.Ice,8,2,3,4);
  Check(p.magic1==MagicType.Fire,"bot loadout unaffected");
  return checks;
 }
}
'@
Add-Type -TypeDefinition ($production + "`n" + $stub)
Write-Output ("Lobby room loadout checks passed: " + [PlayerData]::RunChecks())
