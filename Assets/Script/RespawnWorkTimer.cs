using System;
using System.Diagnostics;
using UnityEngine;

// Measures one-off initialization only; no Update, scene search or allocation on the fast path.
internal readonly struct RespawnWorkTimer : IDisposable
{
    private static RespawnFrameProbe probe;

    internal static void WatchFrames()
    {
        if (!Application.isPlaying) return;
        if (probe == null)
        {
            var host = new GameObject("RespawnFrameProbe");
            probe = host.AddComponent<RespawnFrameProbe>();
        }
        probe.Watch();
    }
    private readonly string stage;
    private readonly UnityEngine.Object context;
    private readonly long started;
    public RespawnWorkTimer(string stage, UnityEngine.Object context)
    {
        this.stage = stage;
        this.context = context;
        started = Stopwatch.GetTimestamp();
    }
    public void Dispose()
    {
        double milliseconds = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        if (milliseconds < 8.0) return;
        UnityEngine.Debug.LogWarning($"[RespawnPerf] frame={Time.frameCount} stage={stage} elapsed={milliseconds:F1}ms object={context?.name}", context);
    }
}

// Lives independently of avatars so destruction/recreation cannot erase the sample.
internal sealed class RespawnFrameProbe : MonoBehaviour
{
    private double previousTime;
    private double watchUntil;
    private int previousGc;
    private Player previousPlayer;
    private Vector3 previousPosition;
    private bool watching;

    private void Awake()
    {
        previousTime = Time.realtimeSinceStartupAsDouble;
        previousGc = GC.CollectionCount(0);
    }

    internal void Watch()
    {
        watchUntil = Time.realtimeSinceStartupAsDouble + 8;
        if (watching) return;
        watching = true;
        UnityEngine.Debug.Log("[RespawnFrame] Frame monitoring started (8 seconds after latest spawn).");
    }

    private void Update()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        double milliseconds = (now - previousTime) * 1000;
        int collections = GC.CollectionCount(0);
        Player local = Player.LocalPlayer;
        if (watching && milliseconds >= 100)
        {
            float distance = local != null && local == previousPlayer
                ? Vector3.Distance(previousPosition, local.transform.position) : 0;
            UnityEngine.Debug.LogWarning($"[RespawnFrame] frame={Time.frameCount} gap={milliseconds:F1}ms gc={collections - previousGc} localTravel={distance:F2}m avatarChanged={local != previousPlayer}");
        }
        if (watching && now >= watchUntil)
        {
            watching = false;
            UnityEngine.Debug.Log("[RespawnFrame] Frame monitoring finished.");
        }
        previousTime = now;
        previousGc = collections;
        previousPlayer = local;
        if (local != null) previousPosition = local.transform.position;
    }
}
