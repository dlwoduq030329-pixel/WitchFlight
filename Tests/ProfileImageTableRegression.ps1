# Run the actual shared lookup and UI resolvers without loading Unity scenes.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$table = Get-Content Assets/Script/ProfileImageTable.cs -Raw
$bridge = Get-Content Assets/Script/linkuserinfo.cs -Raw
$kill = Get-Content Assets/Script/BattleKillFeed.cs -Raw
$hud = Get-Content Assets/Script/BattleHud.cs -Raw
$lobby = Get-Content Assets/Script/LobbyPlayerInitializer.cs -Raw
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
public class Image {
 UnityEngine.Sprite current;
 public int SpriteAssignments;
 public UnityEngine.Sprite sprite {get=>current;set{current=value;SpriteAssignments++;}}
 public bool enabled;
}
public class NetworkObject {public bool IsValid=true,HasInputAuthority=true;}
public class PlayerData {
 public static PlayerData Local;
 public NetworkObject Object=new NetworkObject();
 public bool IsLoadoutInitialized=true;
 public int playerprofile;
}
public static class DataConfig {public static int playerprofile;}
public class DatabaseManager {
 public static DatabaseManager Instance;
 public bool IsDataConfigReady,HasLoadedProfile;
}
public class linkuserinfo {
 public ProfileImageTable profileTable;
'@
$killStub = @'
}
public class KillFeed {
 public ProfileImageTable profileTable;
'@
$hudStub = @'
}
public class ProfileHud {
 public ProfileImageTable profileTable;
 public Image playerProfileImage;
'@
$lobbyStub = @'
}
public class ProfileLobby {
 public ProfileImageTable profileTable;
 public Image playerProfileImage;
'@
$canCustomize = [regex]::Match($lobby, '(?ms)^    private bool CanCustomize =>.*?;').Value
if (!$canCustomize) { throw 'Missing lobby data readiness check' }
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
  var hud=new ProfileHud();hud.BindPlayerProfile();Check(true,"unassigned HUD image is safe");
  hud.playerProfileImage=new Image();PlayerData.Local=null;
  hud.BindPlayerProfile();Check(hud.playerProfileImage.sprite==null&&!hud.playerProfileImage.enabled,"HUD waits for local PlayerData");
  var local=new PlayerData();PlayerData.Local=local;
  hud.BindPlayerProfile();Check(hud.profileTable==table&&hud.playerProfileImage.sprite==b&&hud.playerProfileImage.enabled,"HUD automatically resolves the shared table and local index without a living Player");
  int assignments=hud.playerProfileImage.SpriteAssignments;
  hud.BindPlayerProfile();Check(hud.playerProfileImage.SpriteAssignments==assignments,"unchanged HUD sprite is not reassigned every frame");
  Set(table,"profiles",new[]{a,b,c});local.playerprofile=2;
  hud.BindPlayerProfile();Check(hud.playerProfileImage.sprite==c,"replicated profile changes update HUD");
  local.playerprofile=99;hud.BindPlayerProfile();Check(hud.playerProfileImage.sprite==fallback,"HUD uses shared fallback for unknown index");
  local.IsLoadoutInitialized=false;hud.BindPlayerProfile();Check(!hud.playerProfileImage.enabled&&hud.playerProfileImage.sprite==null,"uninitialized local data cannot display a stale profile");
  local.IsLoadoutInitialized=true;local.playerprofile=0;local.Object.IsValid=false;
  hud.BindPlayerProfile();Check(!hud.playerProfileImage.enabled,"invalid network object is ignored");
  local.Object.IsValid=true;local.Object.HasInputAuthority=false;
  hud.BindPlayerProfile();Check(!hud.playerProfileImage.enabled,"remote authority cannot replace local profile");
  local.Object=null;hud.BindPlayerProfile();Check(!hud.playerProfileImage.enabled,"missing network object is safe");
  local.Object=new NetworkObject();hud.profileTable=custom;
  hud.BindPlayerProfile();Check(hud.playerProfileImage.sprite==c&&hud.playerProfileImage.enabled,"HUD respects explicit profile table");
  hud.profileTable=empty;hud.BindPlayerProfile();Check(!hud.playerProfileImage.enabled&&hud.playerProfileImage.sprite==null,"empty HUD table hides previous profile");
  hud.profileTable=table;hud.BindPlayerProfile();Check(hud.playerProfileImage.sprite==a&&hud.playerProfileImage.enabled,"HUD recovers when data and table become available");
  PlayerData.Local=null;hud.BindPlayerProfile();Check(!hud.playerProfileImage.enabled&&hud.playerProfileImage.sprite==null,"leaving the account clears the previous HUD profile");
  var lobby=new ProfileLobby();lobby.RefreshPlayerProfile();Check(true,"unassigned lobby Image is safe");
  lobby.playerProfileImage=new Image();DatabaseManager.Instance=null;
  lobby.RefreshPlayerProfile();Check(!lobby.playerProfileImage.enabled,"lobby waits for database initialization");
  DatabaseManager.Instance=new DatabaseManager{HasLoadedProfile=true};DataConfig.playerprofile=2;
  lobby.RefreshPlayerProfile();Check(!lobby.playerProfileImage.enabled,"lobby waits until server data has been applied");
  DatabaseManager.Instance.IsDataConfigReady=true;
  lobby.RefreshPlayerProfile();Check(lobby.playerProfileImage.sprite==c&&lobby.playerProfileImage.enabled,"lobby uses DataConfig before a Fusion PlayerData exists");
  assignments=lobby.playerProfileImage.SpriteAssignments;
  lobby.RefreshPlayerProfile();Check(lobby.playerProfileImage.SpriteAssignments==assignments,"unchanged lobby sprite is not reassigned");
  DataConfig.playerprofile=1;lobby.RefreshPlayerProfile();Check(lobby.playerProfileImage.sprite==b,"silent login snapshot or profile edit updates the lobby");
  DatabaseManager.Instance.HasLoadedProfile=false;lobby.RefreshPlayerProfile();Check(lobby.playerProfileImage.sprite==null&&!lobby.playerProfileImage.enabled,"logout or account mismatch clears stale lobby profile");
  DatabaseManager.Instance.HasLoadedProfile=true;DataConfig.playerprofile=99;
  lobby.RefreshPlayerProfile();Check(lobby.playerProfileImage.sprite==fallback,"unknown lobby index uses shared fallback");
  lobby.profileTable=custom;DataConfig.playerprofile=0;
  lobby.RefreshPlayerProfile();Check(lobby.playerProfileImage.sprite==c,"lobby respects explicit table assignment");
  lobby.profileTable=empty;lobby.RefreshPlayerProfile();Check(!lobby.playerProfileImage.enabled&&lobby.playerProfileImage.sprite==null,"empty lobby table hides stale sprite");
  typeof(ProfileImageTable).GetField("defaultTable",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).SetValue(null,null);
  UnityEngine.Resources.Asset=null;bridge.profileTable=null;feed.profileTable=null;
  Check(bridge.ResolveProfileSprite(0)==null,"missing shared resource is safe");
  feed.SetProfile(image,0);Check(image.sprite==null&&!image.enabled,"missing shared resource does not leave stale kill image");
  hud.profileTable=null;PlayerData.Local=local;hud.BindPlayerProfile();Check(hud.playerProfileImage.sprite==null&&!hud.playerProfileImage.enabled,"missing HUD resource is safe");
  lobby.profileTable=null;lobby.RefreshPlayerProfile();Check(lobby.playerProfileImage.sprite==null&&!lobby.playerProfileImage.enabled,"missing lobby resource is safe");
  return count;
 }
}
'@
# Serialized fields are populated by Unity (reflection in these tests), not C# assignment.
Add-Type -TypeDefinition ("#pragma warning disable 0649`n" + $table + $stubs + (Method $bridge 'ResolveProfileSprite') + $killStub + (Method $kill 'SetProfile') + $hudStub + (Method $hud 'BindPlayerProfile') + $lobbyStub + $canCustomize + (Method $lobby 'RefreshPlayerProfile') + $tests)
"Profile table and UI lookup checks passed: $([ProfileChecks]::Run())"

