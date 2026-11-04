using System;
using UnityEngine;

// Scene-owned references. Requests keep their own handle so overlapping work cannot hide each other.
public sealed class MainLoadingUI : MonoBehaviour
{
    [SerializeField] private GameObject introLoading;
    [SerializeField] private GameObject lobbyLoading;
    private static MainLoadingUI instance;
    private int introRequests;
    private int lobbyRequests;

    private void Awake()
    {
        instance = this;
        SetVisible(introLoading, false);
        SetVisible(lobbyLoading, false);
    }

    public static IDisposable BeginIntro() => Begin(true);
    public static IDisposable BeginLobby() => Begin(false);

    private static IDisposable Begin(bool intro)
    {
        if (instance == null) return null;
        if (intro) instance.introRequests++;
        else instance.lobbyRequests++;
        SetVisible(intro ? instance.introLoading : instance.lobbyLoading, true);
        return new Request(instance, intro);
    }

    private void End(bool intro)
    {
        if (intro) SetVisible(introLoading, --introRequests > 0);
        else SetVisible(lobbyLoading, --lobbyRequests > 0);
    }

    private static void SetVisible(GameObject target, bool visible)
    {
        if (target == null) return;
        if (visible) target.transform.SetAsLastSibling();
        target.SetActive(visible);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private sealed class Request : IDisposable
    {
        private MainLoadingUI owner;
        private readonly bool intro;

        public Request(MainLoadingUI owner, bool intro)
        {
            this.owner = owner;
            this.intro = intro;
        }

        public void Dispose()
        {
            if (owner != null) owner.End(intro);
            owner = null;
        }
    }
}
