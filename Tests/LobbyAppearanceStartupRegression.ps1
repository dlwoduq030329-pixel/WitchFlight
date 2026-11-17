$ErrorActionPreference = 'Stop'
$source = Get-Content "$PSScriptRoot/../Assets/Script/NetworkGameManager.Robby.cs" -Raw
$method = [regex]::Match($source, '(?s)public bool CanReceiveLobbyAppearance\(NetworkRunner runner\).*?;').Value
if (!$method) { throw 'Appearance admission method missing' }
$code = @'
using System;
public class NetworkRunner { public bool IsRunning=true; }
public class LobbyAdmissionTest {
 public NetworkRunner _runner=new NetworkRunner();
 public bool IsDeathMatch,IsTraining,IsRandomMatch,stopInProgress,cancelRequested,isGameStarting,IsBattleSceneLoaded;
 public static void Run(){
  var m=new LobbyAdmissionTest();
  if(!m.CanReceiveLobbyAppearance(m._runner))throw new Exception("Startup appearance rejected");
  if(m.CanReceiveLobbyAppearance(null)||m.CanReceiveLobbyAppearance(new NetworkRunner()))throw new Exception("Wrong runner accepted");
  foreach(var name in new[]{"IsDeathMatch","IsTraining","IsRandomMatch","stopInProgress","cancelRequested","isGameStarting","IsBattleSceneLoaded"}){
   var field=typeof(LobbyAdmissionTest).GetField(name); field.SetValue(m,true);
   if(m.CanReceiveLobbyAppearance(m._runner))throw new Exception("Invalid phase accepted: "+name);
   field.SetValue(m,false);
  }
  m._runner.IsRunning=false;
  if(m.CanReceiveLobbyAppearance(m._runner))throw new Exception("Stopped runner accepted");
 }
'@
Add-Type -TypeDefinition ($code+$method+'}')
[LobbyAdmissionTest]::Run()
Write-Output 'PASS: startup appearance accepted; wrong runner, stopped runner, other modes and shutdown rejected.'
