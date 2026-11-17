using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

internal readonly struct RespawnWorkTimer : IDisposable
{
    private static RespawnFrameProbe probe;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap() { probe = null; EnsureProbe(); }
    private static void EnsureProbe()
    {
        if (probe != null || !Application.isPlaying) return;
        var host = new GameObject("RespawnFrameProbe v2");
        UnityEngine.Object.DontDestroyOnLoad(host);
        probe = host.AddComponent<RespawnFrameProbe>();
    }
    internal static void WatchFrames() { EnsureProbe(); if (probe != null) probe.Watch(); }
    internal static void Mark(string stage, UnityEngine.Object context)
    {
        WatchFrames();
        UnityEngine.Debug.Log($"[RespawnTrace v2] utc={DateTime.UtcNow:O} frame={Time.frameCount} event={stage} object={context?.name}", context);
    }
    private readonly string stage;
    private readonly UnityEngine.Object context;
    private readonly long started;
    public RespawnWorkTimer(string stage, UnityEngine.Object context)
    {
        this.stage=stage; this.context=context; started=Stopwatch.GetTimestamp();
    }
    public void Dispose()
    {
        double ms=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
        if (probe != null) probe.Record(stage,ms);
        if(ms>=8) UnityEngine.Debug.LogWarning($"[RespawnPerf v2] utc={DateTime.UtcNow:O} frame={Time.frameCount} stage={stage} elapsed={ms:F2}ms object={context?.name}",context);
    }
}

// Created BEFORE scene loading: never starts its clock after the hitch we are investigating.
internal sealed class RespawnFrameProbe : MonoBehaviour
{
    private double previousTime, watchUntil, nextSample, maxGap;
    private int previousGc, samples, slowFrames, lastTick;
    private bool watching, hadTick;
    private Player previousPlayer;
    private Vector3 previousPosition, previousCameraPosition;
    private Camera cachedCamera;
    private readonly Dictionary<string,(int count,double total,double max)> stages = new();
    private static double Now => Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
    private void Awake() { previousTime=Now; previousGc=GC.CollectionCount(0); }
    internal void Watch()
    {
        double now=Now;
        watchUntil=now+10;
        if(watching)return;
        watching=true; samples=slowFrames=0; maxGap=0; stages.Clear(); nextSample=now;
        UnityEngine.Debug.Log($"[RespawnFrame v2] START utc={DateTime.UtcNow:O} frame={Time.frameCount} sinceLastUpdate={(now-previousTime)*1000:F1}ms editor={Application.isEditor} build={Application.version}");
    }
    internal void Record(string stage,double ms)
    {
        if(!watching)return;
        stages.TryGetValue(stage,out var v);
        stages[stage]=(v.count+1,v.total+ms,Math.Max(v.max,ms));
    }
    private void Update()
    {
        double now=Now, gap=(now-previousTime)*1000;
        int gc=GC.CollectionCount(0);
        var player=Player.LocalPlayer;
        if(cachedCamera==null)cachedCamera=Camera.main;
        var runner=NetworkGameManager.Instance!=null?NetworkGameManager.Instance.Runner:null;
        bool running=runner!=null&&runner.IsRunning;
        int tick=running?runner.Tick.Raw:0;
        int tickDelta=running&&hadTick?tick-lastTick:0;
        Vector3 position=player!=null?player.transform.position:Vector3.zero;
        Vector3 cameraPosition=cachedCamera!=null?cachedCamera.transform.position:Vector3.zero;
        if(watching)
        {
            samples++; maxGap=Math.Max(maxGap,gap); if(gap>=50)slowFrames++;
            if(gap>=50 || now>=nextSample)
            {
                string state=player!=null&&player.Object!=null&&player.Object.IsValid?$"alive={player.IsAlive} id={player.Object.Id}":"noLocalAvatar";
                string message=$"[RespawnFrame v2] utc={DateTime.UtcNow:O} frame={Time.frameCount} gap={gap:F1}ms tick={tick} tickDelta={tickDelta} running={running} server={(running&&runner.IsServer)} gc0={gc-previousGc} heapMB={GC.GetTotalMemory(false)/1048576.0:F1} avatarChanged={player!=previousPlayer} {state} pos={position:F2} travel={(player==previousPlayer?Vector3.Distance(position,previousPosition):0):F2} cameraTravel={Vector3.Distance(cameraPosition,previousCameraPosition):F2} timeScale={Time.timeScale:F2} focus={Application.isFocused}";
                if(gap>=50)UnityEngine.Debug.LogWarning(message);else UnityEngine.Debug.Log(message);
                nextSample=now+1;
            }
            if(now>=watchUntil)
            {
                watching=false;
                UnityEngine.Debug.Log($"[RespawnFrame v2] END samples={samples} maxGap={maxGap:F1}ms framesOver50ms={slowFrames}");
                foreach(var stage in stages)UnityEngine.Debug.Log($"[RespawnPerf v2] SUMMARY stage={stage.Key} count={stage.Value.count} total={stage.Value.total:F2}ms max={stage.Value.max:F2}ms");
            }
        }
        previousTime=now; previousGc=gc; previousPlayer=player; previousPosition=position;
        previousCameraPosition=cameraPosition; lastTick=tick; hadTick=running;
    }
}
