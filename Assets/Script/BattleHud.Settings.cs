using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class BattleHud
{
    [Header("Shared settings UI")]
    [SerializeField] private GameObject settingsPrefab;
    [SerializeField] private GameObject settingsPanel;
    private GameObject battleSettings;
    private bool leavingFromMenu;
    private DOTweenAnimation[] menuEntranceTweens = System.Array.Empty<DOTweenAnimation>();

    private void BindSceneMenu()
    {
        // Include inactive panels, and only search the BattleHud's own scene.
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (menuPanel == null && string.Equals(child.name, "MenuUI", System.StringComparison.OrdinalIgnoreCase))
                    menuPanel = child.gameObject;
                if (settingsPanel == null && string.Equals(child.name, "SettingUI", System.StringComparison.OrdinalIgnoreCase))
                    settingsPanel = child.gameObject;
            }
        if (menuPanel != null)
        {
            // Background owns the slide/fade entrance. TabImage tweens belong to button hover.
            Transform background = menuPanel.transform.Find("Background");
            if (background != null)
                menuEntranceTweens = background.GetComponents<DOTweenAnimation>();
            foreach (DOTweenAnimation animation in menuEntranceTweens)
            {
                animation.autoPlay = false;
                animation.autoKill = false;
                if (animation.tween != null && animation.tween.IsActive())
                    animation.tween.SetAutoKill(false).Pause();
            }
            foreach (Button button in menuPanel.GetComponentsInChildren<Button>(true))
            {
                if (button.name != "Button01" && button.name != "Button02" &&
                    button.name != "Button03" && button.name != "BackButton") continue;
                // Replace old SetActive/quit navigation. Hover EventTriggers remain intact.
                button.onClick = new Button.ButtonClickedEvent();
                if (button.name == "Button01") resumeButton = button;
                else if (button.name == "BackButton") button.onClick.AddListener(Resume);
                else if (button.name == "Button02") button.onClick.AddListener(OpenSettings);
                else button.onClick.AddListener(LeaveBattleFromMenu);
            }
            PrepareMenuCanvas(menuPanel, 300);
            menuPanel.SetActive(false);
        }
        battleSettings = settingsPanel;
        if (battleSettings != null)
        {
            BindSettingsBackButton();
            battleSettings.SetActive(false);
        }
    }

    private void RestartMenuEntrance()
    {
        foreach (DOTweenAnimation animation in menuEntranceTweens)
        {
            if (animation == null || !animation.isActiveAndEnabled || !animation.isActive || !animation.isValid) continue;
            // Reuse the tween so closing halfway through cannot change its original endpoint.
            animation.CreateTween(false, false);
            if (animation.tween != null && animation.tween.IsActive())
                animation.tween.SetAutoKill(false).SetUpdate(true).Restart();
        }
    }

    private void PauseMenuEntrance()
    {
        foreach (DOTweenAnimation animation in menuEntranceTweens)
            if (animation != null && animation.tween != null && animation.tween.IsActive())
                animation.tween.Pause();
    }

    private static void PrepareMenuCanvas(GameObject panel, int order)
    {
        Canvas canvas = panel.GetComponent<Canvas>();
        if (canvas == null) canvas = panel.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = order;
        if (panel.GetComponent<GraphicRaycaster>() == null) panel.AddComponent<GraphicRaycaster>();
    }

    private void BindSettingsBackButton()
    {
        PrepareMenuCanvas(battleSettings, 301);
        foreach (Button button in battleSettings.GetComponentsInChildren<Button>(true))
            if (button.name == "BackButton")
            {
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(CloseSettings);
            }
    }

    public void OpenSettings()
    {
        if (!MenuOpen) SetMenuOpen(true);
        if (!MenuOpen) return;
        if (battleSettings == null)
        {
            if (settingsPrefab == null) return;
            battleSettings = Instantiate(settingsPrefab, menuPanel.transform, false);
            battleSettings.name = "SettingUI";
            var rect = battleSettings.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            BindSettingsBackButton();
        }
        battleSettings.transform.SetAsLastSibling();
        battleSettings.SetActive(true);
    }
    public void CloseSettings()
    {
        if (battleSettings != null) battleSettings.SetActive(false);
        PlayerPrefs.Save();
    }
    public async void LeaveBattleFromMenu()
    {
        if (leavingFromMenu || NetworkGameManager.Instance == null) return;
        leavingFromMenu = true;
        try { await NetworkGameManager.Instance.ReturnToMainMenuAsync(); }
        catch (System.Exception exception) { Debug.LogException(exception); }
        finally { leavingFromMenu = false; }
    }
}
