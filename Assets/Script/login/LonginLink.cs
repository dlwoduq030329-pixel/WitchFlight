using TMPro;
using UnityEngine;
using UnityEngine.UI;
public class LonginLink : MonoBehaviour
{
    [SerializeField]
    TMP_InputField idInput;
    [SerializeField]
    TMP_InputField passwordInput;
    [SerializeField]
    Toggle isRemember;
    [Header("Login feedback")]
    [SerializeField] private Button loginButton;
    [SerializeField] private TMP_Text statusText;
    private LoginManager subscribedManager;
    private TMP_Text buttonLabel;
    private string originalButtonLabel;

    public void TryLogin()
    {
        BindManager();
        if (subscribedManager == null || idInput == null || passwordInput == null)
        {
            ShowStatus("로그인 화면 연결을 확인해주세요.");
            return;
        }
        if (subscribedManager.IsLoggingIn) { ShowStatus(subscribedManager.LoginStatus); return; }
        subscribedManager.Login(idInput.text,passwordInput.text);

        if(isRemember != null && isRemember.isOn)
        {
            PlayerPrefs.SetString("ID", idInput.text);
            PlayerPrefs.SetString("PASSWORD", passwordInput.text);
            PlayerPrefs.SetInt("BOOL", 1);
        }
        else
        {
            PlayerPrefs.SetInt("BOOL", 0);
            PlayerPrefs.DeleteKey("ID");
            PlayerPrefs.DeleteKey("PASSWORD");
        }
    }
    

    public void RememberIdnPassword()
    {

    }

    public void OnEnable()
    {
        BindManager();
        int temp = PlayerPrefs.GetInt("BOOL");
        if (isRemember != null) isRemember.isOn = temp != 0;
        if (temp != 0)
        {
            if (idInput != null) idInput.text = PlayerPrefs.GetString("ID");
            if (passwordInput != null) passwordInput.text = PlayerPrefs.GetString("PASSWORD");
        }
    }

    private void Start() { BindManager(); }

    private void BindManager()
    {
        if (buttonLabel == null && loginButton != null)
        {
            buttonLabel = loginButton.GetComponentInChildren<TMP_Text>(true);
            if (buttonLabel != null) originalButtonLabel = buttonLabel.text;
        }
        LoginManager manager = LoginManager.Instance;
        if (manager != subscribedManager)
        {
            Unsubscribe();
            subscribedManager = manager;
            if (manager != null)
            {
                manager.OnLoginStatusChanged.AddListener(ShowStatus);
                manager.OnLoginFailed.AddListener(ShowStatus);
            }
        }
        if (manager != null) ShowStatus(manager.LoginStatus);
    }

    public void ShowStatus(string message)
    {
        bool busy = subscribedManager != null && subscribedManager.IsLoggingIn;
        if (loginButton != null) loginButton.interactable = !busy;
        if (buttonLabel != null) buttonLabel.text = busy ? message : originalButtonLabel;
        if (statusText == null) return;
        statusText.raycastTarget = false;
        statusText.text = message;
        statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    private void Unsubscribe()
    {
        if (subscribedManager != null)
        {
            subscribedManager.OnLoginStatusChanged.RemoveListener(ShowStatus);
            subscribedManager.OnLoginFailed.RemoveListener(ShowStatus);
        }
        subscribedManager = null;
    }

    private void OnDisable() { Unsubscribe(); }
}
