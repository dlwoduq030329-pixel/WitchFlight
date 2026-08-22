using UnityEngine;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Collections;
using UnityEngine.UI;
using TMPro;
public class NicknameValidator : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private TMP_Text guideText;
    [SerializeField] private Button confirmButton;

    private readonly string[] bannedWords =
    {
        "Âðµû", "¹ÌÄ£","½Ã¹ß", "¤µ¤²", "º´½Å", "°³»õ³¢", "¤²¤µ", "¹Ùº¸", "Çü½Å", "¾Ö¹Ì", "Ã¢³â", "¸ÛÃ»ÀÌ"
    };

    private bool isValid;

    private const int MIN_LENGTH = 2;
    private const int MAX_LENGTH = 10;

    /* =========================
       Lifecycle
       ========================= */

    private void OnEnable()
    {
        nicknameInput.onValueChanged.AddListener(OnNicknameChanged);
    }

    private void OnDisable()
    {
        nicknameInput.onValueChanged.RemoveListener(OnNicknameChanged);
    }

    private void OnNicknameChanged(string value)
    {
        ClampLength(value);
        ValidateNickname(nicknameInput.text);
    }

    /* =========================
       Length Clamp (½Ç½Ã°£)
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
       Validation (ÀÔ·Â ¿Ï·á ÈÄ)
       ========================= */

    public void ValidateNickname(string nickname)
    {
        isValid = false;

        if (nickname.Length < MIN_LENGTH || nickname.Length > MAX_LENGTH)
        {
            SetGuide("2-10 ±ÛÀÚ·Î ¼³Á¤ °¡´ÉÇÕ´Ï´Ù.");
            return;
        }

        if (ContainsSpecialChar(nickname))
        {
            SetGuide("Æ¯¼ö¹®ÀÚ´Â »ç¿ë ºÒ°¡´ÉÇÕ´Ï´Ù.");
            return;
        }

        if (ContainsBannedWord(nickname))
        {
            SetGuide("¿å¼³ ¹× ºñ¼Ó¾î´Â »ç¿ë ºÒ°¡´ÉÇÕ´Ï´Ù.");
            return;
        }

        // Åë°ú
        isValid = true;
        guideText.text = "»ç¿ë °¡´ÉÇÑ ´Ð³×ÀÓÀÔ´Ï´Ù.";
        //guideText.color = validColor;
        confirmButton.interactable = true;
    }

    /* =========================
       Helpers
       ========================= */

    private void SetGuide(string message)
    {
        guideText.text = message;
        //guideText.color = invalidColor;
        confirmButton.interactable = false;
    }

    private bool ContainsSpecialChar(string text)
    {
        return !Regex.IsMatch(text, @"^[a-zA-Z0-9°¡-ÆR]+$");
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
        if (!isValid)
            return;


        //UIConfigManager.Instance.SetNickname(nicknameInput.text);
        //bug.Log($"´Ð³×ÀÓ UIConfig ÀúÀå ¿Ï·á: {nicknameInput.text}");
    }

    private static Color HexToColor(string hex)
    {
        ColorUtility.TryParseHtmlString($"#{hex}", out Color color);
        return color;
    }
}
