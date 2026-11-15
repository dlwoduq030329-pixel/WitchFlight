# Read-only gameplay diagnosis in a separate, minimal Unity project.
# Copies production math/movement method text; never imports or edits the Battle scene.
param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.0.77f1/Editor/Unity.exe',
    [switch]$AllMagic,
    [switch]$ValidateOnly,
    [switch]$InteractionsOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'UnityHomingProbe'
if ($project.StartsWith([IO.Path]::GetFullPath($root), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use an output directory outside the game repository.'
}
function Read-Project([string]$path) { [IO.File]::ReadAllText((Join-Path $root $path)) }
function Extract-Method([string]$source, [string]$name) {
    $match = [regex]::Match($source, '(?ms)^    (?:public|private|internal) (?:static )?[\w<>]+ ' + $name + '\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$match.Success) { throw "Cannot extract production method $name" }
    $match.Value
}
$projectile = Read-Project 'Assets/Script/MagicProjectile.cs'
$player = Read-Project 'Assets/Script/Player.cs'
$template = Read-Project 'Tests/HomingManeuverDiagnostics.cs.txt'
$flight = foreach ($name in 'DrainFlightSpeed','FlightTurnSpeed','FlightDistance','FlightLifetime','HomingAimDelta','ResolveHomingDirection','TrySweptTargetContact') {
    Extract-Method $projectile $name
}
$exhaust = [regex]::Match($projectile, '(?ms)^    public static bool UsesFlightExhaustion\([^;]+;').Value
if (!$exhaust) { throw 'Missing UsesFlightExhaustion' }
$motion = foreach ($name in 'ProcessCruiseInput','BeginDashWindup','TickDashWindup','StartCruiseBoost','UpdateSpeed','PlayerTurn','IsFiniteDirection','NormalizePitch') {
    Extract-Method $player $name
}
$template = $template.Replace('__FLIGHT__', ($flight -join "`n") + "`n" + $exhaust)
$template = $template.Replace('__MOTION__', ($motion -join "`n"))
$template = $template.Replace('__SEGMENT__', (Extract-Method (Read-Project 'Assets/Script/BattleFlag.cs') 'SegmentDistanceSquared'))
$template = $template.Replace('__TURN_SETTINGS__', (Extract-Method (Read-Project 'Assets/Script/MagicStatTable.cs') 'ApplyHomingTurnSettings'))
if ($AllMagic -or $ValidateOnly -or $InteractionsOnly) {
    $all = Read-Project 'Tests/AllMagicManeuverDiagnostics.cs.txt'
    $all = $all.Replace('__SUMMON_BOLT__', (Extract-Method (Read-Project 'Assets/Script/MagicProjectile.Constructs.cs') 'SummonBoltStats'))
    $all = $all.Replace('__FOCUS_SHOT__', (Extract-Method (Read-Project 'Assets/Script/PerkEffects.cs') 'FocusBladeShot'))
    $all = $all.Replace('__RAIN_POSITION__', (Extract-Method (Read-Project 'Assets/Script/Player.BladeRain.cs') 'RainCirclePosition'))
    $template += "`n" + $all
}
foreach ($dir in 'Assets/Editor','Packages','ProjectSettings','Inputs') {
    [IO.Directory]::CreateDirectory((Join-Path $project $dir)) | Out-Null
}
[IO.File]::WriteAllText((Join-Path $project 'Assets/Editor/HomingDiagnostics.cs'), $template)
[IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'), '{"dependencies":{"com.unity.modules.physics":"1.0.0"}}')
[IO.File]::WriteAllText((Join-Path $project 'ProjectSettings/ProjectVersion.txt'), (Read-Project 'ProjectSettings/ProjectVersion.txt'))
$inputs = @('Assets/Resources/MagicStatTable.asset','Assets/Resources/EquipmentStatTable.asset','Assets/Resources/FlightCombatTable.asset','Assets/Ch/ChPrefab.prefab','Assets/Script/Player.cs','Assets/Script/MagicProjectile.cs','Assets/Script/BattleFlag.cs','Assets/Script/MagicStatTable.cs','Assets/Script/MagicProjectile.Constructs.cs','Assets/Script/Player.BladeRain.cs','Assets/Script/Player.MagicStorm.cs','Assets/Script/Player.Blade.cs','Assets/Script/PerkEffects.cs','Assets/Script/PlayerBroomAnimation.cs')
$hashes = foreach ($path in $inputs) {
    Copy-Item -LiteralPath (Join-Path $root $path) -Destination (Join-Path $project ('Inputs/' + [IO.Path]::GetFileName($path)))
    [pscustomobject]@{ path=$path; sha256=(Get-FileHash -LiteralPath (Join-Path $root $path)).Hash }
}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $project 'input-hashes.json') -Encoding utf8
Write-Output "Unity diagnostic project: $project"
$entryPoint = if ($ValidateOnly) {'HomingDiagnostics.Entry.ValidateOnly'} elseif ($InteractionsOnly) {'HomingDiagnostics.Entry.RunInteractions'} elseif ($AllMagic) {'HomingDiagnostics.Entry.RunAll'} else {'HomingDiagnostics.Entry.Run'}
$unityArguments = @('-batchmode','-nographics','-projectPath',('"' + $project + '"'),'-executeMethod',$entryPoint,'-logFile',('"' + (Join-Path $project 'unity.log') + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $unityArguments -WindowStyle Hidden -PassThru -Wait
$exitCode = $process.ExitCode
foreach ($record in $hashes) {
    if ((Get-FileHash -LiteralPath (Join-Path $root $record.path)).Hash -ne $record.sha256) { throw "Gameplay input changed during run: $($record.path)" }
}
if ($exitCode -ne 0) { throw "Unity diagnostic failed ($exitCode); inspect $project/unity.log" }
Get-Content -LiteralPath (Join-Path $project 'summary.txt')
