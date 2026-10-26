using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class WitchFlightMagicCircleRepair
{
    const string PrefabPath = "Assets/VFX/MaicCircle/PS_MagicCircle.prefab";
    [MenuItem("Tools/WitchFlight/Repair and Verify Magic Circle %#F5")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run outside Play mode.");
        var report = new StringBuilder();
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null || !shader.isSupported) throw new InvalidOperationException("URP particle shader unavailable.");
        foreach (string name in new[] { "magic_circle", "magic_circle_spin", "star" })
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/VFX/MaicCircle/material/" + name + ".mat");
            bool converted = mat.shader == shader;
            var tex = mat.GetTexture(converted ? "_BaseMap" : "_MainTex");
            var tint = converted ? mat.GetColor("_BaseColor") : mat.GetColor("_EmissionColor");
            mat.parent = null;
            mat.shader = shader;
            mat.shaderKeywords = Array.Empty<string>();
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0);
            mat.SetFloat("_Cull", 0); mat.SetFloat("_ZWrite", 0);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ColorMode", 0);
            mat.SetFloat("_SoftParticlesEnabled", 0); mat.SetFloat("_CameraFadingEnabled", 0);
            mat.SetFloat("_DistortionEnabled", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = 3000;
            mat.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(mat);
            report.AppendLine(name + ": URP unlit, two-sided, texture=" + tex);
        }
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.loop = true; main.playOnAwake = true; main.startDelay = 0;
                main.stopAction = ParticleSystemStopAction.None;
                if (ps.GetComponent<ParticleSystemRenderer>().renderMode == ParticleSystemRenderMode.Mesh)
                {
                    // The three mesh rings were authored with a 0.0001s lifetime.
                    main.startLifetime = main.duration;
                    main.maxParticles = 2;
                    ps.transform.localRotation = Quaternion.identity;
                    ps.transform.localScale = Vector3.one;
                }
                var emission = ps.emission; emission.enabled = true;
                if (ps.GetComponent<ParticleSystemRenderer>().renderMode == ParticleSystemRenderMode.Mesh)
                    emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Verify(report);
    }

    static void Verify(StringBuilder report)
    {
        Directory.CreateDirectory("Temp/MagicCircleVerification");
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        var cameraObject = new GameObject("Magic circle verification camera");
        var rt = new RenderTexture(512, 512, 24);
        var old = RenderTexture.active;
        try
        {
            root.transform.SetPositionAndRotation(new Vector3(10000,10000,10000), Quaternion.identity);
            root.transform.localScale = Vector3.one * 1.8f;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            var cam = cameraObject.AddComponent<Camera>(); cam.enabled = false;
            cam.transform.position = root.transform.position + new Vector3(0,0,-5);
            cam.transform.LookAt(root.transform);
            cam.orthographic = true; cam.orthographicSize = 2.5f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << 31; cam.nearClipPlane = .1f; cam.farClipPlane = 20;
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (float time in new[] { .02f, .35f, 6f })
            {
                foreach (var ps in systems)
                {
                    ps.Simulate(time, false, true, true);
                    if (ps.particleCount == 0) throw new Exception(ps.name + " empty at " + time);
                    report.AppendLine(ps.name + " t=" + time + " particles=" + ps.particleCount);
                    var pr = ps.GetComponent<ParticleSystemRenderer>();
                    report.AppendLine("renderer=" + pr.sharedMaterial.shader.name + " bounds=" + pr.bounds.size + " mesh=" + pr.mesh);
                }
                RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                var image = new Texture2D(512,512,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,512,512),0,0); image.Apply();
                int visible = 0;
                foreach (var pixel in image.GetPixels32()) if (pixel.g > 15 || pixel.b > 15) visible++;
                File.WriteAllBytes("Temp/MagicCircleVerification/circle-" + time.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ".png", image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                report.AppendLine("Visible pixels at " + time + ": " + visible);
                if (visible < 100) throw new Exception("No visible circle at " + time);
            }
            root.SetActive(false); root.transform.position += Vector3.right * 10; root.SetActive(true);
            foreach (var ps in systems)
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Play(false);
                ps.Simulate(.02f, false, false, true);
                if (ps.particleCount == 0 || ps.main.simulationSpace != ParticleSystemSimulationSpace.Local)
                    throw new Exception("Restart/follow failed: " + ps.name);
            }
            report.AppendLine("PASS: immediate emission, 0.35s visibility, sustained loop, restart and local follow.");
            Debug.Log(report.ToString());
        }
        catch (Exception e) { report.AppendLine("FAIL: " + e); Debug.LogException(e); }
        finally
        {
            RenderTexture.active = old;
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(cameraObject);
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            File.WriteAllText("Temp/MagicCircleVerification/report.txt", report.ToString());
        }
    }
}
