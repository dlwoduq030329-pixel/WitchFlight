using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class BattleMovementLogViewer
{
    private bool densityMode,densityBusy,densityHumans=true,densityBots=true,densityAllFiles;
    private float densityHorizontal=20f,densityVertical=10f,densityLow,densityHigh,densityOpacity=.75f;
    private string densityStatus="파일을 가져온 뒤 통합 밀도를 계산하세요.";
    private string[] importedDensityFiles=Array.Empty<string>();
    private BattleMovementDensity.Result density;
    private CancellationTokenSource densityCancellation;
    private GameObject densityRoot;
    private Mesh densityMesh;
    private int densityDisplayMode; // 0: single altitude, 1: X/Z total, 2: solid 3D
    private double[] densityThresholds=new double[5],densityVolumeThresholds=new double[5];
    private Dictionary<Vector3Int,double> densityPlanCells=new();
    private float densityPlaneY;
    private string densitySelection="";
    private LineRenderer densitySelectionOutline;
    private static readonly Color[] DensityColors = {
        new(.12f,.26f,.85f),new(.08f,.72f,.85f),new(1f,.87f,.12f),new(1f,.43f,.06f),new(.95f,.08f,.12f) };
    private static readonly string[] DensityLevels={"매우 적음","적음","보통","많음","매우 많음"};
    private Material densityMaterial;
    private Vector2 densityHeightScroll,sidebarScroll;
    private KeyValuePair<int,double>[] densityHeightRows=Array.Empty<KeyValuePair<int,double>>();
    private static readonly Vector3[] CubeCorners = {
        new(-1,-1,-1),new(1,-1,-1),new(1,1,-1),new(-1,1,-1),
        new(-1,-1,1),new(1,-1,1),new(1,1,1),new(-1,1,1) };
    private static readonly int[] CubeIndices = {0,2,1,0,3,2, 4,5,6,4,6,7, 0,4,7,0,7,3,
        1,2,6,1,6,5, 0,1,5,0,5,4, 3,7,6,3,6,2};

    private void SetDensityMode(bool enabled)
    {
        densityMode=enabled; playing=false;
        foreach(var view in views)
        {
            view.marker.SetActive(false);
            foreach(var part in view.parts) part.line.enabled=!enabled && view.visible;
        }
        if(densityRoot!=null) densityRoot.SetActive(enabled && density!=null);
        if(!enabled) RebuildPaths();
        if(enabled && density!=null) FocusDensity();
    }
    private void DrawDensityControls()
    {
        int mode=GUILayout.Toolbar(densityMode?1:0,new[]{"개별 동선","통합 · 고도별 밀도"});
        if((mode==1)!=densityMode) SetDensityMode(mode==1);
        if(!densityMode) return;
        densityAllFiles=GUILayout.Toggle(densityAllFiles,"저장된 모든 경기 통합 (해제: 마지막 가져온 파일/첫 선택 경기)");
        GUILayout.BeginHorizontal();
        densityHumans=GUILayout.Toggle(densityHumans,"유저 포함");
        densityBots=GUILayout.Toggle(densityBots,"봇 포함");
        GUILayout.EndHorizontal();
        GUILayout.Label($"가로 구간 {densityHorizontal:F0}m / 고도 구간 {densityVertical:F0}m");
        densityHorizontal=Mathf.Round(GUILayout.HorizontalSlider(densityHorizontal,2f,100f));
        densityVertical=Mathf.Round(GUILayout.HorizontalSlider(densityVertical,2f,100f));
        round=GUILayout.Toolbar(round,new[]{"전체","1R","2R","3R"});
        GUI.enabled=!densityBusy && (densityHumans || densityBots);
        if(GUILayout.Button("통합 밀도 계산 / 조건 적용")) BuildDensity();
        GUI.enabled=true;
        if(densityBusy && GUILayout.Button("계산 취소")) densityCancellation?.Cancel();
        GUILayout.Label(densityStatus);
        if(density==null || density.seconds.Count==0) return;
        int displayMode=GUILayout.Toolbar(densityDisplayMode,new[]{"고도 단면","전체 평면","3D 분포"});
        if(displayMode!=densityDisplayMode)
        {
            densityDisplayMode=displayMode;densitySelection="";
            if(displayMode==0) densityHigh=densityLow+density.verticalSize;
            top=displayMode!=2; if(top)yaw=0;
            RebuildDensityMesh();FocusDensity();
        }
        GUILayout.Label(densityDisplayMode==1 ? "모든 고도의 체류 시간을 한 평면에 합산" :
            densityDisplayMode==0 ? "고도 한 구간씩 표시 · 다른 높이와 겹치지 않음" : "불투명 입체 칸 · 우클릭 드래그로 회전");
        GUILayout.Label("색상별 실제 체류 시간 (인·초)");
        for(int i=0;i<5;i++)
        {
            GUILayout.BeginHorizontal();
            Rect chip=GUILayoutUtility.GetRect(18,18,GUILayout.Width(18));
            Color saved=GUI.color;GUI.color=DensityColors[i];GUI.DrawTexture(chip,Texture2D.whiteTexture);GUI.color=saved;
            string range=i==0 ? $"0 ~ {densityThresholds[i]:N2}" : $"{densityThresholds[i-1]:N2} 초과 ~ {densityThresholds[i]:N2}";
            GUILayout.Label($"{DensityLevels[i]}  {range}");
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("같은 단면 색상 기준으로 고도끼리 비교 가능\n빈 곳: 기록 없음 · 평면의 칸을 클릭하면 수치 표시");
        float min=density.min.y*density.verticalSize,max=(density.max.y+1)*density.verticalSize;
        if(densityDisplayMode!=1)
        {
            GUILayout.Label($"표시 고도 Y {densityLow:F0} ~ {densityHigh:F0}m");
            densityLow=Mathf.Clamp(Mathf.Round(GUILayout.HorizontalSlider(densityLow,min,max-density.verticalSize)/density.verticalSize)*density.verticalSize,min,max-density.verticalSize);
            if(densityDisplayMode==0)
            {
                densityHigh=densityLow+density.verticalSize;
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("◀ 아래 고도")){densityLow=Mathf.Max(min,densityLow-density.verticalSize);densityHigh=densityLow+density.verticalSize;RebuildDensityMesh();FocusDensity();}
                if(GUILayout.Button("위 고도 ▶")){densityLow=Mathf.Min(max-density.verticalSize,densityLow+density.verticalSize);densityHigh=densityLow+density.verticalSize;RebuildDensityMesh();FocusDensity();}
                GUILayout.EndHorizontal();
            }
            else densityHigh=Mathf.Clamp(Mathf.Round(GUILayout.HorizontalSlider(densityHigh,min+density.verticalSize,max)/density.verticalSize)*density.verticalSize,densityLow+density.verticalSize,max);
        }
        if(densityDisplayMode!=2)
        {
            GUILayout.Label($"색상 불투명도 {densityOpacity:P0} (낮추면 맵이 더 보임)");
            densityOpacity=GUILayout.HorizontalSlider(densityOpacity,.15f,1f);
        }
        if(GUILayout.Button("표시 적용")) {RebuildDensityMesh();FocusDensity();}
        if(GUILayout.Button("전체 고도 입체로 보기"))
        {densityDisplayMode=2;densityLow=min;densityHigh=max;top=false;RebuildDensityMesh();FocusDensity();}
        GUILayout.Label("고도별 체류 비중 (전체 집계 기준)");
        densityHeightScroll=GUILayout.BeginScrollView(densityHeightScroll,GUILayout.Height(115));
        int first=Mathf.Max(0,Mathf.FloorToInt(densityHeightScroll.y/24f));
        int last=Mathf.Min(densityHeightRows.Length,first+7);
        GUILayout.Space(first*24f);
        for(int i=first;i<last;i++)
        {
            var band=densityHeightRows[i];
            float low=band.Key*density.verticalSize,high=low+density.verticalSize;
            if(GUILayout.Button($"Y {low:F0}~{high:F0}m  {band.Value/density.totalSeconds:P1}  ({band.Value:F1}인·초)",GUILayout.Height(20)))
            {densityDisplayMode=0;densityLow=low;densityHigh=high;top=true;yaw=0;RebuildDensityMesh();FocusDensity();}
        }
        GUILayout.Space(Mathf.Max(0,densityHeightRows.Length-last)*24f);
        GUILayout.EndScrollView();
    }

    private async void BuildDensity()
    {
        if(densityBusy) return;
        string[] sources=(densityAllFiles?files:importedDensityFiles).ToArray();
        if(sources.Length==0) {densityStatus="먼저 CSV / XLSX 파일을 가져오거나 '저장된 모든 경기 통합'을 선택하세요.";return;}
        densityCancellation=new CancellationTokenSource();
        var cancellation=densityCancellation;
        float horizontal=densityHorizontal,vertical=densityVertical;
        bool humans=densityHumans,bots=densityBots;
        int chosenRound=round;
        string scene=log?.scenePath;
        densityBusy=true;densityStatus="파일 읽기 / 3D 밀도 계산 중…";
        try
        {
            var result=await Task.Run(()=>
            {
                var logs=new List<BattleMovementLog>(); int total=0;
                foreach(string file in sources)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var loaded=BattleMovementLogFiles.Read(file);
                    total+=loaded.players.Sum(p=>p.points.Count);
                    if(total>3000000) throw new InvalidDataException("통합 입력이 300만 좌표를 넘습니다. 날짜 범위를 나눠 주세요.");
                    logs.Add(loaded);
                }
                return BattleMovementDensity.Build(logs,horizontal,vertical,humans,bots,chosenRound,
                    scene ?? logs[0].scenePath,cancellation.Token);
            },cancellation.Token);
            if(this==null || cancellation.IsCancellationRequested) return;
            density=result;
            densityHeightRows=result.altitudeSeconds.ToArray();densityHeightScroll=Vector2.zero;
            densityVolumeThresholds=BattleMovementDensityDisplay.Thresholds(result.seconds.Values);
            int activeBand=result.altitudeSeconds.Count>0 ? result.altitudeSeconds.OrderByDescending(p=>p.Value).First().Key : result.min.y;
            densityDisplayMode=0;densityLow=activeBand*result.verticalSize;densityHigh=densityLow+result.verticalSize;
            top=true;yaw=0;densitySelection="";
            densityStatus=$"맵: {Path.GetFileNameWithoutExtension(result.scenePath)} / 고도는 월드 Y 기준\n"+
                $"{result.matches}경기 · 참가 기록 {result.players}명 · {result.samples:N0}좌표\n"+
                $"총 체류 {result.totalSeconds:N1}인·초 / 최다 구간 {result.peakSeconds:N1}인·초\n"+
                $"중복 제외 {result.duplicates:N0} / 다른 맵 제외 {result.skippedMaps}";
            if(result.seconds.Count==0) densityStatus+="\n선택 조건에 맞는 생존 동선이 없습니다.";
            if(result.versions>1) densityStatus+=$"\n게임 버전 {result.versions}종 포함: 맵 배치가 같았는지 확인하세요.";
            RebuildDensityMesh();if(densityMode)FocusDensity();
        }
        catch(OperationCanceledException) {if(this!=null)densityStatus="계산을 취소했습니다.";}
        catch(Exception e) {if(this!=null)densityStatus="통합 실패: "+e.Message;}
        finally {densityBusy=false;if(densityCancellation==cancellation)densityCancellation=null;cancellation.Dispose();}
    }

    private void RebuildDensityMesh()
    {
        if(density==null) return;
        bool planar=densityDisplayMode!=2;
        densitySelection="";
        if(densitySelectionOutline!=null)densitySelectionOutline.enabled=false;
        densityPlanCells=planar ? BattleMovementDensityDisplay.Project(density,densityLow,densityHigh,densityDisplayMode==1) : new();
        var cells=planar ? densityPlanCells.ToArray() : density.seconds.Where(p=>p.Key.y*density.verticalSize>=densityLow && p.Key.y*density.verticalSize<densityHigh).ToArray();
        densityThresholds=densityDisplayMode==1 ? BattleMovementDensityDisplay.Thresholds(densityPlanCells.Values) : densityVolumeThresholds;
        densityPlaneY=densityDisplayMode==1 ? (density.max.y+1)*density.verticalSize : (densityLow+densityHigh)*.5f;
        if(cells.Length>32000)
        {densityStatus+="\n표시 구간이 32,000개를 넘습니다. 고도를 좁히거나 구간 크기를 키우세요.";if(densityRoot!=null)densityRoot.SetActive(false);return;}
        if(densityMaterial==null)
        {
            var shader=Resources.Load<Shader>("MovementDensity");
            if(shader==null) {densityStatus+="\nMovementDensity 셰이더를 찾을 수 없습니다.";return;}
            densityMaterial=new Material(shader);
        }
        if(densityRoot==null)
        {
            densityRoot=new GameObject("Combined movement density",typeof(MeshFilter),typeof(MeshRenderer));
            densityRoot.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var renderer=densityRoot.GetComponent<MeshRenderer>();renderer.sharedMaterial=densityMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        densityMaterial.SetInt("_ZTest",(int)(planar ? CompareFunction.Always : CompareFunction.LessEqual));
        densityMaterial.SetInt("_ZWrite",planar?0:1);
        densityMaterial.SetInt("_Cull",(int)(planar?CullMode.Off:CullMode.Back));
        densityMaterial.SetFloat("_FaceShading",planar?0f:1f);
        int vertexCount=planar?4:8,indexCount=planar?6:36;
        var vertices=new Vector3[cells.Length*vertexCount];var colors=new Color[cells.Length*vertexCount];var indices=new int[cells.Length*indexCount];
        Vector3 size=new Vector3(density.horizontalSize,density.verticalSize,density.horizontalSize);
        for(int i=0;i<cells.Length;i++)
        {
            var cell=cells[i];
            Vector3 center=Vector3.Scale((Vector3)cell.Key+Vector3.one*.5f,size);
            Color color=DensityColors[BattleMovementDensityDisplay.Band(cell.Value,densityThresholds)];
            color.a=planar?densityOpacity:1f;
            if(planar)
            {
                center.y=densityPlaneY;
                float half=density.horizontalSize*.45f;
                vertices[i*4]=center+new Vector3(-half,0,-half);vertices[i*4+1]=center+new Vector3(half,0,-half);
                vertices[i*4+2]=center+new Vector3(half,0,half);vertices[i*4+3]=center+new Vector3(-half,0,half);
                for(int c=0;c<4;c++)colors[i*4+c]=color;
                int offset=i*4,t=i*6;
                indices[t]=offset;indices[t+1]=offset+2;indices[t+2]=offset+1;
                indices[t+3]=offset;indices[t+4]=offset+3;indices[t+5]=offset+2;
            }
            else
            {
                for(int corner=0;corner<8;corner++)
                {vertices[i*8+corner]=center+Vector3.Scale(CubeCorners[corner],size)*.43f;colors[i*8+corner]=color;}
                for(int t=0;t<36;t++)indices[i*36+t]=i*8+CubeIndices[t];
            }
        }
        var mesh=new Mesh {name="Movement density voxels",indexFormat=IndexFormat.UInt32};
        mesh.vertices=vertices;mesh.colors=colors;mesh.triangles=indices;mesh.RecalculateBounds();
        densityRoot.GetComponent<MeshFilter>().sharedMesh=mesh;
        if(densityMesh!=null)Destroy(densityMesh);densityMesh=mesh;
        densityRoot.SetActive(densityMode);
    }
    private void FocusDensity()
    {
        if(density==null || density.seconds.Count==0) return;
        float shownLow=densityDisplayMode==1?densityPlaneY:densityLow;
        float shownHigh=densityDisplayMode==1?densityPlaneY:densityHigh;
        Vector3 low=new(density.min.x*density.horizontalSize,shownLow,density.min.z*density.horizontalSize);
        Vector3 high=new((density.max.x+1)*density.horizontalSize,shownHigh,(density.max.z+1)*density.horizontalSize);
        focus=(low+high)*.5f;distance=Mathf.Clamp((high-low).magnitude*1.2f,30,8000);
    }
    private void DensityPointer(Event pointer)
    {
        if(!densityMode || density==null || densityDisplayMode==2 || densityRoot==null || !densityRoot.activeSelf ||
            pointer.type!=EventType.MouseDown || pointer.button!=0 || pointer.mousePosition.x<=Sidebar) return;
        var ray=viewCamera.ScreenPointToRay(new Vector3(pointer.mousePosition.x,Screen.height-pointer.mousePosition.y,0));
        var plane=new Plane(Vector3.up,new Vector3(0,densityPlaneY,0));
        if(!plane.Raycast(ray,out float distanceAlong)) return;
        Vector3 p=ray.GetPoint(distanceAlong);
        var key=new Vector3Int(Mathf.FloorToInt(p.x/density.horizontalSize),0,Mathf.FloorToInt(p.z/density.horizontalSize));
        densityPlanCells.TryGetValue(key,out double seconds);
        float x=key.x*density.horizontalSize,z=key.z*density.horizontalSize;
        densitySelection=$"X {x:F0}~{x+density.horizontalSize:F0} / Z {z:F0}~{z+density.horizontalSize:F0}  ·  체류 {seconds:N2}인·초";
        if(densitySelectionOutline==null)
        {
            var outline=new GameObject("Selected density cell",typeof(LineRenderer));outline.transform.SetParent(densityRoot.transform,false);
            densitySelectionOutline=outline.GetComponent<LineRenderer>();densitySelectionOutline.useWorldSpace=true;
            densitySelectionOutline.loop=true;densitySelectionOutline.positionCount=4;densitySelectionOutline.sortingOrder=1;
            densitySelectionOutline.sharedMaterial=densityMaterial;
            densitySelectionOutline.startColor=densitySelectionOutline.endColor=Color.white;
            densitySelectionOutline.shadowCastingMode=ShadowCastingMode.Off;densitySelectionOutline.receiveShadows=false;
        }
        float inset=density.horizontalSize*.03f,edge=density.horizontalSize-inset,y=densityPlaneY+.05f;
        densitySelectionOutline.startWidth=densitySelectionOutline.endWidth=density.horizontalSize*.025f;
        densitySelectionOutline.SetPosition(0,new Vector3(x+inset,y,z+inset));densitySelectionOutline.SetPosition(1,new Vector3(x+edge,y,z+inset));
        densitySelectionOutline.SetPosition(2,new Vector3(x+edge,y,z+edge));densitySelectionOutline.SetPosition(3,new Vector3(x+inset,y,z+edge));
        densitySelectionOutline.enabled=true;
        pointer.Use();
    }
    private void DrawDensityCaption()
    {
        if(!densityMode || density==null || densityRoot==null || !densityRoot.activeSelf) return;
        string title=densityDisplayMode==0 ? $"고도 단면  Y {densityPlaneY-density.verticalSize*.5f:F0} ~ {densityPlaneY+density.verticalSize*.5f:F0}m" :
            densityDisplayMode==1 ? "전체 고도 합산 · 평면 밀도" : "3D 밀도 분포";
        GUI.Box(new Rect(Sidebar+12,12,Mathf.Max(200,Screen.width-Sidebar-24),28),title);
        if(!string.IsNullOrEmpty(densitySelection))GUI.Box(new Rect(Sidebar+12,Screen.height-46,Mathf.Max(200,Screen.width-Sidebar-24),34),densitySelection);
    }
    private void ClearDensity()
    {
        densityCancellation?.Cancel();
        if(densityRoot!=null)Destroy(densityRoot);
        if(densityMesh!=null)Destroy(densityMesh);
        if(densityMaterial!=null)Destroy(densityMaterial);
    }
}
