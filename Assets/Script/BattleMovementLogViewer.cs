using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BattleMovementLogViewer : MonoBehaviour
{
    [SerializeField] private Material pathMaterial;
    [SerializeField, Min(0.05f)] private float pathWidth=0.6f;
    [SerializeField] private bool pathsThroughWalls=true;
    private sealed class PathPart
    {
        public LineRenderer line;
        public BattleMovementPoint[] points;
        public int count=-1;
    }
    private sealed class TrackView
    {
        public BattleMovementTrack track;
        public Color color;
        public bool visible=true;
        public GameObject marker;
        public readonly List<PathPart> parts=new();
    }
    private static readonly Color[] Colors={new(0.15f,0.95f,1),new(1,0.3f,0.38f),new(0.45f,0.65f,1),new(1,0.7f,0.2f),Color.magenta,Color.green};
    private readonly List<TrackView> views=new();
    private readonly List<Material> materials=new();
    private Camera viewCamera;
    private BattleMovementLog log;
    private string[] files=Array.Empty<string>();
    private string importPath="",status="경기 파일을 선택하거나 뒤끝 로그 내보내기 파일을 가져오세요.";
    private Vector2 scroll;
    private Vector3 focus;
    private float distance=400,yaw=25,pitch=55,time,speed=1;
    private bool playing,fullPaths=true,top;
    private int round;
    private Font font;
    private GUIStyle labelStyle;
    private const float Sidebar=340;

    private void Awake()
    {
        // The dedicated diagnostic scene contains no gameplay manager or network runner.
        var cameraObject=new GameObject("Movement log camera",typeof(Camera));
        viewCamera=cameraObject.GetComponent<Camera>();
        viewCamera.nearClipPlane=0.3f; viewCamera.farClipPlane=10000; viewCamera.fieldOfView=55;
        viewCamera.clearFlags=CameraClearFlags.SolidColor; viewCamera.backgroundColor=new Color(.08f,.11f,.15f);
        viewCamera.allowHDR=false; viewCamera.allowMSAA=false;
        font=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","Arial"},15);
        RefreshFiles();
        var renderers=FindObjectsByType<Renderer>(FindObjectsInactive.Exclude,FindObjectsSortMode.None);
        if(renderers.Length>0)
        {
            Bounds bounds=renderers[0].bounds;
            foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            focus=bounds.center; distance=Mathf.Clamp(bounds.size.magnitude,50,3000);
        }
        if(files.Length>0) Load(files[0]);
    }
    private void Update()
    {
        if(log!=null && playing)
        {
            time=Mathf.Min(log.duration,time+Time.unscaledDeltaTime*speed);
            if(time>=log.duration) playing=false;
        }
        viewCamera.rect=new Rect(Sidebar/Screen.width,0,Mathf.Max(.1f,1-Sidebar/Screen.width),1);
        Quaternion rotation=Quaternion.Euler(top?90:pitch,yaw,0);
        viewCamera.transform.SetPositionAndRotation(focus-rotation*Vector3.forward*distance,rotation);
        viewCamera.orthographic=top; viewCamera.orthographicSize=distance*.45f;
        UpdatePaths();
    }
    private void RefreshFiles() => files=BattleMovementLogFiles.List();
    private void Load(string path)
    {
        try
        {
            var loaded=BattleMovementLogFiles.Read(path);
            ClearViews(); log=loaded; time=0; round=0; playing=false;
            for(int i=0;i<log.players.Count;i++)
            {
                var track=log.players[i];
                var view=new TrackView {track=track,color=Colors[i%Colors.Length]};
                view.marker=GameObject.CreatePrimitive(PrimitiveType.Sphere);
                view.marker.name="Participant "+track.participant; Destroy(view.marker.GetComponent<Collider>());
                view.marker.transform.localScale=Vector3.one*2;
                var mat=MakeMaterial(view.color);
                view.marker.GetComponent<Renderer>().sharedMaterial=mat;
                views.Add(view);
            }
            RebuildPaths(); FocusPaths();
            status=$"{log.startedUtc}\n{log.mode} / {log.players.Count}명 / {log.duration:F1}초\n종료: {log.endReason}\n버전: {log.gameVersion}";
            if(log.endReason=="partial_server_export") status+="\n주의: 일부 구간이 누락된 서버 기록";
        }
        catch(Exception e) { status="불러오기 실패: "+e.Message; }
    }
    private Material MakeMaterial(Color color)
    {
        Material mat=pathMaterial!=null?new Material(pathMaterial):new Material(Shader.Find("Sprites/Default"));
        mat.color=color;
        if(mat.HasProperty("_ZTest")) mat.SetInt("_ZTest",(int)(pathsThroughWalls?CompareFunction.Always:CompareFunction.LessEqual));
        materials.Add(mat); return mat;
    }
    private void RebuildPaths()
    {
        foreach(var view in views)
        {
            foreach(var part in view.parts) Destroy(part.line.gameObject);
            view.parts.Clear();
            var run=new List<BattleMovementPoint>();
            void Flush()
            {
                if(run.Count<2) {run.Clear();return;}
                var go=new GameObject("Path "+view.track.participant,typeof(LineRenderer));
                go.transform.SetParent(transform,false);
                var line=go.GetComponent<LineRenderer>();
                line.sharedMaterial=view.marker.GetComponent<Renderer>().sharedMaterial;
                line.startWidth=line.endWidth=pathWidth; line.useWorldSpace=true;
                line.shadowCastingMode=ShadowCastingMode.Off; line.receiveShadows=false;
                line.positionCount=0;
                view.parts.Add(new PathPart {line=line,points=run.ToArray()}); run.Clear();
            }
            foreach(var point in view.track.points)
            {
                if(round!=0 && point.round!=round) {Flush();continue;}
                if(run.Count>0 && (run[run.Count-1].segment!=point.segment ||
                    point.time-run[run.Count-1].time>Mathf.Max(1f,log.sampleInterval*4))) Flush();
                run.Add(point);
                if(run.Count==4096) {Flush();run.Add(point);}
            }
            Flush();
        }
    }
    private void UpdatePaths()
    {
        foreach(var view in views)
        {
            foreach(var part in view.parts)
            {
                part.line.enabled=view.visible;
                int count=fullPaths?part.points.Length:UpperBound(part.points,time);
                if(count==part.count) continue;
                int old=part.count; part.line.positionCount=count;
                for(int i=Mathf.Max(0,old);i<count;i++) part.line.SetPosition(i,part.points[i].position);
                part.count=count;
            }
            int index=UpperBound(view.track.points,time)-1;
            bool show=view.visible && index>=0;
            if(show)
            {
                var point=view.track.points[index];
                show=point.alive && (round==0 || point.round==round) && time-point.time<Mathf.Max(1f,log.sampleInterval*4);
                Vector3 pos=point.position;
                if(index+1<view.track.points.Count)
                {
                    var next=view.track.points[index+1];
                    if(point.segment==next.segment && next.time-point.time<Mathf.Max(1f,log.sampleInterval*4))
                        pos=Vector3.Lerp(pos,next.position,Mathf.InverseLerp(point.time,next.time,time));
                }
                view.marker.transform.position=pos;
            }
            view.marker.SetActive(show);
        }
    }
    private static int UpperBound(IList<BattleMovementPoint> points,float at)
    {
        int lo=0,hi=points.Count;
        while(lo<hi) {int mid=(lo+hi)/2;if(points[mid].time<=at) lo=mid+1;else hi=mid;}
        return lo;
    }
    private void FocusPaths()
    {
        var points=views.Where(v=>v.visible).SelectMany(v=>v.track.points).Where(p=>round==0||p.round==round).ToArray();
        if(points.Length==0) return;
        var bounds=new Bounds(points[0].position,Vector3.zero);
        foreach(var p in points) bounds.Encapsulate(p.position);
        focus=bounds.center; distance=Mathf.Clamp(bounds.size.magnitude*1.2f,30,8000);
    }
    private void OnGUI()
    {
        // IMGUI pointer events work with either project input backend, without gameplay bindings.
        var pointer=Event.current;
        if(viewCamera!=null && pointer.mousePosition.x>Sidebar)
        {
            if(pointer.type==EventType.ScrollWheel)
            {
                distance=Mathf.Clamp(distance*Mathf.Exp(pointer.delta.y*.04f),5,8000);
                pointer.Use();
            }
            else if(pointer.type==EventType.MouseDrag && pointer.button==1)
            {
                yaw+=pointer.delta.x*.3f;
                pitch=Mathf.Clamp(pitch+pointer.delta.y*.3f,-85,89);top=false;
                pointer.Use();
            }
            else if(pointer.type==EventType.MouseDrag && pointer.button==2)
            {
                focus+=(-viewCamera.transform.right*pointer.delta.x+viewCamera.transform.up*pointer.delta.y)*distance*.001f;
                pointer.Use();
            }
        }
        if(font!=null) GUI.skin.font=font;
        GUI.Box(new Rect(0,0,Sidebar,Screen.height),GUIContent.none);
        GUILayout.BeginArea(new Rect(10,10,Sidebar-20,Screen.height-20));
        GUILayout.Label("전투 동선 기록");
        GUILayout.Label("서버 콘솔 CSV / JSON 또는 로컬 경기 JSON");
        importPath=GUILayout.TextField(importPath);
#if UNITY_EDITOR
        if(GUILayout.Button("찾아보기…"))
        {
            string chosen=UnityEditor.EditorUtility.OpenFilePanel("동선 기록 가져오기","","csv,json,jsonl");
            if(!string.IsNullOrEmpty(chosen)) importPath=chosen;
        }
#endif
        if(GUILayout.Button("파일 가져오기"))
        {
            try {var imported=BattleMovementImport.Import(importPath.Trim().Trim('"'));RefreshFiles();Load(imported[0]);status+=$"\n{imported.Length}경기 가져옴";}
            catch(Exception e) {status=e.Message;}
        }
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("목록 새로고침")) RefreshFiles();
        if(GUILayout.Button("기록 폴더")) {Directory.CreateDirectory(BattleMovementLogFiles.DirectoryPath);Application.OpenURL(new Uri(BattleMovementLogFiles.DirectoryPath+Path.DirectorySeparatorChar).AbsoluteUri);}
        GUILayout.EndHorizontal();
        scroll=GUILayout.BeginScrollView(scroll,GUILayout.Height(Mathf.Min(180,Screen.height*.2f)));
        foreach(string file in files) if(GUILayout.Button(Path.GetFileNameWithoutExtension(file))) Load(file);
        GUILayout.EndScrollView();
        GUILayout.Label(status);
        if(log!=null)
        {
            GUILayout.Label($"시간 {time:F1} / {log.duration:F1}초");
            time=GUILayout.HorizontalSlider(time,0,Mathf.Max(.01f,log.duration));
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(playing?"일시 정지":"재생")) {if(time>=log.duration) time=0;playing=!playing;}
            if(GUILayout.Button(speed+"배속")) speed=speed>=4?.25f:speed*2;
            GUILayout.EndHorizontal();
            fullPaths=GUILayout.Toggle(fullPaths,"전체 경로 표시 (해제하면 시간까지 표시)");
            int selected=GUILayout.Toolbar(round,new[]{"전체","1R","2R","3R"});
            if(selected!=round) {round=selected;RebuildPaths();}
            foreach(var view in views)
            {
                GUI.contentColor=view.color;
                view.visible=GUILayout.Toggle(view.visible,$"{view.track.nickname} {(view.track.bot?"[BOT]":"")} / 팀 {view.track.team}");
            }
            GUI.contentColor=Color.white;
        }
        top=GUILayout.Toggle(top,"위에서 보기");
        if(GUILayout.Button("경로 중심으로 이동")) FocusPaths();
        GUILayout.Label("우클릭 드래그: 회전\n휠: 확대 / 휠 버튼 드래그: 이동\n선이 끊긴 곳: 부활·포탈·라운드 변경·기록 누락");
        GUILayout.EndArea();
        labelStyle??=new GUIStyle(GUI.skin.label) {fontSize=14,alignment=TextAnchor.MiddleCenter};
        foreach(var view in views)
        {
            if(!view.marker.activeSelf) continue;
            Vector3 p=viewCamera.WorldToScreenPoint(view.marker.transform.position);
            if(p.z<0 || p.x<Sidebar) continue;
            string label=view.track.nickname;
            int index=UpperBound(view.track.points,time)-1;
            if(index>=0 && view.track.points[index].flag) label+=" [깃발]";
            GUI.color=Color.black;GUI.Label(new Rect(p.x-99,Screen.height-p.y-29,200,25),label,labelStyle);
            GUI.color=view.color;GUI.Label(new Rect(p.x-100,Screen.height-p.y-30,200,25),label,labelStyle);
        }
        GUI.color=Color.white;
    }
    private void ClearViews()
    {
        foreach(var view in views) {Destroy(view.marker);foreach(var part in view.parts) Destroy(part.line.gameObject);}
        views.Clear(); foreach(var mat in materials) Destroy(mat); materials.Clear();
    }
    private void OnDestroy() {ClearViews();if(viewCamera!=null) Destroy(viewCamera.gameObject);if(font!=null) Destroy(font);}
}
