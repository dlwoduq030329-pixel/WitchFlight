using UnityEngine;
using UnityEngine.Events;
using BackEnd;
using UnityEngine.UI;
using TMPro;

public partial class LoginManager : MonoBehaviour
{
    public static LoginManager Instance;

    public bool IsInitialized { get; private set; }
    public bool IsInitializing { get; private set; }
    private System.Action<bool, string> initializationCompleted;
    public bool IsLoggedIn { get; private set; }
    public bool IsLoggingIn { get; private set; }
    public bool IsLoggingOut { get; private set; }
    public bool IsSigningUp { get; private set; }
    private int signUpAttempt;
    private float signUpDeadline;
    private System.IDisposable loginLoading, signUpLoading;
    private int loginAttempt;
    private int initializationAttempt;
    private float initializationDeadline, loginDeadline;
    private readonly System.Collections.Concurrent.ConcurrentQueue<System.Action> completions = new();
    [Header("Request timeouts (real seconds)")]
    [SerializeField, Min(5f)] private float initializationTimeout = 20f;
    [SerializeField, Min(5f)] private float loginTimeout = 20f;
    public string LoginStatus { get; private set; } = string.Empty;
    public UnityEvent<string> OnLoginStatusChanged = new UnityEvent<string>();

    [Header("Events")]
    public UnityEvent OnInitializeSuccess;
    public UnityEvent<string> OnInitializeFailed;

    public UnityEvent OnLoginSuccess;
    public UnityEvent<string> OnLoginFailed;
    public UnityEvent OnLoginStarted = new UnityEvent();
    public UnityEvent OnLoggedOut = new UnityEvent();

