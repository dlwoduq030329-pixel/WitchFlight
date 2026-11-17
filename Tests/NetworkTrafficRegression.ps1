$ErrorActionPreference = 'Stop'
function Extract-Method($source, $signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "Missing method: $signature" }
    $open = $source.IndexOf('{', $start)
    $depth = 1; $end = $open + 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        elseif ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($start, $end - $start)
}
$player = Get-Content "$PSScriptRoot/../Assets/Script/Player.cs" -Raw
$appearance = Get-Content "$PSScriptRoot/../Assets/Script/PlayerRobby.Appearance.cs" -Raw
$cast = Extract-Method $player 'private void PresentCast('
$flush = Extract-Method $appearance 'private void FlushLobbyAppearance('
$queue = 'private void QueueLobbyAppearance() => appearanceDirty = true;'
if (!$appearance.Contains($queue)) { throw 'Appearance queue implementation changed' }
$code = @'
using System;
public enum MagicType { Fire }
public struct Vector3 {
 public float x,y,z;
 public Vector3(float a,float b,float c){x=a;y=b;z=c;}
 public override bool Equals(object obj) => obj is Vector3 v && x==v.x && y==v.y && z==v.z;
 public override int GetHashCode()=>0;
}
public class NetworkTrafficChecks {
 int point,beam,sends,snapshot,last;
 Vector3 savedOrigin,savedEnd;
 bool appearanceDirty;
 void RPC_PresentCastAt(MagicType m,Vector3 o){point++;savedOrigin=o;savedEnd=o;}
 void RPC_PresentCastBeam(MagicType m,Vector3 o,Vector3 e){beam++;savedOrigin=o;savedEnd=e;}
 void SendLobbyAppearance(){sends++;last=snapshot;}
 static void Check(bool ok){if(!ok)throw new Exception("Network traffic regression");}
 public static void Run(){
  var c=new NetworkTrafficChecks();var p=new Vector3(1,2,3);
  c.PresentCast(MagicType.Fire,p,p);
  Check(c.point==1&&c.beam==0&&c.savedEnd.Equals(p));
  var q=new Vector3(1,2,3.000001f);c.PresentCast(MagicType.Fire,p,q);
  Check(c.point==1&&c.beam==1&&c.savedOrigin.Equals(p)&&c.savedEnd.Equals(q));
  c.FlushLobbyAppearance();Check(c.sends==0);
  for(int i=1;i<=20;i++){c.snapshot=i;c.QueueLobbyAppearance();}
  c.FlushLobbyAppearance();Check(c.sends==1&&c.last==20);
  c.FlushLobbyAppearance();Check(c.sends==1);
  c.snapshot=21;c.QueueLobbyAppearance();c.FlushLobbyAppearance();Check(c.sends==2&&c.last==21);
 }
'@
Add-Type -TypeDefinition ($code + $cast + $queue + $flush + '}')
[NetworkTrafficChecks]::Run()
Write-Output 'Network RPC payload routing and appearance coalescing checks passed: 6'
