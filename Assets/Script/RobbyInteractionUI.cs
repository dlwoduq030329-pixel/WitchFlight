using TMPro;
using UnityEngine;

// Uses the authored InterActiveCanvas/Interactive/Text(TMP); cached once on startup.
public sealed class RobbyInteractionUI : MonoBehaviour
{
    [SerializeField] private GameObject prompt;
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private UnityEngine.UI.Text legacyText;
    private RobbyInteractionZone displayed;
    private void Awake()
    {
        var promptNode = transform.Find("Interactive") ?? transform.Find("InterActive");
        if (promptNode != null) prompt = promptNode.gameObject;
        var textNode = promptNode != null ? (promptNode.Find("Text(TMP)") ?? promptNode.Find("Text (TMP)")) : null;
        promptText = textNode != null ? textNode.GetComponent<TMP_Text>() : null;
        legacyText = textNode != null ? textNode.GetComponent<UnityEngine.UI.Text>() : null;
        SetPromptVisible(false);
    }
    private void SetPromptVisible(bool value)
    {
        if (prompt != null) prompt.SetActive(value);
        if (promptText != null) promptText.gameObject.SetActive(value);
        if (legacyText != null) legacyText.gameObject.SetActive(value);
    }
    private void Update()
    {
        // Keep the existing prompt visible, but do not interpret typed E as interaction.
        if (MainRoomChatUI.BlocksLobbyInput) return;
        var player = PlayerRobby.LocalPlayer;
        RobbyInteractionZone next = null;
        if (player != null && !player.InteractionBusy && RobbyPlayerCamera.CanUsePractice)
        {
            float nearest = float.PositiveInfinity;
            foreach (var zone in RobbyInteractionZone.Nearby)
            {
                if (zone == null || !zone.isActiveAndEnabled || !zone.Contains(player.transform.position + Vector3.up * .8f)) continue;
                float distance = (zone.transform.position - player.transform.position).sqrMagnitude;
                if (distance < nearest) { next = zone; nearest = distance; }
            }
        }
        if (next != displayed)
        {
            displayed = next;
            SetPromptVisible(next != null);
            if (next != null)
            {
                if (promptText != null) promptText.text = next.Label;
                if (legacyText != null) legacyText.text = next.Label;
            }
        }
        if (next != null && Input.GetKeyDown(KeyCode.E))
        {
            if (next.IsOccupiedByOther(player))
            { Debug.Log("[Robby interaction] 다른 플레이어가 사용 중입니다.", next); return; }
            if (next.Kind == RobbyInteractionZone.Action.Pet && next.CatTarget == null ||
                next.Kind == RobbyInteractionZone.Action.Sit && next.SeatTarget == null)
            { Debug.LogWarning("[Robby interaction] 고양이 대상 또는 앉을 위치를 Inspector에서 지정해주세요: " + next.name, next); return; }
            player.RequestInteraction(next.Id);
        }
    }
    private void OnDisable() { displayed = null; SetPromptVisible(false); }
}
