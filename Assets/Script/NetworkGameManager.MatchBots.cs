using Fusion;
using UnityEngine;
using System.Collections.Generic;

public partial class NetworkGameManager
{
    public bool IsBotMatch => !IsTraining && BattleMatchRules.HasBots(CurrentMatchMode);
    private void ReplaceDepartedHumanWithBot(int team)
    {
        if (!IsRoomHost || !IsBotMatch || (team != 1 && team != 2)) return;
        int count = 0, slot = 1;
        var appearances = new List<PlayerConfig>();
        var titles = new List<int>();
        foreach (var existing in playerDatas.Values)
        {
            if (existing == null || existing.Object == null || !existing.Object.IsValid) continue;
            if (existing.IsMatchBot) titles.Add(existing.EquippedTitleId);
            if (existing.teamIndex != team) continue;
            count++;
            appearances.Add(existing.GetPlayerConfig());
        }
        if (count >= RequiredPlayerCount / 2) return;
        while (playerDatas.ContainsKey(BattleParticipants.BotRef(slot))) slot++;
        var config = MatchBotSettings.RandomAppearance(appearances);
        int title = TitleCatalog.ChooseBotTitle(titles);
        NetworkObject obj = null;
        try
        {
            obj = _runner.Spawn(playerDataPrefab, Vector3.zero, Quaternion.identity, PlayerRef.None,
                onBeforeSpawned: (r, o) => o.GetComponent<PlayerData>().InitializeMatchBot(slot, team, config, title));
            if (obj == null) throw new System.InvalidOperationException("Replacement bot spawn failed.");
            var data = obj.GetComponent<PlayerData>();
            playerDatas[data.ParticipantRef] = data;
            _runner.MakeDontDestroyOnLoad(obj.gameObject);
            // Preserve the existing supports/fighters and their current round loadouts.
            // This bot fills the human's objective slot rather than reassigning all bots.
            data.SetMatchBotRole(MatchBotRole.Objective, count);
            data.ChooseBotCounters(false);
            BattleManager.Instance?.SpawnReplacementBot(data);
            Debug.Log($"[MatchConnection] Replaced departed human: team={team}, bot={data.ParticipantRef}", this);
        }
        catch (System.Exception error)
        {
            if (obj != null && obj.IsValid)
            {
                var data = obj.GetComponent<PlayerData>();
                if (data != null) playerDatas.Remove(data.ParticipantRef);
                _runner.Despawn(obj);
            }
            Debug.LogException(error, this);
            SetMatchStatus("이탈한 플레이어를 대체할 봇 생성에 실패했습니다. Console을 확인해주세요.");
        }
    }

    private void AssignBotRoles()
    {
        // Assign once before character spawning. PlayerData preserves roles across respawns.
        var bots = new List<PlayerData>();
        for (int team = 1; team <= 2; team++)
        {
            bots.Clear();
            foreach (var data in playerDatas.Values)
                if (data != null && data.IsMatchBot && data.teamIndex == team) bots.Add(data);
            bots.Sort((a, b) => a.ParticipantRef.RawEncoded.CompareTo(b.ParticipantRef.RawEncoded));
            for (int i = 0; i < bots.Count; i++)
                bots[i].SetMatchBotRole(MatchBotRoles.ForTeamSlot(RequiredPlayerCount / 2, i), i);
        }
    }

    private bool FillBotSeats()
    {
        if (!IsRoomHost || !IsBotMatch) return false;
        var created = new List<NetworkObject>();
        try
        {
            int slot = 1;
            for (int team = 1; team <= 2; team++)
            {
                int count = 0;
                foreach (var data in playerDatas.Values)
                    if (data != null && data.teamIndex == team) count++;
                while (count < RequiredPlayerCount / 2)
                {
                    while (playerDatas.ContainsKey(BattleParticipants.BotRef(slot))) slot++;
                    int selectedSlot = slot, selectedTeam = team;
                    var teammateAppearance = new List<PlayerConfig>();
                    foreach (var existing in playerDatas.Values)
                        if (existing != null && existing.IsMatchBot && existing.teamIndex == team)
                            teammateAppearance.Add(existing.GetPlayerConfig());
                    var config = MatchBotSettings.RandomAppearance(teammateAppearance);
                    var usedBotTitles = new List<int>();
                    foreach (var existing in playerDatas.Values)
                        if (existing != null && existing.IsMatchBot) usedBotTitles.Add(existing.EquippedTitleId);
                    int botTitleId = TitleCatalog.ChooseBotTitle(usedBotTitles);
                    NetworkObject obj = _runner.Spawn(playerDataPrefab, Vector3.zero, Quaternion.identity, PlayerRef.None,
                        onBeforeSpawned: (r,o) => o.GetComponent<PlayerData>().InitializeMatchBot(selectedSlot, selectedTeam, config, botTitleId));
                    if (obj == null) throw new System.InvalidOperationException("Bot PlayerData spawn failed.");
                    created.Add(obj);
                    var data = obj.GetComponent<PlayerData>();
                    playerDatas[data.ParticipantRef] = data;
                    _runner.MakeDontDestroyOnLoad(obj.gameObject);
                    count++; slot++;
                }
            }
            AssignBotRoles();
            foreach (var data in playerDatas.Values) if (data != null && data.IsMatchBot && !IsRaceMode) data.ChooseBotCounters(false);
            return true;
        }
        catch (System.Exception error)
        {
            foreach (var obj in created)
                if (obj != null && obj.IsValid)
                {
                    var data = obj.GetComponent<PlayerData>();
                    if (data != null) playerDatas.Remove(data.ParticipantRef);
                    _runner.Despawn(obj);
                }
            Debug.LogException(error, this);
            SetMatchStatus("봇 생성에 실패했습니다. Console을 확인한 뒤 다시 시작해주세요.");
            return false;
        }
    }
}
