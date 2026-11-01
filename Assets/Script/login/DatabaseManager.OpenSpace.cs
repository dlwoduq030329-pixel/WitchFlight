using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BackEnd;
using UnityEngine;

public partial class DatabaseManager
{
    public bool IsOpenSpaceWriteInProgress { get; private set; }
    private string cachedOpenSpaceJson;
    private OpenSpaceProgress cachedOpenSpaceData;
    public OpenSpaceProgress OpenSpaceData
    {
        get
        {
            string json=currentPlayerSetting?.openSpaceProgressJson??"{}";
            if(cachedOpenSpaceData==null || cachedOpenSpaceJson!=json)
            {cachedOpenSpaceData=OpenSpaceProgress.Parse(json);cachedOpenSpaceJson=json;}
            return cachedOpenSpaceData;
        }
    }
    public string OpenSpacePublicId => PublicAccountKey(LoadedAccountId);
    public static string PublicAccountKey(string account)
    {
        if (string.IsNullOrEmpty(account)) return string.Empty;
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("OpenSpace:" + account))).Replace("-", "");
    }
    private string OpenSpaceJournal => Path.Combine(Application.persistentDataPath, "OpenSpacePending", PublicAccountKey(loadedUserInDate) + ".json");
    public bool HasOpenSpacePending => HasLoadedProfile && File.Exists(OpenSpaceJournal);
    public string OpenSpaceSaveMessage { get; private set; } = "";

    // A single account-row update commits inventory, collection, unlocks and optional currency.
    // This uses the existing client-owned account data model, not a server anti-cheat economy.
    public async Task<OpenSpaceReceipt> CommitOpenSpaceAsync(OpenSpaceRequest requested = null)
    {
        if (!CanAutoSave || IsOpenSpaceWriteInProgress || IsVoicePurchaseInProgress)
        { OpenSpaceSaveMessage = "계정 정보를 불러오거나 저장하는 중입니다."; return null; }
        float until = Time.realtimeSinceStartup + 15;
        while (autoSaveInFlight || (autoSaveDirty && !HasOpenSpacePending))
        {
            if (Time.realtimeSinceStartup > until) { OpenSpaceSaveMessage="기존 계정 저장을 기다리고 있습니다. 다시 시도해주세요."; return null; }
            await Task.Yield();
            if (this == null || !CanAutoSave) return null;
        }
        if (IsOpenSpaceWriteInProgress) return null;
        IsOpenSpaceWriteInProgress = true;
        string owner=loadedUserInDate, rowId=currentPlayerSettingInDate, journal=OpenSpaceJournal;
        int session=dataSessionVersion;
        bool Current() => this != null && session==dataSessionVersion && loadedUserInDate==owner && Backend.IsLogin && Backend.UserInDate==owner;
        try
        {
            OpenSpaceRequest request;
            if (File.Exists(journal))
            {
                request=JsonUtility.FromJson<OpenSpaceRequest>(File.ReadAllText(journal));
                if (requested != null && requested.id != request.id)
                { OpenSpaceSaveMessage="이전 결과를 먼저 확인해주세요. 결과 확인 / 재시도를 눌러주세요."; return null; }
            }
            else
            {
                if (requested == null) return null;
                request=requested;
                Directory.CreateDirectory(Path.GetDirectoryName(journal));
                string temporary=journal+".tmp";
                File.WriteAllText(temporary,JsonUtility.ToJson(request),Encoding.UTF8);
                File.Move(temporary,journal);
            }
            OpenSpaceSaveMessage="결과 확인 중…";
            var read = await AwaitBackend(done => Backend.GameData.GetMyData(TABLE_NAME,new Where(),result=>done(result)));
            if (!Current()) return null;
            if (!read.IsSuccess()) throw new InvalidOperationException(read.GetMessage());
            var rows=read.FlattenRows();
            if (rows==null || !rows.IsArray || rows.Count!=1 || ReadString(rows[0],"inDate","")!=rowId)
                throw new InvalidOperationException("계정 데이터 행을 확인할 수 없습니다.");
            var server=ReadPlayerSetting(rows[0]);
            var progress=OpenSpaceProgress.Parse(server.openSpaceProgressJson);
            var receipt=progress.Find(request.id);
            if (receipt==null)
            {
                try { receipt=progress.Apply(request,DateTime.UtcNow.ToString("O")); }
                catch (InvalidOperationException e)
                {
                    // Definitive validation failure, before any write. Do not trap an invalid operation forever.
                    File.Delete(journal); OpenSpaceSaveMessage=e.Message; return null;
                }
                if (receipt.coins>int.MaxValue-server.money) throw new InvalidOperationException("재화 한도를 확인해주세요.");
                server.money+=receipt.coins;
                server.openSpaceProgressJson=JsonUtility.ToJson(progress);
                var param=new Param(); param.Add("openSpaceProgressJson",server.openSpaceProgressJson);
                if (receipt.coins>0) param.Add("money",server.money);
                var saved=await AwaitBackend(done => Backend.GameData.UpdateV2(TABLE_NAME,rowId,owner,param,result=>done(result)));
                if (!Current()) return null;
                if (!saved.IsSuccess()) throw new InvalidOperationException(saved.GetMessage());
            }
            currentPlayerSetting.openSpaceProgressJson=server.openSpaceProgressJson;
            ApplyEconomySnapshot(server.money,server.itemInventoryJson);
            File.Delete(journal);
            OpenSpaceSaveMessage=receipt.kind=="catch" ? "생선 +1 · 가방 저장 완료" : receipt.kind=="feed" ?
                (receipt.newMotion ? "새 고양이 모션 해금 · 저장 완료" : "이미 해금한 모션이에요. 생선 1마리 사용 완료") : "저장 완료";
            return receipt;
        }
        catch (Exception e)
        {
            if (Current()) OpenSpaceSaveMessage="결과 확인이 지연됩니다. 재시도해주세요. (LoginPlayerData.openSpaceProgressJson 문자열 컬럼 확인) " + e.Message;
            return null;
        }
        finally { if (this != null) IsOpenSpaceWriteInProgress=false; }
    }
    private static async Task<BackendReturnObject> AwaitBackend(Action<Action<BackendReturnObject>> start)
    {
        var completion=new TaskCompletionSource<BackendReturnObject>();
        start(result=>completion.TrySetResult(result));
        if (await Task.WhenAny(completion.Task,Task.Delay(20000))!=completion.Task)
            throw new TimeoutException("서버 응답 대기 시간 초과. 동일 요청으로 결과를 재확인합니다.");
        return await completion.Task;
    }
}
