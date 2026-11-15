using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Only the hovered icon runs pointer-follow work. The authored panel is reused.
public sealed class LobbyEquipmentHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Action enter, leave;
    public RectTransform panel;
    private bool hovering;
    private Canvas canvas;
    public void OnPointerEnter(PointerEventData data)
    {
        hovering = true;
        enter?.Invoke();
        canvas = panel != null ? panel.GetComponentInParent<Canvas>() : null;
        Follow(data.position);
    }
    public void OnPointerExit(PointerEventData data) => Hide();
    private void OnDisable() => Hide();
    private void Hide()
    {
        if (!hovering) return;
        hovering = false;
        leave?.Invoke();
    }
    private void LateUpdate()
    {
        if (hovering && panel != null && panel.gameObject.activeInHierarchy)
            Follow(Input.mousePosition);
    }
    private void Follow(Vector2 screen)
    {
        if (panel == null || !(panel.parent is RectTransform parent)) return;
        panel.pivot = new Vector2(screen.x < Screen.width * .5f ? 0 : 1,
            screen.y >= Screen.height * .5f ? 0 : 1);
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, screen, camera, out var point))
            panel.position = point;
    }
}
