using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ReactiveShieldVfx))]
public sealed class ReactiveShieldVfxEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        bool changed = EditorGUI.EndChangeCheck();
        var shield = (ReactiveShieldVfx)target;
        if (changed) shield.Rebuild();
        EditorGUILayout.HelpBox($"{shield.CellCount} cells. Visual only: no damage, blocking or network dependencies.\nPlay the ReactiveShieldPreview scene: click to hit, Space to toggle automatic hits.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Test hit / Front"))
                shield.ReportHit(shield.transform.TransformPoint(Vector3.back*shield.Radius));
            if (GUILayout.Button("Clear hit effects")) shield.ClearHits();
        }
    }
}
