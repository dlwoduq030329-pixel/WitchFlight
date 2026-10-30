using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class BattleRoundUI
{
    [Serializable] public sealed class ReadyProfile
    {
        public GameObject root;
        public Image image;
        public GameObject readyCheck;
    }
    [Header("Round readiness profiles (empty = find Profiles under MagicSelectUI)")]
    [SerializeField] private Transform profilesRoot;
    [SerializeField] private ReadyProfile[] readyProfiles = new ReadyProfile[4];
    [SerializeField] private ProfileImageTable profileTable;
    private bool readyRequestPending, requestedReady, profilesBound;
    private float readyRequestedAt;
    private readonly List<PlayerData> readyRoster = new();
    private readonly Dictionary<Button, bool> lockedSwapButtons = new();
    private static readonly Color ReadyDark = new Color32(0x3D, 0x3D, 0x3D, 255);
    private static readonly Color ReadyCyan = new Color32(0x44, 0xDC, 0xCC, 255);

    public void ToggleRoundReady()
    {
        var battle = BattleManager.Instance;
        if (!HasLocalData || returning || requestPending || readyRequestPending || battle == null ||
            battle.Phase != BattleStartPhase.Intermission || battle.PhaseRemainingSeconds <= 0 ||
            localData.MagicChangeRound != battle.RoundNumber) return;
        requestedReady = !localData.RoundSwapReady;
        readyRequestPending = true;
        readyRequestedAt = Time.unscaledTime;
        if (!localData.RequestRoundReady(battle.RoundNumber, requestedReady)) readyRequestPending = false;
        if (requestedReady) SetDescriptionVisible(false);
        SetSwapEditingLocked(true);
    }

    private void RefreshReadyUI(BattleManager battle)
    {
        BindReadyProfiles();
        bool ready = HasLocalData && localData.RoundSwapReady;
        if (readyRequestPending)
        {
            if (HasLocalData && ready == requestedReady)
            {
                readyRequestPending = false;
                Status(ready ? "준비 완료. 다른 플레이어를 기다립니다. 다시 누르면 취소합니다." : "준비를 취소했습니다. 마법을 교체할 수 있습니다.");
            }
            else if (Time.unscaledTime - readyRequestedAt > 5f)
            { readyRequestPending = false; Status("준비 상태 응답이 지연됩니다. 다시 확인해주세요."); }
        }
        SetSwapEditingLocked(ready || readyRequestPending);
        if (resetMagicButton != null)
        {
            resetMagicButton.interactable = HasLocalData && !returning && !requestPending && !readyRequestPending &&
                localData.MagicChangeRound == battle.RoundNumber && battle.PhaseRemainingSeconds > 0;
            if (resetMagicButton.targetGraphic != null) resetMagicButton.targetGraphic.color = ready ? ReadyCyan : ReadyDark;
            var colors = resetMagicButton.colors;
            colors.normalColor = colors.highlightedColor = colors.selectedColor = colors.pressedColor = colors.disabledColor = Color.white;
            colors.colorMultiplier = 1; resetMagicButton.colors = colors;
            var label = resetMagicButton.GetComponentInChildren<TMP_Text>(true);
            Text(label, ready ? "준비 완료 취소" : "준비 완료");
            if (label != null) label.color = ready ? ReadyDark : ReadyCyan;
        }
        readyRoster.Clear();
        var runner = HasLocalData ? localData.Runner : NetworkGameManager.Instance?.Runner;
        foreach (var data in PlayerData.PerkPlayers)
            if (runner != null && data != null && data.Object != null && data.Object.IsValid &&
                data.Runner == runner && data.IsLoadoutInitialized) readyRoster.Add(data);
        readyRoster.Sort(CompareReadyProfiles);
        if (profileTable == null) profileTable = ProfileImageTable.Default;
        for (int i = 0; i < readyProfiles.Length; i++)
        {
            var slot = readyProfiles[i]; if (slot == null || slot.root == null) continue;
            bool present = i < readyRoster.Count;
            Show(slot.root, present);
            if (!present) { Show(slot.readyCheck, false); continue; }
            var data = readyRoster[i];
            if (slot.image != null)
            {
                var sprite = profileTable != null ? profileTable.GetSprite(data.playerprofile) : null;
                slot.image.sprite = sprite; slot.image.enabled = sprite != null;
            }
            Show(slot.readyCheck, data.MagicChangeRound == battle.RoundNumber && data.RoundSwapReady);
        }
    }

    private int CompareReadyProfiles(PlayerData a, PlayerData b)
    {
        int Rank(PlayerData data) => data == localData ? 0 : localData != null && data.teamIndex == localData.teamIndex ? 1 : 2;
        int rank = Rank(a).CompareTo(Rank(b));
        return rank != 0 ? rank : a.ParticipantRef.RawEncoded.CompareTo(b.ParticipantRef.RawEncoded);
    }

    private void SetSwapEditingLocked(bool locked)
    {
        if (locked)
        {
            if (lockedSwapButtons.Count > 0 || magicSwapPanel == null) return;
            foreach (var button in magicSwapPanel.GetComponentsInChildren<Button>(true))
                if (button != resetMagicButton)
                { lockedSwapButtons[button] = button.interactable; button.interactable = false; }
        }
        else
        {
            foreach (var entry in lockedSwapButtons) if (entry.Key != null) entry.Key.interactable = entry.Value;
            lockedSwapButtons.Clear();
        }
    }

    private void BindReadyProfiles()
    {
        if (profilesBound || magicSwapPanel == null) return;
        if (profilesRoot == null)
            foreach (var child in magicSwapPanel.GetComponentsInChildren<Transform>(true))
                if (string.Equals(child.name, "Profiles", StringComparison.OrdinalIgnoreCase)) { profilesRoot = child; break; }
        if (readyProfiles == null || readyProfiles.Length != 4) Array.Resize(ref readyProfiles, 4);
        if (profilesRoot == null)
            profilesRoot = Rect("Profiles", magicSwapPanel.transform, new Vector2(0, 440), new Vector2(360, 70));
        for (int i = 0; i < 4; i++)
        {
            readyProfiles[i] ??= new ReadyProfile(); var slot = readyProfiles[i];
            if (slot.root == null && i < profilesRoot.childCount) slot.root = profilesRoot.GetChild(i).gameObject;
            if (slot.root == null)
            {
                slot.root = Rect("Profile0" + (i + 1), profilesRoot, new Vector2(-135 + 90 * i, 0), new Vector2(64, 64)).gameObject;
                slot.image = Image(slot.root.transform, Vector2.zero, new Vector2(64, 64));
                slot.readyCheck = Label(slot.root.transform, "ProfileCheck", "준비", new Vector2(0, -22), new Vector2(64, 24), 18).gameObject;
            }
            foreach (Transform child in slot.root.GetComponentsInChildren<Transform>(true))
            {
                if (slot.readyCheck == null && child.name.IndexOf("check", StringComparison.OrdinalIgnoreCase) >= 0)
                    slot.readyCheck = child.gameObject;
                if (slot.image == null && child.name.StartsWith("ProfileImage", StringComparison.OrdinalIgnoreCase))
                    slot.image = child.GetComponent<Image>();
            }
            if (slot.image == null) slot.image = slot.root.GetComponent<Image>();
            Show(slot.readyCheck, false);
        }
        Show(profilesRoot.gameObject, true);
        profilesBound = true;
    }
}
