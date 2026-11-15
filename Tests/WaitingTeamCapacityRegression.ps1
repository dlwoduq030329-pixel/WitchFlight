# Production team-option block and callback with deterministic UI/network doubles.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$ui=Get-Content Assets/Script/RoomMatchSettingsUI.Teams.cs -Raw
$rules=Get-Content Assets/Script/BattleMatchRules.cs -Raw
$rules=$rules.Substring(0,$rules.IndexOf('    // Stable client-relative ordering:')) + '}'
$rules=$rules -replace 'using Fusion;',''
$capacity=[regex]::Match($ui,'(?s)        int capacity = .*?        teamDropdown.interactable = true;').Value
$callback=[regex]::Match($ui,'(?ms)^    public void SelectTeam\(int value\)\r?\n    \{.*?^    \}').Value
if(!$capacity -or !$callback){throw 'Missing production team UI paths'}
$source=@'
using System;
'@ + $rules + @'
public class NetworkObject {public bool IsValid=true;}
public class PlayerData {
 public static PlayerData Local;public NetworkObject Object=new();public bool IsLoadoutInitialized=true;
 public object Runner;public int Team=1,RequestedTeam;
 public int teamIndex {get {if(Object==null||!Object.IsValid)throw new Exception("Read before spawn");return Team;}}
 public void RequestWaitingTeam(int value){RequestedTeam=value;}
}
public class NetworkGameManager {public bool IsWaitingInCodeRoom=true;public BattleMatchMode CurrentMatchMode;public object Runner=new();}
public class TMP_Dropdown {
 public class Option {public string text;}
 public Option[] options={new(),new()};public bool IsExpanded,interactable;public int value,refreshes;
 public void Hide(){IsExpanded=false;}public void SetValueWithoutNotify(int v){value=v;}public void RefreshShownValue(){refreshes++;}
}
public class Probe {
 public NetworkGameManager manager=new();public TMP_Dropdown teamDropdown=new();
 public bool allowTeamA,allowTeamB;
 public (int teamA,int teamB,int mine,int capacity)? teamOptionsKey;
 public void Refresh(int teamA,int teamB){var local=PlayerData.Local;
'@ + $capacity + '}' + $callback + @'
}
public static class CapacityChecks {
 static int n;static void Check(bool ok,string m){if(!ok)throw new Exception(m);n++;}
 public static int Run(){
  var p=new Probe();PlayerData.Local=new(){Runner=p.manager.Runner,Team=1};
  for(int i=0;i<10;i++){
   p.manager.CurrentMatchMode=BattleMatchRules.FromDropdown(i);int capacity=i%5+1;
   p.Refresh(1,capacity-1);
   Check(p.allowTeamA&&p.allowTeamB,"available place in mode "+i);
   Check(p.teamDropdown.options[1].text==$"B팀 ({capacity-1}/{capacity})","correct capacity text in mode "+i);
   p.Refresh(1,capacity);Check(!p.allowTeamB,"full team disabled in mode "+i);
   int before=p.teamDropdown.refreshes;p.Refresh(1,capacity);
   Check(p.teamDropdown.refreshes==before,"unchanged options do not refresh mode "+i);
  }
  p.manager.CurrentMatchMode=BattleMatchMode.TwoVsTwo;p.Refresh(1,2);
  Check(!p.allowTeamB,"2v2 team full");
  p.manager.CurrentMatchMode=BattleMatchMode.FiveVsFive;p.Refresh(1,2);
  Check(p.allowTeamB&&p.teamDropdown.options[1].text=="B팀 (2/5)","mode-only change refreshes capacity");
  p.SelectTeam(1);Check(PlayerData.Local.RequestedTeam==2,"valid team request preserved");
  PlayerData.Local.RequestedTeam=0;PlayerData.Local.Object.IsValid=false;p.SelectTeam(1);
  Check(PlayerData.Local.RequestedTeam==0,"despawn callback never reads network team or sends request");
  PlayerData.Local.Object=null;p.SelectTeam(1);Check(true,"missing network object safe");
  PlayerData.Local=null;p.SelectTeam(1);Check(true,"missing local player safe");
  return n;
 }
}
'@
Add-Type -TypeDefinition $source
"Waiting-team capacity and callback checks passed: $([CapacityChecks]::Run())"
