using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// One-shot editor setup also updates an already-open scene without reloading/saving it.
internal static class WitchFlightSunFlareSetup
{
    const string RequestPath = "Temp/WitchFlightSunFlareSetup.request";
    const string ScenePath = "Assets/Scenes/Battle.unity";
    const string DataPath = "Assets/URP_Flares_Pack/Prefabs/Sun/Sun_Flare_2.asset";

    [InitializeOnLoadMethod]
    static void Initialize()
    {
        if (!File.Exists(RequestPath)) return;
        EditorApplication.delayCall += ApplyPending;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnSceneOpened(Scene scene, OpenSceneMode mode) => ApplyPending();
    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) ApplyPending();
    }

    static void ApplyPending()
    {
        if (!File.Exists(RequestPath)) { Unsubscribe(); return; }
        if (Apply(false)) { File.Delete(RequestPath); Unsubscribe(); }
    }

    static void Unsubscribe()
    {
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    [MenuItem("Tools/WitchFlight/Apply SRP Sun Flare")]
    static void ApplyFromMenu()
    {
        if (Apply(true) && File.Exists(RequestPath)) File.Delete(RequestPath);
    }

    static bool Apply(bool showMissing)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            if (showMissing) Debug.LogWarning("Open the Battle scene first to apply the sun flare.");
            return false;
        }
        var data = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(DataPath);
        if (data == null) { Debug.LogWarning("Sun_Flare_2 asset is not imported yet."); return false; }
        foreach (var root in scene.GetRootGameObjects())
        foreach (var light in root.GetComponentsInChildren<Light>(true))
        {
            if (light.name != "light_sun" || light.type != LightType.Directional) continue;
            var flare = light.GetComponent<LensFlareComponentSRP>();
            if (flare == null) flare = Undo.AddComponent<LensFlareComponentSRP>(light.gameObject);
            Undo.RecordObject(flare, "Apply SRP Sun Flare");
            flare.enabled = true;
            flare.lensFlareData = data;
            flare.intensity = 0.45f;
            flare.scale = 0.75f;
            flare.useOcclusion = true;
            flare.occlusionRadius = 0.26f;
            flare.occlusionOffset = 0f;
            flare.sampleCount = 32;
            flare.allowOffScreen = false;
            flare.attenuationByLightShape = false;
            flare.lightOverride = null;
            flare.radialScreenAttenuationCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
            PrefabUtility.RecordPrefabInstancePropertyModifications(flare);
            EditorUtility.SetDirty(flare);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("SRP Sun_Flare_2 applied to light_sun. Save the Battle scene to keep this setup.", flare);
            return true;
        }
        Debug.LogWarning("Battle scene's directional light_sun was not found.");
        return false;
    }
}
