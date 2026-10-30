using UnityEngine;

public static class TitleProgression
{
    // Emitted only for newly earned battle titles, never for login/load restoration.
    public static event System.Action<int> TitleUnlocked;
    private static float nextProgressSave;
    public static bool Ready => DatabaseManager.Instance != null && DatabaseManager.Instance.HasLoadedProfile && DatabaseManager.Instance.IsDataConfigReady;
    public static TitleProgress Read()
    {
        try {
            var p=JsonUtility.FromJson<TitleProgress>(string.IsNullOrWhiteSpace(DataConfig.titleProgressJson)?"{}":DataConfig.titleProgressJson);
            p ??= new TitleProgress(); p.Normalize(); return p;
        } catch(System.Exception e) {
            // Preserve an unreadable server record instead of replacing lifetime progress.
            Debug.LogError("칭호 기록을 읽지 못했습니다. 기존 기록을 보존합니다: "+e.Message); return null;
        }
    }
    public static void OnProfileLoaded()
    {
        nextProgressSave=0f;
        if(!Ready)return;
        var p=Read(); if(p==null)return;
        p.GrantLogin();
        using var batch=DataConfig.BeginChangeBatch();
        DataConfig.titleProgressJson=JsonUtility.ToJson(p);
        if(!p.Owns(DataConfig.equippedTitleId) || TitleCatalog.Find(DataConfig.equippedTitleId)==null) DataConfig.equippedTitleId=1;
    }
    public static bool Owns(int id)=>Ready && (Read()?.Owns(id) ?? false);
    public static string Description(int id)
    {
        var t=TitleCatalog.Find(id);
        return t==null?string.Empty:t.hidden && !Owns(id)?"히든 칭호입니다.":t.description;
    }
    public static bool Equip(int id)
    {
        if(TitleCatalog.Find(id)==null || !Owns(id))return false;
        DataConfig.equippedTitleId=id;
        PlayerData.Local?.SetEquippedTitle(id);
        return true;
    }
    public static void Observe(PlayerData data, bool flush = false)
    {
        if(!Ready || data==null || data.Object==null || !data.Object.IsValid || !data.Object.HasInputAuthority || data.IsMatchBot)return;
        if(string.IsNullOrEmpty(data.TitleMatchId.ToString()))return;
        var p=Read(); if(p==null)return;
        int owned=p.unlocked.Count;
        if(p.Apply(data.GetTitleSnapshot()) &&
            (flush || p.unlocked.Count!=owned || data.TitleMatchCompleted || Time.unscaledTime>=nextProgressSave)) {
            nextProgressSave=Time.unscaledTime+10f;
            DataConfig.titleProgressJson=JsonUtility.ToJson(p);
            for(int i=owned;i<p.unlocked.Count;i++)TitleUnlocked?.Invoke(p.unlocked[i]);
        }
    }
}
