using DG.Tweening;
using TMPro;
using UnityEngine;

// Reuses the authored SuccessPopup for both purchase results. No new UI or polling.
public sealed class StorePurchaseFeedback : MonoBehaviour
{
    [SerializeField] private GameObject popup;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private DOTweenAnimation entryAnimation;

    public void PrepareSuccess()
    {
        if (messageText != null) messageText.text = "구매 완료!";
    }

    public void ShowInsufficientFunds()
    {
        if (popup == null) return;
        if (messageText != null) messageText.text = "재화가 부족합니다!";
        popup.SetActive(true);
        if (entryAnimation != null) entryAnimation.DORestartAllById("ChulSuccess");
    }
}
