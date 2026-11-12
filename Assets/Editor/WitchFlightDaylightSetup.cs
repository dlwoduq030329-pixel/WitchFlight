using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Editor-only, one-shot setup for an already-open Battle scene. Never saves user edits.
internal static class WitchFlightDaylightSetup
{
    const string RequestPath = "Temp/WitchFlightDaylightRestore.request";
    const string ProfilePath = "Assets/Scenes/Battle/Battle_Daylight14_Profile.asset";
    const string OriginalProfilePath = "Assets/Scenes/Battle/post Profile.asset";

    [InitializeOnLoadMethod]
    static void Initialize()
    {
        if (!File.Exists(RequestPath)) return;
        EditorApplication.delayCall += ApplyPending;
        EditorSceneManager.sceneOpened += SceneOpened;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    static void SceneOpened(Scene scene, OpenSceneMode mode) => ApplyPending();
    static void PlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) ApplyPending();
    }

    static void ApplyPending()
    {
        if (File.Exists(RequestPath) && !Apply()) return;
        Complete();
    }

    static void Complete()
    {
        if (File.Exists(RequestPath)) File.Delete(RequestPath);
        EditorSceneManager.sceneOpened -= SceneOpened;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
    }

    [MenuItem("Tools/WitchFlight/Restore Previous Battle Lighting")]
    static void ApplyFromMenu()
    {
        if (Apply()) Complete();
        else Debug.LogWarning("Open Battle in Edit Mode to restore the previous lighting.");
    }

    static bool Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Battle.unity");
        if (!scene.IsValid() || !scene.isLoaded) return false;
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(OriginalProfilePath);
        if (profile == null) return false;
        Light sun = null;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var candidate in root.GetComponentsInChildren<Light>(true))
            if (candidate.name == "light_sun" && candidate.type == LightType.Directional) sun = candidate;
        if (sun == null) return false;

        Undo.RecordObject(sun, "Restore previous daylight");
        sun.color = new Color(1f, .96f, .9f);
        sun.intensity = 1.25f;
        sun.useColorTemperature = false;
        sun.shadows = LightShadows.Hard;
        sun.shadowStrength = .88f;
        var additional = sun.GetComponent<UniversalAdditionalLightData>();
        if (additional != null)
        {
            Undo.RecordObject(additional, "Restore shadow quality");
            additional.softShadowQuality = SoftShadowQuality.UsePipelineSettings;
            EditorUtility.SetDirty(additional);
        }
        // Restore the previously enabled component only on this Battle sun.
        foreach (var behaviour in sun.GetComponents<MonoBehaviour>())
        {
            if (behaviour == null || behaviour.GetType().Name != "sui_demo_LightHandler") continue;
            Undo.RecordObject(behaviour, "Restore original light handler");
            var serialized = new SerializedObject(behaviour);
            serialized.FindProperty("dayIntensity").floatValue = 1.25f;
            serialized.FindProperty("dayColor").colorValue = sun.color;
            serialized.ApplyModifiedProperties();
            behaviour.enabled = true;
            EditorUtility.SetDirty(behaviour);
        }

        var previousScene = SceneManager.GetActiveScene();
        try
        {
            SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.white;
            RenderSettings.ambientEquatorColor = new Color(.7058824f, .5803922f, .48235294f);
            RenderSettings.ambientGroundColor = new Color(.30980393f, .30980393f, .4509804f);
            RenderSettings.fogColor = new Color(.57796365f, .96089345f, .9622642f);
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = 550f;
        }
        finally { if (previousScene.IsValid()) SceneManager.SetActiveScene(previousScene); }

        foreach (var root in scene.GetRootGameObjects())
        foreach (var volume in root.GetComponentsInChildren<Volume>(true))
        {
            string path = AssetDatabase.GetAssetPath(volume.sharedProfile);
            if (!volume.isGlobal || (path != OriginalProfilePath && path != ProfilePath)) continue;
            Undo.RecordObject(volume, "Restore previous color grading");
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
        }
        EditorUtility.SetDirty(sun);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Previous Battle lighting restored. Sun rotation, skybox and lens flare preserved. Save Battle to keep the restored setup.", sun);
        return true;
    }
}
