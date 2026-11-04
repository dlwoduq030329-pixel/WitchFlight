using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BackEnd;
using UnityEngine;

// One host-owned snapshot per round; no per-frame player scans or collection in training.
public sealed class MagicPickTelemetry : MonoBehaviour
{
    public const string LogType="magic_pick_v1";
    private static MagicPickTelemetry instance;
    private float nextAttempt;
    private int version,failures;
    private bool busy;
    public static void Activate()
    {
        if(instance!=null) return;
        var root=new GameObject("Magic pick log upload");DontDestroyOnLoad(root);
        instance=root.AddComponent<MagicPickTelemetry>();
    }
    private static string Outbox(string owner)
    {
        using var sha=SHA256.Create();
        string key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(owner))).Replace("-","");
        return Path.Combine(Application.persistentDataPath,"MagicPickOutbox",key);
    }
    public static void Record(MagicPickLog log)
    {
        try
        {
            int stored=MagicPickLogStore.Store(log);
            if(stored<0) {Debug.LogWarning("마법 픽 기록 충돌: "+log.matchId+" R"+log.round);return;}
            string owner=BattleMovementUpload.CurrentOwner;
            if(string.IsNullOrEmpty(owner)) {Debug.LogWarning("뒤끝 로그인 세션이 없어 마법 픽은 로컬에만 기록합니다.");return;}
            BattleMovementLogFiles.Write(Path.Combine(Outbox(owner),MagicPickLogStore.FileName(log)),JsonUtility.ToJson(log));
            Activate();if(!instance.busy) instance.nextAttempt=Mathf.Min(instance.nextAttempt,Time.unscaledTime+1);
        }
        catch(Exception e) {Debug.LogWarning("마법 픽 기록 저장 실패: "+e.Message);}
    }
    private void Update()
    {
        if(Time.unscaledTime<nextAttempt) return;
        if(busy) {busy=false;version++;Retry("응답 시간 초과");return;}
        nextAttempt=Time.unscaledTime+10;
        string owner=BattleMovementUpload.CurrentOwner;
        if(string.IsNullOrEmpty(owner)) return;
        try
        {
            string folder=Outbox(owner);
            if(!Directory.Exists(folder)) return;
            var paths=Directory.GetFiles(folder,"*.json");if(paths.Length==0) return;
            Array.Sort(paths,StringComparer.Ordinal);string path=paths[0];
            var log=MagicPickLogStore.Parse(File.ReadAllText(path));
            var param=new Param();param.Add("matchId",log.matchId);param.Add("round",log.round);
            param.Add("startedUtc",log.startedUtc);param.Add("mode",log.mode);
            param.Add("encoding","json");param.Add("payload",JsonUtility.ToJson(log));
            busy=true;nextAttempt=Time.unscaledTime+45;int request=++version;
            Backend.GameLog.InsertLogV2(LogType,param,result=>{
                if(this==null || version!=request) return;
                busy=false;
                if(result.IsSuccess())
                {
                    try{File.Delete(path);}catch(Exception e){Debug.LogWarning(e.Message);}
                    failures=0;nextAttempt=Time.unscaledTime+1;
                }
                else Retry(result.GetMessage());
            });
        }
        catch(Exception e){busy=false;Retry(e.Message);}
    }
    private void Retry(string reason)
    {
        failures++;nextAttempt=Time.unscaledTime+Mathf.Min(300,10*Mathf.Pow(2,Mathf.Min(5,failures-1)));
        if(failures==1 || failures%10==0) Debug.LogWarning("마법 픽 서버 업로드 대기 (파일 보존, 자동 재시도): "+reason);
    }
}
