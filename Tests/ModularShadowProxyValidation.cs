// Run in the isolated Unity validation project, with a script-free copy of Battle.unity.
// Copy into its Assets/Editor folder, then -executeMethod ModularShadowProxyValidation.Run.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ModularShadowProxyValidation
{
    static int checks;
    static void Check(bool condition, string description)
    { if (!condition) throw new Exception(description); checks++; }
    static void Invoke(ModularShadowProxy proxy, string name) => typeof(ModularShadowProxy)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(proxy, null);
    static void Tick(ModularShadowProxy proxy) => Invoke(proxy, "LateUpdate");
    static void CheckCycle(ModularShadowProxy proxy)
    { for (int i = 0; i <= proxy.Bake.batches.Length; i++) Tick(proxy); }
    static long Indices(Mesh mesh) => Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i));
    static Camera camera;
    static ModularShadowProxy gpuProxy;
    static Material gpuMaterial;
    static Renderer floor;
    public static void VerifySceneHookup()
    {
        EditorSceneManager.OpenScene("Assets/MapAudit/ShadowHookup.unity", OpenSceneMode.Single);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorApplication.playModeStateChanged += OnPlayState;
        EditorApplication.isPlaying = true;
    }
    static void OnPlayState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        try
        {
            var proxy = UnityEngine.Object.FindFirstObjectByType<ModularShadowProxy>();
            Check(proxy != null && proxy.Bake != null, "Scene component and baked asset resolve");
            Check(proxy.ActiveBatchCount == 88 && proxy.CombinedSourceCount == 1809, "Real OnEnable activates the connected scene bake");
            proxy.enabled = false;
            Check(proxy.ActiveBatchCount == 0, "Real OnDisable restores originals");
            Check(proxy.GetComponentsInChildren<MeshRenderer>().Count(r => r.shadowCastingMode == ShadowCastingMode.On) == 2223, "All original casters restored in Play mode");
            proxy.enabled = true;
            Check(proxy.ActiveBatchCount == 88, "Re-enabling works before deferred destruction of the old proxies");
            proxy.gameObject.SetActive(false);
            Check(proxy.ActiveBatchCount == 0, "Disabling the map restores source state");
            proxy.gameObject.SetActive(true);
            Check(proxy.ActiveBatchCount == 88, "Reactivating the map restores optimization");
            Debug.Log("SHADOW_PROXY_SCENE_PLAY_OK checks=" + checks);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void Run()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/MapAudit/BattleAudit.unity", OpenSceneMode.Single);
            Transform root = scene.GetRootGameObjects().Single(g => g.name == "Map_ver03 (1)").transform;
            var all = root.GetComponentsInChildren<MeshRenderer>(true);
            int before = all.Count(r => r.enabled && r.gameObject.activeInHierarchy && r.shadowCastingMode == ShadowCastingMode.On);
            Directory.CreateDirectory("Assets/GeneratedShadows"); AssetDatabase.Refresh();
            var bake = ModularShadowBaker.Bake(root, "Assets/GeneratedShadows/BattleMapShadows.asset");
            Check(bake.batches.Length > 0, "Map bake cannot be empty");
            Check(bake.batches.Sum(b => b.sources.Length) > 1000, "Expected substantial modular wall coverage");
            foreach (var batch in bake.batches)
            {
                Check(Indices(batch.mesh) == batch.sources.Sum(s => Indices(s.mesh)), "No shadow triangles removed or filled");
                Check(batch.sources.All(s => ModularShadowProxy.Resolve(root, s.path).name == s.objectName), "All source paths resolve");
            }
            var proxy = root.gameObject.AddComponent<ModularShadowProxy>(); proxy.Bake = bake; Invoke(proxy, "Initialize");
            Check(proxy.ActiveBatchCount == bake.batches.Length, "All map batches activate");
            int combined = proxy.CombinedSourceCount;
            Check(all.All(r => !r.gameObject.activeInHierarchy || !r.enabled || r.shadowCastingMode != ShadowCastingMode.ShadowsOnly), "Visible modules remain visible");
            var batch0 = bake.batches[0];
            var source = ModularShadowProxy.Resolve(root, batch0.sources[0].path).GetComponent<MeshRenderer>();
            var second = ModularShadowProxy.Resolve(root, batch0.sources[1].path).GetComponent<MeshRenderer>();
            ModularShadowProxy.BeginSourceFade(source); ModularShadowProxy.BeginSourceFade(second);
            Check(source.shadowCastingMode == ShadowCastingMode.On && second.shadowCastingMode == ShadowCastingMode.On, "Fade restores original chunk casters");
            ModularShadowProxy.EndSourceFade(source);
            Check(source.shadowCastingMode == ShadowCastingMode.On, "Second fading wall keeps fallback active");
            ModularShadowProxy.EndSourceFade(second);
            Check(source.shadowCastingMode == ShadowCastingMode.Off, "Last restored wall resumes batching");
            source.enabled = false; Tick(proxy);
            Check(proxy.ActiveBatchCount == bake.batches.Length - 1, "Disabled source does not leave a phantom shadow");
            source.enabled = true; CheckCycle(proxy);
            Check(proxy.ActiveBatchCount == bake.batches.Length, "Re-enabled source resumes batch");
            var pos = source.transform.localPosition; source.transform.localPosition += Vector3.up; CheckCycle(proxy);
            Check(source.shadowCastingMode == ShadowCastingMode.On, "Moved source falls back");
            source.transform.localPosition = pos; CheckCycle(proxy);
            Check(source.shadowCastingMode == ShadowCastingMode.Off, "Restored transform resumes batch");
            var block = new MaterialPropertyBlock(); block.SetFloat("_Test", 1); source.SetPropertyBlock(block); CheckCycle(proxy);
            Check(source.shadowCastingMode == ShadowCastingMode.On, "Per-renderer material overrides preserved via fallback");
            source.SetPropertyBlock(null); CheckCycle(proxy);
            var materials = source.sharedMaterials; source.sharedMaterials = Array.Empty<Material>(); CheckCycle(proxy);
            Check(source.shadowCastingMode == ShadowCastingMode.On, "Changed material falls back");
            source.sharedMaterials = materials; CheckCycle(proxy);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 30; i++) Tick(proxy);
            watch.Stop();
            Debug.Log($"SHADOW_PROXY_MAP originalCasters={before} combinedSources={combined} chunks={bake.batches.Length} remainingCasters={before-combined+bake.batches.Length} validationMs={watch.Elapsed.TotalMilliseconds/30:0.000}");
            proxy.enabled = false;
            Invoke(proxy, "OnDisable"); // Edit-mode harness explicitly dispatches runtime callbacks.
            Check(all.Count(r => r.enabled && r.gameObject.activeInHierarchy && r.shadowCastingMode == ShadowCastingMode.On) == before, "Disabling restores all original shadow flags");
            Check(!root.GetComponentsInChildren<MeshRenderer>(true).Any(r => r.shadowCastingMode == ShadowCastingMode.ShadowsOnly), "Proxy objects cleaned up");
            UnityEngine.Object.DestroyImmediate(proxy);
            SetupGPU();
            EditorApplication.delayCall += Capture;
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static void SetupGPU()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        ShaderUtil.allowAsyncCompilation = false;
        QualitySettings.shadows = UnityEngine.ShadowQuality.All;
        QualitySettings.shadowDistance = 80;
        var pipeline = UniversalRenderPipelineAsset.Create(renderer);
        var settings = new SerializedObject(pipeline);
        settings.FindProperty("m_MainLightShadowsSupported").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo(); pipeline.shadowDistance = 80;
        pipeline.mainLightShadowmapResolution = 2048; pipeline.shadowCascadeCount = 1;
        pipeline.shadowDepthBias = .1f; pipeline.shadowNormalBias = .1f;
        GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientIntensity = 0; RenderSettings.ambientProbe = new SphericalHarmonicsL2();
        var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(25, 150, 0); light.shadows = LightShadows.Hard; light.intensity = 1;
        RenderSettings.sun = light;
        gpuMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); gpuMaterial.color = Color.white;
        gpuMaterial.SetFloat("_ReceiveShadows", 1); gpuMaterial.DisableKeyword("_RECEIVE_SHADOWS_OFF");
        floor = GameObject.CreatePrimitive(PrimitiveType.Plane).GetComponent<Renderer>();
        floor.transform.localScale = Vector3.one * 3; floor.sharedMaterial = gpuMaterial;
        var root = new GameObject("Proxy GPU test").transform;
        var boxes = new System.Collections.Generic.List<MeshRenderer>();
        foreach (float x in new[] { -3f, -2f, 2f, 3f })
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(root);
            cube.transform.position = new Vector3(x, 1, 0); cube.transform.localScale = new Vector3(1, 2, 1);
            var r = cube.GetComponent<MeshRenderer>(); r.sharedMaterial = gpuMaterial; boxes.Add(r);
        }
        // A deliberately open central gap catches a proxy that fills holes with a solid box.
        var combined = new Mesh(); combined.CombineMeshes(boxes.Select(r => new CombineInstance {
            mesh = r.GetComponent<MeshFilter>().sharedMesh, transform = r.localToWorldMatrix }).ToArray());
        var bake = ScriptableObject.CreateInstance<ModularShadowBake>();
        bake.batches = new[] { new ModularShadowBake.Batch { mesh = combined, materials = new[] { gpuMaterial }, renderingLayerMask = boxes[0].renderingLayerMask,
            sources = boxes.Select((r,i) => new ModularShadowBake.Source { path = i.ToString(), objectName = r.name,
                mesh = r.GetComponent<MeshFilter>().sharedMesh, matrix = r.localToWorldMatrix }).ToArray() } };
        gpuProxy = root.gameObject.AddComponent<ModularShadowProxy>(); gpuProxy.Bake = bake;
        camera = new GameObject("Shadow comparison").AddComponent<Camera>();
        camera.transform.position = new Vector3(8, 10, -12); camera.transform.LookAt(new Vector3(0, 0, 2));
        camera.targetTexture = new RenderTexture(640, 480, 24);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray;
        camera.GetUniversalAdditionalCameraData().renderShadows = true;
    }
    static Color32[] Pixels(string name)
    {
        camera.Render();
        RenderTexture.active = camera.targetTexture;
        var texture = new Texture2D(640, 480, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); texture.Apply();
        File.WriteAllBytes(Path.GetFullPath("../shadow-" + name + ".png"), texture.EncodeToPNG());
        var data = texture.GetPixels32(); UnityEngine.Object.DestroyImmediate(texture); RenderTexture.active = null;
        return data;
    }
    static void Capture()
    {
        try
        {
            Pixels("warmup"); var original = Pixels("original");
            Debug.Log("SHADOW_TEST params=" + Shader.GetGlobalVector("_MainLightShadowParams") + " supported=" + SystemInfo.supportsShadows + " quality=" + QualitySettings.shadows);
            Invoke(gpuProxy, "Initialize"); Check(gpuProxy.ActiveBatchCount == 1, "GPU batch active");
            var merged = Pixels("merged");
            int changed = 0;
            for (int i = 0; i < original.Length; i++)
                if (Mathf.Abs(original[i].r - merged[i].r) > 3 || Mathf.Abs(original[i].g - merged[i].g) > 3) changed++;
            Check(changed < original.Length / 200, "Merged shadow silhouette deviates by more than 0.5% pixels");
            gpuProxy.enabled = false;
            Invoke(gpuProxy, "OnDisable");
            foreach (var r in gpuProxy.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            var noShadow = Pixels("none"); int shadowPixels = 0;
            int maxDelta = 0;
            for (int i = 0; i < original.Length; i++) if (noShadow[i].r - original[i].r > 20) shadowPixels++;
            for (int i = 0; i < original.Length; i++) maxDelta = Mathf.Max(maxDelta, noShadow[i].r-original[i].r);
            Debug.Log($"SHADOW_TEST count={shadowPixels} maxDelta={maxDelta} keyword={Shader.IsKeywordEnabled("_MAIN_LIGHT_SHADOWS")} receive={floor.receiveShadows}");
            Check(shadowPixels > 500, "Test must actually render cast shadows");
            Debug.Log($"SHADOW_PROXY_GPU changedPixels={changed} shadowPixels={shadowPixels}");
            Debug.Log($"SHADOW_PROXY_VALIDATION_OK checks={checks}"); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
