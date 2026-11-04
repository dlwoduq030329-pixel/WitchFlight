using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// One owner for the authored intro, click-to-enter prompt and login transition.
// Does not change the prefab's saved events or any authentication state.
public sealed class LoginIntroTransition : IDisposable
{
    private readonly GameObject intro, prompt, form;
    private readonly Button skip, enter;
    private readonly CanvasGroup promptGroup;
    private readonly DOTweenAnimation[] introAnimations;
    private readonly DOTweenAnimation[] promptAnimations;
    private readonly List<Action> restoreEvents = new();
    private Tween fade;
    private bool introFinished, loginOpened;

    public static LoginIntroTransition TryCreate(Transform root, GameObject form)
    {
        if (root == null || form == null) return null;
        Transform intro = Find(root, "IntroAction"), prompt = Find(root, "ClickUI");
        Button enter = prompt != null ? Find(prompt, "ClcikButton")?.GetComponent<Button>() : null;
        Button skip = intro != null ? Find(intro, "SkipButton")?.GetComponent<Button>() : null;
        Transform ending = intro != null ? Find(intro, "ExhibitionText") : null;
        DOTweenAnimation end = ending != null ? ending.GetComponent<DOTweenAnimation>() : null;
        if (intro == null || prompt == null || enter == null || skip == null || end == null) return null;
        return new LoginIntroTransition(intro.gameObject, prompt.gameObject, form, enter, skip, end);
    }

    private LoginIntroTransition(GameObject intro, GameObject prompt, GameObject form,
        Button enter, Button skip, DOTweenAnimation end)
    {
        this.intro = intro; this.prompt = prompt; this.form = form; this.enter = enter; this.skip = skip;
        promptGroup = prompt.GetComponent<CanvasGroup>();
        if (promptGroup == null) promptGroup = prompt.AddComponent<CanvasGroup>();
        introAnimations = intro.GetComponentsInChildren<DOTweenAnimation>(true);
        promptAnimations = prompt.GetComponentsInChildren<DOTweenAnimation>(true);

        // Disable only the old visibility/transition calls; keep unrelated designer events.
        Replace(skip.onClick, target => target == intro, FinishIntro);
        Replace(end.onComplete, target => target == intro || target == prompt, FinishIntro);
        Replace(enter.onClick, target => target == form || target == prompt || target == enter.gameObject ||
            target is DOTweenAnimation animation && animation.transform.IsChildOf(prompt.transform), EnterLogin);
    }

    private void Replace(UnityEvent events, Func<UnityEngine.Object, bool> ownedTarget, UnityAction replacement)
    {
        for (int i = 0; i < events.GetPersistentEventCount(); i++)
        {
            string method = events.GetPersistentMethodName(i);
            if ((method != "SetActive" && method != "DORestart") || !ownedTarget(events.GetPersistentTarget(i))) continue;
            int index = i;
            UnityEventCallState previous = events.GetPersistentListenerState(i);
            events.SetPersistentListenerState(i, UnityEventCallState.Off);
            restoreEvents.Add(() => events.SetPersistentListenerState(index, previous));
        }
        events.AddListener(replacement);
        restoreEvents.Add(() => events.RemoveListener(replacement));
    }

    public void ShowInitial()
    {
        form.SetActive(false);
        promptGroup.alpha = 1;
        promptGroup.interactable = promptGroup.blocksRaycasts = true;
        enter.gameObject.SetActive(true);
        prompt.SetActive(true);
        if (!intro.activeSelf) FinishIntro();
    }

    public void FinishIntro()
    {
        if (introFinished || loginOpened) return;
        introFinished = true;
        StopIntro();
        promptGroup.alpha = 1;
        promptGroup.interactable = promptGroup.blocksRaycasts = true;
        enter.gameObject.SetActive(true);
        prompt.SetActive(true);
    }

    private void EnterLogin() => OpenLogin(true);

    public void OpenLogin(bool animate = false)
    {
        loginOpened = introFinished = true;
        StopIntro();
        fade?.Kill();
        // Alpha alone does not prevent an invisible full-screen Image intercepting input.
        promptGroup.interactable = promptGroup.blocksRaycasts = false;
        enter.gameObject.SetActive(false);
        foreach (var animation in promptAnimations) if (animation != null) animation.tween?.Kill();
        form.SetActive(true);
        if (animate && prompt.activeInHierarchy && promptGroup.alpha > 0)
            fade = promptGroup.DOFade(0, 1f).SetUpdate(true).OnComplete(() => prompt.SetActive(false));
        else { promptGroup.alpha = 0; prompt.SetActive(false); }
    }

    public void Dismiss()
    {
        loginOpened = introFinished = true;
        StopIntro();
        fade?.Kill();
        promptGroup.interactable = promptGroup.blocksRaycasts = false;
        prompt.SetActive(false);
    }

    private void StopIntro()
    {
        foreach (var animation in introAnimations) if (animation != null) animation.tween?.Kill();
        intro.SetActive(false);
    }

    public void Dispose()
    {
        fade?.Kill();
        foreach (var restore in restoreEvents) restore();
        restoreEvents.Clear();
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }
}
