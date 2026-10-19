using System;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class linkuserinfo
{
    // Reuse the authored Card01..Card04, including the two inactive cards.
    // Do not create or reposition card roots, and keep explicit Inspector bindings.
    private void EnsureAuthoredWaitingSlots()
    {
        if (CodeWaitingRoomPanel == null) return;
        if (waitingRoomSlots == null) waitingRoomSlots = Array.Empty<WaitingRoomSlot>();
        if (waitingRoomSlots.Length < 4) Array.Resize(ref waitingRoomSlots, 4);
        for (int i = 0; i < 4; i++)
        {
            Transform card = waitingRoomSlots[i]?.slotRoot != null
                ? waitingRoomSlots[i].slotRoot.transform : waitingRoomPanel.transform.Find("Card0" + (i + 1));
            if (card == null) continue;
            var slot = waitingRoomSlots[i] ??= new WaitingRoomSlot();
            if (slot.slotRoot == null) slot.slotRoot = card.gameObject;
            if (slot.playerLabel == null)
                slot.playerLabel = i == 0 ? Aslotname : i == 1 ? Bslotname : card.Find("Text")?.GetComponent<TMP_Text>();
            if (slot.profileImage == null)
            {
                slot.profileImage = i == 0 ? AslotProfile : i == 1 ? BslotProfile : FindCardProfile(card);
                // Saved Card03/04 have a card background and a name but no profile Image yet.
                // Add only the missing portrait inside those existing cards, matching Card01.
                if (slot.profileImage == null && i >= 2)
                    slot.profileImage = CreateMissingCardProfile(card);
            }
            if (slot.teamLabel == null) slot.teamLabel = card.Find("Team Label")?.GetComponent<TMP_Text>();
            if (slot.teamLabel == null)
            {
                var label = new GameObject("Team Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                label.transform.SetParent(card, false);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = Vector2.one * .5f;
                label.rectTransform.anchoredPosition = new Vector2(0, 105);
                label.rectTransform.sizeDelta = new Vector2(220, 36);
                if (slot.playerLabel != null) label.font = slot.playerLabel.font;
                label.fontSize = 22; label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white; label.raycastTarget = false; label.richText = false;
                slot.teamLabel = label;
            }
        }
    }

    private static Image FindCardProfile(Transform card)
    {
        // Prefer the deepest Profile Image, not the card's decorative background/frame.
        Image found = null;
        foreach (Image image in card.GetComponentsInChildren<Image>(true))
            if (image.transform != card && image.name.StartsWith("Profile", StringComparison.Ordinal)) found = image;
        return found;
    }

    private Image CreateMissingCardProfile(Transform card)
    {
        var image = new GameObject("Profile", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(card, false);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.one * .5f;
        image.rectTransform.anchoredPosition = new Vector2(0, 5);
        image.rectTransform.sizeDelta = new Vector2(100, 100);
        Transform sourceCard = waitingRoomPanel.transform.Find("Card01");
        if (AslotProfile != null && sourceCard != null)
        {
            RectTransform source = AslotProfile.rectTransform;
            image.rectTransform.anchoredPosition = sourceCard.InverseTransformPoint(source.TransformPoint(source.rect.center));
            if (source.rect.width > 0 && source.rect.height > 0) image.rectTransform.sizeDelta = source.rect.size;
            image.material = AslotProfile.material;
        }
        image.preserveAspect = true; image.raycastTarget = false;
        return image;
    }

    // Host-first cards are stable on every peer; the dropdown follows the local player's card.
    public Image GetCodeWaitingRoomProfile(PlayerRef player)
    {
        if (!waitingRoomInitialized || CodeWaitingRoomPanel == null) return null;
        int index = waitingParticipants.IndexOf(player);
        return index >= 0 && waitingRoomSlots != null && index < waitingRoomSlots.Length
            ? waitingRoomSlots[index]?.profileImage : null;
    }
}
