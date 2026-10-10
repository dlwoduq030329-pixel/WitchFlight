using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

// Scene-owned UI coordinator. No scene loading and no new network manager.
public sealed class LoginFlowController : MonoBehaviour
{
    [SerializeField] private LoginManager loginManager;
    [SerializeField] private DatabaseManager databaseManager;
    [Header("Existing Main scene UI")]
    [SerializeField] private GameObject introRoot;
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject lobbyRoot;
    [SerializeField] private LobbyPlayerInitializer lobbyInitializer;
    [SerializeField] private TMP_Text errorText;
    [SerializeField] private GameObject errorPanel;
    [Header("Optional designer events")]
    [SerializeField] private UnityEvent onLobbyReady = new UnityEvent();
    [SerializeField] private UnityEvent<string> onFlowFailed = new UnityEvent<string>();

    public bool IsLobbyReady { get; private set; }
    private bool processing;
    private LonginLink loginForm;
    private LoginManager subscribedLoginManager;

    private void Awake()
    {
        if (introRoot != null) loginForm = introRoot.GetComponentInChildren<LonginLink>(true);
    }

    private void OnEnable()
    {
        ResolveSessionManagers();
    }

    private void Start()
    {
        // Main contains scene references to duplicate managers. Their Awake destroys
        // them on a return trip; always use the surviving authenticated singletons.
        ResolveSessionManagers();
        if (loginManager != null && loginManager.IsLoggedIn) ContinueAfterLogin();
        else ShowLogin();
    }

    private void OnDisable()
    {
        UnsubscribeLogin();
    }

    private void ResolveSessionManagers()
    {
        LoginManager live = LoginManager.Instance != null ? LoginManager.Instance :
            loginManager != null ? loginManager : FindFirstObjectByType<LoginManager>();
        databaseManager = DatabaseManager.Instance != null ? DatabaseManager.Instance :
            databaseManager != null ? databaseManager : FindFirstObjectByType<DatabaseManager>();
        loginManager = live;
        if (subscribedLoginManager == live) return;
        UnsubscribeLogin();
        subscribedLoginManager = live;
        if (live == null) return;
        live.OnLoginSuccess.AddListener(ContinueAfterLogin);
        live.OnLoginStarted.AddListener(ShowLogin);
        live.OnLoggedOut.AddListener(ShowLogin);
        live.OnLoginFailed.AddListener(Fail);
    }

    private void UnsubscribeLogin()
    {
        if (subscribedLoginManager != null)
        {
            subscribedLoginManager.OnLoginSuccess.RemoveListener(ContinueAfterLogin);
            subscribedLoginManager.OnLoginStarted.RemoveListener(ShowLogin);
            subscribedLoginManager.OnLoggedOut.RemoveListener(ShowLogin);
            subscribedLoginManager.OnLoginFailed.RemoveListener(Fail);
        }
        subscribedLoginManager = null;
    }

    private void ShowLogin()
    {
        IsLobbyReady = false;
        SetActive(lobbyRoot, false);
        SetActive(introRoot, true);
        SetActive(loginPanel, true);
        SetActive(errorPanel, false);
        if (loginForm != null) loginForm.gameObject.SetActive(true);
        if (errorText != null) errorText.text = string.Empty;
    }

    // Also usable by a Retry button after a database failure (no second login required).
    public void ContinueAfterLogin()
    {
        if (processing || IsLobbyReady) return;
        ResolveSessionManagers();
        if (loginManager == null || !loginManager.IsLoggedIn)
        {
            Fail("먼저 로그인해주세요.");
            return;
        }
        if (databaseManager == null || lobbyRoot == null || lobbyInitializer == null)
        {
            Fail("LoginFlowController의 Database/UI/Initializer 연결을 확인해주세요.");
            return;
        }
        processing = true;
        try
        {
            // Do not clear a valid session or discard an in-flight customization save
            // when returning from Battle. HasLoadedProfile checks the account identity.
            if (!databaseManager.HasLoadedProfile && !databaseManager.TryLoadPlayerSetting(false))
            {
                Fail(databaseManager.LastError);
                return;
            }
            // Nickname registration belongs to signup, never to the login flow.
            OpenLobby();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Fail("데이터를 불러오지 못했습니다. 다시 시도해주세요.");
        }
        finally { processing = false; }
    }

    private void OpenLobby()
    {
        if (IsLobbyReady) return;
        if (databaseManager == null || !databaseManager.HasLoadedProfile)
        {
            Fail("LoginPlayerData의 저장 데이터 또는 nickname이 없습니다. 회원가입 시 저장 경로를 확인해주세요.");
            return;
        }
        // Open the existing Main lobby; apply saved data before exposing it to rendering/input.
        SetActive(lobbyRoot, true);
        try
        {
            if (databaseManager.IsDataConfigReady)
                lobbyInitializer.RefreshFromDataConfig();
            else if (!lobbyInitializer.InitializeFromLoadedData())
            {
                Fail(databaseManager.LastError);
                return;
            }
            IsLobbyReady = true;
            SetActive(introRoot, false);
            if (errorText != null) errorText.text = string.Empty;
            onLobbyReady.Invoke();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Fail("로비 외형 초기화에 실패했습니다. 연결을 확인하고 다시 시도해주세요.");
        }
    }

    private void Fail(string message)
    {
        IsLobbyReady = false;
        if (databaseManager != null) databaseManager.SuspendLobbyInitialization();
        SetActive(lobbyRoot, false);
        SetActive(introRoot, true);
        SetActive(loginPanel, true);
        if (loginForm != null) loginForm.gameObject.SetActive(true);
        if (errorText != null) errorText.text = message;
        SetActive(errorPanel, true);
        Debug.LogError(message);
        onFlowFailed.Invoke(message);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active) target.SetActive(active);
    }
}
