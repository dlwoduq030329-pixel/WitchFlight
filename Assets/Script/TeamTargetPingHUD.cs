using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class TeamTargetPingHUD : MonoBehaviour
{
    [SerializeField, Min(20f)] private float pingRadius = 120f;
    [SerializeField] private bool previewRadius = true;
    [SerializeField] private bool previewDuringPlay;
    [SerializeField] private RectTransform radiusGuide;
    public void SetPingRadius(float value)
    {
        pingRadius = Mathf.Max(20f, value);
        UpdateGuide();
    }
    private Camera worldCamera;
    private Camera uiCamera;
    private readonly List<PlayerData> allies = new(5);
    [SerializeField] private RectTransform[] directions = new RectTransform[5];
    [SerializeField] private RectTransform[] heads = new RectTransform[5];
    [SerializeField] private RectTransform canvasRect;
    private GameObject canvasObject => canvasRect != null ? canvasRect.gameObject : null;
    private Player localTarget;
    private float localHeadUntil;

    private void OnEnable()
    {
        enemyLockOn.TargetDesignated += LocalPing;
        UpdateGuide();
    }
    private void OnValidate()
    {
        pingRadius = Mathf.Max(20f, pingRadius);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }
    private bool IsBound => canvasRect != null && directions.Length == 5 && heads.Length == 5 &&
        System.Array.TrueForAll(directions, item => item != null) && System.Array.TrueForAll(heads, item => item != null);
    private void UpdateGuide()
    {
        if (radiusGuide == null) return;
        radiusGuide.sizeDelta = Vector2.one * (pingRadius * 2f);
        bool show = previewRadius && (!Application.isPlaying || previewDuringPlay);
        if (radiusGuide.gameObject.activeSelf != show) radiusGuide.gameObject.SetActive(show);
    }
    private void LateUpdate()
    {
        if (!IsBound) return;
        UpdateGuide();
        if (!Application.isPlaying)
        {
            canvasObject.SetActive(true);
            for (int i = 0; i < 5; i++)
            {
                float angle = i * Mathf.PI * 2f / 5f;
                directions[i].anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * pingRadius;
                directions[i].localRotation = Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg);
                directions[i].gameObject.SetActive(previewRadius);
                heads[i].gameObject.SetActive(false);
            }
            return;
        }
        if (worldCamera == null) worldCamera = Camera.main;
        Render(Player.LocalPlayer, worldCamera, enemyLockOn.LocalDesignatedTarget, pingRadius);
    }

    private void LocalPing(Player target)
    {
        localTarget = target;
        localHeadUntil = Time.unscaledTime + 3f;
    }

    private void Render(Player owner, Camera camera, Player designated, float radius)
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
        var canvas = canvasRect.GetComponentInParent<Canvas>();
        var root = canvas != null ? canvas.rootCanvas : null;
        uiCamera = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
        canvasObject.SetActive(true);
        // Fixed UI centre, independent of mouse aim and the forward-flight marker.
        Vector2 center = Vector2.zero;
        Vector2 centerScreen = RectTransformUtility.WorldToScreenPoint(uiCamera, canvasRect.TransformPoint(center));
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
            directions[i].gameObject.SetActive(false);
            heads[i].gameObject.SetActive(false);
            if (!valid) continue;
            Vector3 screen = camera.WorldToScreenPoint(target.LockAimPoint);
            bool onScreen = screen.z > 0f && camera.pixelRect.Contains((Vector2)screen);
            directions[i].gameObject.SetActive(!onScreen);
            Vector2 delta = (Vector2)screen - centerScreen;
            if (screen.z < 0f) delta = -delta;
            if (delta.sqrMagnitude < 0.01f) delta = Vector2.up;
            delta.Normalize();
            directions[i].anchoredPosition = center + delta * radius;
            directions[i].localRotation = Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg-90f);
            Vector3 headScreen = camera.WorldToScreenPoint(target.LockAimPoint + Vector3.up * 1.5f);
            if (head && headScreen.z > 0 && camera.pixelRect.Contains(headScreen))
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, headScreen, uiCamera, out var position);
                heads[i].anchoredPosition = position + new Vector2(0f, 22f);
                heads[i].localRotation = Quaternion.Euler(0,0,180f);
                heads[i].gameObject.SetActive(true);
            }
        }
    }
    public void Hide()
    {
        foreach (var arrow in directions) if (arrow != null) arrow.gameObject.SetActive(false);
        foreach (var arrow in heads) if (arrow != null) arrow.gameObject.SetActive(false);
    }
    private void OnDisable()
    {
        enemyLockOn.TargetDesignated -= LocalPing;
        Hide();
    }
}
