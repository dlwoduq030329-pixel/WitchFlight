using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TitleValidationRuntime;

// Isolated Unity editor fixture. Backend and transport are doubles; JSON, UI,
// catalog, profile selection and title selection execute production code.
public static class TitleSystemValidation
{
    private static int count;
    private static void Check(bool value,string message) {if(!value)throw new Exception(message);count++;}
    private static void Field(object value,string name,object field)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(value,field);
    private static void Invoke(object value,string method)=>value.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,null);
    private static GameObject Node(string name,Transform parent=null)
    {var g=new GameObject(name,typeof(RectTransform));g.transform.SetParent(parent,false);return g;}
    private static TMP_Text Text(string name,Transform parent)=>Node(name,parent).AddComponent<TextMeshProUGUI>();
    public static void Run()
    {
        try {RunCore();RunUnlockEvents();RunUI();Debug.Log("TITLE_SYSTEM_OK checks="+count);}
        catch(Exception e) {Debug.LogException(e);UnityEditor.EditorApplication.Exit(1);}
    }
    private static void RunCore()
    {
        Check(TitleCatalog.All.Count==33 && TitleCatalog.All.Select(t=>t.id).Distinct().Count()==33,"33 stable unique IDs");
        Check(TitleCatalog.All.Count(t=>t.hidden)==7,"seven hidden titles");
        foreach(var title in TitleCatalog.All)
        {
            var p=new TitleProgress();p.GrantLogin();
            var s=new TitleMatchSnapshot{id="a"};
            int n=title.required;
            switch(title.condition) {
                case TitleCondition.Login: Check(p.Owns(title.id),"first login");continue;
                case TitleCondition.Matches:p.matches=n-1;break;
                case TitleCondition.BotMatches:p.botMatches=n-1;s.botMatch=true;break;
                case TitleCondition.MagicKills:s.magicKills[(int)title.magic]=n-1;break;
                case TitleCondition.Underwater:s.underwater=n-1;break;
                case TitleCondition.Deaths:s.deaths=n-1;break;
                case TitleCondition.Emotes:s.emotes=n-1;break;
                case TitleCondition.BlackDevil:s.blackKills=n-1;break;
                case TitleCondition.DoubleHit:break;
                case TitleCondition.HealthPacks:s.healthPacks=n-1;break;
                case TitleCondition.AltitudeDeaths:s.altitudeDeaths=n-1;break;
            }
            p.Apply(s);Check(!p.Owns(title.id),"below threshold: "+title.name);
            s=JsonUtility.FromJson<TitleMatchSnapshot>(JsonUtility.ToJson(s));
            switch(title.condition) {
                case TitleCondition.Matches:case TitleCondition.BotMatches:s.completed=true;break;
                case TitleCondition.MagicKills:s.magicKills[(int)title.magic]++;break;
                case TitleCondition.Underwater:s.underwater++;break;
                case TitleCondition.Deaths:s.deaths++;break;
                case TitleCondition.Emotes:s.emotes++;break;
                case TitleCondition.BlackDevil:s.blackKills++;break;
                case TitleCondition.DoubleHit:s.doubleHit=true;break;
                case TitleCondition.HealthPacks:s.healthPacks++;break;
                case TitleCondition.AltitudeDeaths:s.altitudeDeaths++;break;
            }
            p.Apply(s);Check(p.Owns(title.id),"threshold: "+title.name);
            var loaded=JsonUtility.FromJson<TitleProgress>(JsonUtility.ToJson(p));loaded.Normalize();
            Check(loaded.Owns(title.id),"JSON ownership roundtrip: "+title.name);
            loaded.Apply(s);Check(loaded.matches==p.matches && loaded.deaths==p.deaths && loaded.magicKills.SequenceEqual(p.magicKills),"snapshot replay is idempotent");
        }
        var progress=new TitleProgress();
        progress.Apply(new TitleMatchSnapshot{id="one",emotes=9,healthPacks=9,altitudeDeaths=2,blackKills=9,deaths=25,underwater=2000});
        progress.Apply(new TitleMatchSnapshot{id="two",emotes=1,healthPacks=1,altitudeDeaths=1,blackKills=1,deaths=25,underwater=1600,completed=true,botMatch=true});
        Check(!progress.Owns(28)&&!progress.Owns(29)&&!progress.Owns(32)&&!progress.Owns(33),"per-game conditions do not accumulate across games");
        Check(progress.Owns(27)&&progress.Owns(30)&&progress.matches==1&&progress.botMatches==1,"lifetime and completed bot counters");
        Check(!progress.Apply(new TitleMatchSnapshot{id="two",completed=true,deaths=999}),"final receipt cannot be counted twice");
        progress=new TitleProgress();
        progress.Apply(new TitleMatchSnapshot{id="a",deaths=4});
        Check(!progress.Apply(new TitleMatchSnapshot{id="a",deaths=1}),"stale watermark rejected");
        progress.Apply(new TitleMatchSnapshot{id="a",deaths=5});Check(progress.deaths==5,"no extra deaths from stale snapshot");
        Check(TitleCatalog.Find(10).required==30&&TitleCatalog.Find(13).required==20,"ice and vision middle tiers retain requested thresholds");
    }
    private static void RunUnlockEvents()
    {
        var received=new System.Collections.Generic.List<int>();
        Action<int> listener=id=>received.Add(id);
        TitleProgression.TitleUnlocked+=listener;
        try {
            TitleValidationRuntime.DataConfig.titleProgressJson="{}";
            TitleProgression.OnProfileLoaded();
            Check(received.Count==0,"login restoration never announces battle unlocks");
            var data=new TitleValidationRuntime.PlayerData();
            data.snapshot.magicKills[1]=30;
            string beforeTraining=TitleValidationRuntime.DataConfig.titleProgressJson;
            TitleValidationRuntime.NetworkGameManager.Instance.IsTraining=true;
            TitleProgression.Observe(data,true);
            Check(TitleValidationRuntime.DataConfig.titleProgressJson==beforeTraining && received.Count==0,
                "training forced save cannot grant titles, accumulate progress or emit notices");
            TitleValidationRuntime.NetworkGameManager.Instance.IsTraining=false;
            TitleProgression.Observe(data,true);
            Check(received.SequenceEqual(new[]{6,7}),"simultaneous battle unlocks emit each new title in order");
            TitleProgression.Observe(data,true);
            TitleProgression.OnProfileLoaded();
            Check(received.Count==2,"snapshot replay and account reload do not repeat notices");
        } finally {TitleProgression.TitleUnlocked-=listener;}
    }
    private static void RunUI()
    {
        TitleValidationRuntime.DataConfig.titleProgressJson="{}";
        TitleValidationRuntime.DataConfig.equippedTitleId=0;
        TitleProgression.OnProfileLoaded();
        Check(TitleProgression.Owns(1)&&TitleValidationRuntime.DataConfig.equippedTitleId==1,"new account receives and equips first title");
        var root=Node("AccountSetUI");root.SetActive(false);
        var scroll=Node("ChinghoScroll View",root.transform);var content=Node("Content",scroll.transform);
        var button=Node("Button",content.transform).AddComponent<Button>();Text("Text (TMP)",button.transform);
        var popup=Node("Popup",root.transform);var name=Text("NameText",popup.transform);var description=Text("ContentText",popup.transform);Text("Have",popup.transform);
        var confirm=Node("ConfirmButton",popup.transform).AddComponent<Button>();
        var profileButton=Node("ProfileButton",root.transform).AddComponent<Button>();
        var profiles=root.AddComponent<AccountProfileUI>();
        Field(profiles,"confirmButton",confirm);Field(profiles,"titleChoicesRoot",scroll.transform);
        Field(profiles,"profileChoices",new[]{new AccountProfileUI.ProfileChoice{button=profileButton,profileIndex=2}});
        Field(profiles,"profileTable",ProfileImageTable.Default);
        root.SetActive(true);
        // In edit mode explicitly drive the same lifecycle used on entering the account UI.
        Invoke(profiles,"Awake");Invoke(profiles,"OnDisable");Invoke(profiles,"OnEnable");
        var titles=root.GetComponent<AccountTitleUI>();
        Invoke(titles,"OnDisable");Invoke(titles,"OnEnable");
        Check(content.GetComponentsInChildren<Button>(true).Length==33,"clones template to cover 33 title choices");
        titles.SelectTitle(27);Check(description.text=="히든 칭호입니다."&&!confirm.interactable,"hidden locked title shows no condition and cannot equip");
        titles.ConfirmSelectedTitle();Check(TitleValidationRuntime.DataConfig.equippedTitleId==1,"direct confirm cannot equip locked title");
        var progress=TitleProgression.Read();progress.Apply(new TitleMatchSnapshot{id="ui",underwater=3600});
        TitleValidationRuntime.DataConfig.titleProgressJson=JsonUtility.ToJson(progress);titles.Refresh();
        Check(description.text.Contains("1시간")&&confirm.interactable,"hidden condition revealed after unlock");
        confirm.onClick.Invoke();Check(TitleValidationRuntime.DataConfig.equippedTitleId==27,"shared confirm equips title");
        string saved=TitleValidationRuntime.DataConfig.titleProgressJson;
        TitleProgression.OnProfileLoaded();Check(TitleValidationRuntime.DataConfig.equippedTitleId==27&&TitleValidationRuntime.DataConfig.titleProgressJson==saved,"relogin retains selection and progress");
        profiles.SelectProfile(2);titles.SelectTitle(1);confirm.onClick.Invoke();
        Check(TitleValidationRuntime.DataConfig.playerprofile==0&&TitleValidationRuntime.DataConfig.equippedTitleId==1,"title selection cancels stale profile selection");
        titles.SelectTitle(27);profiles.SelectProfile(2);confirm.onClick.Invoke();
        Check(TitleValidationRuntime.DataConfig.playerprofile==2&&TitleValidationRuntime.DataConfig.equippedTitleId==1,"profile selection cancels stale title selection");
        titles.SelectTitle(31);profiles.SelectProfile(2);Check(confirm.interactable,"locked title must not disable profile confirmation");
        UnityEngine.Object.DestroyImmediate(root);
    }
}
