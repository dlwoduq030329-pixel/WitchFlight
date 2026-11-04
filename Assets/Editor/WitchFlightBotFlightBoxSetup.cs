using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WitchFlightBotFlightBoxSetup
{
    [MenuItem("Tools/WitchFlight/Bake and Verify Bot Flight Box %#F4")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play mode first.");
        var table = AssetDatabase.LoadAssetAtPath<MapBoundaryTable>("Assets/Resources/MapBoundaryTable.asset");
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Battle.unity");
        var report = new StringBuilder();
        try
        {
            Bounds positions = default;
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.activeSelf || !root.name.StartsWith("Map_ver03", StringComparison.Ordinal)) continue;
                report.AppendLine("Map root: " + root.name);
                // Object pivots, not mesh bounds; omit organisational parents and VFX.
                foreach (var t in root.GetComponentsInChildren<Transform>(false))
                {
                    if (t.GetComponent<MeshFilter>() == null && t.GetComponent<Terrain>() == null) continue;
                    if (count++ == 0) positions = new Bounds(t.position, Vector3.zero);
                    else positions.Encapsulate(t.position);
                }
            }
            if (count == 0) throw new Exception("No active map object positions found.");
            Undo.RecordObject(table, "Bake bot flight box");
            positions.Expand(140f); // 70 world units on each face.
            table.botFlightBox = positions; table.botFlightBoxEnabled = true;
            EditorUtility.SetDirty(table); AssetDatabase.SaveAssets();
            if (!table.TryGetBotFlightBounds(out var safe)) throw new Exception("Invalid flight box.");
            report.AppendLine("Object positions: " + count + "; padding: 70 per face");
            report.AppendLine("Raw box min=" + positions.min + " max=" + positions.max);
            report.AppendLine("Safe box min=" + safe.min + " max=" + safe.max);
            var random = new System.Random(157);
            for (int i = 0; i < 10000; i++)
            {
                Vector3 p = new Vector3((float)random.NextDouble()*2000-1000,
                    (float)random.NextDouble()*1000-500,(float)random.NextDouble()*2000-1000);
                var clamped = table.ClampBotPosition(p);
                if (!safe.Contains(clamped) || table.ExceedsAltitudeLimit(clamped.y)) throw new Exception("Clamp failed: " + p);
                var goal = table.BotRetreatDestination(clamped, p, Vector3.forward, 45f);
                if (!safe.Contains(goal) || Mathf.Abs(goal.y-clamped.y) > .001f) throw new Exception("Retreat escaped/climbed.");
                var next = table.ClampBotPosition(clamped + p * .1f);
                if (!safe.Contains(next)) throw new Exception("High speed movement escaped.");
            }
            foreach (var corner in new[] { safe.min, safe.max, new Vector3(safe.min.x,safe.center.y,safe.max.z),
                new Vector3(safe.max.x,safe.center.y,safe.min.z) })
            {
                var goal = table.BotRetreatDestination(corner, safe.center, Vector3.up, 45);
                if ((goal-corner).sqrMagnitude < 4) throw new Exception("Corner retreat stalled.");
            }
            report.AppendLine("PASS: 10000 clamp/retreat/high-speed cases; 4 corner escape cases.");
            Debug.Log(report.ToString());
        }
        catch (Exception e) { report.AppendLine("FAIL: " + e); Debug.LogException(e); }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            Directory.CreateDirectory("Temp/BotFlightBox");
            File.WriteAllText("Temp/BotFlightBox/report.txt", report.ToString());
        }
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    static void Draw(BattleManager manager, GizmoType type)
    {
        if (manager.MapBoundary == null || !manager.MapBoundary.TryGetBotFlightBounds(out var bounds)) return;
        Gizmos.color = new Color(.1f,1f,.6f,.7f);
        Gizmos.DrawWireCube(bounds.center,bounds.size);
    }
}
