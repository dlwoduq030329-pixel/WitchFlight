using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class TeamTargetPingHUD : MonoBehaviour
{
    private static readonly Color32[] Colors = {
        new Color32(231,40,56,255), new Color32(45,145,255,255),
        new Color32(50,235,100,255), new Color32(255,225,45,255), new Color32(255,95,195,255) };
    private readonly List<PlayerData> allies = new(5);
    private readonly RectTransform[] directions = new RectTransform[5];
    private readonly RectTransform[] heads = new RectTransform[5];
    private GameObject canvasObject;
    private RectTransform canvasRect;
    private Player localTarget;
    private float localHeadUntil;

    public void LocalPing(Player target)
    {
        localTarget = target;
        localHeadUntil = Time.unscaledTime + 3f;
    }

    public void Render(Player owner, Camera camera, Player designated, float radius)
    {
        if (owner == null || owner != Player.LocalPlayer || owner.Object == null || !owner.Object.IsValid ||
            !owner.IsAlive || camera == null || CombatPresentation.MenuOpen ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
        { Hide(); return; }
        var local = PlayerData.Local;
        allies.Clear();
        foreach (var data in PlayerData.PerkPlayers)
            if (data != null && data != local && data.Object != null && data.Object.IsValid &&
                data.Runner == owner.Runner && data.teamIndex == owner.TeamIndex && !data.IsMatchBot)
                allies.Add(data);
        allies.Sort((a,b) => a.ParticipantRef.RawEncoded.CompareTo(b.ParticipantRef.RawEncoded));
        EnsureCanvas();
        canvasObject.SetActive(true);
        // Centre the ring on the same forward-flight marker used by BattleHud.
        Vector2 centerScreen = BattleHud.GetForwardAimScreenPoint(camera);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, centerScreen, null, out var center);
        for (int i = 0; i < 5; i++)
        {
            Player target = null;
            bool head = false;
            if (i == 0)
            {
                target = designated;
                head = target != null && target == localTarget && Time.unscaledTime < localHeadUntil;
            }
            else if (i - 1 < allies.Count)
            {
                var data = allies[i-1];
                if (data.TeamPingTarget.IsValid && owner.Runner.TryFindObject(data.TeamPingTarget, out var obj))
                    target = obj.GetComponent<Player>();
                head = !data.TeamPingHeadTimer.ExpiredOrNotRunning(owner.Runner);
            }
            bool valid = target != null && target.Object != null && target.Object.IsValid &&
                target.Runner == owner.Runner && target.IsTargetableBy(owner);
            directions[i].gameObject.SetActive(valid);
            heads[i].gameObject.SetActive(false);
            if (!valid) continue;
            Vector3 screen = camera.WorldToScreenPoint(target.LockAimPoint);
            Vector2 delta = (Vector2)screen - centerScreen;
            if (screen.z < 0f) delta = -delta;
            if (delta.sqrMagnitude < 0.01f) delta = Vector2.up;
            delta.Normalize();
            // Small concentric offsets keep multiple teammates' arrows legible.
            directions[i].anchoredPosition = center + delta * (radius + i * 10f);
            directions[i].localRotation = Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg-90f);
            Vector3 headScreen = camera.WorldToScreenPoint(target.LockAimPoint + Vector3.up * 1.5f);
            if (head && headScreen.z > 0 && camera.pixelRect.Contains(headScreen))
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, headScreen, null, out var position);
                heads[i].anchoredPosition = position + new Vector2((i-2)*22f, 22f);
                heads[i].localRotation = Quaternion.Euler(0,0,180f);
                heads[i].gameObject.SetActive(true);
            }
        }
    }
    private void EnsureCanvas()
    {
        if (canvasObject != null) return;
        canvasObject = new GameObject("TeamTargetPings", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasRect = canvasObject.GetComponent<RectTransform>();
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920,1080);
        for (int i=0;i<5;i++)
        {
            directions[i] = CreateArrow("Direction " + i, Colors[i]);
            heads[i] = CreateArrow("Head ping " + i, Colors[i]);
        }
    }
    private RectTransform CreateArrow(string name, Color tint)
    {
        var arrow = new GameObject(name, typeof(RectTransform), typeof(DesignatedTargetTriangle));
        var rect = arrow.GetComponent<RectTransform>();
        rect.SetParent(canvasRect, false);
        rect.sizeDelta = new Vector2(24,30);
        var graphic = arrow.GetComponent<DesignatedTargetTriangle>();
        graphic.color = tint;
        graphic.raycastTarget = false;
        return rect;
    }
    public void Hide() { if (canvasObject != null) canvasObject.SetActive(false); }
    private void OnDisable() => Hide();
    private void OnDestroy() { if (canvasObject != null) Destroy(canvasObject); }
}
