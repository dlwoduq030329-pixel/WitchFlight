using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// id psword 서버 저장 및 중복 확인 함수는 없음
public class SignUpValidator : MonoBehaviour
{
    public TMP_InputField idInputField;
    public TMP_InputField passwordInputField;
    public TMP_InputField passwordConfirmInputField;

    public TMP_Text idDialogText;
    public TMP_Text passwordDialogText;
    //public Button DuplicateIDButton;
    public Button signUpButton;

    private bool isIDChecked = false;
    private readonly Color errorColor = new Color32(0xEB, 0x05, 0x05, 255);
    private readonly Color successColor = new Color32(0x11, 0xA8, 0x7D, 255);

    private bool isUpdatingInput = false;

    private void OnEnable()
    {
        idInputField.text = string.Empty;
        passwordInputField.text = string.Empty;
        passwordConfirmInputField.text = string.Empty;
    }


    /// ID 입력 검사
    public void ValidateID(string value)
    {

        isIDChecked = false;

        if (string.IsNullOrEmpty(value))
        {
            idDialogText.text = "";
            return;
        }

        // 소문자 영어 + 숫자만 허용
        bool isValid = Regex.IsMatch(value, @"^[a-z0-9]+$");

        if (!isValid)
        {
            idDialogText.color = errorColor;
            idDialogText.text = "ID는 소문자 영어로만 입력 가능합니다.";
        }
        else
        {
            idDialogText.text = "";

        }

        UpdateSignUpButton();
    }

    /// 중복확인 버튼
    public void CheckDuplicateID()
    {
        string id = idInputField.text;

        // 빈 문자열
        if (string.IsNullOrEmpty(id))
        {
            idDialogText.color = errorColor;
            idDialogText.text = "ID를 입력해주세요.";

            isIDChecked = false;
            UpdateSignUpButton();
            return;
        }

        // 소문자 + 숫자 검사
        bool isValid = Regex.IsMatch(id, @"^[a-z0-9]+$");

        if (!isValid)
        {
            idDialogText.color = errorColor;
            idDialogText.text = "ID는 소문자 영어와 숫자만 입력 가능합니다.";

            isIDChecked = false;
            UpdateSignUpButton();
            return;
        }
        if (id.Length > 20)
        {
            idDialogText.color = errorColor;
            idDialogText.text = "ID는 20자 이내로 입력해주세요.";

            isIDChecked = false;
            UpdateSignUpButton();
            return;
        }
        // 여기서 나중에 서버 중복검사 추가 예정

        idDialogText.color = successColor;
        idDialogText.text = "사용 가능한 ID입니다.";

        isIDChecked = true;

        UpdateSignUpButton();
    }

    /// 비밀번호 입력 검사
    public void ValidatePassword(string value)
    {
        if (isUpdatingInput) return;

        string lower = value.ToLower();

        // 소문자 + 숫자 + 특수문자 허용
        string filtered = Regex.Replace(
            lower,
            @"[^a-z0-9!@#$%^&*()_+\-=\[\]{};':"",.<>/?\\|`~]",
            ""
        );

        if (filtered != value)
        {
            isUpdatingInput = true;
            passwordInputField.text = filtered;
            passwordInputField.caretPosition = filtered.Length;
            isUpdatingInput = false;

            passwordDialogText.color = errorColor;
            passwordDialogText.text = "비밀번호는 소문자 영어, 숫자, 특수문자만 입력 가능합니다.";
            return;
        }

        ValidatePasswordConfirm(passwordConfirmInputField.text);
        UpdateSignUpButton();
    }

    /// 비밀번호 확인
    public void ValidatePasswordConfirm(string value)
    {
        if (string.IsNullOrEmpty(passwordInputField.text) ||
            string.IsNullOrEmpty(passwordConfirmInputField.text))
        {
            passwordDialogText.text = "";
            return;
        }

        if (passwordInputField.text == passwordConfirmInputField.text)
        {
            passwordDialogText.color = successColor;
            passwordDialogText.text = "비밀번호가 같습니다.";
        }
        else
        {
            passwordDialogText.color = errorColor;
            passwordDialogText.text = "비밀번호가 다릅니다.";
        }
        UpdateSignUpButton();
    }

    //회원가입 버튼ㅁ
    private void UpdateSignUpButton()
    {
        bool validID =
            !string.IsNullOrEmpty(idInputField.text) &&
            Regex.IsMatch(idInputField.text, @"^[a-z0-9]+$");

        bool validPassword =
            !string.IsNullOrEmpty(passwordInputField.text) &&
            Regex.IsMatch(
                passwordInputField.text,
                @"^[a-z0-9!@#$%^&*()_+\-=\[\]{};':"",.<>/?\\|`~]+$"
            );

        bool passwordMatch =
            passwordInputField.text == passwordConfirmInputField.text &&
            !string.IsNullOrEmpty(passwordConfirmInputField.text);

        signUpButton.interactable =
            validID &&
            validPassword &&
            passwordMatch &&
            isIDChecked;

        //여기서 서버나 뭐 그런데에 닉네임 비번 저장하셈
    }

    public void TryRegister()
    {
        LoginManager.Instance.SignUp(idInputField.text, passwordInputField.text);
    }
}
