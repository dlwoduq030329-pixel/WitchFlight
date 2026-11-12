using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class BattlePerkUI
{
    private bool hasAuthoredTab, authoredTabWasOpen, authoredTabLocalReady;
    private readonly Transform[] authoredTabRows = new Transform[10];
    private readonly PerkPlayerView[] authoredTabViews = new PerkPlayerView[10];
    private readonly PlayerData[] authoredTabPlayers = new PlayerData[10];
    private readonly List<(PlayerData player, int team)> authoredTabMembership = new();
    private PlayerData authoredTabLocal;

    private static Transform TabChild(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name.Trim(), name, StringComparison.OrdinalIgnoreCase)) return child;
        return null;
    }

    private static T TabComponent<T>(Transform row, string name) where T : Component =>
        TabChild(row, name)?.GetComponent<T>();

    // Resolve the scene-authored layout once, including inactive canvases. Never clone or resize rows.
    private void TryBindAuthoredTab()
    {
        foreach (var root in gameObject.scene.GetRootGameObjects())
        foreach (var candidate in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (!string.Equals(candidate.name.Trim(), "TabUI", StringComparison.OrdinalIgnoreCase)) continue;
            var own = TabChild(candidate, "TeamALayoutGroup");
            var enemy = TabChild(candidate, "TeamBLayoutGroup");
            if (own == null || enemy == null) continue;
            bool valid = true;
            for (int side = 0; side < 2; side++)
            for (int i = 0; i < 5; i++)
            {
                int index = side * 5 + i;
                var row = TabChild(side == 0 ? own : enemy, $"Player{i + 1:00}Panel");
                authoredTabRows[index] = row;
                if (row == null) { valid = false; continue; }
                var view = new PerkPlayerView
                {
                    playerName = TabComponent<TMP_Text>(row, "Nickname"),
                    profile = TabComponent<Image>(row, "ProfileImage") ?? TabComponent<Image>(row, "profileImae"),
                    damageDealtText = TabComponent<TMP_Text>(row, "Damage"),
                    damageTakenText = TabComponent<TMP_Text>(row, "DamageTaken"),
                    killsText = TabComponent<TMP_Text>(row, "KillCount"),
                    deathsText = TabComponent<TMP_Text>(row, "DeathCount"),
                    flagIndicator = TabChild(row, "FlagImage")?.gameObject,
                    slot1Effects = Array.Empty<PerkCardView>(),
                    slot2Effects = Array.Empty<PerkCardView>(),
                    commonEffects = Array.Empty<PerkCardView>(),
                    effects = new PerkCardView[4]
                };
                for (int perk = 0; perk < 4; perk++)
                {
                    view.effects[perk] = new PerkCardView { icon = TabComponent<Image>(row, $"Perk{perk + 1}") };
                    SetIcon(view.effects[perk].icon, null);
                }
                authoredTabViews[index] = view;
                // Bind at the card root: profile/name/title can be nested independently by designers.
                PlayerTitleLabel.Bind(row, null);
                Show(view.flagIndicator, false);
                Show(row.gameObject, false);
            }
            if (!valid)
            {
                Debug.LogError("TabUI: 각 팀의 Player01Panel~Player05Panel을 확인하세요.", candidate);
                return;
            }
            if (comparisonRoot != candidate.gameObject) Show(comparisonRoot, false);
            if (teamComparisonRoot != candidate.gameObject) Show(teamComparisonRoot, false);
            comparisonRoot = teamComparisonRoot = candidate.gameObject;
            hasAuthoredTab = true;
            Show(own.gameObject, true);
            Show(enemy.gameObject, true);
            ReadOnly(comparisonRoot);
            BindTabTooltip(candidate);
            Show(comparisonRoot, false);
            return;
        }
    }

    // While Tab is held, compare only the small registered participant list. Sorting/discovery happens
    // on opening or membership/team changes, and text/icon writes happen only for changed snapshots.
    private bool AuthoredTabRosterChanged()
    {
        bool localReady = Ready(local);
        bool changed = authoredTabLocal != local || authoredTabLocalReady != localReady;
        authoredTabLocalReady = localReady;
        int index = 0;
        if (local != null && local.Object != null && local.Object.IsValid)
        foreach (var data in PlayerData.PerkPlayers)
        {
            if (data == null || data.Object == null || !data.Object.IsValid || data.Runner != local.Runner ||
                !data.IsLoadoutInitialized || (data.teamIndex != 1 && data.teamIndex != 2)) continue;
            var member = (data, data.teamIndex);
            if (index >= authoredTabMembership.Count) { authoredTabMembership.Add(member); changed = true; }
            else if (authoredTabMembership[index] != member) { authoredTabMembership[index] = member; changed = true; }
            index++;
        }
        if (index < authoredTabMembership.Count)
        {
            authoredTabMembership.RemoveRange(index, authoredTabMembership.Count - index);
            changed = true;
        }
        authoredTabLocal = local;
        return changed;
    }

    private void UpdateAuthoredTab(bool open)
    {
        if (!open)
        {
            Show(comparisonRoot, false);
            authoredTabWasOpen = false;
            return;
        }
        bool rosterChanged = AuthoredTabRosterChanged();
        bool refresh = !authoredTabWasOpen || rosterChanged;
        authoredTabWasOpen = true;
        if (refresh)
        {
            BattleMatchRules.CollectRoster(Ready(local) ? local.Runner : null, comparisonRoster);
            Array.Clear(authoredTabPlayers, 0, authoredTabPlayers.Length);
            int ally = 0, enemy = 5;
            foreach (var data in comparisonRoster)
            {
                // CollectRoster places self first, then allies, then opponents on either client.
                if (data.teamIndex == local.teamIndex)
                { if (ally < 5) authoredTabPlayers[ally++] = data; }
                else if (enemy < 10) authoredTabPlayers[enemy++] = data;
            }
        }
        for (int i = 0; i < authoredTabPlayers.Length; i++)
        {
            var data = authoredTabPlayers[i];
            bool changed = UpdateSnapshot(i, data);
            if (!refresh && !changed) continue;
            tabTooltipDirty = true;
            FillPlayer(authoredTabViews[i], data);
            PlayerTitleLabel.Bind(authoredTabRows[i], Ready(data) ? data : null);
            Show(authoredTabRows[i].gameObject, Ready(data));
        }
        // Populate hidden rows first, avoiding a frame of stale names/icons when reopening.
        Show(comparisonRoot, true);
    }
}
