using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class BattlePerkUI
{
    private RectTransform tabInfoPanel, tabInfoParent;
    private Image tabInfoImage;
    private TMP_Text tabInfoName, tabInfoDescription;
    private Canvas tabInfoCanvas;
    private bool tabOwnsCursor, tabBlocksCombat, tabWaitForMouseRelease, tabTooltipDirty = true;
    private Vector2 tabLastPointer = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
    private Vector2 tabLastScreen;
    private int tabHoveredRow = -1, tabHoveredPerk = -1;

    public static bool TabCapturesMouse => active != null && active.isActiveAndEnabled && active.hasAuthoredTab &&
        active.Allowed && !BattleEmoteUI.CapturesMouse && !active.gate.Showing && Input.GetKey(KeyCode.Tab);

    private void BindTabTooltip(Transform root)
    {
        tabInfoPanel = (TabChild(root, "InfoPanel") ?? TabChild(root, "InoPanel")) as RectTransform;
        if (tabInfoPanel == null) return;
        tabInfoParent = tabInfoPanel.parent as RectTransform;
        tabInfoCanvas = tabInfoPanel.GetComponentInParent<Canvas>();
        tabInfoImage = TabComponent<Image>(tabInfoPanel, "MagicImage");
        tabInfoName = TabComponent<TMP_Text>(tabInfoPanel, "MagicName");
        tabInfoDescription = TabComponent<TMP_Text>(tabInfoPanel, "PerkText");
        // This panel must never cover an icon's hover target or intercept input.
        ReadOnly(tabInfoPanel.gameObject);
        tabInfoPanel.SetAsLastSibling();
        Show(tabInfoPanel.gameObject, false);
    }

    private void UpdateTabInteraction(bool open)
    {
        if (open)
        {
            if (!tabOwnsCursor) tabTooltipDirty = true;
            tabOwnsCursor = true;
            tabWaitForMouseRelease = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }
        else
        {
            HideTabTooltip();
            if (tabOwnsCursor)
            {
                tabOwnsCursor = false;
                var battle = BattleManager.Instance;
                bool flight = Application.isFocused && !CombatPresentation.MenuOpen && battle != null &&
                    (battle.IsGameplayActive || battle.Phase == BattleStartPhase.Countdown);
                Cursor.lockState = flight ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !flight;
            }
        }
        // A mouse button held while inspecting the scoreboard must not become an attack on closing it.
        tabBlocksCombat = open || tabWaitForMouseRelease;
        if (!open && !Input.GetMouseButton(0) && !Input.GetMouseButton(1)) tabWaitForMouseRelease = false;
    }

    private void HideTabTooltip()
    {
        if (tabInfoPanel != null) Show(tabInfoPanel.gameObject, false);
        tabHoveredRow = tabHoveredPerk = -1;
        tabTooltipDirty = true;
    }

    // Screen-space Y grows upward. Keep the exact four pivots requested for this UI.
    private static Vector2 TabTooltipPivot(Vector2 pointer, Vector2 screen) =>
        new Vector2(pointer.x < screen.x * .5f ? 0f : 1f, pointer.y >= screen.y * .5f ? 0f : 1f);

    private void UpdateTabTooltip()
    {
        if (tabInfoPanel == null || tabInfoParent == null) return;
        Vector2 pointer = Input.mousePosition;
        Vector2 screen = new Vector2(Screen.width, Screen.height);
        bool pointerChanged = pointer != tabLastPointer || screen != tabLastScreen;
        if (!pointerChanged && !tabTooltipDirty) return;
        tabLastPointer = pointer;
        tabLastScreen = screen;
        Camera uiCamera = tabInfoCanvas != null && tabInfoCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? tabInfoCanvas.worldCamera : null;
        int hoveredRow = -1, hoveredPerk = -1;
        // Forty cached rectangles at most, only when the pointer/layout/data changes. No raycast
        // allocations, object discovery, or dependency on an EventSystem in the authored scene.
        for (int row = 0; row < authoredTabViews.Length && hoveredRow < 0; row++)
        {
            if (!authoredTabRows[row].gameObject.activeInHierarchy || !Ready(authoredTabPlayers[row])) continue;
            for (int perk = 0; perk < 4; perk++)
            {
                var card = authoredTabViews[row].effects[perk];
                if (card.displayedPerk == PerkId.None || card.icon == null || !card.icon.isActiveAndEnabled) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(card.icon.rectTransform, pointer, uiCamera)) continue;
                hoveredRow = row; hoveredPerk = perk; break;
            }
        }
        if (hoveredRow < 0)
        {
            HideTabTooltip();
            tabTooltipDirty = false;
            return;
        }
        if (tabTooltipDirty || hoveredRow != tabHoveredRow || hoveredPerk != tabHoveredPerk)
        {
            var card = authoredTabViews[hoveredRow].effects[hoveredPerk];
            var definition = authoredTabPlayers[hoveredRow].PerkTable.Get(card.displayedPerk);
            if (definition == null) { HideTabTooltip(); return; }
            SetIcon(tabInfoImage, card.icon.sprite);
            Text(tabInfoName, definition.displayName);
            Text(tabInfoDescription, Description(definition));
        }
        tabHoveredRow = hoveredRow;
        tabHoveredPerk = hoveredPerk;
        tabTooltipDirty = false;
        tabInfoPanel.pivot = TabTooltipPivot(pointer, screen);
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(tabInfoParent, pointer, uiCamera, out Vector3 world))
            tabInfoPanel.position = world;
        Show(tabInfoPanel.gameObject, true);
    }
}
