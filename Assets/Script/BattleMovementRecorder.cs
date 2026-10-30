using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BattleMovementRecorder : MonoBehaviour
{
    [SerializeField] private bool recordMovement = true;
    [SerializeField] private bool uploadToServer = true;
    [SerializeField, Min(10f)] private float uploadInterval = 30f;
    [SerializeField, Min(0.1f)] private float sampleInterval = 0.25f;
    [SerializeField, Min(5f)] private float saveInterval = 15f;
    [SerializeField, Min(1000)] private int maximumPoints = 200000;
    private sealed class TrackState
    {
        public BattleMovementTrack track;
        public NetworkId character;
        public int portal, round, segment;
        public bool continuous;
        public Vector3 previous;
        public int uploaded;
    }
    private readonly Dictionary<int, TrackState> tracks = new();
    private NetworkRunner runner;
    private BattleManager battle;
    private BattleMovementLog log;
    private float started, nextSample, nextSave;
    private float nextUpload;
    private int partIndex;
    private string uploadOwner;
    private int pointCount;
    private bool finished;
    private string path;
    private Task saveTask = Task.CompletedTask;
    public string CurrentLogPath => path;

    public void Begin(NetworkRunner host, BattleManager manager)
    {
        if (!recordMovement || log != null || host == null || !host.IsServer || manager == null || !manager.CollectMovementLogs) return;
        runner = host; battle = manager;
        started = Time.unscaledTime;
        nextSample = started; nextSave = started + saveInterval;
        nextUpload = started + Mathf.Max(10f, uploadInterval);
        uploadOwner = BattleMovementUpload.CurrentOwner;
        if(uploadToServer && !string.IsNullOrEmpty(uploadOwner)) BattleMovementUpload.Activate();
        else if(uploadToServer) Debug.LogWarning("뒤끝 로그인 세션이 없어 동선은 로컬 파일에만 기록합니다.");
        string id = Guid.NewGuid().ToString("N");
        log = new BattleMovementLog { matchId=id, startedUtc=DateTime.UtcNow.ToString("O"),
            scenePath=gameObject.scene.path, gameVersion=Application.version,
            mode=NetworkGameManager.Instance != null ? NetworkGameManager.Instance.CurrentMatchMode.ToString() : "Unknown",
            training=manager.IsTraining, sampleInterval=Mathf.Max(0.1f,sampleInterval), endReason="recording" };
        path = Path.Combine(BattleMovementLogFiles.DirectoryPath, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + id + ".json");
    }

    private void Update()
    {
        if (log == null || finished) return;
        if (runner == null || !runner.IsRunning || !runner.IsServer) { Finish("disconnected"); return; }
        if (battle == null) { Finish("scene_closed"); return; }
        log.duration = Time.unscaledTime - started;
        if (Time.unscaledTime >= nextSample)
        {
            nextSample = Time.unscaledTime + log.sampleInterval;
            Capture();
        }
        if (Time.unscaledTime >= nextSave)
        {
            nextSave = Time.unscaledTime + Mathf.Max(5f,saveInterval);
            Save(false);
        }
        if (Time.unscaledTime >= nextUpload)
        {
            nextUpload = Time.unscaledTime + Mathf.Max(10f, uploadInterval);
            Upload();
        }
    }

    private void Capture()
    {
        if (!battle.IsGameplayActive)
        {
            foreach (var state in tracks.Values) state.continuous = false;
            return;
        }
        foreach (PlayerData data in PlayerData.PerkPlayers)
        {
            if (data == null || data.Object == null || !data.Object.IsValid || data.Runner != runner) continue;
            int id = data.ParticipantRef.RawEncoded;
            if (!tracks.TryGetValue(id,out TrackState state))
            {
                state = new TrackState { track=new BattleMovementTrack { participant=id } };
                tracks.Add(id,state); log.players.Add(state.track);
            }
            state.track.nickname=data.playerName.ToString(); state.track.team=data.teamIndex; state.track.bot=data.IsMatchBot;
            Player player=Player.FindInRunner(runner,data.ParticipantRef);
            if (player == null || player.Object == null || !player.Object.IsValid) { state.continuous=false; continue; }
            Vector3 position=player.transform.position;
            bool alive=player.IsAlive;
            if (!alive && !state.continuous && state.track.points.Count > 0 &&
                !state.track.points[state.track.points.Count-1].alive) continue;
            if (!state.continuous || !state.character.Equals(player.Object.Id) || state.round!=battle.RoundNumber ||
                state.portal!=player.PortalExitSequence || (position-state.previous).sqrMagnitude>10000f)
                state.segment++;
            state.track.points.Add(new BattleMovementPoint { time=log.duration, position=position, round=battle.RoundNumber,
                segment=state.segment, alive=alive, flag=BattleFlag.Instance!=null && BattleFlag.Instance.IsCarriedBy(runner,data.ParticipantRef) });
            state.character=player.Object.Id; state.round=battle.RoundNumber; state.portal=player.PortalExitSequence;
            state.previous=position; state.continuous=alive;
            pointCount++;
            if (pointCount>=Mathf.Clamp(maximumPoints,1000,250000)) { Finish("point_limit"); return; }
        }
    }

    public void Finish(string reason)
    {
        if (log == null || finished) return;
        finished=true; log.endReason=reason; log.duration=Time.unscaledTime-started;
        Save(true);
        Upload();
    }
    private void Upload()
    {
        if (!uploadToServer || string.IsNullOrEmpty(uploadOwner)) return;
        var packet = new BattleMovementLog { matchId=log.matchId, startedUtc=log.startedUtc,
            scenePath=log.scenePath, gameVersion=log.gameVersion, mode=log.mode, training=log.training,
            sampleInterval=log.sampleInterval, duration=log.duration, endReason=log.endReason, partIndex=partIndex };
        // Bound each durable packet, even if saving was delayed or a long training session is running.
        bool more;
        do
        {
            packet.players.Clear();
            more=false;
            foreach (var state in tracks.Values)
            {
                int count=Mathf.Min(600, state.track.points.Count-state.uploaded);
                var source=state.track;
                var track=new BattleMovementTrack { participant=source.participant,team=source.team,
                    nickname=source.nickname,bot=source.bot,points=source.points.GetRange(state.uploaded,count) };
                packet.players.Add(track);
                if (state.uploaded+count<source.points.Count) more=true;
            }
            packet.partIndex=partIndex;
            if (!BattleMovementUpload.Enqueue(packet,uploadOwner)) return;
            foreach(var state in tracks.Values) state.uploaded+=Mathf.Min(600,state.track.points.Count-state.uploaded);
            partIndex++;
        } while(more);
    }
    private void Save(bool final)
    {
        if (!final && !saveTask.IsCompleted) return;
        string json=JsonUtility.ToJson(log);
        string destination=path;
        Task previous=saveTask;
        saveTask=Task.Run(async () => {
            try { await previous; BattleMovementLogFiles.Write(destination,json); }
            catch(Exception exception) { Debug.LogWarning("동선 로그 저장 실패: "+exception.Message); }
        });
    }
    private void OnDestroy() => Finish("scene_closed");
    private void OnApplicationQuit()
    {
        Finish("application_quit");
        // Bounded shutdown wait; ordinary gameplay never waits for disk I/O.
        try { saveTask.Wait(1000); } catch(Exception) { }
    }
}
