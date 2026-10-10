param([string]$Revision = '',[switch]$DefinitionsOnly)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Read-Asset([string]$path) {
    if ($Revision) { return (git show ($Revision + ':' + $path.Replace('\','/'))) -join "`n" }
    return Get-Content -LiteralPath $path -Raw
}
function Blocks([string]$text) {
    $map = @{}
    foreach ($m in [regex]::Matches($text, '(?ms)^--- !u!(\d+) &(\d+)[^\n]*\n.*?(?=^--- !u!|\z)')) {
        $map[$m.Groups[2].Value] = $m.Value
    }
    return $map
}
function Ref([string]$text,[string]$field) {
    return [regex]::Match($text, '(?m)^\s*'+[regex]::Escape($field)+': \{fileID: (\d+)').Groups[1].Value
}
$scene = Blocks (Read-Asset 'Assets/Scenes/Main.unity')
$assets = @{}
$paths = @{}
foreach ($meta in (rg --files Assets -g '*.prefab.meta')) {
    $guid = [regex]::Match((Get-Content -LiteralPath $meta -Raw), 'guid: (\w+)').Groups[1].Value
    $paths[$guid] = $meta.Substring(0,$meta.Length-5)
}
function Asset([string]$guid) {
    if (!$assets.ContainsKey($guid) -and $paths.ContainsKey($guid)) { $assets[$guid] = Blocks (Read-Asset $paths[$guid]) }
    return $assets[$guid]
}
function Label([string]$id, [hashtable]$map, [int]$depth=0) {
    if (!$id -or $id -eq '0') {return '(None)'}
    if ($depth -gt 35) {return '(depth limit)'}
    $block = $map[$id]
    if (!$block) {return "(MISSING:$id)"}
    if ($block -match '^---[^\r\n]+stripped') {
        $source = [regex]::Match($block,'m_CorrespondingSourceObject: \{fileID: (\d+), guid: (\w+)')
        $sourceMap = Asset $source.Groups[2].Value
        $instance = Ref $block 'm_PrefabInstance'
        $parent = Ref $map[$instance] 'm_TransformParent'
        $prefix = if ($parent -and $parent -ne '0') {(Label $parent $map ($depth+1)) + '/'} else {''}
        return $prefix + (Label $source.Groups[1].Value $sourceMap ($depth+1)) + " [instance=$instance]"
    }
    $go = Ref $block 'm_GameObject'
    if ($go -and $go -ne '0') {return Label $go $map ($depth+1)}
    $name = [regex]::Match($block,'(?m)^  m_Name: ([^\r\n]*)').Groups[1].Value
    if ($block -match '^--- !u!1 ') {
        $transform = [regex]::Match($block,'component: \{fileID: (\d+)').Groups[1].Value
        $parent = Ref $map[$transform] 'm_Father'
        if ($parent -and $parent -ne '0') {return (Label $parent $map ($depth+1)) + '/' + $name}
    }
    return $name
}
function Object-Key([string]$id, [hashtable]$map, [string]$rootName = '') {
    $block=$map[$id]
    if (!$block) {return $null}
    $go=Ref $block 'm_GameObject'
    if ($go -and $go -ne '0') {
        $owner=Object-Key $go $map $rootName
        $type=[regex]::Match($block,'^--- !u!(\d+)').Groups[1].Value
        $script=[regex]::Match($block,'m_Script:.*guid: (\w+)').Groups[1].Value
        return $owner+'|'+$type+':'+$script
    }
    if ($block -notmatch '^--- !u!1 ') {return $null}
    $name=[regex]::Match($block,'(?m)^  m_Name: ([^\r\n]*)').Groups[1].Value
    if ($rootName -and $name -eq $rootName) {return $name}
    $tr=[regex]::Match($block,'component: \{fileID: (\d+)').Groups[1].Value
    $parent=Ref $map[$tr] 'm_Father'
    if (!$parent -or $parent -eq '0') {return $name}
    $parentGo=Ref $map[$parent] 'm_GameObject'
    $rank=0
    foreach($m in [regex]::Matches(([regex]::Match($map[$parent],'(?s)m_Children:.*?m_Father:').Value),'fileID: (\d+)')) {
        if($m.Groups[1].Value -eq $tr) {break}
        $sibling=Ref $map[$m.Groups[1].Value] 'm_GameObject'
        if ($sibling -and [regex]::Match($map[$sibling],'(?m)^  m_Name: ([^\r\n]*)').Groups[1].Value -eq $name) {$rank++}
    }
    return (Object-Key $parentGo $map $rootName)+'/'+$name+'['+$rank+']'
}
if ($DefinitionsOnly) { return }
$customGuid = 'c65b85f415ffdf64ab4962c17ee06dc6'
$custom = Asset $customGuid
foreach ($entry in $scene.GetEnumerator()) {
    $block = $entry.Value
    if ($block -notmatch ('m_SourcePrefab:.*' + $customGuid)) {continue}
    $parent = Ref $block 'm_TransformParent'
    "CUSTOM INSTANCE $($entry.Key) parent=$(Label $parent $scene)"
    $overrides=@{}
    foreach($m in [regex]::Matches($block,'(?m)^    - target: \{fileID: (\d+), guid: (\w+), type: 3\}\r?\n      propertyPath: ([^\r\n]+)\r?\n      value: ([^\r\n]*)\r?\n      objectReference: ([^\r\n]+)')) {
        $overrides[$m.Groups[1].Value+':'+$m.Groups[3].Value] = @{value=$m.Groups[4].Value;reference=$m.Groups[5].Value}
    }
    foreach($c in $custom.GetEnumerator()) {
        if ($c.Value -match 'guid: 71d37c8c418946cda9b240efb8fd12ee') {
            "MAGIC CONTROLLER $($c.Key) $(Label $c.Key $custom)"
            $c.Value.Substring($c.Value.IndexOf('  hatChoices:'))
            foreach ($o in $overrides.GetEnumerator() | Where-Object {$_.Key.StartsWith($c.Key+':')}) {"OVERRIDE $($o.Key) = $($o.Value.value) $($o.Value.reference)"}
        }
        $events=[regex]::Matches($c.Value,'(?s)- m_Target: \{fileID: (\d+)\}.*?(?=\r?\n      - m_Target:|\z)')
        for($i=0;$i -lt $events.Count;$i++) {
            $e=$events[$i].Value
            if($e -notmatch 'm_TargetAssemblyTypeName: (LobbyPlayerInitializer|NetworkGameManager|LoginManager|LoginFlowController|DatabaseManager)') {continue}
            $method=[regex]::Match($e,'m_MethodName: (\S+)').Groups[1].Value
            $arg=[regex]::Match($e,'m_IntArgument: (\d+)').Groups[1].Value
            $target=Ref $e 'm_Target'
            if(!$target) {$target=$events[$i].Groups[1].Value}
            $override=$overrides[$c.Key+':m_OnClick.m_PersistentCalls.m_Calls.Array.data['+$i+'].m_Target']
            $where='asset'
            if($override) {$target=[regex]::Match($override.reference,'fileID: (\d+)').Groups[1].Value;$where='scene'}
            "EVENT $($c.Key)[$i] $method($arg) => $where $target : $(Label $c.Key $custom)"
        }
    }
}
foreach($id in @('1900010002','1900010003')) {
    "CONTROLLER $id"
    foreach($m in [regex]::Matches($scene[$id],'(?m)^  (\w+): \{fileID: (\d+)\}')) {
        "$($m.Groups[1].Value) => $($m.Groups[2].Value) $(Label $m.Groups[2].Value $scene)"
    }
}
