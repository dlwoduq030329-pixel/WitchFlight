using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Real Unity layout/mesh/render check in an isolated preview scene; no gameplay/login needed.
[InitializeOnLoad]
public static class WitchFlightRearViewValidation
{
    private const string Request = "Temp/WitchFlightRearView.validate";
    private const string Folder = "Temp/RearViewValidation";
    static WitchFlightRearViewValidation() => EditorApplication.update += Tick;
    private static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        File.Delete(Request);
        Validate();
    }

    [MenuItem("Tools/WitchFlight/Validate Rear View Rendering")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Folder);
        var log = new StringBuilder();
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture rt = null;
        Texture2D pixels = null;
        try
        {
            var settings = FlightCombatTable.Default;
            log.AppendLine("Settings loaded=" + (settings != null));
            log.AppendLine($"Left viewport={settings.rearViewRect}, right viewport={settings.rightThreatViewRect}");
            // Check Unity's deserialized values, not the numeric text in YAML: a Rect
            // without serializedVersion: 2 can silently load as an empty rectangle.
            if (settings.rearViewRect.width <= 0f || settings.rearViewRect.height <= 0f ||
                settings.rightThreatViewRect.width <= 0f || settings.rightThreatViewRect.height <= 0f)
                throw new InvalidOperationException("Rear view Rect deserialized with no drawable area. Check the asset serialization version.");
            Type type = typeof(RearThreatCamera);
            Type viewType = type.GetNestedType("View", BindingFlags.NonPublic);
            object view = Activator.CreateInstance(viewType, true);
            type.GetMethod("CreateDefaultPanel", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { view, false, settings });
            GameObject canvasRoot = (GameObject)viewType.GetField("canvas").GetValue(view);
            SceneManager.MoveGameObjectToScene(canvasRoot, scene);
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            RectTransform panel = (RectTransform)viewType.GetField("panel").GetValue(view);
            RawImage image = (RawImage)viewType.GetField("image").GetValue(view);
            // Use an opaque test texture so mask/geometry failure cannot be confused with world-camera failure.
            image.texture = Texture2D.whiteTexture;
            image.color = new Color(.1f, .7f, 1f, 1f);
            Rect requested = settings.rearViewRect;
            panel.anchorMin = new Vector2(requested.xMin, requested.yMin);
            panel.anchorMax = new Vector2(requested.xMax, requested.yMax);
            Canvas.ForceUpdateCanvases();
            log.AppendLine($"Overlay canvas={((RectTransform)canvas.transform).rect} panel={panel.rect} image={image.rectTransform.rect} position={panel.position}");
            foreach (Graphic graphic in canvasRoot.GetComponentsInChildren<Graphic>())
            {
                graphic.Rebuild(CanvasUpdate.PreRender);
                Mesh mesh = graphic.canvasRenderer.GetMesh();
                log.AppendLine($"{graphic.name}: rect={graphic.rectTransform.rect} vertices={(mesh != null ? mesh.vertexCount : 0)} triangles={(mesh != null ? mesh.triangles.Length : 0)} culled={graphic.canvasRenderer.cull} color={graphic.color}");
            }
            var cameraRoot = new GameObject("Rear UI validation camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            Camera camera = cameraRoot.GetComponent<Camera>();
            camera.enabled = false;
            camera.cameraType = CameraType.Game;
            camera.scene = scene;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.15f, .15f, .15f, 1);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.orthographic = true;
            camera.orthographicSize = 5;
            rt = new RenderTexture(960, 540, 24);
            rt.Create();
            camera.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 2;
            Canvas.ForceUpdateCanvases();
            log.AppendLine($"Render canvas={((RectTransform)canvas.transform).rect} panel={panel.rect} image={image.rectTransform.rect}");
            if (GraphicsSettings.currentRenderPipeline != null)
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
            else camera.Render();
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                pixels = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Folder + "/panel.png", pixels.EncodeToPNG());
                int visiblePixels = 0;
                foreach (Color32 pixel in pixels.GetPixels32())
                    if (pixel.b > 180 && pixel.g > 100 && pixel.r < 100) visiblePixels++;
                log.AppendLine($"Visible masked test-image pixels={visiblePixels}");
                if (visiblePixels < 100) throw new InvalidOperationException("Rear panel did not render the test image.");
                log.AppendLine("PASS: viewport deserialization, generated panel geometry and stencil rendering.");
            }
            finally { RenderTexture.active = previous; }
            canvasRoot.SetActive(false);
            ValidateEffectRendering(scene, log);
        }
        catch (Exception e) { log.AppendLine(e.ToString()); }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            File.WriteAllText(Folder + "/report.txt", log.ToString());
        }
    }

    private static void ValidateEffectRendering(Scene scene, StringBuilder log)
    {
        var root = new GameObject("Threat VFX render probe", typeof(Camera));
        SceneManager.MoveGameObjectToScene(root, scene);
        Camera camera = root.GetComponent<Camera>();
        camera.enabled = false;
        camera.scene = scene;
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
        camera.transform.position = new Vector3(0, 0, -8);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.16f, .2f, .25f, 1);
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 30;
        camera.fieldOfView = 60;
        camera.aspect = 640f / 384f;
        var data = camera.GetUniversalAdditionalCameraData();
        data.renderShadows = false;

        var sourceRoot = new GameObject("Threat source settings probe", typeof(Camera));
        SceneManager.MoveGameObjectToScene(sourceRoot, scene);
        Camera source = sourceRoot.GetComponent<Camera>();
        source.enabled = false;
        source.allowHDR = true;
        source.backgroundColor = camera.backgroundColor;
        var sourceData = source.GetUniversalAdditionalCameraData();
        sourceData.renderPostProcessing = true;
        sourceData.volumeLayerMask = 1 << 30;
        var volumeRoot = new GameObject("Threat bloom probe", typeof(Volume));
        SceneManager.MoveGameObjectToScene(volumeRoot, scene);
        volumeRoot.layer = 30;
        var volume = volumeRoot.GetComponent<Volume>();
        volume.isGlobal = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = profile;
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(.5f);
        bloom.intensity.Override(2f);
        var rt = new RenderTexture(640, 384, 24);
        Texture2D pixels = null;
        try
        {
            string[] paths = { "Assets/VFX/Magic/Prefabs/PS_Projectile_Fire_02.prefab", "Assets/VFX/Magic/Prefabs/PS_Projectile_Ice_02.prefab" };
            for (int i = 0; i < paths.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (prefab == null) throw new InvalidOperationException("Missing VFX probe prefab: " + paths[i]);
                var effect = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                effect.transform.position = new Vector3(i == 0 ? -1.5f : 1.5f, 0, 0);
                foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles.useAutoRandomSeed = false;
                    particles.randomSeed = 123;
                    particles.Simulate(.7f, false, true, false);
                }
            }
            rt.Create();
            pixels = new Texture2D(640, 384, TextureFormat.RGB24, false);
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 0)
                {
                    camera.allowHDR = false;
                    data.renderPostProcessing = false;
                    data.requiresColorOption = data.requiresDepthOption = CameraOverrideOption.Off;
                }
                else
                {
                    rt.Release();
                    rt.format = RenderTextureFormat.DefaultHDR;
                    rt.Create();
                    typeof(RearThreatCamera).GetMethod("SyncRenderSettings", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { source, camera });
                    if (!camera.allowHDR || !data.renderPostProcessing || data.volumeLayerMask != sourceData.volumeLayerMask ||
                        data.volumeTrigger != source.transform || data.requiresColorOption != CameraOverrideOption.On ||
                        data.requiresDepthOption != CameraOverrideOption.On)
                        throw new InvalidOperationException("Threat camera render settings did not synchronize.");
                }
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = rt;
                    pixels.ReadPixels(new Rect(0, 0, 640, 384), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(Folder + (pass == 0 ? "/effects-before.png" : "/effects-after.png"), pixels.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; }
            }
            log.AppendLine("PASS: real fire/ice prefab renders completed; HDR, post-processing, volume mask/trigger, color/depth textures synchronized. Compare effects-before/after.png visually.");
        }
        finally
        {
            rt.Release(); Object.DestroyImmediate(rt);
            if (pixels != null) Object.DestroyImmediate(pixels);
            volume.sharedProfile = null;
            foreach (var component in profile.components) Object.DestroyImmediate(component);
            Object.DestroyImmediate(profile);
        }
    }
}
