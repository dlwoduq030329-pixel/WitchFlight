using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class PlayerData
{
    // This object survives character respawns and all three rounds. Never save to DataConfig/BACKND.
    private static readonly HashSet<PlayerData> perkPlayers = new();
    public static IEnumerable<PlayerData> PerkPlayers => perkPlayers;
    public static PlayerData FindPerkData(NetworkRunner runner, PlayerRef player)
    {
        foreach (var data in perkPlayers)
            if (data != null && data.Object != null && data.Object.IsValid && data.Runner == runner && data.ParticipantRef == player) return data;
        return null;
    }
    [Networked] public bool PerksInitialized { get; private set; }
    [Networked] public int PerkXp { get; private set; }
    [Networked] public int PerkChoicesEarned { get; private set; }
    [Networked] public int PerkChoicesUsed { get; private set; }
    [Networked] public int PerkSlot1Level { get; private set; }
    [Networked] public int PerkSlot2Level { get; private set; }
    [Networked] public int CommonPerksMask { get; private set; }
    [Networked] public int PerkRevision { get; private set; }
    [Networked] private PerkId LeftCommonCandidate { get; set; }
    [Networked] private PerkId RightCommonCandidate { get; set; }
    private PerkXpLedger perkLedger;
    private float passiveXpTime;
    public event Action<bool, PerkId, int> PerkSelectionResult;
    public PerkStatTable PerkTable => BattleManager.Instance != null ? BattleManager.Instance.Perks : PerkStatTable.Default;
    public int PendingPerkChoices => Mathf.Max(0, PerkChoicesEarned - PerkChoicesUsed);
    public int PerkLevel(int slot) => slot == 1 ? PerkSlot1Level : slot == 2 ? PerkSlot2Level : 0;
    public bool HasCommonPerk(PerkId id) => (CommonPerksMask & PerkRules.Bit(id)) != 0;
    public bool HasSpellPerk(int slot, PerkId id)
    {
        MagicType magic = slot == 1 ? magic1 : slot == 2 ? magic2 : MagicType.None;
        int level = PerkLevel(slot);
        return level >= 1 && PerkRules.Spell(magic,1) == id || level >= 2 && PerkRules.Spell(magic,2) == id;
    }
    public PerkId GetPerkCandidate(bool right)
    {
        if (!PerksInitialized || PendingPerkChoices == 0 || PerkChoicesUsed >= PerkRules.MaxChoices) return PerkId.None;
        return right ? PerkRules.Candidate(magic2, PerkSlot2Level, PerkSlot1Level, RightCommonCandidate)
            : PerkRules.Candidate(magic1, PerkSlot1Level, PerkSlot2Level, LeftCommonCandidate);
    }
    public bool CanSelectPerks => Object != null && Object.IsValid && PerksInitialized &&
        BattleManager.Instance != null && (BattleManager.Instance.Phase == BattleStartPhase.Playing ||
        BattleManager.Instance.Phase == BattleStartPhase.Countdown) && PendingPerkChoices > 0;

    public void ResetMatchPerks()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
        PerkXp = PerkChoicesEarned = PerkChoicesUsed = PerkSlot1Level = PerkSlot2Level = CommonPerksMask = 0;
        PerkRevision++;
        PerksInitialized = true;
        perkLedger = new PerkXpLedger(); passiveXpTime = 0;
        RefreshCommonCandidates();
    }
    private void RefreshCommonCandidates()
    {
        // Stable offers for this choice. Reopening UI and swapping weapons cannot reroll them.
        int seed = ParticipantRef.PlayerId + PerkChoicesUsed * 3;
        LeftCommonCandidate = PerkRules.CommonCandidate(CommonPerksMask, seed);
        RightCommonCandidate = PerkRules.CommonCandidate(CommonPerksMask, seed + 5, LeftCommonCandidate);
    }
    public void PerkLoadoutChanged()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || !PerksInitialized) return;
        PerkRevision++; // Reject stale requests made against the old magic, but preserve XP and levels.
    }
    public bool RequestPerk(bool right)
    {
        if (!CanSelectPerks || !Object.HasInputAuthority || GetPerkCandidate(right) == PerkId.None) return false;
        RPC_SelectPerk(right, PerkRevision, GetPerkCandidate(right)); return true;
    }
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SelectPerk(bool right, int revision, PerkId expected)
    {
        PerkId candidate = GetPerkCandidate(right);
        bool valid = CanSelectPerks && revision == PerkRevision && candidate != PerkId.None && candidate == expected;
        if (valid)
        {
            if (PerkRules.Common(candidate)) {
                valid = (PerkSlot1Level == 2 || PerkSlot2Level == 2) && !HasCommonPerk(candidate);
                if (valid) CommonPerksMask |= PerkRules.Bit(candidate);
            }
            else if (right) {
                valid = PerkRules.CanUpgrade(PerkSlot2Level,PerkSlot1Level);
                if (valid) PerkSlot2Level++;
            }
            else {
                valid = PerkRules.CanUpgrade(PerkSlot1Level,PerkSlot2Level);
                if (valid) PerkSlot1Level++;
            }
            if (valid) {
                PerkChoicesUsed++; PerkRevision++; RefreshCommonCandidates();
                Player.FindInRunner(Runner,ParticipantRef)?.RefreshPerkVitals(false);
            }
        }
        RPC_PerkSelectionResult(valid,candidate,PerkRevision);
    }
    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_PerkSelectionResult(bool accepted, PerkId perk, int revision)
        => PerkSelectionResult?.Invoke(accepted,perk,revision);

    public override void FixedUpdateNetwork()
    {
        if (!CanAwardPerkXp()) return;
        perkLedger ??= new PerkXpLedger();
        perkLedger.Advance(Runner.DeltaTime);
        // Weight only passive time, not action XP. Fractional progress survives pickup/drop,
        // so low rates never round to zero and switching carrier cannot bypass the penalty.
        bool carryingFlag = BattleFlag.Instance != null && BattleFlag.Instance.IsCarriedBy(Runner, ParticipantRef);
        passiveXpTime += Runner.DeltaTime * (carryingFlag ? Mathf.Clamp01(PerkTable.flagCarrierPassiveXpMultiplier) : 1f);
        float interval = Mathf.Max(.1f,PerkTable.passiveInterval);
        while (passiveXpTime + .00001f >= interval) {
            passiveXpTime -= interval; AddPerkXp(PerkTable.passiveXp);
        }
    }
    private bool CanAwardPerkXp() => Object != null && Object.IsValid && Object.HasStateAuthority &&
        PerksInitialized && BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive;
    private void AddPerkXp(int amount)
    {
        if (amount <= 0 || PerkChoicesEarned >= PerkRules.MaxChoices) return;
        PerkXp = (int)Math.Min(100000000L,(long)PerkXp + amount);
        while (PerkChoicesEarned < PerkRules.MaxChoices && PerkXp >= PerkTable.RequiredXp(PerkChoicesEarned)) {
            PerkXp -= PerkTable.RequiredXp(PerkChoicesEarned); PerkChoicesEarned++;
        }
        if (PerkChoicesEarned == PerkRules.MaxChoices) PerkXp = 0;
    }
    private void ActionXp(int amount)
    {
        perkLedger ??= new PerkXpLedger();
        AddPerkXp(perkLedger.Cap(Mathf.Max(0,amount),PerkTable.actionWindowSeconds,PerkTable.actionXpLimit));
    }
    public void AwardPerkDamage(Player victim, float damage, float lifeMaxHp)
    {
        if (!CanAwardPerkXp() || victim == null || victim.IsTestBot || victim.TeamIndex == teamIndex ||
            victim.Runner != Runner || victim.ParticipantRef == ParticipantRef) return;
        perkLedger ??= new PerkXpLedger();
        ActionXp(perkLedger.Damage(victim.Object.Id.Raw, damage, lifeMaxHp,
            PerkTable.damageFraction,PerkTable.damageQuotasPerLife,PerkTable.damageXp));
    }
    public void AwardPerkKill(Player victim)
    {
        if (!CanAwardPerkXp() || victim == null || victim.IsTestBot || victim.TeamIndex == teamIndex || victim.Runner != Runner) return;
        perkLedger ??= new PerkXpLedger();
        if (perkLedger.Kill(victim.Object.Id.Raw) && perkLedger.RewardReady(0,PerkTable.killRewardCooldown)) ActionXp(PerkTable.killXp);
    }
    public void AwardPerkBinding(Player victim)
    {
        if (!CanAwardPerkXp() || victim == null || victim.IsTestBot || victim.TeamIndex == teamIndex || victim.Runner != Runner) return;
        perkLedger ??= new PerkXpLedger();
        if (perkLedger.RewardReady(1,PerkTable.bindingRewardCooldown)) ActionXp(PerkTable.bindingXp);
    }
    public void AwardPerkFlare()
    {
        if (!CanAwardPerkXp()) return;
        perkLedger ??= new PerkXpLedger();
        if (perkLedger.RewardReady(2,PerkTable.flareRewardCooldown)) ActionXp(PerkTable.flareXp);
    }
    public void AwardPerkFlag(int round)
    {
        if (!CanAwardPerkXp()) return;
        perkLedger ??= new PerkXpLedger();
        if (perkLedger.Flag(round)) ActionXp(PerkTable.flagXp);
    }
    public void AwardPerkRound(int round)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || !PerksInitialized) return;
        perkLedger ??= new PerkXpLedger();
        if (perkLedger.RoundBonus(round)) AddPerkXp(PerkTable.roundBonusXp);
    }
}
