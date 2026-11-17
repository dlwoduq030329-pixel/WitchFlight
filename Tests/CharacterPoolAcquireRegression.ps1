$ErrorActionPreference = 'Stop'
$source = Get-Content "$PSScriptRoot/../Assets/Script/MagicNetworkObjectPool.cs" -Raw
$start = $source.IndexOf('    public override NetworkObjectAcquireResult AcquirePrefabInstance(')
if ($start -lt 0) { throw 'Acquire override missing' }
$open = $source.IndexOf('{', $start)
$depth = 1; $end = $open + 1
while ($depth -gt 0) {
    if ($source[$end] -eq '{') { $depth++ }
    elseif ($source[$end] -eq '}') { $depth-- }
    $end++
}
$method = $source.Substring($start, $end-$start)
$stubs = @'
using System;
public struct PlayerRef {
 public int Value;
 public static PlayerRef None => default;
}
public class NetworkRunner {}
public class NetworkObject {}
public class Meta { public PlayerRef InputAuthority; }
public struct NetworkPrefabAcquireContext { public Meta Meta; }
public enum NetworkObjectAcquireResult { Success, Retry }
public class Provider {
 public bool Throw, Retry;
 public Action Inspect;
 public virtual NetworkObjectAcquireResult AcquirePrefabInstance(NetworkRunner runner, in NetworkPrefabAcquireContext context, out NetworkObject instance) {
  Inspect?.Invoke();
  if (Throw) throw new InvalidOperationException("simulated provider error");
  instance=Retry?null:new NetworkObject();
  return Retry?NetworkObjectAcquireResult.Retry:NetworkObjectAcquireResult.Success;
 }
}
public class Pool : Provider {
 private PlayerRef acquiringOwner;
 public void Check(int expected){if(acquiringOwner.Value!=expected)throw new Exception("Wrong owner scope");}
 public static void Run(){
  var pool=new Pool(); var runner=new NetworkRunner();
  pool.acquiringOwner=new PlayerRef{Value=9};
  pool.Inspect=()=>pool.Check(0);
  var local=new NetworkPrefabAcquireContext();
  if(pool.AcquirePrefabInstance(runner,local,out var instance)!=NetworkObjectAcquireResult.Success||instance==null)throw new Exception("Local spawn failed");
  pool.Check(9);
  pool.Inspect=()=>pool.Check(3);
  var remote=new NetworkPrefabAcquireContext{Meta=new Meta{InputAuthority=new PlayerRef{Value=3}}};
  pool.AcquirePrefabInstance(runner,remote,out instance); pool.Check(9);
  pool.Retry=true;
  if(pool.AcquirePrefabInstance(runner,remote,out instance)!=NetworkObjectAcquireResult.Retry||instance!=null)throw new Exception("Retry changed");
  pool.Check(9);
  pool.Throw=true;
  try {pool.AcquirePrefabInstance(runner,remote,out instance);throw new Exception("Exception swallowed");}
  catch(InvalidOperationException) {pool.Check(9);}
 }
'@
Add-Type -TypeDefinition ($stubs + $method + '}')
[Pool]::Run()
Write-Output 'Character pool acquisition passed: null meta, remote owner, retry, exception scope restoration.'
