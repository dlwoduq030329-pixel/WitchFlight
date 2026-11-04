using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Keep the code popup open until validation AND room creation succeed.</summary>
public class CreateRoomUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField roomCodeInput;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button createButton;
    [SerializeField] private GameObject waitingRoomUI;
    [SerializeField] private GameObject inputPanel;
    private bool requestPending;

    private void OnEnable()
    {
        SetStatus(requestPending ? "방을 만드는 중..." : RoomCodeRules.Hint);
        if (roomCodeInput != null) roomCodeInput.richText = false;
        if (createButton != null) createButton.interactable = !requestPending;
    }

    public void TryCreateRoom()
    {
        if (requestPending) return;
        if (!RoomCodeRules.TryValidate(roomCodeInput != null ? roomCodeInput.text : null,
            out string code, out string error))
        {
            SetStatus(error);
            return;
        }
        if (NetworkGameManager.Instance == null)
        {
            SetStatus("네트워크 매니저가 준비되지 않았습니다.");
            return;
        }
        requestPending = true;
        if (createButton != null) createButton.interactable = false;
        SetStatus("방을 만드는 중...");
        NetworkGameManager.Instance.CreateRoom(code, OnCreateSuccess, OnCreateFailed);
    }

    private void OnCreateSuccess()
    {
        FinishRequest();
        if (waitingRoomUI != null) waitingRoomUI.SetActive(true);
        if (roomCodeInput != null) roomCodeInput.SetTextWithoutNotify(string.Empty);
        if (inputPanel != null) inputPanel.SetActive(false);
    }

    private void OnCreateFailed(string error)
    {
        FinishRequest();
        SetStatus(error);
    }

    private void FinishRequest()
    {
        requestPending = false;
        if (createButton != null) createButton.interactable = true;
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        else Debug.LogWarning(message, this);
    }
}
