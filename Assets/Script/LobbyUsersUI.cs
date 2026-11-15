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
    [SerializeField] private Color allyColor = Color.red;
    [SerializeField] private Color enemyColor = Color.blue;
    private readonly List<PlayerData> roster = new(10);
    private bool dirty = true, wasVisible;
    private NetworkRunner lastRunner;
    private PlayerData lastLocal;

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
        bool visible = showInformation != null && tabUI != null && !tabUI.activeInHierarchy &&
            manager != null && manager.IsWaitingInCodeRoom && runner != null && runner.IsRunning &&
            local != null && local.Object != null && local.Object.IsValid && local.Runner == runner && local.teamIndex > 0;
        if (runner != lastRunner || local != lastLocal || visible != wasVisible) dirty = true;
        lastRunner = runner; lastLocal = local; wasVisible = visible;
        if (showInformation == null) return;
        if (showInformation.activeSelf != visible) showInformation.SetActive(visible);
        if (!visible || !dirty || users == null) return;
        dirty = false;
        Refresh(runner, local);
    }

    private void Refresh(NetworkRunner runner, PlayerData local)
    {
        roster.Clear();
        foreach (var data in PlayerData.PerkPlayers)
            if (data != null && data.Object != null && data.Object.IsValid && data.Runner == runner && data.teamIndex > 0)
                roster.Add(data);
        roster.Sort((a, b) =>
        {
            int aSide = a == local ? 0 : a.teamIndex == local.teamIndex ? 1 : 2;
            int bSide = b == local ? 0 : b.teamIndex == local.teamIndex ? 1 : 2;
            return aSide != bSide ? aSide.CompareTo(bSide) : a.ParticipantRef.RawEncoded.CompareTo(b.ParticipantRef.RawEncoded);
        });
        for (int i = 0; slots != null && i < slots.Length; i++)
        {
            Image slot = slots[i];
            if (slot == null) continue;
            bool occupied = i < roster.Count;
            if (slot.gameObject.activeSelf != occupied) slot.gameObject.SetActive(occupied);
            if (occupied) slot.color = roster[i].teamIndex == local.teamIndex ? allyColor : enemyColor;
        }
        // The authored HorizontalLayoutGroup centres only active children. No frame-by-frame layout work.
        if (users.transform is RectTransform rect) LayoutRebuilder.MarkLayoutForRebuild(rect);
    }
}
