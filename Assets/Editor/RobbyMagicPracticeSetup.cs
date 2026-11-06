using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Edit-time wiring only. Existing authored UI and dummy are used as-is.
internal static class RobbyMagicPracticeSetup
{
    [InitializeOnLoadMethod] private static void Queue() => EditorApplication.delayCall += Setup;
    [MenuItem("Tools/Witch Flight/Setup Robby Magic Practice")]
    private static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Setup; return; }
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Main.unity");
        if (!scene.IsValid() || !scene.isLoaded) return;
        var nodes = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
        var canvas = nodes.FirstOrDefault(x => x.name == "InteractiveCanvas");
        var cat = nodes.FirstOrDefault(x => x.name == "CupCat_DynamicBone");
        if (canvas == null || cat == null) return;
        var practiceType = Type.GetType("RobbyMagicPractice, Assembly-CSharp");
        var dummyType = Type.GetType("RobbyPracticeDummy, Assembly-CSharp");
        if (practiceType == null || dummyType == null) return;
        var existing = canvas.GetComponent(practiceType);
        if (existing != null && new SerializedObject(existing).FindProperty("target").objectReferenceValue != null) return;
        var aim = cat.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "LockOnPos");
        var bone = cat.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Bone");
        var sliders = canvas.GetComponentsInChildren<Slider>(true);
        var first = sliders.FirstOrDefault(x => x.name == "SkillSlider01");
        var second = sliders.FirstOrDefault(x => x.name == "SkillSlider02");
        if (aim == null || bone == null || first == null || second == null)
        { Debug.LogWarning("[Robby practice] LockOnPos, Bone, SkillSlider01/02 references are required."); return; }
        bool dirty = scene.isDirty;
        var dummy = cat.GetComponent(dummyType) ?? Undo.AddComponent(cat.gameObject, dummyType);
        var d = new SerializedObject(dummy);
        d.FindProperty("lockOnPos").objectReferenceValue = aim;
        d.FindProperty("springBone").objectReferenceValue = bone;
        d.ApplyModifiedProperties();
        var controller = existing ?? Undo.AddComponent(canvas.gameObject, practiceType);
        var data = new SerializedObject(controller);
        data.FindProperty("target").objectReferenceValue = dummy;
        data.FindProperty("aimReticle").objectReferenceValue = canvas.GetComponentsInChildren<RectTransform>(true)
            .FirstOrDefault(x => x.name == "Crosshair");
        var array = data.FindProperty("skillSliders"); array.arraySize = 2;
        array.GetArrayElementAtIndex(0).objectReferenceValue = first;
        array.GetArrayElementAtIndex(1).objectReferenceValue = second;
        data.FindProperty("magicTable").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Resources/MagicStatTable.asset");
        data.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(dummy);
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!dirty) EditorSceneManager.SaveScene(scene);
        Debug.Log("[Robby practice] Existing InteractiveCanvas and CupCat linked. Save Main if it has unsaved changes.");
    }
}
