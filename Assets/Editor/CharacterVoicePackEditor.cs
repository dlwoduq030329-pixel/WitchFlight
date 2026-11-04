using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CharacterVoicePack))]
public sealed class CharacterVoicePackEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (!GUILayout.Button("폴더에서 목소리 파일 가져오기")) return;
        string folder = EditorUtility.OpenFolderPanel("목소리 폴더", Application.dataPath + "/Sound/Voice", "");
        if (string.IsNullOrEmpty(folder)) return;
        string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
        folder = Path.GetFullPath(folder).Replace('\\', '/');
        if (!folder.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase))
        { EditorUtility.DisplayDialog("목소리 폴더", "프로젝트 Assets 내부의 폴더를 선택해주세요.", "확인"); return; }
        string relative = "Assets" + folder.Substring(assets.Length);
        var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { relative })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<AudioClip>).Where(clip => clip != null).ToArray();
        serializedObject.Update();
        var list = serializedObject.FindProperty("clips");list.arraySize = clips.Length;
        for (int i = 0; i < clips.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        serializedObject.ApplyModifiedProperties();
    }
}
