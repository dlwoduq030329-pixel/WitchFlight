# Exercise production input normalization without Unity; network/UI need two-client testing.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content -Raw Assets/Script/PlayerData.LobbyChat.cs
$method = [regex]::Match($source,'(?ms)^    internal static string CleanLobbyChat\(.*?^    \}')
if (-not $method.Success) { throw 'Missing production chat normalizer' }
$code = 'using System; using System.Text; public static class LobbyChatChecks { public const int LobbyChatCharacterLimit=100; ' + $method.Value.Replace('internal static','public static') + ' }'
Add-Type -TypeDefinition $code
function Check($inputText, $expected, $label) {
    if ([LobbyChatChecks]::CleanLobbyChat($inputText) -cne $expected) { throw $label }
}
Check $null '' 'null input'
Check '   ' '' 'empty message'
Check '  안녕하세요  ' '안녕하세요' 'Korean and trimming'
Check "첫 줄`n둘째 줄`t끝" '첫 줄 둘째 줄 끝' 'single-line formatting'
Check ('a'+[char]0+'b') 'ab' 'control filtering'
Check ('가'*101) ('가'*100) 'message size bound'
$emoji = [char]::ConvertFromUtf32(0x1F63A)
Check ('a'*98+$emoji) ('a'*98+$emoji) 'complete emoji at limit'
Check ('a'*99+$emoji) ('a'*99) 'do not split emoji at limit'
Check ([string][char]0xD83D+'x'+[char]0xDE3A) 'x' 'reject unpaired surrogates'
Check '<color=red>hello</color>' '<color=red>hello</color>' 'preserve literal text; renderer disables richText'
'Lobby chat text checks passed: 10'
