# Exercise the production client acknowledgement guard independently of Unity/Fusion transport.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$code = Get-Content Assets/Script/BattleIntroPresentation.cs -Raw
$method = [regex]::Match($code, '(?ms)^    private static void ReportPreparedClient\(Player player\)\r?\n    \{.*?^    \}')
if (!$method.Success) { throw 'Missing production ready guard' }
$source = @'
public class Player { public int reports; public void ReportBattleSceneReady(){reports++;} }
public static class BattleLoadingUI {
 public static bool AssetsReady; public static string status;
 public static void SetStatus(string text){status=text;}
 public static void SetProgress(float progress){}
}
public class Probe {
'@
$source += $method.Value.Replace('private static', 'public static')
$source += '}'
Add-Type -TypeDefinition $source
$player = [Player]::new()
[Probe]::ReportPreparedClient($player)
if ($player.reports -ne 0) { throw 'Incomplete resource loading acknowledged readiness' }
[BattleLoadingUI]::AssetsReady = $true
[Probe]::ReportPreparedClient($player)
if ($player.reports -ne 1 -or ![BattleLoadingUI]::status) { throw 'Prepared client did not acknowledge or show peer wait' }
[BattleLoadingUI]::AssetsReady = $false
[Probe]::ReportPreparedClient($player)
if ($player.reports -ne 1) { throw 'Lost readiness was acknowledged' }
'Battle resource readiness guard: 3 checks passed.'
