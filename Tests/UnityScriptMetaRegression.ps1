# Read-only guard for Unity import failures that a direct C# compile cannot detect.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

function Test-ScriptGuid([string]$line) {
    return $line -cmatch '^guid: [0-9a-fA-F]{32}$'
}
if (Test-ScriptGuid 'guid: d4e948c8f65f4513ae24bf403ec35e893') { throw '33-character GUID was accepted' }
if (Test-ScriptGuid 'guid: 685db540d89c4e83872857d52ba9434') { throw '31-character GUID was accepted' }
if (Test-ScriptGuid 'guid: z85db540d89c4e83872857d52ba9434b') { throw 'Non-hex GUID was accepted' }
if (!(Test-ScriptGuid 'guid: 685db540d89c4e83872857d52ba9434b')) { throw 'Valid GUID was rejected' }

$seen = @{}
$count = 0
foreach ($script in (rg --files Assets/Script -g '*.cs')) {
    $meta = "$script.meta"
    if (!(Test-Path -LiteralPath $meta)) { throw "Missing script metadata: $meta" }
    $lines = @(Get-Content -LiteralPath $meta | Where-Object { $_ -match '^guid:' })
    if ($lines.Count -ne 1 -or !(Test-ScriptGuid $lines[0])) { throw "Invalid script GUID: $meta" }
    $guid = $lines[0].Substring(6).ToLowerInvariant()
    if ($seen.ContainsKey($guid)) { throw "Duplicate GUID: $meta and $($seen[$guid])" }
    $seen[$guid] = $meta
    $count++
}
"Script metadata checks passed: $count scripts; valid, unique GUIDs and metadata present."
