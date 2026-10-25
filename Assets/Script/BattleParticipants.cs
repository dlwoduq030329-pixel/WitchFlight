using System.Collections.Generic;
using Fusion;

// Bot identities are application keys only, never Fusion input authorities or connected peers.
public static class BattleParticipants
{
    public static PlayerRef BotRef(int slot) => PlayerRef.FromIndex(1024 + slot);
    public static IEnumerable<PlayerRef> Refs(NetworkRunner runner)
    {
        foreach (var human in runner.ActivePlayers) yield return human;
        foreach (var data in PlayerData.PerkPlayers)
            if (data != null && data.Object != null && data.Object.IsValid && data.Runner == runner && data.IsMatchBot)
                yield return data.ParticipantRef;
    }
    public static bool TryGetPlayerObject(NetworkRunner runner, PlayerRef participant, out NetworkObject result)
    {
        foreach (var player in Player.ActiveCombatants)
            if (player != null && player.Object != null && player.Object.IsValid && player.Runner == runner &&
                player.IsMatchBot && player.ParticipantRef == participant) { result = player.Object; return true; }
        if (participant.RawEncoded >= BotRef(1).RawEncoded) { result = null; return false; }
        return runner.TryGetPlayerObject(participant, out result);
    }
}
