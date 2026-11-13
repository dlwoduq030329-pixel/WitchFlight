// Run in an isolated Unity project with the production ReactiveShieldVfx copied in.
// Exercises native material-slot precedence and OnEnable/ReportHit, not a shader mock.
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ReactiveShieldPoolHitValidation
{
    private static int checks;
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorApplication.playModeStateChanged += OnPlay;
        EditorApplication.isPlaying = true;
    }
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; }

    private static void OnPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        try
        {
            var root = new GameObject("Pooled shield native regression");
            root.SetActive(false);
            root.AddComponent<MeshFilter>();
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Unlit/Color"));
            var shield = root.AddComponent<ReactiveShieldVfx>();
            root.transform.position = new Vector3(40, 20, 70);
            root.transform.rotation = Quaternion.Euler(0, 60, 0);
            root.transform.localScale = Vector3.one * 1.1f;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                // Exact restore order used by MagicVfxLease.Restore before activation.
                renderer.SetPropertyBlock(new MaterialPropertyBlock());
                renderer.SetPropertyBlock(new MaterialPropertyBlock(), 0);
                root.SetActive(true);
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, 0);
                Check(block.GetFloat("_ShieldRadius") == shield.Radius, "Shield properties must override the pool's material slot");
                Check(block.GetVectorArray("_ShieldHits")[0].w == 0, "Reused shield clears previous impact");
                Vector3 local = new Vector3(.25f, .15f, -.95f).normalized;
                shield.ReportHit(root.transform.TransformPoint(local * shield.Radius));
                renderer.GetPropertyBlock(block, 0);
                Vector4 hit = block.GetVectorArray("_ShieldHits")[0];
                Check(hit.w == 1 && Vector3.Distance(new Vector3(hit.x, hit.y, hit.z), local) < .0001f,
                    "Hit must reach the rendered material slot in local surface coordinates");
                Check(block.GetVectorArray("_ShieldTimings")[0].w == 1, "Impact timer is active");
                shield.SetBaseVisibility(0);
                renderer.GetPropertyBlock(block, 0);
                Check(block.GetFloat("_BaseVisibility") == 0 && block.GetVectorArray("_ShieldHits")[0].w == 1,
                    "Fading idle shield preserves red impact wave");
                shield.ReportHit(root.transform.TransformPoint(Vector3.right * shield.Radius));
                renderer.GetPropertyBlock(block, 0);
                Check(block.GetVectorArray("_ShieldHits")[1].w == 1, "Two same-frame attacks retain separate impacts");
                root.SetActive(false);
            }
            Debug.Log("REACTIVE_PARRY_NATIVE_OK checks=" + checks);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
