using UnityEngine;
using UnityEngine.UI;

// Authored beneath BattleUI's moving forward-aim marker. No runtime UI creation
// or scene search: its parent supplies the position and normal HUD visibility.
[DisallowMultipleComponent]
public sealed class DashChargeHud : MonoBehaviour
{
    [SerializeField] private Slider[] segments = new Slider[6];
    [SerializeField] private RectTransform leftLine;
    [SerializeField] private RectTransform rightLine;
    [SerializeField, Min(1f)] private float segmentSpacing = 54f;
    [SerializeField, Min(0f)] private float lineOffset = 40f;
    [SerializeField] private CanvasGroup visibility;
    [Tooltip("충전 표시만 부드럽게 따라가는 시간. 소모는 즉시 반영합니다.")]
    [SerializeField, Min(.001f)] private float fillSmoothingSeconds = .04f;
    [Header("충전 완료 반짝임")]
    [SerializeField] private Color rechargeFlashColor = Color.white;
    [SerializeField, Min(.01f)] private float rechargeFlashSeconds = .3f;
    private Graphic[] fillGraphics;
    private Color[] fillColors;
    private float[] flashStarted;
    private Player previousPlayer;
    private int previousCharges = -1;
    private int previousCapacity = -1;

    private void OnEnable()
    {
        previousPlayer = null; previousCharges = -1; previousCapacity = -1;
        // HUD visibility toggles frequently between rounds. Reuse storage while
        // still rebinding the authored graphics and their current colours each enable.
        if (fillGraphics == null || fillGraphics.Length != segments.Length)
        {
            fillGraphics = new Graphic[segments.Length];
            fillColors = new Color[segments.Length];
            flashStarted = new float[segments.Length];
        }
        for (int i = 0; i < segments.Length; i++)
        {
            fillGraphics[i] = segments[i] != null && segments[i].fillRect != null
                ? segments[i].fillRect.GetComponent<Graphic>() : null;
            if (fillGraphics[i] != null) fillColors[i] = fillGraphics[i].color;
            flashStarted[i] = -1f;
        }
    }

    private void ClearRechargeFlashes()
    {
        if (flashStarted == null) return;
        for (int i = 0; i < flashStarted.Length; i++)
        {
            if (flashStarted[i] < 0f) continue;
            if (fillGraphics[i] != null) fillGraphics[i].color = fillColors[i];
            flashStarted[i] = -1f;
        }
    }

    private void OnDisable() => ClearRechargeFlashes();

    private void UpdateRechargeFlash(int index, bool completed, bool filled)
    {
        Graphic graphic = fillGraphics[index];
        if (graphic == null) return;
        if (completed)
        {
            // Capture the authored/current tint before changing it; never alter bar dimensions.
            if (flashStarted[index] < 0f) fillColors[index] = graphic.color;
            flashStarted[index] = Time.unscaledTime;
        }
        if (flashStarted[index] < 0f) return;
        float t = (Time.unscaledTime - flashStarted[index]) / Mathf.Max(.01f, rechargeFlashSeconds);
        if (!filled || t >= 1f)
        {
            graphic.color = fillColors[index];
            flashStarted[index] = -1f;
            return;
        }
        graphic.color = Color.Lerp(rechargeFlashColor, fillColors[index], Mathf.SmoothStep(0f, 1f, t));
    }

    private void UpdateCapacity(int capacity)
    {
        CopyFirstSegmentLayout();
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            segments[i].gameObject.SetActive(i < capacity);
            var rect = (RectTransform)segments[i].transform;
            rect.anchoredPosition = new Vector2((i - (capacity - 1) * .5f) * segmentSpacing, rect.anchoredPosition.y);
        }
        float edge = (capacity - 1) * .5f * segmentSpacing + lineOffset;
        if (leftLine != null) leftLine.anchoredPosition = new Vector2(-edge, leftLine.anchoredPosition.y);
        if (rightLine != null) rightLine.anchoredPosition = new Vector2(edge, rightLine.anchoredPosition.y);
        previousCapacity = capacity;
    }

    // Copy the authored template only when the HUD/capacity changes, never during fill updates.
    // Keep all six scene objects and their independent Slider values/references.
    [ContextMenu("첫 번째 대쉬 바 크기를 나머지에 적용")]
    private void CopyFirstSegmentLayout()
    {
        if (segments == null || segments.Length == 0 || segments[0] == null) return;
        var template = (RectTransform)segments[0].transform;
        for (int i = 1; i < segments.Length; i++)
        {
            Slider segment = segments[i];
            if (segment == null) continue;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObjects(segment.GetComponentsInChildren<RectTransform>(true), "Copy dash bar layout");
#endif
            CopyRectLayout(template, (RectTransform)segment.transform, segment.fillRect, true);
        }
    }

    private static void CopyRectLayout(RectTransform source, RectTransform target, RectTransform fill, bool root)
    {
        // Slider owns the fill anchors; copying those would overwrite its current charge fraction.
        if (target != fill)
        {
            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
        }
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.localScale = source.localScale;
        target.localRotation = source.localRotation;
        Vector3 position = source.anchoredPosition3D;
        if (root) position.x = target.anchoredPosition3D.x;
        target.anchoredPosition3D = position;
        for (int i = 0; i < Mathf.Min(source.childCount, target.childCount); i++)
        {
            var sourceChild = source.GetChild(i) as RectTransform;
            var targetChild = target.GetChild(i) as RectTransform;
            if (sourceChild != null && targetChild != null && sourceChild.name == targetChild.name)
                CopyRectLayout(sourceChild, targetChild, fill, false);
        }
    }

    private void LateUpdate()
    {
        var player = Player.LocalPlayer;
        bool visible = player != null && player.Object != null && player.Object.IsValid && player.IsAlive &&
            BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive && !CombatPresentation.MenuOpen;
        if (visibility != null) visibility.alpha = visible ? 1 : 0;
        if (!visible)
        {
            ClearRechargeFlashes();
            previousPlayer = null; previousCharges = -1;
            return;
        }
        int count = player.DashCharges;
        bool capacityChanged = previousCapacity != player.MaximumDashCharges;
        bool charged = !capacityChanged && previousPlayer == player && previousCharges >= 0 && count > previousCharges;
        if (capacityChanged || previousPlayer != player) ClearRechargeFlashes();
        if (capacityChanged) UpdateCapacity(player.MaximumDashCharges);
        bool snap = capacityChanged || previousPlayer != player || previousCharges != count;
        float progress = player.DashRechargeProgress;
        float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(.001f, fillSmoothingSeconds));
        for (int i = 0; i < Mathf.Min(segments.Length, previousCapacity); i++)
        {
            var slider = segments[i];
            if (slider == null) continue;
            float target = i < count ? 1 : i == count ? progress : 0;
            float value = snap || target < slider.value || target >= 1 ? target : Mathf.Lerp(slider.value, target, blend);
            slider.SetValueWithoutNotify(value);
            UpdateRechargeFlash(i, charged && i >= previousCharges && i < count, i < count);
        }
        previousPlayer = player; previousCharges = count;
    }
}
