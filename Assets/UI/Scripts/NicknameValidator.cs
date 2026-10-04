using UnityEngine;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Collections;
using UnityEngine.UI;
using TMPro;
public class NicknameValidator : MonoBehaviour
{
    public event System.Action RegistrationCompleted;
    public event System.Action RegistrationCancelled;
    private bool isSaving;
    [Header("UI")]
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private TMP_Text guideText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private GameObject nicknamePopup;
    [SerializeField] private GameObject registerPopup;

    private readonly string[] bannedWords =
    {
        "찐따", "미친","시발", "ㅅㅂ", "병신", "개새끼", "ㅂㅅ", "바보", "형신", "애미", "창년", "멍청이"
    };

    private bool isValid;

    private const int MIN_LENGTH = 2;
    private const int MAX_LENGTH = 10;

    /* =========================
       Lifecycle
       ========================= */

    private void OnEnable()
    {
        if (nicknameInput == null) return;
        nicknameInput.onValueChanged.AddListener(OnNicknameChanged);
        ValidateNickname(nicknameInput.text);
    }

    private void OnDisable()
    {
        if (nicknameInput != null) nicknameInput.onValueChanged.RemoveListener(OnNicknameChanged);
    }

    private void OnNicknameChanged(string value)
    {
        ClampLength(value);
        ValidateNickname(nicknameInput.text);
    }

    /* =========================
       Length Clamp (실시간)
       ========================= */

    private void ClampLength(string value)
    {
        if (value.Length <= MAX_LENGTH)
            return;

        nicknameInput.SetTextWithoutNotify(
            value.Substring(0, MAX_LENGTH)
        );

        ValidateNickname(nicknameInput.text);
    }

    /* =========================
       Validation (입력 완료 후)
       ========================= */

    public void ValidateNickname(string nickname)
    {
        nickname = nickname ?? string.Empty;
        isValid = false;

        if (nickname.Length < MIN_LENGTH || nickname.Length > MAX_LENGTH)
        {
            SetGuide("2-10 글자로 설정 가능합니다.");
            return;
        }

        if (ContainsSpecialChar(nickname))
        {
            SetGuide("특수문자는 사용 불가능합니다.");
            return;
        }

        if (ContainsBannedWord(nickname))
        {
            SetGuide("욕설 및 비속어는 사용 불가능합니다.");
            return;
        }

        // 통과
        isValid = true;
        if (guideText != null) guideText.text = "사용 가능한 닉네임입니다.";
        //guideText.color = validColor;
        if (confirmButton != null) confirmButton.interactable = !isSaving;
    }

    /* =========================
       Helpers
       ========================= */

    private void SetGuide(string message)
    {
        if (guideText != null) guideText.text = message;
        //guideText.color = invalidColor;
        if (confirmButton != null) confirmButton.interactable = false;
    }

    private bool ContainsSpecialChar(string text)
    {
        return !Regex.IsMatch(text, @"^[a-zA-Z0-9가-힣]+$");
    }

    private bool ContainsBannedWord(string text)
    {
        string lower = text.ToLower();

        foreach (var word in bannedWords)
        {
            if (lower.Contains(word))
                return true;
        }

        return false;
    }

    public void OnConfirm()
    {
        if (isSaving || nicknameInput == null) return;
        ValidateNickname(nicknameInput.text);
        if (!isValid)
            return;
        DatabaseManager database = DatabaseManager.Instance;
        if (database == null)
        {
            ShowSaveError("DatabaseManager가 없습니다.");
            return;
        }
        isSaving = true;
        if (confirmButton != null) confirmButton.interactable = false;
        try
        {
            if (database.SaveRegistrationNickname(nicknameInput.text))
            {

                RegistrationCompleted?.Invoke();
                registerPopup.SetActive(false);
                nicknamePopup.SetActive(false);
            }
            else
                ShowSaveError(database.LastError);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            ShowSaveError("저장 중 오류가 발생했습니다. 다시 시도해주세요.");
        }
        finally
        {
            isSaving = false;
            if (confirmButton != null) confirmButton.interactable = isValid;
        }
    }

    public void ResetInput()
    {
        if (nicknameInput != null) nicknameInput.SetTextWithoutNotify(string.Empty);
        ValidateNickname(string.Empty);
    }

    public void OnCancel()
    {
        if (isSaving) return;
        if (RegistrationCancelled != null) RegistrationCancelled.Invoke();
        else gameObject.SetActive(false);
    }

    private void ShowSaveError(string message)
    {
        if (guideText != null) guideText.text = string.IsNullOrEmpty(message)
            ? "저장하지 못했습니다. 다시 시도해주세요." : message;
        Debug.LogError(message);
    }

    private static Color HexToColor(string hex)
    {
        ColorUtility.TryParseHtmlString($"#{hex}", out Color color);
        return color;
    }
}
