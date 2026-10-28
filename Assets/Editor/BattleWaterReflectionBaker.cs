using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Editor-only: the game samples this saved cubemap; it never renders these six views.
public static class BattleWaterReflectionBaker
{
    public const string OutputPath = "Assets/VFX/SkyBox/Texture/BattleWaterReflection.asset";

    [MenuItem("Tools/WitchFlight/Bake Battle Water Reflection", true)]
    static bool CanBake() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Tools/WitchFlight/Bake Battle Water Reflection")]
    public static void BakeOpenBattle()
    {
        var battle = SceneManager.GetSceneByName("Battle");
        if (!battle.IsValid() || !battle.isLoaded)
            throw new InvalidOperationException("Open the Battle scene before baking its water reflection.");
        IslandAtmosphere clouds = null;
        foreach (var candidate in UnityEngine.Object.FindObjectsByType<IslandAtmosphere>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (candidate.gameObject.scene == battle && new SerializedObject(candidate).FindProperty("kind").enumValueIndex == 0)
            { clouds = candidate; break; }
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/VFX/SkyBox/Material/BattleCloudSky.mat");
        Bake(sky, clouds, new Vector3(0, -30, 0), OutputPath);
        Debug.Log("Battle water reflection baked. The existing cubemap reference is preserved.");
    }

    public static void Bake(Material sky, IslandAtmosphere clouds, Vector3 capturePosition, string path)
    {
        if (sky == null) throw new ArgumentNullException(nameof(sky));
        const int size = 256;
        var scene = EditorSceneManager.NewPreviewScene();
        var previousRT = RenderTexture.active;
        Material frozenSky = null;
        RenderTexture target = null;
        Texture2D readback = null;
        Cubemap cube = null;
        Mesh cloudMesh = null;
        try
        {
            frozenSky = new Material(sky) { hideFlags = HideFlags.HideAndDontSave };
            frozenSky.SetFloat("_UseSyncedTime", 1);
            frozenSky.SetFloat("_SkyTimeHours", sky.GetFloat("_MaterialTimeHours"));
            frozenSky.SetFloat("_SkyAnimationSeconds", 0);
            var cameraObject = new GameObject("Water reflection bake camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 90; camera.aspect = 1;
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 2000;
            camera.transform.position = capturePosition;
            cameraObject.AddComponent<Skybox>().material = frozenSky;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            camera.GetUniversalAdditionalCameraData().renderShadows = false;

            if (clouds != null)
            {
                var root = new GameObject("Frozen cloud banks"); root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetPositionAndRotation(clouds.transform.position, clouds.transform.rotation);
                root.transform.localScale = clouds.transform.lossyScale;
                var source = new GameObject("Cloud material source");
                source.transform.SetParent(root.transform, false);
                source.AddComponent<ParticleSystem>();
                var copy = root.AddComponent<IslandAtmosphere>();
                EditorUtility.CopySerialized(clouds, copy);
                root.SetActive(true);
                copy.SendMessage("OnEnable"); // runtime component, explicitly evaluated once in this preview scene
                // Freeze billboards toward the capture point once. Otherwise each
                // cubemap face turns them differently and leaves visible face seams.
                var batch = root.GetComponentInChildren<ParticleSystem>();
                if (batch != null)
                {
                    var items = new ParticleSystem.Particle[batch.particleCount];
                    int count = batch.GetParticles(items);
                    var vertices = new Vector3[count * 4];
                    var uv = new Vector2[count * 4];
                    var colors = new Color32[count * 4];
                    var triangles = new int[count * 6];
                    for (int index = 0; index < count; index++)
                    {
                        var item = items[index];
                        var position = batch.transform.TransformPoint(item.position);
                        var rotation = Quaternion.LookRotation((capturePosition - position).normalized, Vector3.up)
                            * Quaternion.AngleAxis(-item.rotation, Vector3.forward);
                        var halfSize = Vector3.Scale(item.startSize3D, batch.transform.lossyScale) * 0.5f;
                        for (int corner = 0; corner < 4; corner++)
                        {
                            float x = corner == 1 || corner == 2 ? 1 : -1;
                            float y = corner >= 2 ? 1 : -1;
                            int vertex = index * 4 + corner;
                            vertices[vertex] = position + rotation * new Vector3(x * halfSize.x, y * halfSize.y, 0);
                            uv[vertex] = new Vector2((x + 1) * 0.5f, (y + 1) * 0.5f);
                            colors[vertex] = item.startColor;
                        }
                        int v = index * 4, t = index * 6;
                        triangles[t] = v; triangles[t+1] = v+1; triangles[t+2] = v+2;
                        triangles[t+3] = v; triangles[t+4] = v+2; triangles[t+5] = v+3;
                    }
                    cloudMesh = new Mesh { name = "Fixed cloud reflection geometry", vertices = vertices, uv = uv, colors32 = colors, triangles = triangles };
                    cloudMesh.RecalculateBounds();
                    var fixedClouds = new GameObject("Fixed clouds");
                    SceneManager.MoveGameObjectToScene(fixedClouds, scene);
                    fixedClouds.AddComponent<MeshFilter>().sharedMesh = cloudMesh;
                    var particleRenderer = batch.GetComponent<ParticleSystemRenderer>();
                    fixedClouds.AddComponent<MeshRenderer>().sharedMaterial = particleRenderer.sharedMaterial;
                    particleRenderer.enabled = false;
                }
            }

            target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            target.Create();
            readback = new Texture2D(size, size, TextureFormat.RGBAHalf, false, true);
            cube = new Cubemap(size, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.MipChain)
            { name = "BattleWaterReflection", filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            var forwards = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var ups = new[] { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };
            for (int face = 0; face < 6; face++)
            {
                camera.transform.rotation = Quaternion.LookRotation(forwards[face], ups[face]);
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    throw new InvalidOperationException("An active URP renderer is required to bake the water reflection.");
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, size, size), 0, 0); readback.Apply(false);
                // Render requests read back bottom-up, whereas GPU cubemap faces
                // use top-down rows. Match their orientation before generating mips.
                var pixels = readback.GetPixels();
                var flipped = new Color[pixels.Length];
                for (int y = 0; y < size; y++)
                    Array.Copy(pixels, y * size, flipped, (size - 1 - y) * size, size);
                cube.SetPixels(flipped, (CubemapFace)face);
            }
            cube.Apply(true, false);
            var existing = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (existing != null) { EditorUtility.CopySerialized(cube, existing); EditorUtility.SetDirty(existing); }
            else { AssetDatabase.CreateAsset(cube, path); cube = null; }
            AssetDatabase.SaveAssets();
        }
        finally
        {
            RenderTexture.active = previousRT;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (cube != null) UnityEngine.Object.DestroyImmediate(cube);
            EditorSceneManager.ClosePreviewScene(scene);
            if (cloudMesh != null) UnityEngine.Object.DestroyImmediate(cloudMesh);
            if (frozenSky != null) UnityEngine.Object.DestroyImmediate(frozenSky);
        }
    }
}
