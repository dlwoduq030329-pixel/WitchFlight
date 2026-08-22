using TMPro;
using UnityEngine;

public class LonginLink : MonoBehaviour
{
    [SerializeField]
    TMP_InputField idInput;
    [SerializeField]
    TMP_InputField passwordInput;

    public void TryLogin()
    {
        LoginManager.Instance.Login(idInput.text,passwordInput.text);
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
