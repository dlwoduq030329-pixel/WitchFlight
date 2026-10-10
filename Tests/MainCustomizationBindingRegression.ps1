# Read-only YAML checks for the ACTIVE Main UI, including scene prefab overrides.
# Does not log in, write player data, or run Unity Play Mode.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/MainSceneBindingAudit.ps1" -DefinitionsOnly
$script:checks = 0
function Check([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    $script:checks++
}
function Overrides([string]$block) {
    $result = @{}
    foreach ($m in [regex]::Matches($block, '(?m)^    - target: \{fileID: (\d+),[^\n]*\n      propertyPath: ([^\r\n]+)\r?\n      value: ([^\r\n]*)\r?\n      objectReference: ([^\r\n]+)')) {
        $key = $m.Groups[1].Value + ':' + $m.Groups[2].Value
        Check (!$result.ContainsKey($key)) "Duplicate override: $key"
        $result[$key] = @{value=$m.Groups[3].Value; reference=$m.Groups[4].Value}
    }
    return $result
}
function Resolved([string]$id, [hashtable]$map) {
    $block = $map[$id]
    Check (![string]::IsNullOrEmpty($block)) "Missing object $id"
    if ($block -match '^---[^\r\n]+stripped') {
        $m = [regex]::Match($block, 'm_CorrespondingSourceObject: \{fileID: (\d+), guid: (\w+)')
        return Resolved $m.Groups[1].Value (Asset $m.Groups[2].Value)
    }
    return $block
}
function Typed([string]$id, [hashtable]$map, [string]$guid) {
    $block = Resolved $id $map
    Check ($block -match ('m_Script:.*guid: '+$guid)) "Wrong component type at $id"
}

$customGuid = 'c65b85f415ffdf64ab4962c17ee06dc6'
$custom = Asset $customGuid
$customInstance = '2689755113089230609'
$overrides = Overrides $scene[$customInstance]
$controllerId = '7255314972244087111'
$controller = $custom[$controllerId]
$buttonGuid = '4e29b1a8efbd4b44bb3f3716e73f07ff'
$initializerId = '1900010003'
$initializer = $scene[$initializerId]
Check ($scene['8096963428161526588'] -match 'm_IsActive: 1') 'Main uses the active IntroAndRobby tree'
Check ($scene['1667728812'] -match 'm_IsActive: 0') 'Spare IntroAndRobby copy must stay inactive'
Check ((Label (Ref $scene[$customInstance] 'm_TransformParent') $scene) -eq 'IntroAndRobby/Robby/robbyCanvas') 'Customization must belong to the active lobby'
Check ($controller -match 'm_Enabled: 1') 'Magic customization controller enabled'
Check ($initializer -match 'm_Enabled: 1') 'Lobby initializer enabled'

# An empty asset event target is intentional for a scene-owned controller ONLY
# when the active scene supplies a valid override. Checking the prefab alone misses this.
foreach ($spec in @(@('SetHairColorPreset',9,'Hair/Panel/HairColors'), @('SetClothColorPreset',6,'Boddy/Panel/CostumColor'), @('SetEyeColorPreset',7,'Boddy/Panel/EyeColor'))) {
    $method = $spec[0]
    $indices = @()
    foreach ($entry in $custom.GetEnumerator()) {
        if ($entry.Value -notmatch ('m_MethodName: '+$method+'\r?\n')) { continue }
        Typed $entry.Key $custom $buttonGuid
        Check ((Label $entry.Key $custom).Contains($spec[2])) "$method belongs to its color group"
        $calls = [regex]::Matches($entry.Value, '(?s)- m_Target: \{fileID: (\d+)\}.*?(?=\r?\n      - m_Target:|\z)')
        for ($i=0; $i -lt $calls.Count; $i++) {
            $call = $calls[$i].Value
            if ($call -notmatch ('m_MethodName: '+$method+'\r?\n')) { continue }
            $property = 'm_OnClick.m_PersistentCalls.m_Calls.Array.data['+$i+']'
            $size = $overrides[$entry.Key+':m_OnClick.m_PersistentCalls.m_Calls.Array.size']
            Check ($null -eq $size -or [int]$size.value -gt $i) "$method must not be removed by an event-array override"
            $target = $overrides[$entry.Key+':'+$property+'.m_Target']
            Check ($null -ne $target -and $target.reference -eq "{fileID: $initializerId}") "$method target on active Main"
            Check ($call -match 'm_CallState: 2') "$method runtime listener enabled"
            Check ($call -match 'm_Mode: 3') "$method uses an integer preset index"
            # No silent instance override may change the intended method/argument/state.
            foreach ($field in @('m_MethodName','m_CallState','m_Mode','m_Arguments.m_IntArgument')) {
                Check (!$overrides.ContainsKey($entry.Key+':'+$property+'.'+$field)) "$method unexpected override of $field"
            }
            $indices += [int][regex]::Match($call,'m_IntArgument: (\d+)').Groups[1].Value
        }
    }
    Check ($indices.Count -eq $spec[1]) "$method button count"
    Check ((($indices | Sort-Object) -join ',') -eq ((0..($spec[1]-1)) -join ',')) "$method indices must be unique and consecutive"
}

foreach ($spec in @(@('bangsLengthSlider','FrontHairDistance','67db9e8f0e2ae9c40bc1e2b64352a6b4'), @('bangsDirectionSlider','FrontHairDIrection','67db9e8f0e2ae9c40bc1e2b64352a6b4'), @('sideHairLengthSlider','SideHairDistance','67db9e8f0e2ae9c40bc1e2b64352a6b4'), @('bangsLengthText','FrontHairDistance','f4688fdb7df04437aeb418b961361dc5'), @('bangsDirectionText','FrontHairDIrection','f4688fdb7df04437aeb418b961361dc5'), @('sideHairLengthText','SideHairDistance','f4688fdb7df04437aeb418b961361dc5'))) {
    $id = Ref $initializer $spec[0]
    Typed $id $scene $spec[2]
    Check ((Ref $scene[$id] 'm_PrefabInstance') -eq $customInstance) "$($spec[0]) belongs to active customization"
    Check ((Label $id $scene).Contains($spec[1])) "$($spec[0]) points to the correct control"
}
Typed (Ref $initializer 'appearance') $scene '9e4dd8ced7ea41b49e2b80bd715c1a10'
Check ((Label (Ref $initializer 'appearance') $scene).StartsWith('IntroAndRobby/Robby/ChPrefab ')) 'Preview appearance belongs to active character'
Check ((Ref $scene['1900010002'] 'lobbyInitializer') -eq $initializerId) 'Login success initializes this preview'
Check ((Ref $scene[(Ref $initializer 'customizationPanel')] 'm_PrefabInstance') -eq $customInstance) 'Equipment visibility follows active customization'
Check (Test-Path 'Assets/Resources/EyeTexturePresetTable.asset') 'Eye texture Resources fallback exists'

foreach ($spec in @(@('hatChoices','hat',3,'Scroll ViewHat'), @('broomChoices','broom',3,'Scroll ViewBroom'), @('magicChoices','magic',10,'Scroll ViewWand03'))) {
    $section = [regex]::Match($controller, '(?ms)^  '+$spec[0]+':\r?\n.*?(?=^  \w+:|\z)').Value
    $choices = [regex]::Matches($section, '(?s)  - '+$spec[1]+': (\d+)\r?\n    button: \{fileID: (\d+)\}')
    for ($i=0; $i -lt $spec[2]; $i++) {
        Check ($i -lt $choices.Count) "$($spec[0])[$i] exists"
        $id = $choices[$i].Groups[2].Value
        Typed $id $custom $buttonGuid
        Check ((Label $id $custom).Contains($spec[3])) "$($spec[0])[$i] correct list"
        Check (!$overrides.ContainsKey($controllerId+':'+$spec[0]+'.Array.data['+$i+'].button')) "$($spec[0])[$i] must not be erased by a scene override"
    }
}
foreach ($field in @('hatSlotButton','broomSlotButton','slot1Button','slot2Button','equipButton')) { Typed (Ref $controller $field) $custom $buttonGuid }
foreach ($field in @('hatSlotImage','broomSlotImage','slot1Image','slot2Image')) { Typed (Ref $controller $field) $custom 'fe87c0e1cc204ed48ad3b37840f39efc' }
foreach ($field in @('selectedItemName','selectedItemDescription')) { Typed (Ref $controller $field) $custom 'f4688fdb7df04437aeb418b961361dc5' }
# Both magic slots must open the shared, wired list, not the older duplicate lists.
foreach ($field in @('slot1Button','slot2Button')) {
    $button = $custom[(Ref $controller $field)]
    $found = $false
    foreach ($event in [regex]::Matches($button,'(?s)- m_Target: \{fileID: (\d+)\}.*?(?=\r?\n      - m_Target:|\z)')) {
        $path = Label $event.Groups[1].Value $custom
        if ($path -eq 'CustumUI/Weaon/Scroll ViewWand03') {
            $found = $true
            Check ($event.Value -match 'm_MethodName: SetActive' -and $event.Value -match 'm_BoolArgument: 1') "$field opens shared magic list"
        }
    }
    Check $found "$field has a shared-list event"
}
$store = Overrides $scene['218581912103391646']
foreach ($i in 0..2) {
    $target = $store['2980333732566018784:m_OnClick.m_PersistentCalls.m_Calls.Array.data['+$i+'].m_Target']
    $id = [regex]::Match($target.reference, 'fileID: (\d+)').Groups[1].Value
    Check ((Label $id $scene).StartsWith('IntroAndRobby/Robby/robbyCanvas/MenuUI')) 'Store exit targets the active menu'
    $null = Resolved $id $scene
}
# Detect dangling nonzero local IDs in both assets (Unity built-in external refs have a GUID).
foreach ($map in @($scene, $custom)) {
    foreach ($entry in $map.GetEnumerator()) {
        foreach ($m in [regex]::Matches($entry.Value, '\{fileID: ([1-9]\d*)\}')) {
            Check ($map.ContainsKey($m.Groups[1].Value)) "Dangling local reference $($entry.Key) -> $($m.Groups[1].Value)"
        }
    }
}
"Main customization binding checks passed: $checks"
