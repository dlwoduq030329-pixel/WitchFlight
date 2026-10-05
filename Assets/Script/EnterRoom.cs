using TMPro;
using UnityEngine;

public class EnterRoom : MonoBehaviour
{
    [SerializeField]
    TMP_InputField[] texts;

    [SerializeField]
    GameObject robbyUI;
    [SerializeField]
    TextMeshProUGUI waringText;
    [SerializeField]
    GameObject idInputText;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void TryEnterRoom()
    {
        string passwordPack = string.Empty;

        for(int i =0;i<texts.Length;i++)
        {
            passwordPack += texts[i].text;
        }

        NetworkGameManager.Instance.JoinRoom(
     passwordPack,
     OnJoinSuccess,
     OnJoinFailed
 );
    }

    void OnJoinSuccess()
    {
        // 실제 방 접속 성공
        // 코드 입력창 닫기 → 대기실 UI 표시
        robbyUI.SetActive( true );

        for (int i = 0; i < texts.Length; i++)
        {
             texts[i].text=string.Empty;
        }

        idInputText.SetActive( false );
    }

    void OnJoinFailed(string message)
    {
        // 입력창은 유지하고 오류 문구 표시
        waringText.text = message;
        Debug.LogWarning(message);
    }
}
