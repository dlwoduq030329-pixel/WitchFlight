# Run the actual shared lookup and both UI resolvers without loading Unity scenes.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$table = Get-Content Assets/Script/ProfileImageTable.cs -Raw
$bridge = Get-Content Assets/Script/linkuserinfo.cs -Raw
$kill = Get-Content Assets/Script/BattleKillFeed.cs -Raw
function Method([string]$source, [string]$name) {
    $match = [regex]::Match($source, '(?ms)^    (?:public|private) [\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$match.Success) { throw "Missing method $name" }
    return $match.Value -replace '^    private ', '    public '
}
$stubs = @'
namespace UnityEngine {
 public class ScriptableObject {}
 public class Sprite {}
 public class SerializeField : System.Attribute {}
 public class TooltipAttribute : System.Attribute {public TooltipAttribute(string value){}}
 public class CreateAssetMenuAttribute : System.Attribute {public string fileName,menuName;}
 public static class Resources {
  public static object Asset;public static int Loads;
  public static T Load<T>(string path) where T:class {Loads++;if(path!="ProfileImageTable")throw new System.Exception("Wrong resource path");return Asset as T;}
 }
}
public class Image {public UnityEngine.Sprite sprite;public bool enabled;}
public class linkuserinfo {
 public ProfileImageTable profileTable;
'@
$killStub = @'
}
public class KillFeed {
 public ProfileImageTable profileTable;
'@
$tests = @'
}
public static class ProfileChecks {
 static int count;
 static void Check(bool ok,string reason){if(!ok)throw new System.Exception(reason);count++;}
 static void Set(ProfileImageTable table,string field,object value)=>typeof(ProfileImageTable).GetField(field,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(table,value);
 public static int Run(){
  var table=new ProfileImageTable();var a=new UnityEngine.Sprite();var b=new UnityEngine.Sprite();var c=new UnityEngine.Sprite();var fallback=new UnityEngine.Sprite();
  Check(table.Count==0&&!table.TryGetSprite(0,out _)&&table.GetSprite(0)==null,"empty table is safe");
  Set(table,"profiles",new[]{a,null,c});
  Check(table.Count==3&&table.GetSprite(0)==a&&table.GetSprite(2)==c,"stable array indices include holes");
  Check(!table.TryGetSprite(1,out _)&&!table.TryGetSprite(-1,out _)&&!table.TryGetSprite(int.MaxValue,out _),"missing and invalid indices do not clamp to a different profile");
  Set(table,"fallbackSprite",fallback);
  Check(table.GetSprite(1)==fallback&&table.GetSprite(-1)==fallback&&table.GetSprite(3)==fallback,"fallback handles null entries and unknown IDs");
  Check(table.GetSprite(2)==c,"known entry beats fallback");
  Set(table,"profiles",null);Check(table.Count==0&&table.GetSprite(0)==fallback,"null array is safe");
  Set(table,"profiles",new[]{a,b,c});UnityEngine.Resources.Asset=table;
  Check(ProfileImageTable.Default==table&&ProfileImageTable.Default==table&&UnityEngine.Resources.Loads==1,"default resource loaded only once");
  var bridge=new linkuserinfo();
  Check(bridge.ResolveProfileSprite(0)==a,"VS resolves from the shared table");
  var feed=new KillFeed();var image=new Image();
  feed.SetProfile(image,0);Check(image.enabled&&image.sprite==a,"kill and VS resolve the same shared profile without manual table assignment");
  feed.SetProfile(image,2);Check(image.sprite==c,"opponent profile uses its own index");
  feed.SetProfile(image,100);Check(image.sprite==fallback&&bridge.ResolveProfileSprite(100)==fallback,"shared missing-profile fallback is consistent");
  Check(UnityEngine.Resources.Loads==1,"resolvers do not reload Resources every refresh");
  Set(table,"profiles",new[]{b});feed.SetProfile(image,0);Check(image.sprite==b&&bridge.ResolveProfileSprite(0)==b,"editing shared table updates both lookups");
  var custom=new ProfileImageTable();Set(custom,"profiles",new[]{c});bridge.profileTable=custom;
  Check(bridge.ResolveProfileSprite(0)==c,"explicit table assignment is respected");
  var empty=new ProfileImageTable();bridge.profileTable=empty;feed.profileTable=empty;
  Check(bridge.ResolveProfileSprite(0)==null,"empty table does not substitute any local VS image");
  feed.SetProfile(image,0);Check(image.sprite==null&&!image.enabled,"empty table clears previous kill image");
  Check(bridge.ResolveProfileSprite(-1)==null,"unknown profile cannot use an unrelated per-UI fallback");
  Set(empty,"fallbackSprite",fallback);feed.SetProfile(image,-1);
  Check(image.sprite==fallback&&image.enabled&&bridge.ResolveProfileSprite(-1)==fallback,"only table fallback is used for every UI");
  Set(empty,"fallbackSprite",null);feed.SetProfile(image,0);Check(image.sprite==null&&!image.enabled,"removing table fallback hides missing profile");
  feed.SetProfile(null,0);Check(true,"unassigned image is safe");
  typeof(ProfileImageTable).GetField("defaultTable",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).SetValue(null,null);
  UnityEngine.Resources.Asset=null;bridge.profileTable=null;feed.profileTable=null;
  Check(bridge.ResolveProfileSprite(0)==null,"missing shared resource is safe");
  feed.SetProfile(image,0);Check(image.sprite==null&&!image.enabled,"missing shared resource does not leave stale kill image");
  return count;
 }
}
'@
# Serialized fields are populated by Unity (reflection in these tests), not C# assignment.
Add-Type -TypeDefinition ("#pragma warning disable 0649`n" + $table + $stubs + (Method $bridge 'ResolveProfileSprite') + $killStub + (Method $kill 'SetProfile') + $tests)
"Profile table and UI lookup checks passed: $([ProfileChecks]::Run())"

$asset = Get-Content Assets/Resources/ProfileImageTable.asset -Raw
$scriptGuid = [regex]::Match((Get-Content Assets/Script/ProfileImageTable.cs.meta -Raw),'guid: (\w+)').Groups[1].Value
$assetGuid = [regex]::Match((Get-Content Assets/Resources/ProfileImageTable.asset.meta -Raw),'guid: (\w+)').Groups[1].Value
if (!$asset.Contains('guid: '+$scriptGuid) -or !$asset.Contains('m_Name: ProfileImageTable')) { throw 'Invalid profile table script link' }
foreach ($path in 'Assets/UI/Prefab/BattleUI.prefab','Assets/Scenes/Battle.unity') {
    if (!(Get-Content $path -Raw).Contains('profileTable: {fileID: 11400000, guid: '+$assetGuid)) { throw "Missing shared table binding: $path" }
}
'Profile table asset and saved UI bindings passed.'
foreach ($source in $bridge,$kill,(Get-Content Assets/Editor/WitchFlightIntroBinding.cs -Raw)) {
    if ($source -match 'profileSprites|fallbackProfileSprite|profileSource') { throw 'Per-UI profile lookup must be fully retired' }
}
if (!(Get-Content Assets/Editor/WitchFlightIntroBinding.cs -Raw).Contains('FindProperty("profileTable")')) {
    throw 'Editor rebinding must retain the shared table'
}
'All runtime profile lookups and editor rebinding use the shared table only.'
