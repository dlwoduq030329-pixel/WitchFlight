using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Independent of the 3D camera: the same authored menu works in both lobby views.
[DefaultExecutionOrder(1000)]
public sealed class LobbyEscapeMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    private GameObject menu, settings;
    private DOTweenAnimation[] entrance = System.Array.Empty<DOTweenAnimation>();
    private bool needsBind = true, menuWasVisible;
    private CursorLockMode savedLock;
    private bool savedVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        IsOpen = false;
        var host = new GameObject("Lobby menu controller");
        DontDestroyOnLoad(host);
        host.AddComponent<LobbyEscapeMenu>();
    }
    private void OnEnable() => SceneManager.sceneLoaded += SceneLoaded;
    private void OnDisable()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        IsOpen = false;
    }
    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        needsBind = true;
        menuWasVisible = false;
        IsOpen = false;
    }
    private void Bind()
    {
        needsBind = false;
        menu = settings = null;
        entrance = System.Array.Empty<DOTweenAnimation>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            bool battle = false;
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<BattleHud>(true) != null) { battle = true; break; }
            if (battle) continue;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == "MenuUI") menu = child.gameObject;
                    if (child.name == "SettingUI") settings = child.gameObject;
                }
        }
        if (menu == null) return;
        var background = menu.transform.Find("Background");
        if (background != null) entrance = background.GetComponents<DOTweenAnimation>();
        foreach (var animation in entrance)
        {
            animation.autoKill = false;
            animation.autoPlay = false;
            if (animation.tween != null && animation.tween.IsActive()) animation.tween.SetAutoKill(false).Pause();
        }
        var canvas = menu.GetComponent<Canvas>();
        if (canvas == null) canvas = menu.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 300;
        if (menu.GetComponent<GraphicRaycaster>() == null) menu.AddComponent<GraphicRaycaster>();
    }
    private void RestartEntrance()
    {
        foreach (var animation in entrance)
        {
            if (animation == null || !animation.isActiveAndEnabled || !animation.isActive || !animation.isValid) continue;
            animation.CreateTween(false, false);
            if (animation.tween != null && animation.tween.IsActive())
                animation.tween.SetAutoKill(false).SetUpdate(true).Restart();
        }
    }
    private void LateUpdate()
    {
        if (needsBind) Bind();
        if (NetworkGameManager.Instance != null && NetworkGameManager.Instance.IsBattleSceneLoaded)
        { IsOpen = false; return; }
        if (menu == null) return;
        if (Input.GetKeyDown(KeyCode.Escape) && !MainRoomChatUI.BlocksLobbyInput)
        {
            if (settings != null && settings.activeInHierarchy)
            { settings.SetActive(false); menu.SetActive(true); }
            else menu.SetActive(!menu.activeSelf);
        }
        bool visible = menu.activeInHierarchy;
        bool open = visible || (settings != null && settings.activeInHierarchy);
        if (open && !IsOpen) { savedLock = Cursor.lockState; savedVisible = Cursor.visible; }
        if (visible && !menuWasVisible) RestartEntrance();
        if (!visible && menuWasVisible)
            foreach (var animation in entrance)
                if (animation != null && animation.tween != null && animation.tween.IsActive()) animation.tween.Pause();
        if (open) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        else if (IsOpen) { Cursor.lockState = savedLock; Cursor.visible = savedVisible; }
        menuWasVisible = visible;
        IsOpen = open;
    }
}
