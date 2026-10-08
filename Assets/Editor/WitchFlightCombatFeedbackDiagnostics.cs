using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Opt-in, Editor-only observation. Never changes scenes, player state, or HUD values.
[InitializeOnLoad]
public static class WitchFlightCombatFeedbackDiagnostics
{
    private const string RequestPath = "Temp/WitchFlightCombatFeedback.capture";
    private const string ReportPath = "Temp/WitchFlightCombatFeedback.report.txt";
    private static double nextSample;
    private static double playStarted = -1;
    private static double nextDetails;
    private static bool armed;
    private static readonly StringBuilder report = new StringBuilder();

    static WitchFlightCombatFeedbackDiagnostics() => EditorApplication.update += Observe;

    [MenuItem("Tools/WitchFlight/Capture Combat Feedback Diagnostics")]
    public static void Arm()
    {
        File.WriteAllText(RequestPath, "Capture next Play session for up to 120 seconds.");
    }

    private static void Observe()
    {
        double now = EditorApplication.timeSinceStartup;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || now < nextSample) return;
        nextSample = now + (EditorApplication.isPlaying ? 0.1 : 0.5);
        if (!File.Exists(RequestPath)) { armed = false; playStarted = -1; return; }
        if (!armed)
        {
            armed = true;
            report.Clear();
            report.AppendLine("Combat feedback diagnostics armed. No gameplay or scene values are modified.");
            report.AppendLine("Waiting for Play Mode. Capture ends after 120 seconds or exiting Play Mode.");
            AppendSceneState(report);
            File.WriteAllText(ReportPath, report.ToString());
        }
        if (!EditorApplication.isPlaying)
        {
            if (playStarted >= 0) Finish("Play Mode ended.");
            return;
        }
        if (playStarted < 0) playStarted = now;
        if (now - playStarted > 120) { Finish("120-second capture completed."); return; }
        try
        {
            AppendSample(report, now - playStarted);
            if (now >= nextDetails)
            {
                nextDetails = now + 1;
                AppendSceneState(report);
                File.WriteAllText(ReportPath, report.ToString());
            }
        }
        catch (Exception e)
        {
            report.AppendLine("Diagnostic error: " + e.GetType().Name + ": " + e.Message);
            Finish("Capture stopped on diagnostic error.");
        }
    }

    private static void Finish(string reason)
    {
        report.AppendLine(reason);
        File.WriteAllText(ReportPath, report.ToString());
        File.Delete(RequestPath); // Only our explicit capture request, never project/user files.
        armed = false;
        playStarted = -1;
    }

    private static void AppendSample(StringBuilder log, double elapsed)
    {
        Player local = Player.LocalPlayer;
        bool valid = local != null && local.Object != null && local.Object.IsValid;
        BattleManager battle = BattleManager.Instance;
        log.Append($"t={elapsed:F1} phase={(battle != null ? battle.Phase.ToString() : "no-manager")} " +
            $"local={valid} menu={BattleHud.MenuOpen} cursor={Cursor.lockState}");
        if (valid)
        {
            var targeting = local.GetComponent<enemyLockOn>();
            Player locked = local.GetDisplayedLockTarget();
            bool threat = MagicProjectile.TryGetIncomingThreat(local, 45f, out float distance);
            log.Append($" alive={local.IsAlive} magic={local.GetSelectedMagic()} requiresTarget={local.SelectedMagicStats.requiresTarget}" +
                $" targetSelector={(targeting != null && targeting.CurrentTarget != null)} lockTarget={(locked != null)}" +
                $" progress={local.LockProgress:F2} complete={local.IsFullyLocked} threat={threat} distance={distance:F1}");
        }
        log.AppendLine();
    }

    private static void AppendSceneState(StringBuilder log)
    {
        log.AppendLine("-- Unity scenes / feedback state --");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            log.AppendLine($"scene={scene.name} loaded={scene.isLoaded}");
        }
        var huds = Object.FindObjectsByType<BattleHud>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        log.AppendLine($"hudCount={huds.Length}");
        foreach (BattleHud hud in huds)
        {
            log.AppendLine($"hud={hud.gameObject.name} scene={hud.gameObject.scene.name} enabled={hud.enabled} active={hud.gameObject.activeInHierarchy}" +
                $" owner={(Read(hud, "owner") != null)} threatStrength={Read(hud, "threatStrength")}");
            Camera camera = Read(hud, "worldCamera") as Camera;
            log.AppendLine($"camera={(camera != null ? camera.name : "none")} active={(camera != null && camera.isActiveAndEnabled)}");
            foreach (string field in new[] { "lockMarker", "lockChargeMarker", "incomingMagicWarning", "generatedWarning" })
                AppendView(log, field, Read(hud, field) as Component);
            Canvas canvas = Read(hud, "generatedFeedbackCanvas") as Canvas;
            if (canvas != null)
                log.AppendLine($"canvas active={canvas.isActiveAndEnabled} mode={canvas.renderMode} sort={canvas.sortingOrder}" +
                    $" parent={(canvas.transform.parent != null ? canvas.transform.parent.name : "ROOT")}" +
                    $" rect={((RectTransform)canvas.transform).rect} display={canvas.targetDisplay}");
        }
        Player local = Player.LocalPlayer;
        if (local == null || local.Object == null || !local.Object.IsValid) return;
        var targeting = local.GetComponent<enemyLockOn>();
        Camera main = Camera.main;
        foreach (Player candidate in Player.ActiveCombatants)
        {
            if (candidate == null || candidate == local || candidate.Object == null || !candidate.Object.IsValid) continue;
            Vector3 delta = candidate.LockAimPoint - local.LockAimPoint;
            log.AppendLine($"candidate={candidate.Object.Id} alive={candidate.IsAlive} team={candidate.TeamIndex}" +
                $" eligible={(targeting != null && targeting.CanLockTarget(candidate))} distance={delta.magnitude:F1}" +
                $" frontDot={Vector3.Dot(local.transform.forward, delta):F1}" +
                $" viewport={(main != null ? main.WorldToViewportPoint(candidate.LockAimPoint).ToString() : "no-camera")}");
        }
        var shots = Object.FindObjectsByType<MagicProjectile>(FindObjectsSortMode.None);
        log.AppendLine($"projectileCount={shots.Length}");
        foreach (MagicProjectile shot in shots)
        {
            if (shot.Object == null || !shot.Object.IsValid) continue;
            log.AppendLine($"shot={shot.Magic} team={shot.ShooterTeam} observerTeam={local.TeamIndex} finished={shot.Finished}" +
                $" distance={Vector3.Distance(shot.transform.position, local.LockAimPoint):F1} sameRunner={shot.Runner == local.Runner}");
        }
    }

    private static object Read(BattleHud hud, string field) => typeof(BattleHud)
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(hud);

    private static void AppendView(StringBuilder log, string field, Component view)
    {
        if (view == null) { log.AppendLine(field + "=none"); return; }
        Graphic graphic = view.GetComponent<Graphic>();
        CanvasRenderer renderer = view.GetComponent<CanvasRenderer>();
        RectTransform rect = view.transform as RectTransform;
        log.AppendLine($"{field} active={view.gameObject.activeInHierarchy} rect={(rect != null ? rect.rect.ToString() : "none")}" +
            $" position={view.transform.position} graphic={(graphic != null ? graphic.GetType().Name : "none")}" +
            $" enabled={(graphic != null && graphic.enabled)} color={(graphic != null ? graphic.color.ToString() : "none")}" +
            $" renderer={(renderer != null)} culled={(renderer != null && renderer.cull)}");
    }
}
