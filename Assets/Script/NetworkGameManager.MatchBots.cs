using Fusion;
using UnityEngine;
using System.Collections.Generic;

public partial class NetworkGameManager
{
    public bool IsBotMatch => !IsTraining && BattleMatchRules.HasBots(CurrentMatchMode);
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
            foreach (var data in playerDatas.Values) if (data != null && data.IsMatchBot) data.ChooseBotCounters(false);
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
