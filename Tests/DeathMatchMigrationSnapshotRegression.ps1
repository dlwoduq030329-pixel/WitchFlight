$ErrorActionPreference = 'Stop'
$source = Get-Content "$PSScriptRoot/../Assets/Script/NetworkGameManager.DeathMatchMigration.cs" -Raw
$start = $source.IndexOf('    private Task PushDeathMatchSnapshot(')
$end = $source.IndexOf('    private async void MigrateDeathMatch(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Snapshot methods missing' }
$methods = $source.Substring($start, $end - $start)
$stubs = @'
using System;
using System.Threading.Tasks;
public class NetworkRunner {
 public bool IsRunning=true, IsServer=true;
 public int Calls;
 public TaskCompletionSource<bool> Upload=new TaskCompletionSource<bool>();
 public Task<bool> PushHostMigrationSnapshot(){Calls++;return Upload.Task;}
}
public static class Debug {public static void Log(string message, object context) {} public static void LogWarning(string message, object context) {}}
public class MigrationTest {
 private bool migrationSnapshotBusy, IsMigratingDeathMatch=false;
 private Task migrationSnapshotTask=Task.CompletedTask; private NetworkRunner snapshotConfirmedRunner;
 public static async Task Run(){
  var m=new MigrationTest(); var r=new NetworkRunner();
  var periodic=m.PushDeathMatchSnapshot(r);
  var leaving=m.PushDeathMatchSnapshot(r);
  if(periodic.IsCompleted||leaving.IsCompleted||r.Calls!=1||!ReferenceEquals(periodic,leaving))
   throw new Exception("Host exit must wait for the existing snapshot upload");
  r.Upload.SetResult(true); await leaving;
  if(m.migrationSnapshotBusy)throw new Exception("Upload remained busy");
  r.Upload=new TaskCompletionSource<bool>();
  var failed=m.PushDeathMatchSnapshot(r);r.Upload.SetException(new Exception("offline"));await failed;
  if(m.migrationSnapshotBusy)throw new Exception("Failed upload blocked retry");
  r.Upload=new TaskCompletionSource<bool>();
  var retry=m.PushDeathMatchSnapshot(r);r.Upload.SetResult(true);await retry;
  if(r.Calls!=3)throw new Exception("Retry did not upload");
  r.IsServer=false;await m.PushDeathMatchSnapshot(r);
  await m.PushDeathMatchSnapshot(null);
  if(r.Calls!=3)throw new Exception("Non-host uploaded snapshot");
 }
'@
Add-Type -TypeDefinition ($stubs + $methods + '}')
[MigrationTest]::Run().GetAwaiter().GetResult()
Write-Output 'PASS: concurrent host exit waits for snapshot; failure releases busy state; retry; non-host guard.'

