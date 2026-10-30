using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Lives beside BattleHud, not on the notification that is hidden between awards.
[DisallowMultipleComponent]
public sealed class BattleTitleNotificationUI : MonoBehaviour
{
    [SerializeField] private GameObject notificationRoot;
    [SerializeField] private TMP_Text explanationText;
    [SerializeField, Min(0f)] private float holdSeconds = 3f;
    [SerializeField, Min(0f)] private float fadeSeconds = 0.5f;
    private readonly Queue<int> pending = new();
    private readonly HashSet<int> notified = new();
    private CanvasGroup group;
    private Animator animator;
    private Coroutine presentation;

    private void Awake()
    {
        if (notificationRoot == null)
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name.Equals("GetChingho", StringComparison.OrdinalIgnoreCase) && IsUnderBattleUI(child))
                        notificationRoot = child.gameObject;
        if (notificationRoot == null) return;
        if (explanationText == null)
            foreach (TMP_Text text in notificationRoot.GetComponentsInChildren<TMP_Text>(true))
                if (text.name.Equals("chinghoEXplanation", StringComparison.OrdinalIgnoreCase)) explanationText = text;
        group = notificationRoot.GetComponent<CanvasGroup>() ?? notificationRoot.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        animator = notificationRoot.GetComponent<Animator>();
        notificationRoot.SetActive(false);
    }

    private static bool IsUnderBattleUI(Transform target)
    {
        for (Transform parent = target.parent; parent != null; parent = parent.parent)
            if (parent.name.Equals("BattleUI", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void OnEnable() => TitleProgression.TitleUnlocked += Enqueue;
    private void OnDisable()
    {
        TitleProgression.TitleUnlocked -= Enqueue;
        if (presentation != null) StopCoroutine(presentation);
        presentation = null;
        pending.Clear();
        if (notificationRoot != null) notificationRoot.SetActive(false);
    }

    private void Enqueue(int id)
    {
        if (TitleCatalog.Find(id) == null || notificationRoot == null || explanationText == null || !notified.Add(id)) return;
        pending.Enqueue(id);
    }

    private void Update()
    {
        if (presentation == null && pending.Count > 0 && notificationRoot != null &&
            (notificationRoot.transform.parent == null || notificationRoot.transform.parent.gameObject.activeInHierarchy))
            presentation = StartCoroutine(Present());
    }

    private IEnumerator Present()
    {
        while (pending.Count > 0)
        {
            int id = pending.Dequeue();
            group.alpha = 1f;
            explanationText.richText = false;
            explanationText.text = $"\"{TitleCatalog.Name(id)}\" 획득!";
            notificationRoot.SetActive(true);
            explanationText.gameObject.SetActive(true);
            explanationText.enabled = true;

            // Keep the authored opening animation, including its delayed text reveal.
            float openingSeconds = 0f;
            if (animator != null && animator.enabled && animator.runtimeAnimatorController != null)
            {
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.Rebind();
                animator.Update(0f);
                animator.Play(0, 0, 0f);
                animator.Update(0f);
                openingSeconds = animator.GetCurrentAnimatorStateInfo(0).length / Mathf.Max(0.01f, Mathf.Abs(animator.speed));
            }
            if (openingSeconds > 0f) yield return VisibleDelay(openingSeconds);
            explanationText.gameObject.SetActive(true);
            yield return VisibleDelay(holdSeconds);

            float elapsed = 0f;
            while (elapsed < fadeSeconds)
            {
                yield return null;
                if (!notificationRoot.activeInHierarchy) continue;
                elapsed += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(elapsed / fadeSeconds);
            }
            group.alpha = 0f;
            notificationRoot.SetActive(false);
        }
        presentation = null;
    }

    private IEnumerator VisibleDelay(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            yield return null;
            if (notificationRoot.activeInHierarchy) elapsed += Time.unscaledDeltaTime;
        }
    }
}
