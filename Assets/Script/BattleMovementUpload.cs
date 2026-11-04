using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BackEnd;
using UnityEngine;

// Only match hosts enqueue. Authentication is the existing user's SDK session, never an admin key.
public sealed class BattleMovementUpload : MonoBehaviour
{
    public const string LogType = "battle_movement_v1";
    public static string CurrentOwner => Backend.IsInitialized && Backend.IsLogin ? Backend.UserInDate : null;
    private float nextAttempt;
    private int failures, requestVersion;
    private bool busy;
    private static BattleMovementUpload instance;
    private static string Root => Path.Combine(Application.persistentDataPath,"BattleLogOutbox");

    public static void Activate()
    {
        if(instance!=null) return;
        var go=new GameObject("Battle movement upload");
        DontDestroyOnLoad(go);
        instance=go.AddComponent<BattleMovementUpload>();
    }
    private static string OwnerDirectory(string owner)
    {
        using var sha=SHA256.Create();
        string key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(owner))).Replace("-","");
        return Path.Combine(Root,key);
    }
    public static bool Enqueue(BattleMovementLog packet,string owner)
    {
        try
        {
            string path=Path.Combine(OwnerDirectory(owner),packet.matchId+"_"+packet.partIndex.ToString("D6")+".json");
            BattleMovementLogFiles.Write(path,JsonUtility.ToJson(packet));
            return true;
        }
        catch(Exception e) { Debug.LogWarning("동선 업로드 대기 파일 저장 실패: "+e.Message); return false; }
    }
    public static string Encode(string json)
    {
        using var output=new MemoryStream();
        using(var gzip=new GZipStream(output,System.IO.Compression.CompressionLevel.Fastest,true))
        {
            byte[] bytes=Encoding.UTF8.GetBytes(json);
            gzip.Write(bytes,0,bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }
    public static string Decode(string value)
    {
        if(value.Length>2*1024*1024) throw new InvalidDataException("동선 패킷이 너무 큽니다.");
        using var input=new MemoryStream(Convert.FromBase64String(value));
        using var gzip=new GZipStream(input,CompressionMode.Decompress);
        using var output=new MemoryStream();
        byte[] buffer=new byte[8192];
        int read;
        while((read=gzip.Read(buffer,0,buffer.Length))>0)
        {
            if(output.Length+read>16*1024*1024) throw new InvalidDataException("압축 해제 제한 초과");
            output.Write(buffer,0,read);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }
    private void Update()
    {
        if(Time.unscaledTime<nextAttempt) return;
        if(busy) { busy=false; requestVersion++; Retry("응답 시간 초과"); return; }
        nextAttempt=Time.unscaledTime+10f;
        string owner=CurrentOwner;
        if(string.IsNullOrEmpty(owner)) return;
        string path=null;
        try
        {
            string folder=OwnerDirectory(owner);
            if(!Directory.Exists(folder)) return;
            var files=Directory.GetFiles(folder,"*.json");
            if(files.Length==0) return;
            Array.Sort(files,StringComparer.Ordinal);
            path=files[0];
            string json=File.ReadAllText(path);
            var packet=BattleMovementLogFiles.Parse(json);
            string payload=Encode(json);
            if(payload.Length>900000) throw new InvalidDataException("서버 패킷 크기 초과");
            var param=new Param();
            param.Add("matchId",packet.matchId);
            param.Add("partIndex",packet.partIndex);
            param.Add("startedUtc",packet.startedUtc);
            param.Add("mode",packet.mode);
            param.Add("encoding","gzip-base64");
            param.Add("payload",payload);
            busy=true; nextAttempt=Time.unscaledTime+45f;
            int version=++requestVersion;
            string sentPath=path;
            // SDK invocation remains on Unity's main thread.
            Backend.GameLog.InsertLogV2(LogType,param,response=> {
                if(this==null || version!=requestVersion) return;
                busy=false;
                if(response.IsSuccess())
                {
                    try { File.Delete(sentPath); } catch(Exception e) { Debug.LogWarning(e.Message); }
                    failures=0; nextAttempt=Time.unscaledTime+1f;
                }
                else Retry(response.GetMessage());
            });
        }
        catch(Exception e) { busy=false; Retry(e.Message); }
    }
    private void Retry(string reason)
    {
        failures++;
        nextAttempt=Time.unscaledTime+Mathf.Min(300f,10f*Mathf.Pow(2,Mathf.Min(5,failures-1)));
        if(failures==1 || failures%10==0)
            Debug.LogWarning("동선 서버 업로드 대기 중 (파일 보존, 자동 재시도): "+reason);
    }
}
