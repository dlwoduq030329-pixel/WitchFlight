$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content Assets/Script/login/DatabaseManager.cs -Raw
function Get-Method([string]$signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "Missing method: $signature" }
    $open = $source.IndexOf('{', $start)
    $depth = 1; $end = $open + 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($start, $end - $start).Replace('private ', 'public ')
}
$methods = (Get-Method 'private static void AssignMissingCatAppearance') + "`n" + (Get-Method 'private bool EnsureCatAppearanceSaved')
$stub = @'
using System;
using System.Collections.Generic;
public class Param : Dictionary<string, object> {}
public class Result { public bool IsSuccess()=>Backend.Success; public string GetMessage()=>"test failure"; }
public static class Backend {
 public static string UserInDate="account-A"; public static bool Success=true;
 public static class GameData {
  public static int Writes; public static Param Last;
  public static Result UpdateV2(string table,string row,string owner,Param param){Writes++;Last=param;return new Result();}
 }
}
public class CatCheck {
 public class PlayerSetting {public int catBodyIndex,catEyeIndex;}
 const string TABLE_NAME="LoginPlayerData";
 string currentPlayerSettingInDate="row";
 bool Fail(string message)=>false;
'@
$tests = @'
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 public static void Run(){
  var c=new CatCheck();var first=new PlayerSetting();
  Check(c.EnsureCatAppearanceSaved(first),"first assignment persisted");
  Check(Backend.GameData.Writes==1&&Backend.GameData.Last.Count==2,"migration writes only two fields");
  int body=first.catBodyIndex,eye=first.catEyeIndex;
  Check(body>=1&&body<=7&&eye>=1&&eye<=3,"IDs valid");
  Check(c.EnsureCatAppearanceSaved(first)&&Backend.GameData.Writes==1,"repeat login must not update saved cat");
  var retry=new PlayerSetting();AssignMissingCatAppearance(retry);
  Check(retry.catBodyIndex==body&&retry.catEyeIndex==eye,"retry/concurrent login keeps allocation");
  var partial=new PlayerSetting{catBodyIndex=7,catEyeIndex=0};AssignMissingCatAppearance(partial);
  Check(partial.catBodyIndex==7&&partial.catEyeIndex>=1,"partial migration preserves saved body");
  var existing=new PlayerSetting{catBodyIndex=2,catEyeIndex=3};AssignMissingCatAppearance(existing);
  Check(existing.catBodyIndex==2&&existing.catEyeIndex==3,"valid server choice wins over generated choice");
  Backend.Success=false;
  Check(!c.EnsureCatAppearanceSaved(new PlayerSetting()),"failed persistence must not publish profile");
  Backend.Success=true;var combinations=new HashSet<string>();
  for(int i=0;i<1000;i++){
   Backend.UserInDate="account-"+i;var s=new PlayerSetting();AssignMissingCatAppearance(s);
   Check(s.catBodyIndex>=1&&s.catBodyIndex<=7&&s.catEyeIndex>=1&&s.catEyeIndex<=3,"range");
   combinations.Add(s.catBodyIndex+"/"+s.catEyeIndex);
  }
  Check(combinations.Count==21,"all 21 combinations reachable across accounts");
 }
}
'@
Add-Type -TypeDefinition ($stub + $methods + $tests)
[CatCheck]::Run()
$prefab = Get-Content Assets/Ch/WitchCat.prefab -Raw
$scriptGuid = (Select-String Assets/Script/WitchCatAppearance.cs.meta -Pattern '^guid: (.+)$').Matches[0].Groups[1].Value
if (!$prefab.Contains($scriptGuid)) { throw 'WitchCat component is not bound' }
foreach ($name in @('cat black','cat cheese','cat grey','cat silver tabby','cat tabby','cat tuxedo','cat white','eye blue','eye green','eye yellow')) {
    $guid = (Select-String -LiteralPath "Assets/Ch/cat/Material/$name.mat.meta" -Pattern '^guid: (.+)$').Matches[0].Groups[1].Value
    if (!$prefab.Contains($guid)) { throw "Missing material: $name" }
}
'WitchCat persistence checks passed; all 10 material references verified.'
