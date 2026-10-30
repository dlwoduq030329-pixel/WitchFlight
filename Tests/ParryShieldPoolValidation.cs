// Isolated Unity play-mode regression: real pool/lease, real Shield_01 controller,
// and the production HoldShieldPose method (copied into ParryShieldProduction).
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ParryShieldPoolValidation
{
    static GameObject prefab, view;
    static Transform shield;
    static int checks, cycle, firstId;
    static double holdUntil;
    static readonly Vector3 authoredScale = new(1.5f, 1.5f, 1.2f);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorApplication.playModeStateChanged += OnPlay;
        EditorApplication.isPlaying = true;
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        try
        {
            prefab = new GameObject("Shield fixture"); prefab.SetActive(false);
            var child = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            child.name = "Shield_03"; child.transform.SetParent(prefab.transform, false);
            child.transform.localScale = authoredScale;
            var animator = child.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/ParryValidation/Shield_01.controller");
            Check(animator.runtimeAnimatorController != null, "Real shield controller loaded");
            BeginHold();
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Fail(e); }
    }
    static void BeginHold()
    {
        view = MagicVfxPool.Rent(prefab, Vector3.zero, Quaternion.identity);
        shield = view.transform.Find("Shield_03");
        if (cycle == 0) firstId = view.GetInstanceID();
        else Check(view.GetInstanceID() == firstId, "Same lease reused");
        Check(shield.GetComponent<Animator>().enabled, "Pool restores animator for next use");
        Check(shield.localScale.sqrMagnitude < .0001f, "Reproduced zero-scale first animation frame");
        ParryShieldProduction.HoldShieldPose(view);
        Check(!shield.GetComponent<Animator>().enabled, "Hold disables reveal animator");
        Check(Vector3.Distance(shield.localScale, authoredScale) < .0001f, "Hold immediately restores prefab size");
        holdUntil = EditorApplication.timeSinceStartup + .45;
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < holdUntil) return;
        try
        {
            Check(Vector3.Distance(shield.localScale, authoredScale) < .0001f, "Shield stays visible throughout block window");
            MagicVfxPool.Release(view);
            view = MagicVfxPool.Rent(prefab, Vector3.zero, Quaternion.identity);
            var animator = view.GetComponentInChildren<Animator>();
            Check(animator.enabled, "Success animation works after held lease");
            animator.Update(.17f);
            Check(animator.transform.localScale.sqrMagnitude > 1f, "Success animation expands shield normally");
            MagicVfxPool.Release(view);
            if (++cycle < 3) { BeginHold(); return; }
            EditorApplication.update -= Tick;
            Debug.Log("PARRY_SHIELD_POOL_OK checks=" + checks);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Fail(e); }
    }
    static void Fail(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
}
