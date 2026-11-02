using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class MagicProjectile
{
    private static readonly HashSet<MagicProjectile> timeFields = new();
    [Networked] private float TimeAllyMultiplier { get; set; }
    [Networked] private float TimeEnemyMultiplier { get; set; }
    [Networked] private float TimeShotMultiplier { get; set; }
    private GameObject timeFieldView;
    private TimeClockVisual clockView;
    private float timeLifetimeRemainder;

    public void InitializeTimeField(Player owner, MagicStatEntry entry)
    {
        Initialize(owner, null, entry, Vector3.forward);
        ConstructKind = MagicConstructKind.TimeField;
        ConstructRadius = Mathf.Max(.1f, entry.radius);
        ConstructDelay = Mathf.Max(.1f, entry.effectDuration);
        TimeAllyMultiplier = Mathf.Max(1f, entry.timeAllySpeed);
        TimeEnemyMultiplier = Mathf.Clamp(entry.timeEnemySpeed, .01f, 1f);
        TimeShotMultiplier = Mathf.Clamp(entry.timeProjectileSpeed, .01f, 1f);
        Lifetime = TickTimer.CreateFromSeconds(Runner, ConstructDelay);
        timeFields.Add(this);
    }

    private bool ContainsTimePoint(NetworkRunner runner, Vector3 point) => Object != null && Object.IsValid &&
        Runner == runner && !Finished && !Lifetime.ExpiredOrNotRunning(Runner) &&
        BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive &&
        (point - transform.position).sqrMagnitude <= ConstructRadius * ConstructRadius;

    public static float TimeMovementMultiplier(NetworkRunner runner, Vector3 point, int team)
    {
        float ally = 1f, enemy = 1f;
        foreach (var field in timeFields)
        {
            if (field == null || !field.ContainsTimePoint(runner, point)) continue;
            if (field.ShooterTeam == team) ally = Mathf.Max(ally, field.TimeAllyMultiplier);
            else enemy = Mathf.Min(enemy, field.TimeEnemyMultiplier);
        }
        return enemy < 1f ? enemy : ally;
    }

    public static float TimeProjectileMultiplier(NetworkRunner runner, Vector3 point)
    {
        float speed = 1f;
        foreach (var field in timeFields)
            if (field != null && field.ContainsTimePoint(runner, point)) speed = Mathf.Min(speed, field.TimeShotMultiplier);
        return speed;
    }

    protected void ResetTimeFlight() => timeLifetimeRemainder = 0f;

    protected float TimeFlightStep()
    {
        float speed = TimeProjectileMultiplier(Runner, transform.position);
        if (!Finished && !Settled && !IsConstruct && Lifetime.IsRunning)
        {
            timeLifetimeRemainder += Runner.DeltaTime * (1f - speed);
            int ticks = Mathf.FloorToInt(timeLifetimeRemainder / Runner.DeltaTime + .00001f);
            if (ticks > 0)
            {
                timeLifetimeRemainder -= ticks * Runner.DeltaTime;
                Lifetime = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, Lifetime.RemainingTime(Runner) ?? 0f) + ticks * Runner.DeltaTime);
            }
        }
        return Runner.DeltaTime * speed;
    }

    private void RenderTimeField()
    {
        timeFields.Add(this);
        if (timeFieldView == null)
        {
            timeFieldView = MagicVfxPool.Rent(PresentationStats.timeFieldVfxPrefab, transform.position, Quaternion.identity, transform);
            if (timeFieldView != null) clockView = timeFieldView.GetComponent<TimeClockVisual>();
        }
        if (clockView != null) clockView.Show(ConstructRadius, 1f - Mathf.Clamp01((Lifetime.RemainingTime(Runner) ?? 0f) / Mathf.Max(.1f, ConstructDelay)));
    }

    private void ClearTimeField()
    {
        timeFields.Remove(this);
        if (timeFieldView != null) MagicVfxPool.Release(timeFieldView);
        timeFieldView = null; clockView = null;
    }
}
