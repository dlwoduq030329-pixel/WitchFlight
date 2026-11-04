# Offline checks for the real nested-prefab navigation, not just controller mocks.
# The two magic slots must open the ONE list registered in MagicCustomizationUI.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$prefabText = Get-Content (Join-Path $repo 'Assets/UI/Prefab/IntroAndRobby.prefab') -Raw
$baseText = Get-Content (Join-Path $repo 'Assets/UI/Prefab/CustumUI.prefab') -Raw
$sceneText = Get-Content (Join-Path $repo 'Assets/Scenes/Main.unity') -Raw

function Read-Blocks([string]$text) {
    $result = @{}
    foreach ($match in [regex]::Matches($text, '(?ms)^--- !u!\d+ &(\d+)[^\n]*\n.*?(?=^--- !u!|\z)')) {
        if ($result.ContainsKey($match.Groups[1].Value)) { throw 'Duplicate Unity fileID' }
        $result[$match.Groups[1].Value] = $match.Value
    }
    return $result
}

$prefab = Read-Blocks $prefabText
$base = Read-Blocks $baseText
$controller = @($prefab.Values | Where-Object { $_ -match 'm_Script: .*71d37c8c418946cda9b240efb8fd12ee' })
if ($controller.Count -ne 1) { throw 'Expected one customization controller in IntroAndRobby' }

function Ref([string]$block, [string]$field) {
    return [regex]::Match($block, "(?m)^\s*$field`: \{fileID: (\d+)").Groups[1].Value
}

function Source-Id([string]$id) {
    $source = [regex]::Match($prefab[$id], 'm_CorrespondingSourceObject: \{fileID: (\d+), guid: c65b85f415ffdf64ab4962c17ee06dc6')
    if (!$source.Success) { throw "Expected nested CustumUI reference: $id" }
    return $source.Groups[1].Value
}

function Apply-EventOverrides($events, [string]$text, [string]$id, [string]$guid, [string]$scope) {
    $pattern = '(?m)^    - target: \{fileID: ' + $id + ', guid: ' + $guid + ', type: 3\}\r?\n' +
        '      propertyPath: m_OnClick.m_PersistentCalls.m_Calls.Array.data\[(\d+)\]\.(\S+)\r?\n' +
        '      value: ([^\r\n]*)\r?\n      objectReference: ([^\r\n]*)'
    foreach ($override in [regex]::Matches($text, $pattern)) {
        $event = $events[[int]$override.Groups[1].Value]
        switch ($override.Groups[2].Value) {
            'm_Target' {
                $event.target = [regex]::Match($override.Groups[4].Value, 'fileID: (\d+)').Groups[1].Value
                $event.scope = $scope
            }
            'm_MethodName' { $event.method = $override.Groups[3].Value }
            'm_Arguments.m_BoolArgument' { $event.visible = $override.Groups[3].Value -eq '1' }
            'm_CallState' { $event.enabled = $override.Groups[3].Value -ne '0' }
        }
    }
}

function Click-Slot([string]$field, $active) {
    $id = Ref $controller[0] $field
    $source = Source-Id $id
    $events = @([regex]::Matches($base[$source], '(?s)- m_Target: \{fileID: (\d+)\}.*?(?=- m_Target:|\z)') | ForEach-Object {
        @{
            target = $_.Groups[1].Value
            scope = 'base'
            method = [regex]::Match($_.Value, 'm_MethodName: (\S+)').Groups[1].Value
            visible = [regex]::Match($_.Value, 'm_BoolArgument: (\d+)').Groups[1].Value -eq '1'
            enabled = [regex]::Match($_.Value, 'm_CallState: (\d+)').Groups[1].Value -ne '0'
        }
    })
    Apply-EventOverrides $events $prefabText $source 'c65b85f415ffdf64ab4962c17ee06dc6' 'prefab'
    Apply-EventOverrides $events $sceneText $id '32d42efa5e4f52d489a4ab902868a73e' 'scene'
    foreach ($event in $events) {
        if ($event.enabled -and $event.method -eq 'SetActive' -and $event.target -ne '0') {
            $active[$event.scope + ':' + $event.target] = $event.visible
        }
    }
}

$sharedList = '2666471580574565144'
$checks = 0
foreach ($previous in @('hatSlotButton', 'broomSlotButton')) {
    foreach ($slot in @('slot1Button', 'slot2Button')) {
        $active = @{}
        Click-Slot $previous $active
        Click-Slot $slot $active
        if ($active['prefab:' + $sharedList] -ne $true) {
            throw "$previous -> $slot opens an unbound legacy list instead of the registered magic buttons"
        }
        foreach ($legacy in @('base:6365427300138899317', 'prefab:1395618693815397367')) {
            if ($active[$legacy] -ne $false) { throw "$slot leaves legacy magic list visible: $legacy" }
        }
        foreach ($other in @('base:2331422267171201947', 'base:2466215851916215325')) {
            if ($active[$other] -ne $false) { throw "$slot leaves hat/broom list visible: $other" }
        }
        $checks++
    }
}

# Verify the list we open actually contains every registered spell button.
function In-SharedList([string]$id) {
    for ($depth = 0; $depth -lt 30; $depth++) {
        if ($id -eq $sharedList) { return $true }
        $block = $prefab[$id]
        if (!$block) { return $false }
        $owner = Ref $block 'm_GameObject'
        if ($owner) { $id = $owner; continue }
        $transform = [regex]::Match($block, 'component: \{fileID: (\d+)').Groups[1].Value
        $id = Ref $prefab[$transform] 'm_Father'
        if (!$id -or $id -eq '0') { return $false }
    }
    throw 'Unexpected prefab hierarchy loop'
}

$choices = [regex]::Match($controller[0], '(?s)  magicChoices:.*?(?=  magicTable:)').Value
$buttons = [regex]::Matches($choices, '    button: \{fileID: (\d+)\}')
if ($buttons.Count -ne 10) { throw 'Expected ten registered magic buttons' }
foreach ($button in $buttons) {
    if (!(In-SharedList $button.Groups[1].Value)) { throw "Registered button is outside the shared list: $($button.Value)" }
    $checks++
}
"Magic customization prefab checks passed: $checks"
