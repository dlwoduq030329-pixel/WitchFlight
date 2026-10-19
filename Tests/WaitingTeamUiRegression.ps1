# Real waiting-room population and portrait lookup methods with lightweight UI doubles.
# Does not open Unity or claim rendered layout / multiplayer transport coverage.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$bridge=Get-Content Assets/Script/linkuserinfo.cs -Raw
$waiting=Get-Content Assets/Script/linkuserinfo.WaitingRoom.cs -Raw
function Method([string]$s,[string]$name) {
 $m=[regex]::Match($s,'(?ms)^    (?:private|public) (?:static )?\w+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"}; $m.Value -replace '^    private ','    public '
}
$source=@'
using System;
using System.Collections.Generic;
public struct PlayerRef:IEquatable<PlayerRef> {
 public int Id;public static PlayerRef None=>default;public static implicit operator PlayerRef(int id)=>new(){Id=id};
 public bool Equals(PlayerRef p)=>Id==p.Id;public override bool Equals(object o)=>o is PlayerRef p&&Equals(p);
 public override int GetHashCode()=>Id;public static bool operator==(PlayerRef a,PlayerRef b)=>a.Equals(b);public static bool operator!=(PlayerRef a,PlayerRef b)=>!a.Equals(b);
}
public class GameObject {public bool activeSelf=true;public void SetActive(bool b){activeSelf=b;}}
public class Image {public int Profile=-1;}
public class TMP_Text {public string text;public bool richText=true;}
public class NetworkObject {public PlayerRef InputAuthority;}
public class PlayerData {public NetworkObject Object=new();public bool IsRoomOwner,Ready=true;public int teamIndex,playerprofile;public string playerName;}
public class NetworkRunner {public PlayerRef LocalPlayer;public Info SessionInfo=new();public class Info {public string Name="1234";}}
public class NetworkGameManager {public bool IsWaitingInCodeRoom=true;public int RequiredPlayerCount=4;}
public class WaitingRoomSlot {
 public GameObject slotRoot=new(),occupiedRoot=new(),emptyRoot=new(),localPlayerMarker=new();
 public Image profileImage=new();public TMP_Text playerLabel=new(),teamLabel=new();
}
public class LobbyProbe {
 public List<PlayerData> players=new();public List<PlayerRef> waitingParticipants=new();public PlayerData localPlayerData;
 public WaitingRoomSlot[] waitingRoomSlots={new(),new(),new(),new()};public bool waitingRoomInitialized;
 public TMP_Text roomkeepername=new(),waitingRoomCodeText=new(),waitingRoomCountText=new(),Aslotname=new(),Bslotname=new();
 public Image AslotProfile=new(),BslotProfile=new();public GameObject CodeWaitingRoomPanel=new();
 public bool IsReady(PlayerData d)=>d!=null&&d.Ready;public void EnsureAuthoredWaitingSlots(){}
 public PlayerData GetPlayerData(PlayerRef p)=>players.Find(d=>IsReady(d)&&d.Object.InputAuthority==p);
 public void ApplyProfile(Image image,PlayerData d){if(image!=null)image.Profile=IsReady(d)?d.playerprofile:-1;}
'@
foreach($name in @('RefreshWaitingRoom','SetSlotName')){$source+=Method $bridge $name}
$source+=Method $waiting 'GetCodeWaitingRoomProfile'
$source+=@'
}
public static class Checks {
 static int n;static void Check(bool condition,string message){if(!condition)throw new Exception(message);n++;}
 public static int Run(){
  var ui=new LobbyProbe();var manager=new NetworkGameManager();var runner=new NetworkRunner();
  foreach(int id in new[]{4,2,1,3}) {
   var d=new PlayerData{playerName="Player "+id,playerprofile=10+id,teamIndex=id%2==1?1:2,IsRoomOwner=id==1};
   d.Object.InputAuthority=id;ui.players.Add(d);
  }
  ui.waitingParticipants.AddRange(new PlayerRef[]{1,2,3,4});
  for(int self=1;self<=4;self++){
   runner.LocalPlayer=self;ui.localPlayerData=ui.GetPlayerData(self);ui.RefreshWaitingRoom(manager,runner);
   Check(ReferenceEquals(ui.GetCodeWaitingRoomProfile(self),ui.waitingRoomSlots[self-1].profileImage),"dropdown follows own card even for third/fourth participant "+self);
   for(int i=0;i<4;i++) {
    var slot=ui.waitingRoomSlots[i];Check(slot.slotRoot.activeSelf&&slot.profileImage.Profile==11+i,"existing card active and correct portrait "+self+"/"+i);
    Check(slot.localPlayerMarker.activeSelf==(i==self-1),"only local marker "+self+"/"+i);
   }
  }
  ui.GetPlayerData(3).teamIndex=2;ui.RefreshWaitingRoom(manager,runner);
  Check(ui.waitingRoomSlots[2].teamLabel.text=="B팀"&&ui.waitingRoomSlots[2].profileImage.Profile==13,"team changes label without stealing another player's card");
  ui.waitingParticipants.Remove(3);ui.waitingParticipants.Remove(4);runner.LocalPlayer=1;ui.localPlayerData=ui.GetPlayerData(1);
  ui.RefreshWaitingRoom(manager,runner);
  Check(ui.waitingRoomSlots[2].slotRoot.activeSelf&&ui.waitingRoomSlots[3].slotRoot.activeSelf,"empty extra cards remain visible in 2v2");
  Check(ui.waitingRoomSlots[2].profileImage.Profile==-1&&!ui.waitingRoomSlots[2].occupiedRoot.activeSelf&&ui.waitingRoomSlots[2].emptyRoot.activeSelf,"departed portrait cleared");
  Check(ui.GetCodeWaitingRoomProfile(3)==null,"departed player has no local dropdown anchor");
  var card3=ui.waitingRoomSlots[2].slotRoot;var card4=ui.waitingRoomSlots[3].slotRoot;
  manager.RequiredPlayerCount=2;ui.RefreshWaitingRoom(manager,runner);
  Check(!card3.activeSelf&&!card4.activeSelf&&ui.waitingRoomSlots[0].slotRoot.activeSelf&&ui.waitingRoomSlots[1].slotRoot.activeSelf,"1v1 hides only third/fourth cards");
  manager.RequiredPlayerCount=4;ui.RefreshWaitingRoom(manager,runner);
  Check(ReferenceEquals(card3,ui.waitingRoomSlots[2].slotRoot)&&card3.activeSelf&&card4.activeSelf,"2v2 restores the same authored cards");
  ui.RefreshWaitingRoom(null,null);Check(ui.GetCodeWaitingRoomProfile(1)==null&&!card3.activeSelf&&!card4.activeSelf,"closing session clears anchor and extra cards");
  return n;
 }
}
'@
Add-Type -TypeDefinition $source
"Waiting-room card population, 1v1/2v2 visibility and per-client dropdown anchor checks passed: $([Checks]::Run())"
