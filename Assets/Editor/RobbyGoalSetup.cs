using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class RobbyGoalSetup
{
    [InitializeOnLoadMethod] private static void Queue() => EditorApplication.delayCall += Setup;
    [MenuItem("Tools/Witch Flight/Setup Robby Goal")]
    private static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Setup; return; }
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Main.unity");
        if (!scene.IsValid() || !scene.isLoaded) return;
        var nodes = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
        var goal = nodes.FirstOrDefault(x => x.name == "goal");
        var ball = nodes.FirstOrDefault(x => x.name == "Simple Beach Ball");
        var cats = nodes.Where(x => x.name == "GoalCat").Select(x => x.GetComponentInChildren<Animator>(true)).Where(x => x != null).ToArray();
        if (goal == null || ball == null || cats.Length != 2) return;
        var goalType = Type.GetType("RobbyGoal, Assembly-CSharp");
        var netType = Type.GetType("RobbyGoalNet, Assembly-CSharp");
        var ballType = Type.GetType("RobbyBeachBall, Assembly-CSharp");
        if (goalType == null || netType == null || ballType == null) return;
        var ballComponent = ball.GetComponent(ballType);
        if (ballComponent == null) return;
        var ballData = new SerializedObject(ballComponent);
        if (ballData.FindProperty("goal").objectReferenceValue != null) return;
        var importer = AssetImporter.GetAtPath("Assets/WatingRoom/goal.fbx") as ModelImporter;
        if (importer != null && !importer.isReadable) { importer.isReadable = true; importer.SaveAndReimport(); EditorApplication.delayCall += Setup; return; }
        bool wasDirty = scene.isDirty;
        var controller = goal.GetComponent(goalType) ?? Undo.AddComponent(goal.gameObject, goalType);
        var data = new SerializedObject(controller);
        data.FindProperty("goalTrigger").objectReferenceValue = goal.GetComponents<Collider>().FirstOrDefault(x => x.isTrigger);
        var array = data.FindProperty("goalCats"); array.arraySize = 2;
        for (int i = 0; i < 2; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = cats[i];
        var net = goal.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.Equals("Net", StringComparison.OrdinalIgnoreCase));
        if (net != null)
        {
            var filter = net.GetComponentInChildren<MeshFilter>(true);
            if (filter != null)
            {
                var visual = filter.GetComponent(netType) ?? Undo.AddComponent(filter.gameObject, netType);
                var visualData = new SerializedObject(visual);
                visualData.FindProperty("meshFilter").objectReferenceValue = filter;
                visualData.ApplyModifiedPropertiesWithoutUndo();
                data.FindProperty("net").objectReferenceValue = visual;
                if (filter.GetComponent<Collider>() == null)
                {
                    var collider = Undo.AddComponent<MeshCollider>(filter.gameObject);
                    collider.sharedMesh = filter.sharedMesh;
                }
            }
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        ballData.FindProperty("goal").objectReferenceValue = controller;
        ballData.FindProperty("goalResetSeconds").floatValue = 2f;
        ballData.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ballComponent);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!wasDirty) EditorSceneManager.SaveScene(scene);
        Debug.Log("[Robby goal] Goal trigger, two GoalCats, Net and ball connected. Save Main scene if it has unsaved edits.");
    }
}
