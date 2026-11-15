using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Authored Resources prefab survives the Main -> Battle transition. No runtime UI layout.
public sealed class BattleLoadingUI : MonoBehaviour
{
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Slider progressSlider;
    private static BattleLoadingUI instance;
    private bool preparing, assetsReady;
    private readonly List<Object> retainedAssets = new();
    private static readonly string[] resourceNames = {
        "MagicStatTable", "BasicAttackSettings", "EquipmentStatTable", "FlightCombatTable",
        "PerkStatTable", "ProfileImageTable", "CatCompanionTable", "EmoteTable",
        "BattleFaceExpressions", "BattleAudioTable", "BattleResultsTable"
    };
    public static bool AssetsReady => instance != null && instance.assetsReady;

    public static void Show(string message = "전투 리소스를 불러오는 중...")
    {
        if (instance == null)
        {
            var prefab = Resources.Load<BattleLoadingUI>("BattleLoadingUI");
            if (prefab == null) { Debug.LogError("Resources/BattleLoadingUI prefab is missing."); return; }
            instance = Instantiate(prefab);
            DontDestroyOnLoad(instance.gameObject);
        }
        if (!instance.gameObject.activeSelf) instance.SetProgressInternal(instance.assetsReady ? .7f : 0f, false);
        instance.gameObject.SetActive(true);
        SetStatus(message);
        if (!instance.assetsReady && !instance.preparing)
        {
            instance.preparing = true;
            instance.StartCoroutine(instance.PrepareAssets());
        }
    }

    public static void SetStatus(string message)
    {
        if (instance != null && instance.statusText != null && instance.statusText.text != "로딩중")
            instance.statusText.text = "로딩중";
    }

    // Stage progress, not an estimated download percentage. Never finish before the peer barrier.
    public static void SetProgress(float value)
    {
        if (instance != null) instance.SetProgressInternal(value, true);
    }

    private void SetProgressInternal(float value, bool monotonic)
    {
        if (progressSlider == null) return;
        progressSlider.SetValueWithoutNotify(monotonic ? Mathf.Max(progressSlider.value, Mathf.Clamp01(value)) : Mathf.Clamp01(value));
    }

    public static void Hide()
    {
        if (instance != null) instance.gameObject.SetActive(false);
    }

    private IEnumerator PrepareAssets()
    {
        // Give the overlay a rendered frame before requesting any large dependency graph.
        yield return null;
        yield return null;
        for (int i = 0; i < resourceNames.Length; i++)
        {
            SetStatus($"전투 리소스 준비 중... ({i + 1}/{resourceNames.Length})");
            ResourceRequest request = Resources.LoadAsync<Object>(resourceNames[i]);
            while (!request.isDone)
            {
                SetProgress(.65f * (i + request.progress) / resourceNames.Length);
                yield return null;
            }
            if (request.asset == null)
            {
                SetStatus("전투 리소스를 불러오지 못했습니다.");
                Debug.LogError($"Required battle resource missing: {resourceNames[i]}");
                preparing = false;
                yield break; // Never acknowledge readiness with missing assets.
            }
            if (!retainedAssets.Contains(request.asset)) retainedAssets.Add(request.asset);
            SetProgress(.65f * (i + 1) / resourceNames.Length);
            yield return null;
        }
        BattleAudio.Initialize();
        var table = BattleAudio.Table;
        var clips = new HashSet<AudioClip>();
        foreach (BattleSound sound in new[] { BattleSound.MatchIntro, BattleSound.VSVoice, BattleSound.Countdown })
        {
            var cue = table != null ? table.Get(sound) : null;
            if (cue == null) continue;
            if (cue.clip != null) clips.Add(cue.clip);
            if (cue.variations != null) foreach (var clip in cue.variations) if (clip != null) clips.Add(clip);
        }
        foreach (var clip in clips)
        {
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            while (clip.loadState == AudioDataLoadState.Loading) yield return null;
            if (clip.loadState == AudioDataLoadState.Failed)
                Debug.LogWarning($"Battle intro audio could not be loaded: {clip.name}", clip);
        }
        // Scene activation, spawning and portrait rendering have their own later readiness checks.
        yield return null;
        assetsReady = true;
        SetProgress(.7f);
        preparing = false;
        SetStatus("캐릭터와 VS 화면을 준비하는 중...");
    }

    private void OnDisable() { preparing = false; }
    private void OnDestroy() { if (instance == this) instance = null; }
}
