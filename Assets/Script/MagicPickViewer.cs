using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed class MagicPickViewer : MonoBehaviour
{
    [SerializeField] private MagicStatTable magicTable;
    private List<MagicPickLog> logs=new();
    private MagicPickStatistics stats=new();
    private int roundFilter,population,sort;
    private int[] ordered=Enumerable.Range(1,12).ToArray();
    private string path="",status="magic_pick_v1 CSV/JSON을 가져오면 기존 기록과 누적 집계됩니다.";
    private readonly string[] names=new string[13];
    private readonly Sprite[] icons=new Sprite[13];
    private Vector2 scroll;
    private Font font;
    private GUIStyle title,rowLabel;
    private void Awake()
    {
        font=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","Arial"},16);
        if(magicTable==null) magicTable=MagicStatTable.Default;
        for(int i=1;i<=12;i++)
        {
            names[i]=magicTable!=null?magicTable.GetStats((MagicType)i).displayName:((MagicType)i).ToString();
            if(string.IsNullOrEmpty(names[i])) names[i]=((MagicType)i).ToString();
            icons[i]=magicTable!=null?magicTable.GetIcon((MagicType)i):null;
        }
        Reload();
    }
    private void Reload()
    {
        logs=MagicPickLogStore.ReadAll(out int invalid);Recalculate();
        if(invalid>0) status=$"읽을 수 없는 파일 {invalid}개를 제외했습니다.";
    }
    private void Recalculate()
    {
        stats=MagicPickStatistics.Calculate(logs,roundFilter,population);
        ordered=(sort==2?Enumerable.Range(1,12).OrderBy(i=>names[i]):
            sort==1?Enumerable.Range(1,12).OrderBy(i=>stats.slotCounts[i]).ThenBy(i=>i):
            Enumerable.Range(1,12).OrderByDescending(i=>stats.slotCounts[i]).ThenBy(i=>i)).ToArray();
    }
    private void OnGUI()
    {
        if(font!=null) GUI.skin.font=font;
        title??=new GUIStyle(GUI.skin.label){fontSize=24,fontStyle=FontStyle.Bold};
        rowLabel??=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleLeft,fontSize=16};
        GUI.Box(new Rect(0,0,Screen.width,Screen.height),GUIContent.none);
        GUILayout.BeginArea(new Rect(20,15,Mathf.Max(400,Screen.width-40),Mathf.Max(300,Screen.height-30)));
        scroll=GUILayout.BeginScrollView(scroll);
        GUILayout.Label("마법 픽률 · 누적 기록",title);
        GUILayout.Label("각 라운드 시작 시 장착한 두 슬롯 기준 · 시전 횟수 통계 아님");
        GUILayout.BeginHorizontal();path=GUILayout.TextField(path);
#if UNITY_EDITOR
        if(GUILayout.Button("찾아보기…",GUILayout.Width(100)))
        {
            string chosen=UnityEditor.EditorUtility.OpenFilePanel("마법 픽 로그","","csv,json,jsonl");
            if(!string.IsNullOrEmpty(chosen)) path=chosen;
        }
#endif
        if(GUILayout.Button("가져오기 / 병합",GUILayout.Width(140)))
        {
            try {status=MagicPickLogStore.Import(path.Trim().Trim('"'));Reload();}
            catch(Exception e) {status="가져오기 실패: "+e.Message;}
        }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("기록 새로고침")) Reload();
        if(GUILayout.Button("저장 폴더 열기"))
        {Directory.CreateDirectory(MagicPickLogStore.DirectoryPath);Application.OpenURL(new Uri(MagicPickLogStore.DirectoryPath+Path.DirectorySeparatorChar).AbsoluteUri);}
        GUILayout.EndHorizontal();GUILayout.Label(status);
        int selected=GUILayout.Toolbar(roundFilter,new[]{"전체 라운드 통합","초기 선택 (1R)","2R","3R"});
        int people=GUILayout.Toolbar(population,new[]{"유저만","봇만","유저 + 봇"});
        int sorting=GUILayout.Toolbar(sort,new[]{"픽률 높은 순","픽률 낮은 순","마법 이름순"});
        if(selected!=roundFilter || people!=population || sorting!=sort)
        {roundFilter=selected;population=people;sort=sorting;Recalculate();}
        GUILayout.Space(8);
        GUILayout.Label($"불러온 기록 기준: {stats.matches}경기 · {stats.participantRounds}명×라운드 · 총 {stats.totalSlots}슬롯");
        GUILayout.Label("슬롯 픽률 = 해당 마법 슬롯 수 / 전체 슬롯 수 (합계 100%)\n장착률 = 해당 마법을 하나 이상 장착한 유저·라운드 수 / 전체 유저·라운드 수");
        GUILayout.BeginHorizontal();GUILayout.Label("마법",GUILayout.Width(210));GUILayout.Label("픽 횟수",GUILayout.Width(90));
        GUILayout.Label("슬롯 픽률",GUILayout.Width(120));GUILayout.Label("장착률",GUILayout.Width(110));GUILayout.Label("비율");GUILayout.EndHorizontal();
        foreach(int id in ordered)
        {
            float share=stats.totalSlots>0?100f*stats.slotCounts[id]/stats.totalSlots:0;
            float equipped=stats.participantRounds>0?100f*stats.equippedCounts[id]/stats.participantRounds:0;
            GUILayout.BeginHorizontal(GUILayout.Height(42));
            Rect icon=GUILayoutUtility.GetRect(36,36,GUILayout.Width(36));
            Sprite sprite=icons[id];
            if(sprite!=null)
            {
                Rect uv=sprite.rect;uv.x/=sprite.texture.width;uv.width/=sprite.texture.width;uv.y/=sprite.texture.height;uv.height/=sprite.texture.height;
                GUI.DrawTextureWithTexCoords(icon,sprite.texture,uv);
            }
            GUILayout.Label(names[id],rowLabel,GUILayout.Width(170));
            GUILayout.Label(stats.slotCounts[id].ToString(),rowLabel,GUILayout.Width(90));
            GUILayout.Label($"{share:F1}%",rowLabel,GUILayout.Width(120));
            GUILayout.Label($"{equipped:F1}%",rowLabel,GUILayout.Width(110));
            Rect bar=GUILayoutUtility.GetRect(80,28,GUILayout.ExpandWidth(true));
            if(Event.current.type==EventType.Repaint)
            {
                Color before=GUI.color;GUI.color=new Color(.15f,.2f,.25f);GUI.DrawTexture(bar,Texture2D.whiteTexture);
                bar.width*=share/100;GUI.color=new Color(.27f,.86f,.8f);GUI.DrawTexture(bar,Texture2D.whiteTexture);GUI.color=before;
            }
            GUILayout.EndHorizontal();
        }
        if(stats.totalSlots==0) GUILayout.Label("선택한 조건의 기록이 없습니다. 수집을 켜고 플레이한 뒤 로그를 가져오세요.");
        GUILayout.EndScrollView();GUILayout.EndArea();
    }
    private void OnDestroy(){if(font!=null) Destroy(font);}
}
