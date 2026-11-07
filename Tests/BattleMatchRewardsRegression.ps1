# Run the production reward coordinator with an in-memory account/save API.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content -Raw Assets/Script/BattleMatchRewards.cs
$stub = @'
namespace UnityEngine {
 public static class Debug { public static void Log(object v) {} public static void LogWarning(object v) {} }
}
public sealed class DatabaseManager {
 public static DatabaseManager Instance;
 public static event System.Action DataConfigReadyChanged;
 public bool HasLoadedProfile = true, CanModifyAccountData = true;
 public string LoadedAccountId = "account-a";
 public int balance, additions;
 public bool TryAddMoney(int amount) {
  if (!CanModifyAccountData || amount <= 0 || balance > int.MaxValue - amount) return false;
  balance += amount; additions++; return true;
 }
 public static void ReadyChanged() { DataConfigReadyChanged?.Invoke(); }
}
'@
Add-Type -TypeDefinition ($source + $stub)
function Check($actual,$expected,$label) { if ($actual -ne $expected) { throw "$label : $actual != $expected" } }
Check ([BattleMatchRewards]::Calculate(1259,3)) 425 'floor damage reward'
Check ([BattleMatchRewards]::Calculate(9.99,0)) 0 'less than ten damage'
Check ([BattleMatchRewards]::Calculate(10,0)) 1 'exact boundary'
Check ([BattleMatchRewards]::Calculate(19.99,0)) 1 'fractional damage'
Check ([BattleMatchRewards]::Calculate(-100,-1)) 0 'negative values'
Check ([BattleMatchRewards]::Calculate([float]::NaN,2)) 200 'invalid damage'
Check ([BattleMatchRewards]::Calculate(0,[int]::MaxValue)) ([int]::MaxValue) 'integer overflow protection'
$db = [DatabaseManager]::new()
[DatabaseManager]::Instance = $db
[BattleMatchRewards]::Request('match-1',1259,3)
[BattleMatchRewards]::Request('match-1',1259,3)
Check $db.balance 425 'same result paid once'
Check $db.additions 1 'one accepted money change'
$db.CanModifyAccountData = $false
[BattleMatchRewards]::Request('match-2',100,1)
[BattleMatchRewards]::Request('match-2',100,1)
Check $db.balance 425 'wait for account readiness'
$db.CanModifyAccountData = $true
[DatabaseManager]::ReadyChanged()
Check $db.balance 535 'pay queued reward when ready'
[DatabaseManager]::ReadyChanged()
Check $db.additions 2 'ready notification cannot duplicate payment'
$db.CanModifyAccountData = $false
[BattleMatchRewards]::Request('old-account-pending',1000,5)
$db.LoadedAccountId = 'account-b'
$db.CanModifyAccountData = $true
[DatabaseManager]::ReadyChanged()
Check $db.balance 535 'old account reward must not transfer'
[BattleMatchRewards]::Request('match-1',100,0)
Check $db.balance 545 'different account has independent receipt'
'Battle reward checks passed: rounding, bounds, duplicate prevention, deferred readiness and account isolation.'
