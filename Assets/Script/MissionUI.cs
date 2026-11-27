using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MissionUI : MonoBehaviour
{
    [SerializeField] private Transform content;
    [SerializeField] private GameObject dayTemplate, weekTemplate;
    private sealed class Row { public GameObject root; public TMP_Text name,description,label; public Button confirm; public string id; public Image image; public Sprite defaultSprite; public MissionPeriod period; }
    private readonly List<Row> rows=new();
    private bool bound;
    private void Awake()
    {
        if(content==null) { Debug.LogError("MissionUI Content를 연결해주세요.",this); return; }
        if(dayTemplate==null) dayTemplate=content.Find("DayMissionButton")?.gameObject;
        if(weekTemplate==null) weekTemplate=content.Find("WeekMissionButton")?.gameObject;
        if(dayTemplate==null || weekTemplate==null)
        { Debug.LogError("일간/주간 미션 템플릿을 연결해주세요.",this); return; }
        foreach(Transform child in content) child.gameObject.SetActive(false);
        bound=true;
    }
    private Row CreateRow(MissionPeriod period)
    {
            var template=period==MissionPeriod.Weekly?weekTemplate:dayTemplate;
            var child=Instantiate(template,content,false).transform;
            child.name=template.name+" (Mission)";
            var row=new Row { root=child.gameObject, period=period };
            // Cache the mission icon once, including nested/inactive authored objects.
            foreach(var image in child.GetComponentsInChildren<Image>(true))
                if(string.Equals(image.name,"MissionIMG",StringComparison.OrdinalIgnoreCase))
                { row.image=image; break; }
            if(row.image!=null)row.defaultSprite=row.image.sprite;
            foreach(var text in child.GetComponentsInChildren<TMP_Text>(true))
            {
                if(string.Equals(text.name,"Name",StringComparison.OrdinalIgnoreCase))row.name=text;
                if(string.Equals(text.name,"ContentText",StringComparison.OrdinalIgnoreCase))row.description=text;
            }
            foreach(var button in child.GetComponentsInChildren<Button>(true))
                if(button.name.IndexOf("confirm",StringComparison.OrdinalIgnoreCase)>=0)row.confirm=button;
            if(row.confirm!=null)
            {
                row.label=row.confirm.GetComponentInChildren<TMP_Text>(true);
                row.confirm.onClick=new Button.ButtonClickedEvent();
                row.confirm.onClick.AddListener(()=>Claim(row));
            }
            rows.Add(row);
            return row;
    }
    private void OnEnable()
    {
        MissionProgression.Changed+=Refresh;
        DatabaseManager.DataConfigReadyChanged+=ReadyChanged;
        ReadyChanged();
    }
    private void OnDisable()
    {
        MissionProgression.Changed-=Refresh;
        DatabaseManager.DataConfigReadyChanged-=ReadyChanged;
        CancelInvoke();
    }
    private void ReadyChanged()
    {
        if(!MissionProgression.Ready)MissionProgression.Load();
        ResetPeriod();
    }
    private void ResetPeriod()
    {
        CancelInvoke(nameof(ResetPeriod));
        MissionProgression.EnsurePeriods(); Refresh();
        DateTime utc=DateTime.UtcNow;
        DateTime next=MissionProgression.PeriodDay(utc).AddDays(1).AddHours(-3);
        Invoke(nameof(ResetPeriod),Mathf.Max(1,(float)(next-utc).TotalSeconds));
    }
    private void Refresh()
    {
        if(!bound)return;
        var active=MissionProgression.Active();
        // Active() supplies weekly first, then daily. Move only claimed rewards to the end.
        var claimed=active.FindAll(d=>MissionProgression.Entry(MissionProgression.State,d.missionId).claimed);
        active.RemoveAll(d=>MissionProgression.Entry(MissionProgression.State,d.missionId).claimed);
        active.AddRange(claimed);
        var unused=new List<Row>(rows);
        for(int i=0;i<active.Count;i++)
        {
            var d=active[i];
            var row=unused.Find(r=>r.period==d.period);
            if(row==null) row=CreateRow(d.period); else unused.Remove(row);
            row.root.SetActive(true);
            row.root.transform.SetSiblingIndex(i);
            row.id=d.missionId;
            if(row.image!=null)
            {
                Sprite sprite=d.sprite!=null?d.sprite:row.defaultSprite;
                if(row.image.sprite!=sprite)row.image.sprite=sprite;
            }
            var state=MissionProgression.Entry(MissionProgression.State,d.missionId);
            if(row.name!=null)row.name.text=d.displayName;
            if(row.description!=null)row.description.text=d.description.Replace("{target}",d.target.ToString()).Replace("{requiredHitsPerMagic}",d.requiredHitsPerMagic.ToString())+
                $" ({Mathf.Min(d.target,Mathf.FloorToInt(state.progress))}/{d.target})";
            if(row.confirm!=null)row.confirm.interactable=MissionProgression.Ready && !state.claimed && state.progress>=d.target;
            if(row.label!=null)row.label.text=state.claimed?"수령 완료":state.progress>=d.target?$"보상 받기\n+{d.rewardCurrency}":"진행 중";
        }
        foreach(var row in unused) row.root.SetActive(false);
    }
    private void Claim(Row row)
    {
        string id=row.id; // Refresh at the weekly reset can reassign the clicked row.
        MissionProgression.EnsurePeriods();
        var db=DatabaseManager.Instance;
        if(db!=null && !db.TryClaimMission(id,out string message))
        { if(row.label!=null)row.label.text=message; Debug.Log(message,this); }
        else Refresh();
    }
}
