using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

[CustomEditor(typeof(RaceRing))]
public sealed class RaceRingEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Index는 Hierarchy 순서로 자동 갱신됩니다. 부모 링 다음에 추가로 배치한 하위 링이 이어집니다.", MessageType.Info);
    }
    private void OnSceneGUI()
    {
        var ring = (RaceRing)target;
        Handles.Label(ring.Center, $"Ring {ring.Index}");
    }
}

[InitializeOnLoad]
public static class RaceRingHierarchyIndex
{
    private static bool queued;
    static RaceRingHierarchyIndex()
    {
        EditorApplication.hierarchyChanged += Queue;
        Queue();
    }
    private static void Queue()
    {
        if (queued || EditorApplication.isPlayingOrWillChangePlaymode) return;
        queued = true;
        EditorApplication.delayCall += Refresh;
    }
    private static void Refresh()
    {
        queued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (var course in Object.FindObjectsByType<RaceCourse>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!course.gameObject.scene.IsValid() || EditorUtility.IsPersistent(course)) continue;
            var before = course.OrderedRings;
            var indexes = new Dictionary<RaceRing, int>();
            foreach (var root in course.gameObject.scene.GetRootGameObjects())
                foreach (var ring in root.GetComponentsInChildren<RaceRing>(true)) indexes[ring] = ring.Index;
            // Root-level and nested rings are both discovered by Reindex.
            course.Reindex();
            bool changed = before == null || before.Count != course.RingCount;
            for (int i = 0; i < course.RingCount; i++)
            {
                var ring = course.OrderedRings[i];
                if (!changed && before[i] != ring) changed = true;
                if (indexes[ring] != ring.Index)
                {
                    changed = true;
                    EditorUtility.SetDirty(ring);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(ring);
                }
            }
            if (changed)
            {
                EditorUtility.SetDirty(course);
                EditorSceneManager.MarkSceneDirty(course.gameObject.scene);
            }
        }
    }
}
