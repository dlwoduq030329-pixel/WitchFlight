using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Standalone log viewer: no bots, networking or theoretical trials are spawned here.
public sealed class BotParryProbabilityView : MonoBehaviour
{
    public string logFilePath = "";
    public string botFilter = "";
    private List<BotParryTelemetry.Trial> loaded;
    private readonly List<Vector4> points = new List<Vector4>();
    private string status = "", summary = "", currentFilter = "";
    private float refreshAt;
    private int version = -1;
    private bool live = true;
    private string[] files = new string[0];
    private Vector2 fileScroll;
    private void Start()
    {
        RefreshFiles();
        if (BotParryTelemetry.Trials.Count == 0 && files.Length > 0) LoadFile(files[files.Length - 1]);
        Rebuild();
    }
    private void RefreshFiles()
    {
        try
        {
            files = Directory.Exists(BotParryTelemetry.DirectoryPath) ? Directory.GetFiles(BotParryTelemetry.DirectoryPath, "*.jsonl") : new string[0];
            Array.Sort(files, StringComparer.Ordinal);
        }
        catch (Exception e) { status = e.Message; }
    }
    private void LoadFile(string path)
    {
        try
        {
            BotParryTelemetry.Flush();
            loaded = BotParryTelemetry.Load(path); logFilePath = path; live = false;
            status = "파일 로드 완료: " + loaded.Count + "회 추첨";
            Rebuild();
        }
        catch (Exception e) { status = "읽기 실패: " + e.Message; }
    }
    private void Update()
    {
        if (Time.unscaledTime < refreshAt) return;
        refreshAt = Time.unscaledTime + .5f;
        if ((live && version != BotParryTelemetry.Version) || currentFilter != botFilter) Rebuild();
    }
    private void Rebuild()
    {
        version = BotParryTelemetry.Version; currentFilter = botFilter;
        var rows = live ? BotParryTelemetry.Trials : loaded;
        points.Clear();
        int n=0, good=0, started=0, resolved=0, blocked=0, skipped=0, cancelled=0, pending=0;
        float sum=0;
        if (rows != null) foreach (var r in rows)
        {
            if (!string.IsNullOrEmpty(botFilter) && (r.bot == null || r.bot.IndexOf(botFilter, StringComparison.OrdinalIgnoreCase) < 0)) continue;
            n++; sum += r.probability; if (r.good) good++;
            if (r.started) started++;
            if (r.outcome == "blocked" || r.outcome == "missed") resolved++;
            if (r.outcome == "blocked") blocked++;
            else if (r.outcome == "skipped") skipped++;
            else if (r.outcome == "pending") pending++;
            else if (r.outcome != "missed") cancelled++;
            points.Add(new Vector4(r.probability, sum/n, (float)good/n,
                resolved > 0 ? (float)blocked/resolved : -1));
        }
        summary = $"추첨 {n} | 정상 타이밍 선택 {good} | 실제 시전 {started} | 방어 성공 {blocked} / 결과 확정 시전 {resolved}\n" +
            $"시도 안 함 {skipped} | 취소/중단 {cancelled} | 미확정 {pending}\n" +
            $"평균 추첨 확률 {(n>0?sum/n:0):P2} | 정상 타이밍 선택률 {(n>0?(float)good/n:0):P2} | 실제 시전 성공률 {(resolved>0?(float)blocked/resolved:0):P2}";
    }
    private void OnGUI()
    {
        GUI.Box(new Rect(0,0,Screen.width,Screen.height), GUIContent.none);
        GUILayout.BeginArea(new Rect(16,12,Screen.width-32,Screen.height-24));
        GUILayout.Label("BOTPARRYPOSSABLITY — 봇 패링 관측 로그", new GUIStyle(GUI.skin.label) { fontSize=22, fontStyle=FontStyle.Bold });
        GUILayout.Label("실제 경기 기록만 표시합니다. 호스트에서 경기 실행 → 이 씬을 열고 Play → 로그 선택.");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("현재 실행 기록", GUILayout.Width(120))) { live=true; Rebuild(); }
        if (GUILayout.Button("로그 목록 새로고침", GUILayout.Width(150))) RefreshFiles();
        if (GUILayout.Button("폴더 열기", GUILayout.Width(100)))
        {
            Directory.CreateDirectory(BotParryTelemetry.DirectoryPath);
            Application.OpenURL(new Uri(BotParryTelemetry.DirectoryPath + Path.DirectorySeparatorChar).AbsoluteUri);
        }
        GUILayout.Label(live ? "현재 세션" : "저장된 파일");
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        logFilePath = GUILayout.TextField(logFilePath);
        if (GUILayout.Button("불러오기 / 새로 읽기", GUILayout.Width(160))) LoadFile(logFilePath);
        GUILayout.EndHorizontal();
        fileScroll = GUILayout.BeginScrollView(fileScroll, GUILayout.Height(58));
        foreach (var path in files)
            if (GUILayout.Button(Path.GetFileName(path))) LoadFile(path);
        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal(); GUILayout.Label("봇 ID 필터", GUILayout.Width(90)); botFilter=GUILayout.TextField(botFilter); GUILayout.EndHorizontal();
        GUILayout.Label(summary);
        GUILayout.Label(status + (string.IsNullOrEmpty(BotParryTelemetry.Error) ? "" : " | 기록 오류: " + BotParryTelemetry.Error));
        GUILayout.Label("청록: 매 추첨 확률  /  노랑: 누적 평균 확률  /  초록: 누적 정상 타이밍 선택률  /  분홍: 실제 시전 성공률");
        float height = Mathf.Max(100, Screen.height-390);
        Rect plot = GUILayoutUtility.GetRect(100,height,GUILayout.ExpandWidth(true));
        DrawPlot(plot);
        GUILayout.Label("X: 추첨 순번 / Y: 0~100%. 실제 성공률 = 한 번 이상 막은 시전 ÷ 결과 확정 시전. 미시도·취소·미확정 제외.");
        GUILayout.Label("한 시전이 여러 발을 막아도 성공 1회. ‘전체 공격 중 방어율’과는 다릅니다. 저장 파일의 pending은 미확정 기록입니다.");
        GUILayout.EndArea();
    }
    private void DrawPlot(Rect outer)
    {
        Rect r = new Rect(outer.x+40,outer.y+8,Mathf.Max(1,outer.width-50),Mathf.Max(1,outer.height-28));
        GUI.Box(r, GUIContent.none);
        for(int i=0;i<=4;i++)
        {
            float y=r.yMax-r.height*i/4f;
            Line(new Vector2(r.x,y),new Vector2(r.xMax,y),new Color(.3f,.3f,.3f),1);
            GUI.Label(new Rect(outer.x,y-9,40,20),(i*25)+"%");
        }
        GUI.Label(new Rect(r.x,r.yMax,70,20),points.Count>0?"1":"0");
        GUI.Label(new Rect(r.xMax-80,r.yMax,80,20),points.Count.ToString());
        Color[] colors={Color.cyan,Color.yellow,Color.green,new Color(1,.4f,.8f)};
        int step=Mathf.Max(1,Mathf.CeilToInt(points.Count/1000f));
        for(int c=0;c<4;c++)
        {
            Vector2 previous=default; bool hasPrevious=false;
            for(int i=0;i<points.Count;i=Mathf.Min(i+step,points.Count-1))
            {
                float v=points[i][c];
                if(v>=0)
                {
                    Vector2 p=new Vector2(r.x+r.width*i/Mathf.Max(1,points.Count-1),r.yMax-r.height*v);
                    if(hasPrevious) Line(previous,p,colors[c],c==0?1:2);
                    else { Color old=GUI.color;GUI.color=colors[c];GUI.DrawTexture(new Rect(p.x-2,p.y-2,4,4),Texture2D.whiteTexture);GUI.color=old; }
                    previous=p;hasPrevious=true;
                }
                else hasPrevious=false;
                if(i==points.Count-1) break;
            }
        }
        if(points.Count==0) GUI.Label(new Rect(r.x+12,r.y+12,r.width-24,50),"기록 없음 — 호스트에서 봇과 플레이한 뒤 로그를 불러오세요.");
    }
    private static void Line(Vector2 a,Vector2 b,Color color,float width)
    {
        Matrix4x4 matrix=GUI.matrix;Color old=GUI.color;
        GUI.color=color;GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg,a);
        GUI.DrawTexture(new Rect(a.x,a.y-width*.5f,Vector2.Distance(a,b),width),Texture2D.whiteTexture);
        GUI.matrix=matrix;GUI.color=old;
    }
}