$asset = Get-Content Assets/Resources/ProfileImageTable.asset -Raw
$scriptGuid = [regex]::Match((Get-Content Assets/Script/ProfileImageTable.cs.meta -Raw),'guid: (\w+)').Groups[1].Value
$assetGuid = [regex]::Match((Get-Content Assets/Resources/ProfileImageTable.asset.meta -Raw),'guid: (\w+)').Groups[1].Value
if (!$asset.Contains('guid: '+$scriptGuid) -or !$asset.Contains('m_Name: ProfileImageTable')) { throw 'Invalid profile table script link' }
foreach ($path in 'Assets/UI/Prefab/BattleUI.prefab','Assets/Scenes/Battle.unity','Assets/Scenes/Main.unity') {
    if (!(Get-Content $path -Raw).Contains('profileTable: {fileID: 11400000, guid: '+$assetGuid)) { throw "Missing shared table binding: $path" }
}
'Profile table asset and saved UI bindings passed.'
foreach ($source in $bridge,$kill,$hud,$lobby,(Get-Content Assets/Editor/WitchFlightIntroBinding.cs -Raw)) {
    if ($source -match 'profileSprites|fallbackProfileSprite|profileSource') { throw 'Per-UI profile lookup must be fully retired' }
}
if (!(Get-Content Assets/Editor/WitchFlightIntroBinding.cs -Raw).Contains('FindProperty("profileTable")')) {
    throw 'Editor rebinding must retain the shared table'
}
'All runtime profile lookups and editor rebinding use the shared table only.'
if (!(Method $hud 'BindPlayer').Contains('BindPlayerProfile();')) { throw 'HUD refresh must bind the profile even without a living player' }
$scene = Get-Content Assets/Scenes/Battle.unity -Raw
$hudGuid = [regex]::Match((Get-Content Assets/Script/BattleHud.cs.meta -Raw),'guid: (\w+)').Groups[1].Value
$hudDoc = ($scene -split '(?m)^--- ' | Where-Object { $_.Contains('guid: '+$hudGuid) })
if (!$hudDoc.Contains('profileTable: {fileID: 11400000, guid: '+$assetGuid)) { throw 'HUD must use the shared table' }
$imageId = [regex]::Match($hudDoc,'playerProfileImage: \{fileID: (\d+)\}').Groups[1].Value
if (!$imageId) { throw 'HUD profile image must be assigned' }
$imageDoc = [regex]::Match($scene,'(?ms)^--- !u!114 &'+$imageId+' stripped\r?\n.*?(?=^--- |\z)').Value
if (!$imageDoc.Contains('fileID: 1938420218188023185, guid: f85a690f8032072489dd7b498ab4110d') -or
    !$imageDoc.Contains('m_PrefabInstance: {fileID: 2027782578}') -or
    !$imageDoc.Contains('guid: fe87c0e1cc204ed48ad3b37840f39efc')) { throw 'HUD must reference the HP-adjacent profile Image, not a kill or VS image' }
