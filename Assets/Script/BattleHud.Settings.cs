using UnityEngine;
using UnityEngine.UI;

public sealed partial class BattleHud
{
    [Header("Shared settings UI")]
    [SerializeField] private GameObject settingsPrefab;
    private GameObject battleSettings;
    private bool leavingFromMenu;

    public void OpenSettings()
    {
        if (!MenuOpen) SetMenuOpen(true);
        if (!MenuOpen || settingsPrefab == null) return;
        if (battleSettings == null)
        {
            battleSettings = Instantiate(settingsPrefab, menuPanel.transform, false);
            battleSettings.name = "SettingUI";
            var rect = battleSettings.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            foreach (var button in battleSettings.GetComponentsInChildren<Button>(true))
                if (button.name == "BackButton")
                {
                    // Main-scene navigation does not belong in the battle instance.
                    button.onClick = new Button.ButtonClickedEvent();
                    button.onClick.AddListener(CloseSettings);
                }
        }
        battleSettings.transform.SetAsLastSibling();
        battleSettings.SetActive(true);
    }
    public void CloseSettings()
    {
        if (battleSettings != null) battleSettings.SetActive(false);
        PlayerPrefs.Save();
    }
    private async void LeaveBattleFromMenu()
    {
        if (leavingFromMenu || NetworkGameManager.Instance == null) return;
        leavingFromMenu = true;
        try { await NetworkGameManager.Instance.ReturnToMainMenuAsync(); }
        catch (System.Exception exception) { Debug.LogException(exception); }
        finally { leavingFromMenu = false; }
    }
}
