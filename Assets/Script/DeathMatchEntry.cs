using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Lives on the authored Main button; resolves the persistent manager only on click.
[RequireComponent(typeof(Button))]
public sealed class DeathMatchEntry : MonoBehaviour
{
    private Button button;
    private TMP_Text caption;
    private string originalCaption;
    private Coroutine restore;

    private void Awake()
    {
        button = GetComponent<Button>();
        caption = GetComponentInChildren<TMP_Text>(true);
        originalCaption = caption != null ? caption.text : "";
        button.onClick.AddListener(Join);
    }

    private void Join()
    {
        var manager = NetworkGameManager.Instance;
        if (manager == null) { ShowError("네트워크 초기화가 필요합니다."); return; }
        manager.JoinDeathMatch(ShowError);
    }

    private void ShowError(string message)
    {
        if (this == null || caption == null) return;
        if (restore != null) StopCoroutine(restore);
        caption.text = message;
        if (isActiveAndEnabled) restore = StartCoroutine(RestoreCaption());
    }

    private IEnumerator RestoreCaption()
    {
        yield return new WaitForSecondsRealtime(4f);
        if (caption != null) caption.text = originalCaption;
        restore = null;
    }

    private void OnDisable()
    {
        if (restore != null) StopCoroutine(restore);
        restore = null;
        if (caption != null) caption.text = originalCaption;
    }

    private void OnDestroy() { if (button != null) button.onClick.RemoveListener(Join); }
}
