using UnityEngine;

// Reads render-time replicated state; never plays from predicted/resimulated movement ticks.
[DisallowMultipleComponent]
public sealed class CombatAudioFeedback : MonoBehaviour
{
    private Player owner;
    private bool initialized, alive, boosting, lowHealth, charging;
    private int hit, death, parryStart, parry, portal, lockFailure;
    private MagicType channel;
    private BattleAudio.Loop boostLoop, channelLoop, lockLoop, burnLoop, manaBuffLoop;
    private readonly bool[] statuses = new bool[4];
    private static readonly StatusEffectType[] statusTypes = {
        StatusEffectType.Binding, StatusEffectType.Slow, StatusEffectType.HitStun, StatusEffectType.Burn };
    private static readonly BattleSound[] statusSounds = {
        BattleSound.Binding, BattleSound.Slow, BattleSound.HitStun, BattleSound.Burn };
    private bool IsLocal => owner.Object.HasInputAuthority;

    private void Awake() { owner = GetComponent<Player>(); BattleAudio.Initialize(); }
    private void LateUpdate()
    {
        if (owner == null || owner.Object == null || !owner.Object.IsValid) { StopLoops(); initialized = false; return; }
        if (!initialized)
        {
            hit = owner.HitSequence; death = owner.DeathSequence; parryStart = owner.ParryStartSequence;
            parry = owner.ParrySequence; portal = owner.PortalExitSequence;
            alive = owner.IsAlive;
            lockFailure = owner.LockFailureSequence;
            lowHealth = owner.NowHp <= owner.MaxHp * .25f;
            for (int i = 0; i < statuses.Length; i++) statuses[i] = StatusActive(i);
            initialized = true;
        }
        if (hit != owner.HitSequence) { hit = owner.HitSequence; Sound(BattleSound.DamageReceived); Sound(BattleSound.HurtVoice); }
        if (death != owner.DeathSequence) { death = owner.DeathSequence; Sound(BattleSound.Death); }
        if (parryStart != owner.ParryStartSequence) { parryStart = owner.ParryStartSequence; Sound(BattleSound.ParryStart); }
        if (parry != owner.ParrySequence) { parry = owner.ParrySequence; Sound(BattleSound.ParrySuccess); }
        if (portal != owner.PortalExitSequence) { portal = owner.PortalExitSequence; Sound(BattleSound.Portal); }
        if (!alive && owner.IsAlive) Sound(BattleSound.Respawn);
        alive = owner.IsAlive;
        bool gameplay = owner.IsAlive && BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive;
        if (!gameplay || owner.NowHp > owner.MaxHp * .35f) lowHealth = false;
        if (gameplay && !lowHealth && owner.NowHp <= owner.MaxHp * .25f)
        {
            lowHealth = true;
            if (IsLocal) Sound(BattleSound.LowHealthVoice);
        }
        if (lockFailure != owner.LockFailureSequence)
        {
            lockFailure = owner.LockFailureSequence;
            if (IsLocal) BattleAudio.Play(BattleSound.LockFailed, local: true);
        }
        bool nextCharge = gameplay && IsLocal && owner.SelectedMagicStats.requiresTarget &&
            !owner.SelectedMagicStats.TargetsAllies && (owner.LockProgress > 0f || owner.IsFullyLocked);
        if (!nextCharge) lockLoop.Stop();
        else if (!lockLoop.IsActive) lockLoop = BattleAudio.StartLoop(BattleSound.LockCharging, null, true);
        charging = nextCharge;
        bool nextBoost = gameplay && owner.IsBoosting;
        if (boosting != nextBoost)
        {
            boosting = nextBoost; boostLoop.Stop();
            if (boosting)
            {
                Sound(BattleSound.BoostStart);
                boostLoop = BattleAudio.StartLoop(BattleSound.BoostLoop, transform, IsLocal);
            }
        }
        MagicType nextChannel = gameplay ? owner.ChannelMagic : MagicType.None;
        if (channel != nextChannel)
        {
            channelLoop.Stop();
            if (channel != MagicType.None)
                BattleAudio.PlayMagic(channel, MagicSound.ChannelEnd, owner.MagicCastPosition, transform);
            channel = nextChannel;
            if (channel != MagicType.None)
            {
                BattleAudio.PlayMagic(channel, MagicSound.Cast, owner.MagicCastPosition, transform);
                channelLoop = BattleAudio.StartMagicLoop(channel, MagicSound.ChannelLoop, owner.MagicCastAnchor);
            }
        }
        for (int i = 0; i < statuses.Length; i++)
        {
            bool active = gameplay && StatusActive(i);
            if (active && !statuses[i] && statusTypes[i] != StatusEffectType.Burn) Sound(statusSounds[i]);
            statuses[i] = active;
        }
        // Burn audio follows the replicated perk-created status, not ordinary fire hits.
        bool burning = gameplay && owner.HasStatusEffect(StatusEffectType.Burn);
        if (!burning) burnLoop.Stop();
        else if (!burnLoop.IsActive) burnLoop = BattleAudio.StartLoop(BattleSound.Burn, transform, IsLocal);
        bool recoveringMana = gameplay && owner.HasManaBuff;
        if (!recoveringMana) manaBuffLoop.Stop();
        else if (!manaBuffLoop.IsActive) manaBuffLoop = BattleAudio.StartLoop(BattleSound.ManaBuffZone, transform, IsLocal);
    }
    private bool StatusActive(int index) => statusTypes[index] == StatusEffectType.Slow
        ? owner.HasIceSlowAudio : owner.HasStatusEffect(statusTypes[index]);
    private void Sound(BattleSound sound) => BattleAudio.Play(sound, owner.LockAimPoint, transform, IsLocal);
    private void StopLoops() { boostLoop.Stop(); channelLoop.Stop(); lockLoop.Stop(); burnLoop.Stop(); manaBuffLoop.Stop(); boosting = charging = false; channel = MagicType.None; }
    private void OnDisable() { StopLoops(); initialized = false; }
}
