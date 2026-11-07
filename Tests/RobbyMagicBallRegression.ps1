# Exercise the production sweep and eligibility rule without running Unity.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$ball = Get-Content -Raw Assets/Script/RobbyBeachBall.cs
$practice = Get-Content -Raw Assets/Script/RobbyMagicPractice.cs
$sweep = [regex]::Match($ball, '(?ms)^    internal static bool TrySphereContact\(.*?^    \}').Value
$filter = [regex]::Match($practice, '(?ms)^    internal static bool CanPushBall\(.*?;').Value
if (!$sweep -or !$filter) { throw 'Missing production ball sweep / spell eligibility rule' }
$magic = [regex]::Match((Get-Content -Raw Assets/Script/PlayerData.cs), 'public enum MagicType\s*\{[^}]+\}').Value
$effects = [regex]::Match((Get-Content -Raw Assets/Script/MagicStatTable.cs), 'public enum MagicEffectKind\s*\{[^}]+\}').Value
$shim = @'
using System;
public struct Vector3 {
 public float x,y,z;
 public Vector3(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
 public float sqrMagnitude => x*x+y*y+z*z;
 public static Vector3 operator -(Vector3 a,Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
 public static float Dot(Vector3 a,Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
}
public static class Mathf { public static float Sqrt(float v) => (float)Math.Sqrt(v); }
public struct MagicStatEntry {
 public MagicType magic; public MagicEffectKind effect; public bool requiresTarget;
 public bool TargetsAllies => effect == MagicEffectKind.Healing || effect == MagicEffectKind.MeowFestival;
}
'@
Add-Type -TypeDefinition ($shim + $magic + $effects + 'public static class BallChecks {' +
    $sweep.Replace('internal static','public static') + $filter.Replace('internal static','public static') + '}')
function V($x,$y,$z) { [Vector3]::new($x,$y,$z) }
$script:checks = 0
function Sweep($start,$end,$center,$radius,$expected,$expectedFraction,$label) {
    [float]$fraction = 0
    $actual = [BallChecks]::TrySphereContact($start,$end,$center,$radius,[ref]$fraction)
    if ($actual -ne $expected -or ($expected -and [Math]::Abs($fraction-$expectedFraction) -gt .0001)) { throw $label }
    $script:checks++
}
Sweep (V 0 0 0) (V 10 0 0) (V 5 0 0) 1 $true .4 'direct hit'
Sweep (V 0 0 0) (V 1000 0 0) (V 500 0 0) 1 $true .499 'fast shot cannot tunnel'
Sweep (V 0 0 0) (V 10 0 0) (V 5 3 0) 1 $false 0 'miss'
Sweep (V 0 0 0) (V 10 0 0) (V 5 1 0) 1 $true .5 'tangent'
Sweep (V 0 0 0) (V 10 0 0) (V -5 0 0) 1 $false 0 'behind shot'
Sweep (V 0 0 0) (V 10 0 0) (V 0 0 0) 1 $true 0 'starts inside'
Sweep (V 0 0 0) (V 0 0 0) (V 5 0 0) 1 $false 0 'stationary miss'
Sweep (V 0 0 0) (V 0 0 0) (V 0 0 0) 1 $true 0 'stationary overlap'
Sweep (V 0 0 0) (V 3 0 0) (V 5 0 0) 1 $false 0 'wall-shortened segment'
Sweep (V 0 0 0) (V 10 0 0) (V 5 2 0) 2.5 $true .35 'wide blade side hit'
# Current table: include all non-lock-on attacks, exclude support and lock-on spells.
$allowed = @(3,4,7,8,9,10,13,14)
$table = Get-Content -Raw Assets/Resources/MagicStatTable.asset
$entries = [regex]::Matches($table, '(?ms)^  - magic: (\d+)\r?\n(.*?)(?=^  - magic: |\z)')
if ($entries.Count -lt 17) { throw 'Could not read current magic table' }
foreach ($entry in $entries) {
    $id = [int]$entry.Groups[1].Value
    $body = $entry.Groups[2].Value
    $stats = [MagicStatEntry]::new()
    $stats.magic = [MagicType]$id
    $stats.effect = [MagicEffectKind][int][regex]::Match($body, '(?m)^    effect: (\d+)').Groups[1].Value
    $stats.requiresTarget = [regex]::Match($body, '(?m)^    requiresTarget: (\d+)').Groups[1].Value -eq '1'
    if ([BallChecks]::CanPushBall($stats) -ne ($allowed -contains $id)) { throw "Unexpected eligibility: $($stats.magic)" }
    $script:checks++
}
"Lobby ball checks passed: $checks (sweep + current magic table); Unity/network playtest still required."
