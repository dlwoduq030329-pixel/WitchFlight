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
    private float densityHorizontal=20f,densityVertical=10f,densityLow,densityHigh,densityOpacity=.55f;
    private string densityStatus="파일을 가져온 뒤 통합 밀도를 계산하세요.";
    private string[] importedDensityFiles=Array.Empty<string>();
    private BattleMovementDensity.Result density;
    private CancellationTokenSource densityCancellation;
    private GameObject densityRoot;
    private Mesh densityMesh;
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
        GUILayout.Label("파랑: 적게 체류 → 노랑 → 빨강: 많이 체류\n모든 고도에 같은 색상 기준 적용\n빈 공간: 기록된 체류 없음 (통행 가능 여부와는 별개)");
        GUILayout.Label($"표시 고도 Y {densityLow:F0} ~ {densityHigh:F0}m");
        float min=density.min.y*density.verticalSize,max=(density.max.y+1)*density.verticalSize;
        densityLow=Mathf.Clamp(Mathf.Round(GUILayout.HorizontalSlider(densityLow,min,max-density.verticalSize)/density.verticalSize)*density.verticalSize,min,max-density.verticalSize);
        densityHigh=Mathf.Clamp(Mathf.Round(GUILayout.HorizontalSlider(densityHigh,min+density.verticalSize,max)/density.verticalSize)*density.verticalSize,densityLow+density.verticalSize,max);
        GUILayout.Label($"불투명도 {densityOpacity:P0}");
        densityOpacity=GUILayout.HorizontalSlider(densityOpacity,.1f,.9f);
        if(GUILayout.Button("고도 범위 표시 적용")) RebuildDensityMesh();
        if(GUILayout.Button("전체 고도 표시")) {densityLow=min;densityHigh=max;RebuildDensityMesh();}
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
            {densityLow=low;densityHigh=high;RebuildDensityMesh();FocusDensity();}
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
            densityLow=result.min.y*result.verticalSize;densityHigh=(result.max.y+1)*result.verticalSize;
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
        var cells=density.seconds.Where(p=>p.Key.y*density.verticalSize>=densityLow && p.Key.y*density.verticalSize<densityHigh).ToArray();
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
        var vertices=new Vector3[cells.Length*8];var colors=new Color[cells.Length*8];var indices=new int[cells.Length*36];
        Vector3 size=new Vector3(density.horizontalSize,density.verticalSize,density.horizontalSize);
        for(int i=0;i<cells.Length;i++)
        {
            var cell=cells[i];
            Vector3 center=Vector3.Scale((Vector3)cell.Key+Vector3.one*.5f,size);
            float amount=(float)(Math.Log(1+cell.Value)/Math.Log(1+density.peakSeconds));
            Color color=Color.HSVToRGB(Mathf.Lerp(.66f,0f,amount),.95f,1f);color.a=densityOpacity*Mathf.Lerp(.35f,1f,amount);
            for(int corner=0;corner<8;corner++)
            {vertices[i*8+corner]=center+Vector3.Scale(CubeCorners[corner],size)*.46f;colors[i*8+corner]=color;}
            for(int t=0;t<36;t++)indices[i*36+t]=i*8+CubeIndices[t];
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
        Vector3 low=new(density.min.x*density.horizontalSize,densityLow,density.min.z*density.horizontalSize);
        Vector3 high=new((density.max.x+1)*density.horizontalSize,densityHigh,(density.max.z+1)*density.horizontalSize);
        focus=(low+high)*.5f;distance=Mathf.Clamp((high-low).magnitude*1.2f,30,8000);
    }
    private void ClearDensity()
    {
        densityCancellation?.Cancel();
        if(densityRoot!=null)Destroy(densityRoot);
        if(densityMesh!=null)Destroy(densityMesh);
        if(densityMaterial!=null)Destroy(densityMaterial);
    }
}
