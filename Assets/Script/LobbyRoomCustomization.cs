using UnityEngine;
using UnityEngine.UI;

// Scene-authored waiting-room customization; no generated UI or per-frame lookups.
public sealed class LobbyRoomCustomization : MonoBehaviour
{
    [SerializeField] private GameObject customizationPanel;
    [SerializeField] private GameObject waitingRoomPanel;
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private Button openButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button equipmentTab;
    private static LobbyRoomCustomization instance;
    private bool opened, lobbyWasVisible;
    public static bool IsOpen => instance != null && instance.opened;

    private void Awake()
    {
        instance = this;
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (backButton != null) backButton.onClick.AddListener(Close);
    }

    public void Open()
    {
        var manager = NetworkGameManager.Instance;
        var local = PlayerData.Local;
        if (opened || customizationPanel == null || manager == null || !manager.IsWaitingInCodeRoom ||
            local == null || local.Object == null || !local.Object.IsValid || !local.Object.HasInputAuthority) return;
        lobbyWasVisible = lobbyPanel != null && lobbyPanel.activeSelf;
        opened = true;
        if (waitingRoomPanel != null) waitingRoomPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        customizationPanel.SetActive(true);
        customizationPanel.transform.SetAsLastSibling();
        // Reuse the authored tab's camera, pose and panel transitions.
        if (equipmentTab != null) equipmentTab.onClick.Invoke();
    }

    public void Close()
    {
        if (!opened) return;
        opened = false;
        if (customizationPanel != null) customizationPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(lobbyWasVisible);
        if (waitingRoomPanel != null)
            waitingRoomPanel.SetActive(NetworkGameManager.Instance != null && NetworkGameManager.Instance.IsWaitingInCodeRoom);
    }

    public static void BackToRoom()
    {
        if (!IsOpen) return;
        if (instance.backButton != null) instance.backButton.onClick.Invoke();
        else instance.Close();
    }

    private void Update()
    {
        if (opened && (NetworkGameManager.Instance == null || !NetworkGameManager.Instance.IsWaitingInCodeRoom))
            BackToRoom();
    }

    private void OnDisable() => Close();

    private void OnDestroy()
    {
        if (openButton != null) openButton.onClick.RemoveListener(Open);
        if (backButton != null) backButton.onClick.RemoveListener(Close);
        if (instance == this) instance = null;
    }
}
