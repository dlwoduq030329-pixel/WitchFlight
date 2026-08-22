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

    public void TryLogin()
    {
        LoginManager.Instance.Login(idInput.text,passwordInput.text);

        if(isRemember)
        {
            PlayerPrefs.SetString("ID", idInput.text);
            PlayerPrefs.SetString("PASSWORD", passwordInput.text);
            PlayerPrefs.SetInt("BOOL", 1);
        }
    }
    

    public void RememberIdnPassword()
    {

    }

    public void OnEnable()
    {
        int temp = PlayerPrefs.GetInt("BOOL");

        if(temp == 0)
        {
            isRemember.isOn = false;
        }else
        {
            isRemember.isOn = true;
            idInput.text = PlayerPrefs.GetString("ID");
            passwordInput.text = PlayerPrefs.GetString("PASSWORD"); 
        }
    }
}
