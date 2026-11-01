using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

// Separate runner and input model; never starts or modifies the combat session.
public sealed class OpenSpaceSession : MonoBehaviour, INetworkRunnerCallbacks
{
    public const string ScenePath="Assets/Scenes/openspace.unity", MainPath="Assets/Scenes/Main.unity";
    const string LobbyName="WitchFlight-OpenSpace-v1";
    public static OpenSpaceSession Instance { get; private set; }
    [SerializeField] private OpenSpaceSettings settings;
    public OpenSpaceSettings Settings => settings;
    public NetworkRunner Runner { get; private set; }
    public bool Busy { get; private set; }
    public string Status { get; private set; }="";
    public bool Connected => Runner!=null && Runner.IsRunning && OpenSpaceAvatar.Local!=null;
    private string previousSession;
    private bool shuttingDown, receivedList;
    private List<SessionInfo> available=new();
    private readonly Dictionary<PlayerRef, NetworkObject> players=new();
    private void Awake()
    {
        if(Instance!=null && Instance!=this) {Destroy(gameObject);return;}
        Instance=this; DontDestroyOnLoad(gameObject);
    }
    public async void Enter() => await Connect(false);
    public async void ChangeSession() => await Connect(true);
    public async Task Connect(bool change)
    {
        if(Busy) return;
        var db=DatabaseManager.Instance;
        if(db==null || !db.HasLoadedProfile || !db.IsDataConfigReady) {Status="먼저 로그인해주세요.";return;}
        if(NetworkGameManager.Instance!=null && NetworkGameManager.Instance.IsMatching) {Status="현재 매칭을 종료한 뒤 광장에 입장해주세요.";return;}
        if(db.IsOpenSpaceWriteInProgress) {Status="결과 저장 중입니다. 완료 후 이동해주세요.";return;}
        if(settings==null || settings.runnerPrefab==null || settings.avatarPrefab==null) {Status="광장 프리팹 연결을 확인해주세요.";return;}
        Busy=true;
        try
        {
            if(change && Runner!=null && Runner.IsRunning) previousSession=Runner.SessionInfo.Name;
            else if(!change) previousSession=null;
            await StopRunner();
            if(db.HasOpenSpacePending) await db.CommitOpenSpaceAsync();
            int sceneIndex=SceneUtility.GetBuildIndexByScenePath(ScenePath);
            if(sceneIndex<0) throw new InvalidOperationException("openspace 씬을 빌드 목록에 추가해주세요.");
            for(int attempt=0;attempt<4;attempt++)
            {
                Status="광장 입장 중…";
                Runner=Instantiate(settings.runnerPrefab,transform);
                Runner.name="OpenSpace Network Runner"; Runner.ProvideInput=true; Runner.AddCallbacks(this);
                receivedList=false; available.Clear();
                var lobby=await Runner.JoinSessionLobby(SessionLobby.Custom,LobbyName);
                if(!lobby.Ok) throw new InvalidOperationException(lobby.ShutdownReason.ToString());
                float until=Time.realtimeSinceStartup+2;
                while(!receivedList && Time.realtimeSinceStartup<until) await Task.Yield();
                string chosen=null;
                foreach(var room in available)
                    if(room.IsOpen && room.IsVisible && room.PlayerCount<OpenSpaceSettings.Capacity && room.Name!=previousSession)
                    {chosen=room.Name;break;}
                bool creating=chosen==null;
                var result=await Runner.StartGame(new StartGameArgs {
                    GameMode=creating?GameMode.Host:GameMode.Client,
                    SessionName=chosen??("openspace-"+Guid.NewGuid().ToString("N")),
                    CustomLobbyName=LobbyName, PlayerCount=OpenSpaceSettings.Capacity,
                    IsOpen=true, IsVisible=true, EnableClientSessionCreation=creating,
                    Scene=SceneRef.FromIndex(sceneIndex), SceneManager=Runner.GetComponent<NetworkSceneManagerDefault>()
                });
                if(result.Ok) {Status="광장에 오신 것을 환영해요.";return;}
                await StopRunner();
                if(attempt==3) throw new InvalidOperationException(result.ShutdownReason.ToString());
            }
        }
        catch(Exception e) {Status="광장 연결 실패. 다시 시도하거나 로비로 돌아가주세요. "+e.Message; await StopRunner();}
        finally {Busy=false;}
    }
    private async Task StopRunner()
    {
        shuttingDown=true;
        var old=Runner; Runner=null; players.Clear();
        if(old!=null) {await old.Shutdown(); if(old!=null)Destroy(old.gameObject);}
        shuttingDown=false;
    }
    public async void ReturnToLobby()
    {
        if(Busy || (DatabaseManager.Instance!=null && DatabaseManager.Instance.IsOpenSpaceWriteInProgress)) return;
        Busy=true;
        try
        {
            await StopRunner(); Time.timeScale=1; Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
            if(SceneManager.GetActiveScene().path!=MainPath)
            {
                var operation=SceneManager.LoadSceneAsync(MainPath); while(!operation.isDone) await Task.Yield();
            }
        }
        finally {Busy=false;Status="";}
    }
    private void SpawnParticipant(PlayerRef participant)
    {
        if(Runner==null || !Runner.IsServer || OpenSpaceWorld.Instance==null || players.ContainsKey(participant)) return;
        Transform spawn=OpenSpaceWorld.Instance.Spawns[Mathf.Abs(participant.RawEncoded)%OpenSpaceWorld.Instance.Spawns.Length];
        var avatar=Runner.Spawn(settings.avatarPrefab,spawn.position,spawn.rotation,participant);
        players[participant]=avatar; Runner.SetPlayerObject(participant,avatar);
    }
    public void OnPlayerJoined(NetworkRunner runner,PlayerRef player) {if(runner==Runner)SpawnParticipant(player);}
    public void OnSceneLoadDone(NetworkRunner runner) {if(runner==Runner && runner.IsServer)foreach(var player in runner.ActivePlayers)SpawnParticipant(player);}
    public void OnPlayerLeft(NetworkRunner runner,PlayerRef player)
    {
        if(runner!=Runner || !runner.IsServer)return;
        OpenSpaceHub.Instance?.Release(player);
        if(players.TryGetValue(player,out var obj)) {players.Remove(player);if(obj!=null)runner.Despawn(obj);}
    }
    public void OnInput(NetworkRunner runner, NetworkInput input)
    { if(runner==Runner) input.Set(OpenSpaceWorld.Instance!=null?OpenSpaceWorld.Instance.Input:default(OpenSpaceInput)); }
    public void OnShutdown(NetworkRunner runner,ShutdownReason reason)
    {
        if(shuttingDown || runner!=Runner)return;
        Runner=null; players.Clear();
        Status="광장 연결이 종료되었습니다. 확정된 수집품은 유지됩니다. 다시 입장하거나 로비로 돌아가주세요. ("+reason+")";
    }
    public void OnSessionListUpdated(NetworkRunner runner,List<SessionInfo> sessions) {if(runner==Runner){available=sessions;receivedList=true;}}
    public void OnConnectRequest(NetworkRunner runner,NetworkRunnerCallbackArgs.ConnectRequest request,byte[] token)
    {int count=0;foreach(var p in runner.ActivePlayers)count++;if(count>=OpenSpaceSettings.Capacity)request.Refuse();else request.Accept();}
    public void OnInputMissing(NetworkRunner r,PlayerRef p,NetworkInput i){}
    public void OnConnectedToServer(NetworkRunner r){}
    public void OnDisconnectedFromServer(NetworkRunner r,NetDisconnectReason reason){}
    public void OnConnectFailed(NetworkRunner r,NetAddress address,NetConnectFailedReason reason){}
    public void OnUserSimulationMessage(NetworkRunner r,SimulationMessagePtr message){}
    public void OnCustomAuthenticationResponse(NetworkRunner r,Dictionary<string,object> data){}
    public void OnHostMigration(NetworkRunner r,HostMigrationToken token){}
    public void OnSceneLoadStart(NetworkRunner r){}
    public void OnObjectExitAOI(NetworkRunner r,NetworkObject o,PlayerRef p){}
    public void OnObjectEnterAOI(NetworkRunner r,NetworkObject o,PlayerRef p){}
    public void OnReliableDataReceived(NetworkRunner r,PlayerRef p,ReliableKey k,ArraySegment<byte> data){}
    public void OnReliableDataProgress(NetworkRunner r,PlayerRef p,ReliableKey k,float progress){}
}
public struct OpenSpaceInput : INetworkInput {public Vector3 direction; public NetworkBool stopped;}
