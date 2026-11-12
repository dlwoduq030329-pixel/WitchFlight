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
    [Tooltip("Profiles 아래에 배치된 Profile01, Profile02… 슬롯을 사용합니다. 현재 적 플레이어만 표시합니다.")]
    [SerializeField] private ReadyProfile[] readyProfiles = Array.Empty<ReadyProfile>();
    [SerializeField] private ProfileImageTable profileTable;
    private bool readyRequestPending, requestedReady, profilesBound;
    private float readyRequestedAt;
    private readonly List<PlayerData> readyRoster = new();
    private readonly List<PlayerData> readyRosterScratch = new();
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
        RefreshReadyProfileSlots(battle.RoundNumber);
    }

    private void RefreshReadyProfileSlots(int round)
    {
        CollectReadyOpponents();
        if (profileTable == null) profileTable = ProfileImageTable.Default;
        for (int i = 0; i < readyProfiles.Length; i++)
        {
            var slot = readyProfiles[i]; if (slot == null || slot.root == null) continue;
            bool present = i < readyRoster.Count;
            Show(slot.root, present);
            if (!present)
            {
                Show(slot.readyCheck, false);
                if (slot.image != null) { slot.image.sprite = null; slot.image.enabled = false; }
                continue;
            }
            var data = readyRoster[i];
            if (slot.image != null)
            {
                var sprite = profileTable != null ? profileTable.GetSprite(data.playerprofile) : null;
                if (slot.image.sprite != sprite) slot.image.sprite = sprite;
                if (slot.image.enabled != (sprite != null)) slot.image.enabled = sprite != null;
            }
            Show(slot.readyCheck, data.MagicChangeRound == round && data.RoundSwapReady);
        }
    }

    private void CollectReadyOpponents()
    {
        readyRosterScratch.Clear();
        if (HasLocalData && localData.teamIndex > 0)
            foreach (var data in PlayerData.PerkPlayers)
                if (data != null && data.Object != null && data.Object.IsValid && data.Runner == localData.Runner &&
                    data.IsLoadoutInitialized && data.teamIndex > 0 && data.teamIndex != localData.teamIndex)
                    readyRosterScratch.Add(data);
        bool changed = readyRoster.Count != readyRosterScratch.Count;
        if (!changed)
            foreach (var data in readyRosterScratch)
                if (!readyRoster.Contains(data)) { changed = true; break; }
        if (!changed) return;
        readyRoster.Clear();
        readyRoster.AddRange(readyRosterScratch);
        readyRoster.Sort(CompareReadyProfiles);
    }

    private static int CompareReadyProfiles(PlayerData a, PlayerData b) =>
        a.ParticipantRef.RawEncoded.CompareTo(b.ParticipantRef.RawEncoded);

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
        if (profilesRoot == null)
        {
            readyProfiles = Array.Empty<ReadyProfile>();
            profilesBound = true;
            return;
        }
        var rows = new List<Transform>();
        foreach (Transform child in profilesRoot)
            if (ReadyProfileNumber(child) > 0) rows.Add(child);
        rows.Sort((a, b) => ReadyProfileNumber(a).CompareTo(ReadyProfileNumber(b)));
        // Rebind every authored row; old serialized four-slot references must not limit the new UI.
        readyProfiles = new ReadyProfile[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var slot = readyProfiles[i] = new ReadyProfile { root = rows[i].gameObject };
            foreach (Transform child in slot.root.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name.Trim();
                // Copies authored in the scene can still have ProfileImage04 / Profile04Check inside.
                if (slot.readyCheck == null && name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith("Check", StringComparison.OrdinalIgnoreCase))
                    slot.readyCheck = child.gameObject;
                if (slot.image == null && name.StartsWith("ProfileImage", StringComparison.OrdinalIgnoreCase))
                    slot.image = child.GetComponent<Image>();
            }
            Show(slot.readyCheck, false);
            Show(slot.root, false);
        }
        Show(profilesRoot.gameObject, true);
        profilesBound = true;
    }

    private static int ReadyProfileNumber(Transform row)
    {
        string name = row.name.Trim();
        return name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name.Substring("Profile".Length), out int number) ? number : 0;
    }
}
