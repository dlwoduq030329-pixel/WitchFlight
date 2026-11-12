# Exercises the authored Tab's real membership/refresh methods with offline UI/network doubles.
# Does not replace Unity rendering or multiplayer transport verification.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$sourceText = Get-Content (Join-Path $repo 'Assets/Script/BattlePerkUI.AuthoredTab.cs') -Raw
function Method($name) {
    $match = [regex]::Match($sourceText, '(?ms)^    private (?:bool|void) ' + $name + '\([^\r\n]*\)\r?\n    \{.*?^    \}')
    if (!$match.Success) { throw "Missing method $name" }
    $match.Value.Replace('private ', 'public ')
}
$source = @'
using System; using System.Collections.Generic;
public class Obj { public bool IsValid=true; }
public class PlayerData {
 public static List<PlayerData> PerkPlayers=new(); public Obj Object=new(); public object Runner;
 public int teamIndex,id,revision; public bool flag,IsLoadoutInitialized=true,PerksInitialized=true;
}
public class GameObject { public bool activeSelf; }
public class Transform { public GameObject gameObject=new(); }
public class PerkPlayerView { public PlayerData data; public int writes; }
public static class PlayerTitleLabel { public static void Bind(Transform row,PlayerData data){} }
public static class BattleMatchRules {
 public static PlayerData local; public static int collections;
 public static void CollectRoster(object runner,List<PlayerData> list) {
  collections++;list.Clear(); if(runner==null)return;
  foreach(var d in PlayerData.PerkPlayers)
   if(d.Object.IsValid && d.Runner==runner && d.IsLoadoutInitialized && d.teamIndex>0) list.Add(d);
  list.Sort((a,b)=>{int Side(PlayerData d)=>d==local?0:d.teamIndex==local.teamIndex?1:2;
   int c=Side(a).CompareTo(Side(b));return c!=0?c:a.id.CompareTo(b.id);});
 }
}
public class Probe {
 public PlayerData local,authoredTabLocal;
 public bool authoredTabWasOpen,authoredTabLocalReady,tabTooltipDirty;
 public readonly List<(PlayerData player,int team)> authoredTabMembership=new();
 public readonly PlayerData[] authoredTabPlayers=new PlayerData[10];
 public readonly Transform[] authoredTabRows=new Transform[10];
 public readonly PerkPlayerView[] authoredTabViews=new PerkPlayerView[10];
 public readonly List<PlayerData> comparisonRoster=new();
 public GameObject comparisonRoot=new();
 private (PlayerData,int,bool)[] snapshots=new (PlayerData,int,bool)[10];
 public Probe(){for(int i=0;i<10;i++){authoredTabRows[i]=new();authoredTabViews[i]=new();}}
 public bool Ready(PlayerData d)=>d!=null&&d.Object.IsValid&&d.PerksInitialized;
 public bool UpdateSnapshot(int i,PlayerData d){var s=Ready(d)?(d,d.revision,d.flag):default;bool c=s!=snapshots[i];snapshots[i]=s;return c;}
 public void FillPlayer(PerkPlayerView v,PlayerData d){v.data=d;v.writes++;}
 public static void Show(GameObject root,bool show){root.activeSelf=show;}
'@
$source += (Method AuthoredTabRosterChanged) + "`n" + (Method UpdateAuthoredTab) + "`n}`n"
$source += @'
public static class Checks {
 static int checks;static void Check(bool b,string message){if(!b)throw new Exception(message);checks++;}
 static int Writes(Probe p){int n=0;foreach(var v in p.authoredTabViews)n+=v.writes;return n;}
 public static int Run(){
  for(int size=1;size<=5;size++)for(int ownTeam=1;ownTeam<=2;ownTeam++){
   PlayerData.PerkPlayers.Clear();var runner=new object();var p=new Probe();
   for(int team=1;team<=2;team++)for(int j=0;j<size;j++){
    var d=new PlayerData{Runner=runner,teamIndex=team,id=team*10+j};PlayerData.PerkPlayers.Add(d);
    if(team==ownTeam)p.local=d;
   }
   BattleMatchRules.local=p.local;p.UpdateAuthoredTab(true);
   Check(p.authoredTabPlayers[0]==p.local,"self first on either team");
   for(int i=0;i<5;i++){
    Check(p.authoredTabRows[i].gameObject.activeSelf==(i<size),"own occupied rows only");
    Check(p.authoredTabRows[5+i].gameObject.activeSelf==(i<size),"enemy occupied rows only");
    if(i<size){Check(p.authoredTabPlayers[i].teamIndex==ownTeam,"own team mapping");Check(p.authoredTabPlayers[5+i].teamIndex!=ownTeam,"enemy team mapping");}
   }
   int writes=Writes(p),collections=BattleMatchRules.collections;
   p.UpdateAuthoredTab(true);Check(Writes(p)==writes&&collections==BattleMatchRules.collections,"unchanged open Tab does not sort or rewrite UI");
   p.authoredTabPlayers[5].revision++;p.UpdateAuthoredTab(true);Check(Writes(p)==writes+1,"only changed stats/perks row refreshed");
   writes=Writes(p);p.authoredTabPlayers[0].flag=true;p.UpdateAuthoredTab(true);Check(Writes(p)==writes+1,"flag change refreshes owner");
   var departed=p.authoredTabPlayers[5];PlayerData.PerkPlayers.Remove(departed);departed.Object.IsValid=false;
   p.UpdateAuthoredTab(true);Check(!p.authoredTabRows[5+size-1].gameObject.activeSelf,"departed player leaves no stale row");
   var joined=new PlayerData{Runner=runner,teamIndex=3-ownTeam,id=99};PlayerData.PerkPlayers.Add(joined);
   p.UpdateAuthoredTab(true);Check(p.authoredTabPlayers[5+size-1]==joined&&p.authoredTabRows[5+size-1].gameObject.activeSelf,"existing row reused for new member");
   p.UpdateAuthoredTab(false);writes=Writes(p);collections=BattleMatchRules.collections;
   joined.revision++;p.UpdateAuthoredTab(false);Check(!p.comparisonRoot.activeSelf&&Writes(p)==writes&&collections==BattleMatchRules.collections,"closed Tab does no roster or panel work");
   p.UpdateAuthoredTab(true);Check(p.comparisonRoot.activeSelf&&Writes(p)>writes,"reopen refreshes hidden state");
  }
  PlayerData.PerkPlayers.Clear();var late=new PlayerData{Runner=new object(),teamIndex=1,PerksInitialized=false};
  PlayerData.PerkPlayers.Add(late);var delayed=new Probe{local=late};BattleMatchRules.local=late;
  delayed.UpdateAuthoredTab(true);Check(!delayed.authoredTabRows[0].gameObject.activeSelf,"uninitialized local row hidden");
  late.PerksInitialized=true;delayed.UpdateAuthoredTab(true);Check(delayed.authoredTabRows[0].gameObject.activeSelf,"late initialization without reopening Tab");
  delayed.local=null;delayed.UpdateAuthoredTab(true);Check(!delayed.authoredTabRows[0].gameObject.activeSelf,"lost local player clears rows");
  return checks;
 }
}
'@
Add-Type -TypeDefinition $source
"Authored Tab roster, reuse, readiness and change-only refresh checks passed: $([Checks]::Run())"
