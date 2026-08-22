using UnityEngine;
using UnityEngine.Events;
using BackEnd;
using UnityEngine.UI;
using TMPro;

public class LoginManager : MonoBehaviour
{
    public static LoginManager Instance;

    public bool IsInitialized { get; private set; }
    public bool IsLoggedIn { get; private set; }

    [Header("Events")]
    public UnityEvent OnInitializeSuccess;
    public UnityEvent<string> OnInitializeFailed;

    public UnityEvent OnLoginSuccess;
    public UnityEvent<string> OnLoginFailed;

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
        
        SignUp();
    }

    public void TestLogin()
    {
        Login();
    }


    // 게임 실행 시 자동 호출
    public void InitializeBackend()
    {
        Backend.InitializeAsync(callback =>
        {
            if (callback.IsSuccess())
            {
                IsInitialized = true;

                Debug.Log("BACKND 초기화 성공");

                OnInitializeSuccess?.Invoke();
            }
            else
            {
                Debug.LogError(
                    $"BACKND 초기화 실패 : {callback.GetMessage()}"
                );

                OnInitializeFailed?.Invoke(
                    callback.GetMessage()
                );
            }
        });
    }


    // UI 로그인 버튼에서 호출
    public void Login(string id = "1234", string password = "1234")
    {
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


        Backend.BMember.CustomLogin(
            id,
            password,
            callback =>
            {
                if (callback.IsSuccess())
                {
                    IsLoggedIn = true;

                    Debug.Log("로그인 성공");

                    // 로그인 성공 후
                    // 유저 DataConfig 세팅을 불러옴
                    DatabaseManager.Instance.InitializeDatabase();

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
    public void SignUp(string id = "1234", string password = "1234")
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
        Backend.BMember.Logout();

        IsLoggedIn = false;

        Debug.Log("로그아웃");
    }
}