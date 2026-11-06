using System;
using UnityEditor;
using UnityEngine;

// Import-time only. Never runs in builds or modifies battle/shared animation assets.
internal static class RobbyAnimationSetup
{
    private const string Folder = "Assets/Animations/Art/WatingRoomAnimation/";
    private const string Prefab = "Assets/ChPrefabForRobby.prefab";

    [InitializeOnLoadMethod]
    private static void QueueSetup() => EditorApplication.delayCall += Setup;

    [MenuItem("Tools/Witch Flight/Setup Robby Walk and Kick")]
    [MenuItem("Tools/Witch Flight/Setup Robby Walk Run and Kick")]
    private static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Setup;
            return;
        }
        try
        {
            AnimationClip walk = Prepare("Walking.fbx", true);
            AnimationClip kick = Prepare("Kick Soccerball.fbx", false);
            AnimationClip run = Prepare("Running.fbx", true);
            if (walk == null || kick == null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                foreach (MonoBehaviour component in root.GetComponents<MonoBehaviour>())
                {
                    if (component == null || component.GetType().Name != "PlayerRobby") continue;
                    var data = new SerializedObject(component);
                    var walkProperty = data.FindProperty("walkClip");
                    var kickProperty = data.FindProperty("kickClip");
                    var runProperty = data.FindProperty("runClip");
                    if (walkProperty.objectReferenceValue == walk && kickProperty.objectReferenceValue == kick &&
                        (run == null || runProperty.objectReferenceValue == run)) return;
                    walkProperty.objectReferenceValue = walk;
                    kickProperty.objectReferenceValue = kick;
                    if (run != null) runProperty.objectReferenceValue = run;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, Prefab);
                    Debug.Log("[Robby animation] Humanoid Walking/Running/Kick Soccerball connected to ChPrefabForRobby. Root motion is baked in place; height uses feet.");
                    return;
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    private static AnimationClip Prepare(string filename, bool loop)
    {
        string path = Folder + filename;
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return null;
        bool changed = importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || !importer.importAnimation;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips.Length == 0) { clips = importer.defaultClipAnimations; changed = true; }
        foreach (var clip in clips)
        {
            changed |= clip.loopTime != loop || clip.loopPose != loop || !clip.lockRootRotation ||
                !clip.lockRootHeightY || !clip.lockRootPositionXZ || !clip.heightFromFeet ||
                clip.keepOriginalPositionY || clip.keepOriginalPositionXZ || clip.keepOriginalOrientation;
            clip.loopTime = loop;
            clip.loopPose = loop;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.heightFromFeet = true;
            clip.keepOriginalPositionY = false;
            clip.keepOriginalPositionXZ = false;
            clip.keepOriginalOrientation = false;
        }
        if (changed)
        {
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__") && clip.isHumanMotion)
                return clip;
        Debug.LogError("[Robby animation] Humanoid conversion failed; check the Avatar mapping for " + path);
        return null;
    }
}
