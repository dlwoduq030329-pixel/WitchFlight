using Fusion;
using UnityEngine;

public partial class PlayerData
{
    [Networked] public int MatchBotSlot { get; private set; }
    public bool IsMatchBot => MatchBotSlot > 0;
    public PlayerRef ParticipantRef => IsMatchBot ? BattleParticipants.BotRef(MatchBotSlot) : Object.InputAuthority;
    public void InitializeMatchBot(int slot, int team, PlayerConfig config)
    {
        MatchBotSlot = slot;
        teamIndex = team; camp = team == 1 ? Camp.A : Camp.B;
        config = config.Sanitized();
        hat = (HatType)config.hatIndex; broom = (BroomType)config.broomIndex;
        hairStylePreset = config.hairStylePreset;
        bangsLength = config.bangsLength; bangsDirection = config.bangsDirection;
        sideHairLength = config.sideHairLength; ahogeLength = config.ahogeLength;
        hairColor = config.hairColor; clothColor = config.clothColor; eyeColor = config.eyeColor;
        wandIndex = config.wandIndex;
        playerName = $"마녀 BOT {slot}";
        magic1 = MagicType.Fire; magic2 = MagicType.Flare;
        ready = true; IsLoadoutInitialized = true; IsRoomOwner = false;
    }
    public void ChooseBotCounters(bool previousRound)
    {
        if (!IsMatchBot || !Object.HasStateAuthority) return;
        MagicType first = MagicType.Fire, second = MagicType.Ice;
        // Stable team order distributes opponents without depending on spawn/HashSet enumeration order.
        var opponents = new System.Collections.Generic.List<PlayerData>();
        int teammateOrder = 0;
        foreach (var data in PerkPlayers)
        {
            if (data == null || data.Object == null || !data.Object.IsValid || data.Runner != Runner || !data.IsLoadoutInitialized) continue;
            if (!data.IsMatchBot && data.teamIndex != teamIndex) opponents.Add(data);
            if (data.IsMatchBot && data.teamIndex == teamIndex && data.ParticipantRef.RawEncoded < ParticipantRef.RawEncoded) teammateOrder++;
        }
        opponents.Sort((a,b) => a.ParticipantRef.RawEncoded.CompareTo(b.ParticipantRef.RawEncoded));
        PlayerData opponent = opponents.Count > 0 ? opponents[teammateOrder % opponents.Count] : null;
        if (opponent != null)
        {
            first = previousRound ? opponent.MagicChangeBase1 : opponent.magic1;
            second = previousRound ? opponent.MagicChangeBase2 : opponent.magic2;
        }
        MatchBotSettings.Default.CounterLoadout(first, second, out var a, out var b);
        if (teammateOrder > 0)
        {
            // With two teammates, compare the first bot's planned counters, not its possibly stale current loadout.
            var firstOpponent = opponents.Count > 0 ? opponents[0] : null;
            MagicType otherFirst = firstOpponent == null ? MagicType.Fire : previousRound ? firstOpponent.MagicChangeBase1 : firstOpponent.magic1;
            MagicType otherSecond = firstOpponent == null ? MagicType.Ice : previousRound ? firstOpponent.MagicChangeBase2 : firstOpponent.magic2;
            MatchBotSettings.Default.CounterLoadout(otherFirst, otherSecond, out var x, out var y);
            if ((a == x && b == y) || (a == y && b == x)) MatchBotSettings.Default.DiversifyCounterLoadout(a, ref b);
        }
        if (!previousRound) { magic1 = a; magic2 = b; }
        else if (BattleManager.Instance != null)
        {
            // Obey the same per-round swap limit as humans, including a one-slot limit.
            if (!BattleManager.Instance.TryChangeRoundMagic(this, MagicChangeRound, a, b, out _))
                if (!BattleManager.Instance.TryChangeRoundMagic(this, MagicChangeRound, a, magic2, out _))
                    BattleManager.Instance.TryChangeRoundMagic(this, MagicChangeRound, magic1, b, out _);
        }
    }
    public void ChooseBotPerk()
    {
        if (!IsMatchBot || !Object.HasStateAuthority || !CanSelectPerks) return;
        bool right = GetPerkCandidate(false) == PerkId.None;
        var candidate = GetPerkCandidate(right);
        if (candidate == PerkId.None) return;
        if (PerkRules.Common(candidate)) CommonPerksMask |= PerkRules.Bit(candidate);
        else if (right) PerkSlot2Level++; else PerkSlot1Level++;
        PerkChoicesUsed++; PerkRevision++; RefreshCommonCandidates();
        Player.FindInRunner(Runner, ParticipantRef)?.RefreshPerkVitals(false);
    }
}
