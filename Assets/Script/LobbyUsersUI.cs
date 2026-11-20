using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.UI;

// Scene-owned, on InteractiveCanvas (outside the ShowInformation panel it hides).
[DisallowMultipleComponent]
public sealed class LobbyUsersUI : MonoBehaviour
{
    [SerializeField] private GameObject users;
    [Tooltip("Tab UI가 닫혀 있을 때 표시할 ShowInformation 전체 패널")]
    [SerializeField] private GameObject showInformation;
    [Tooltip("Tab으로 열고 닫는 IntroAndRobby/Robby 오브젝트")]
    [SerializeField] private GameObject tabUI;
    [Tooltip("User, User (1), ... User (9) 순서. 기존 Image만 재사용합니다.")]
    [SerializeField] private Image[] slots;
    [SerializeField] private Color allyColor = new Color32(0, 199, 255, 255);
    [SerializeField] private Color enemyColor = new Color32(255, 0, 0, 255);
    [SerializeField] private Color emptyColor = new Color32(120, 115, 115, 75);
    private readonly List<PlayerData> roster = new(10);
    private bool dirty = true, wasVisible;
    private NetworkRunner lastRunner;
    private PlayerData lastLocal;
    private int lastCapacity = -1;

    private void OnEnable() { PlayerData.LobbyRosterChanged += MarkDirty; dirty = true; }
    private void Awake() { if (showInformation != null) showInformation.SetActive(false); }
    private void OnDisable()
    {
        PlayerData.LobbyRosterChanged -= MarkDirty;
        if (showInformation != null) showInformation.SetActive(false);
        wasVisible = false;
    }
    private void MarkDirty() => dirty = true;

    private void LateUpdate()
    {
        var manager = NetworkGameManager.Instance;
        var runner = manager != null ? manager.Runner : null;
        var local = PlayerData.Local;
        int capacity = manager != null ? manager.RequiredPlayerCount : 0;
        bool visible = showInformation != null && tabUI != null && !tabUI.activeInHierarchy &&
            manager != null && manager.IsWaitingInCodeRoom && runner != null && runner.IsRunning &&
            local != null && local.Object != null && local.Object.IsValid && local.Runner == runner && local.teamIndex > 0;
        if (runner != lastRunner || local != lastLocal || visible != wasVisible || capacity != lastCapacity) dirty = true;
        lastCapacity = capacity;
        lastRunner = runner; lastLocal = local; wasVisible = visible;
        if (showInformation == null) return;
        if (showInformation.activeSelf != visible) showInformation.SetActive(visible);
        if (!visible || !dirty || users == null) return;
        dirty = false;
        Refresh(runner, local, capacity);
    }

    private void Refresh(NetworkRunner runner, PlayerData local, int capacity)
    {
        roster.Clear();
        foreach (var data in PlayerData.PerkPlayers)
            if (data != null && data.Object != null && data.Object.IsValid && data.Runner == runner && data.teamIndex > 0)
                roster.Add(data);
        roster.Sort((a, b) =>
        {
            // Keep each player's order when changing teams; only the colour changes.
            int aSide = a == local ? 0 : 1;
            int bSide = b == local ? 0 : 1;
            return aSide != bSide ? aSide.CompareTo(bSide) : a.ParticipantRef.RawEncoded.CompareTo(b.ParticipantRef.RawEncoded);
        });
        for (int i = 0; slots != null && i < slots.Length; i++)
        {
            Image slot = slots[i];
            if (slot == null) continue;
            bool occupied = i < roster.Count;
            bool available = i < capacity;
            if (slot.gameObject.activeSelf != available) slot.gameObject.SetActive(available);
            slot.color = available && occupied
                ? (roster[i].teamIndex == local.teamIndex ? allyColor : enemyColor)
                : emptyColor;
        }
        // The authored HorizontalLayoutGroup centres only active children. No frame-by-frame layout work.
        if (users.transform is RectTransform rect) LayoutRebuilder.MarkLayoutForRebuild(rect);
    }
}
