using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Authored on the existing Main/OpenSpace button. No runtime search or generated UI.
[RequireComponent(typeof(Button))]
public sealed class OpenSpaceEntry : MonoBehaviour
{
    [SerializeField] private OpenSpaceSession session;
    [SerializeField] private TMP_Text status;
    private Button button;
    private void Awake() {button=GetComponent<Button>();button.onClick.AddListener(Enter);}
    private void Enter() {(OpenSpaceSession.Instance!=null?OpenSpaceSession.Instance:session)?.Enter();}
    private void Update()
    {
        var live=OpenSpaceSession.Instance!=null?OpenSpaceSession.Instance:session;
        button.interactable=live!=null && !live.Busy && DatabaseManager.Instance!=null && DatabaseManager.Instance.IsDataConfigReady;
        if(status!=null && live!=null && status.text!=live.Status)status.text=live.Status;
    }
    private void OnDestroy(){if(button!=null)button.onClick.RemoveListener(Enter);}
}