    public UnityEvent OnSignUpSuccess;
    public UnityEvent<string> OnSignUpFailed;



    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);
    }


    private void Start()
    {
        InitializeBackend();
    }

    private void Update()
    {
        // Keep SDK completions and all UI events on Unity's main thread.
        while (completions.TryDequeue(out var complete))
        {
            try { complete(); }
            catch (System.Exception exception) { Debug.LogException(exception); }
        }
        if (IsInitializing && Time.realtimeSinceStartup >= initializationDeadline)
        {
            initializationAttempt++;
            CompleteBackendInitialization(false, "서버 초기화가 지연되고 있습니다. 연결 상태를 확인한 뒤 다시 눌러주세요.");
        }
        if (IsLoggingIn && Time.realtimeSinceStartup >= loginDeadline)
            FailLogin(loginAttempt, "로그인 응답이 지연되고 있습니다. 잠시 후 다시 시도해주세요.");
        if (IsSigningUp && Time.realtimeSinceStartup >= signUpDeadline)
            FinishSignUp(signUpAttempt, false, "회원가입 응답이 지연되고 있습니다. 로그인으로 가입 여부를 확인해주세요.");
    }

    private void SetLoginStatus(string message)
    {
        LoginStatus = message;
        OnLoginStatusChanged?.Invoke(message);
    }

    public void Register()
    {
        
    }

    public void TestLogin()
    {
    }
    
    // 게임 실행 시 자동 호출
    public void InitializeBackend()
    {
        EnsureBackendInitialized(null);
    }

    // Share one in-flight initialization with startup and registration callers.
    public void EnsureBackendInitialized(System.Action<bool, string> completed)
    {
        if (IsInitializing)
        {
            initializationCompleted += completed;
            return;
        }
        if (Backend.IsInitialized)
        {
            IsInitialized = true;
            completed?.Invoke(true, string.Empty);
            return;
        }

        initializationCompleted += completed;
        IsInitialized = false;
        IsInitializing = true;
        int attempt = ++initializationAttempt;
        initializationDeadline = Time.realtimeSinceStartup + Mathf.Max(5f, initializationTimeout);
        try
        {
            Backend.InitializeAsync(callback => completions.Enqueue(() =>
            {
                if (this == null || attempt != initializationAttempt) return;
                bool success = callback.IsSuccess() && Backend.IsInitialized;
                CompleteBackendInitialization(success, success ? string.Empty : callback.GetMessage());
            }));
        }
        catch (System.Exception exception)
        {
            CompleteBackendInitialization(false, exception.Message);
        }
    }

    private void CompleteBackendInitialization(bool success, string error)
    {
        IsInitializing = false;
        IsInitialized = success;
        var callbacks = initializationCompleted;
        initializationCompleted = null;
        if (success) Debug.Log("BACKND 초기화 성공");
        else Debug.LogError($"BACKND 초기화 실패 : {error}");

        try
        {
            if (success) OnInitializeSuccess?.Invoke();
            else OnInitializeFailed?.Invoke(error);
        }
        catch (System.Exception exception) { Debug.LogException(exception); }

        if (callbacks == null) return;
        foreach (System.Action<bool, string> callback in callbacks.GetInvocationList())
        {
            try { callback(success, error); }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
  

    // UI 로그인 버튼에서 호출
    public void Login(string id, string password)
    {
        if (IsLoggingIn || IsLoggingOut || IsSigningUp) return;
        // After a profile-read failure, the existing Login button retries the data flow.
        if (IsLoggedIn) { OnLoginSuccess?.Invoke(); return; }
        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(password))
        {
            OnLoginFailed?.Invoke(
                "아이디와 비밀번호를 입력해주세요."
            );

            return;
        }

        IsLoggingIn = true;
        loginLoading = MainLoadingUI.BeginIntro();
        int attempt = ++loginAttempt;
        loginDeadline = float.PositiveInfinity; // Initialization has its own deadline.
        try
        {
            OnLoginStarted?.Invoke();
            SetLoginStatus("서버 연결 중...");
            EnsureBackendInitialized((success, error) =>
            {
                if (attempt != loginAttempt || this == null) return;
                if (!success) { FailLogin(attempt, error); return; }
                SubmitLogin(attempt, id, password);
            });
        }
        catch (System.Exception exception) { FailLogin(attempt, exception.Message); }
    }

    private void SubmitLogin(int attempt, string id, string password)
    {
        try
        {
            loginDeadline = Time.realtimeSinceStartup + Mathf.Max(5f, loginTimeout);
            SetLoginStatus("로그인 중...");
            Backend.BMember.CustomLogin(id, password, callback => completions.Enqueue(() =>
            {
                if (this == null || attempt != loginAttempt || !IsLoggingIn) return;
                if (!callback.IsSuccess()) { FailLogin(attempt, callback.GetMessage()); return; }
                IsLoggingIn = false;
                IsLoggedIn = true;
                SetLoginStatus(string.Empty);
                Debug.Log("로그인 성공");
                // Profile loading and lobby readiness still belong to LoginFlowController.
                try { OnLoginSuccess?.Invoke(); }
                finally { loginLoading?.Dispose(); loginLoading = null; }
            }));
        }
        catch (System.Exception exception) { FailLogin(attempt, exception.Message); }
    }

    private void FailLogin(int attempt, string message)
    {
        if (attempt != loginAttempt) return;
        loginAttempt++; // A late callback cannot reopen the lobby after failure/logout.
        IsLoggingIn = false;
        loginLoading?.Dispose();
        loginLoading = null;
        SetLoginStatus(message);
        OnLoginFailed?.Invoke(message);
    }


    // UI 회원가입 버튼에서 호출
    public void SignUp(string id, string password)
    {
        if (IsLoggingOut || IsLoggingIn || IsSigningUp) return;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(password))
        {
            OnSignUpFailed?.Invoke("아이디와 비밀번호를 입력해주세요.");
            return;
        }

        IsSigningUp = true;
        int attempt = ++signUpAttempt;
        signUpDeadline = float.PositiveInfinity;
        signUpLoading = MainLoadingUI.BeginIntro();
        try
        {
            EnsureBackendInitialized((success, error) =>
            {
                if (this == null || attempt != signUpAttempt || !IsSigningUp) return;
                if (!success) { FinishSignUp(attempt, false, error); return; }
                signUpDeadline = Time.realtimeSinceStartup + Mathf.Max(5f, loginTimeout);
                try
                {
                    Backend.BMember.CustomSignUp(id, password, result => completions.Enqueue(() =>
                    {
                        if (this == null) return;
                        FinishSignUp(attempt, result.IsSuccess(), result.GetMessage());
                    }));
                }
                catch (System.Exception exception) { FinishSignUp(attempt, false, exception.Message); }
            });
        }
        catch (System.Exception exception) { FinishSignUp(attempt, false, exception.Message); }
    }

    private void FinishSignUp(int attempt, bool success, string message)
    {
        if (attempt != signUpAttempt || !IsSigningUp) return;
        signUpAttempt++;
        IsSigningUp = false;
        signUpLoading?.Dispose();
        signUpLoading = null;
        if (success) OnSignUpSuccess?.Invoke();
        else OnSignUpFailed?.Invoke(message);
    }

    // 로그아웃
    public void Logout()
    {
        loginLoading?.Dispose();
        loginLoading = null;
        signUpLoading?.Dispose();
        signUpLoading = null;
        signUpAttempt++;
        IsSigningUp = false;
        loginAttempt++;
        IsLoggingIn = false;
        SetLoginStatus(string.Empty);
        Backend.BMember.Logout();

        IsLoggedIn = false;
        DatabaseManager.Instance?.ClearSession();
        OnLoggedOut?.Invoke();

        Debug.Log("로그아웃");
    }

 
}
