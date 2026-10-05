using UnityEngine;
using UnityEngine.Events;
using BackEnd;
using UnityEngine.UI;
using TMPro;

public class LoginManager : MonoBehaviour
{
    public static LoginManager Instance;

    public bool IsInitialized { get; private set; }
    public bool IsInitializing { get; private set; }
    private System.Action<bool, string> initializationCompleted;
    public bool IsLoggedIn { get; private set; }
    public bool IsLoggingIn { get; private set; }
    private int loginAttempt;

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
        try
        {
            Backend.InitializeAsync(callback =>
            {
                if (this == null) return;
                bool success = callback.IsSuccess() && Backend.IsInitialized;
                CompleteBackendInitialization(success, success ? string.Empty : callback.GetMessage());
            });
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
        if (IsLoggingIn) return;
        // After a profile-read failure, the existing Login button retries the data flow.
        if (IsLoggedIn) { OnLoginSuccess?.Invoke(); return; }
        if (!IsInitialized)
        {
            Debug.LogError("BACKND가 아직 초기화되지 않았습니다.");
            return;
        }

        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(password))
        {
            OnLoginFailed?.Invoke(
                "아이디와 비밀번호를 입력해주세요."
            );

            return;
        }

        IsLoggingIn = true;
        int attempt = ++loginAttempt;
        OnLoginStarted?.Invoke();
        Backend.BMember.CustomLogin(
            id,
            password,
            callback =>
            {
                if (attempt != loginAttempt || this == null) return;
                IsLoggingIn = false;
                if (callback.IsSuccess())
                {
                    IsLoggedIn = true;

                    Debug.Log("로그인 성공");

                    // LoginFlowController checks registration, opens the lobby UI,
                    // then applies the saved customization. Authentication alone is not lobby readiness.
                    OnLoginSuccess?.Invoke();
                }
                else
                {
                    Debug.LogError(
                        $"로그인 실패 : {callback.GetMessage()}"
                    );

                    OnLoginFailed?.Invoke(
                        callback.GetMessage()
                    );
                }
            }
        );
    }


    // UI 회원가입 버튼에서 호출
    public void SignUp(string id, string password)
    {
        if (!IsInitialized)
        {
            Debug.LogError("BACKND가 아직 초기화되지 않았습니다.");
            return;
        }

        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(password))
        {
            OnSignUpFailed?.Invoke(
                "아이디와 비밀번호를 입력해주세요."
            );

            return;
        }


        Backend.BMember.CustomSignUp(
            id,
            password,
            callback =>
            {
                if (callback.IsSuccess())
                {
                    Debug.Log("회원가입 성공");

                    OnSignUpSuccess?.Invoke();
                }
                else
                {
                    Debug.LogError(
                        $"회원가입 실패 : {callback.GetMessage()}"
                    );

                    OnSignUpFailed?.Invoke(
                        callback.GetMessage()
                    );
                }
            }
        );


    }


    // 로그아웃
    public void Logout()
    {
        loginAttempt++;
        IsLoggingIn = false;
        Backend.BMember.Logout();

        IsLoggedIn = false;
        DatabaseManager.Instance?.ClearSession();
        OnLoggedOut?.Invoke();

        Debug.Log("로그아웃");
    }

 
}