$prefab = Get-Content Assets/UI/Prefab/BattleUI.prefab -Raw
$sourceImage = [regex]::Match($prefab,'(?ms)^--- !u!114 &1938420218188023185\r?\n.*?(?=^--- |\z)').Value
if (!$sourceImage.Contains('m_GameObject: {fileID: 5447397501059814167}')) { throw 'HUD profile Image source is missing' }
'Battle HUD refresh and saved profile Image binding passed.'
foreach ($method in 'LateUpdate','RefreshCustomizationUI') {
    if (!(Method $lobby $method).Contains('RefreshPlayerProfile();')) { throw "Lobby profile must refresh on $method (including silent login/logout)" }
}
function YamlBlock([string]$text, [string]$id) {
    $block = [regex]::Match($text, '(?ms)^--- !u!\d+ &'+$id+'(?: stripped)?\r?\n.*?(?=^--- |\z)').Value
    if (!$block) { throw "Missing YAML object $id" }
    return $block
}
$vs = Get-Content Assets/UI/Prefab/VSUI.prefab -Raw
$vsBridge = YamlBlock $prefab '8220298834494429443'
foreach ($spec in @(@('localProfileImage','347984970694060090','8238103714858016366'),@('opponentProfileImage','3394237715348283178','5591963939943865801'))) {
    $id = [regex]::Match($vsBridge, $spec[0]+': \{fileID: (\d+)\}').Groups[1].Value
    $ref = YamlBlock $prefab $id
    if (!$ref.Contains('fileID: '+$spec[1]+', guid: 7e152cf3226fafd4a8877a6fd9ac6943') -or
        !$ref.Contains('m_PrefabInstance: {fileID: 3037583284370875108}')) { throw "Wrong VS profile target: $($spec[0])" }
    if (!(YamlBlock $vs $spec[1]).Contains('m_GameObject: {fileID: '+$spec[2]+'}')) { throw "Missing VS profile Image: $($spec[0])" }
    if ($scene -match ('propertyPath: '+$spec[0]+'\s')) { throw 'Battle scene must inherit the repaired VS profile bindings' }
}
foreach ($call in 'ApplyProfile(localProfileImage, localPlayerData);','ApplyProfile(opponentProfileImage, opponentPlayerData);') {
    if (!(Method $bridge 'RefreshSnapshot').Contains($call)) { throw 'VS profiles must resolve independently for each player' }
}
foreach ($text in $prefab,$vs,(Get-Content Assets/Scenes/Main.unity -Raw)) {
    $ids = [regex]::Matches($text,'(?m)^--- !u!\d+ &(\d+)') | ForEach-Object { $_.Groups[1].Value }
    if (@($ids | Group-Object | Where-Object Count -gt 1).Count) { throw 'Duplicate Unity fileID' }
}
'Lobby refresh and VS local/opponent profile bindings passed.'
$main = Get-Content Assets/Scenes/Main.unity -Raw
$initializer = YamlBlock $main '1900010003'
if (!$initializer.Contains('playerProfileImage: {fileID: 1900010010}') -or
    !$initializer.Contains('profileTable: {fileID: 11400000, guid: '+$assetGuid)) { throw 'Main initializer must bind the profile Image and shared table' }
$mainImage = YamlBlock $main '1900010010'
if (!$mainImage.Contains('fileID: 2300791819703023576, guid: 782bcd5720a485045a2e93eeba73c23f') -or
    !$mainImage.Contains('m_PrefabInstance: {fileID: 4120094046389501980}')) { throw 'Main profile must target the same RobbyUI instance as the nickname' }
if (!(YamlBlock $main '939267800656933361').Contains('m_PrefabInstance: {fileID: 4120094046389501980}')) { throw 'Nickname and profile must belong to the same lobby' }
$robby = Get-Content Assets/UI/Prefab/RobbyUI.prefab -Raw
if (!(YamlBlock $robby '2300791819703023576').Contains('guid: fe87c0e1cc204ed48ad3b37840f39efc') -or
    !(YamlBlock $robby '8197042372082339467').Contains('m_AnchorMin: {x: 0, y: 1}')) { throw 'Main profile must be the existing top-left UI Image' }
'Main login initializer and top-left profile binding passed.'
