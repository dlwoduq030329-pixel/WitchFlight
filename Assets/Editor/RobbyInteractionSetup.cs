using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class RobbyInteractionSetup
{
    [InitializeOnLoadMethod] private static void Queue() => EditorApplication.delayCall += Setup;
    [MenuItem("Tools/Witch Flight/Setup Robby Interactions")]
    private static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Setup; return; }
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Main.unity");
        if (!scene.IsValid() || !scene.isLoaded) return;
        var zoneType = Type.GetType("RobbyInteractionZone, Assembly-CSharp");
        var uiType = Type.GetType("RobbyInteractionUI, Assembly-CSharp");
        if (zoneType == null || uiType == null) return;
        var nodes = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
        var canvas = nodes.FirstOrDefault(x => x.name == "InterActiveCanvas") ??
            nodes.FirstOrDefault(x => x.name == "InteractiveCanvas");
        var prompt = canvas != null ? (canvas.Find("Interactive") ?? canvas.Find("InterActive")) : null;
        if (prompt == null) return;
        bool wasDirty = scene.isDirty, changed = false;
        int id = 1;
        var used = new System.Collections.Generic.HashSet<int>();
        foreach (var node in nodes)
        {
            var existing = node.GetComponent(zoneType);
            if (existing != null) used.Add(new SerializedObject(existing).FindProperty("zoneId").intValue);
        }
        foreach (var node in nodes)
        {
            int kind = node.name == "DrinkCollider" ? 0 : node.name == "CatCollider" ? 1 : node.name == "SitCollider" ? 2 : -1;
            if (kind < 0 || node.GetComponent<Collider>() == null || node.GetComponent(zoneType) != null) continue;
            var zone = Undo.AddComponent(node.gameObject, zoneType);
            var data = new SerializedObject(zone);
            while (used.Contains(id)) id++;
            data.FindProperty("zoneId").intValue = id; used.Add(id++);
            data.FindProperty("action").enumValueIndex = kind;
            data.ApplyModifiedPropertiesWithoutUndo();
            var collider = node.GetComponent<Collider>();
            Undo.RecordObject(collider, "Lobby interaction trigger"); collider.isTrigger = true;
            EditorUtility.SetDirty(collider); changed = true;
        }
        var controller = canvas.GetComponent(uiType);
        if (controller == null) { controller = Undo.AddComponent(canvas.gameObject, uiType); changed = true; }
        var ui = new SerializedObject(controller);
        var textNode = prompt.Find("Text(TMP)") ?? prompt.Find("Text (TMP)");
        var text = textNode != null ? textNode.GetComponent<TMPro.TMP_Text>() : null;
        var legacy = textNode != null ? textNode.GetComponent<UnityEngine.UI.Text>() : null;
        if (ui.FindProperty("prompt").objectReferenceValue != prompt.gameObject ||
            ui.FindProperty("promptText").objectReferenceValue != text ||
            ui.FindProperty("legacyText").objectReferenceValue != legacy)
        {
            ui.FindProperty("prompt").objectReferenceValue = prompt.gameObject;
            ui.FindProperty("promptText").objectReferenceValue = text;
            ui.FindProperty("legacyText").objectReferenceValue = legacy;
            ui.ApplyModifiedPropertiesWithoutUndo(); changed = true;
        }
        if (!changed) return;
        EditorSceneManager.MarkSceneDirty(scene);
        if (!wasDirty) EditorSceneManager.SaveScene(scene);
        Debug.Log("[Robby interaction] Zones and existing InterActive UI wired. Assign Cat Target and Seat Target on each zone; save Main.");
    }
}
