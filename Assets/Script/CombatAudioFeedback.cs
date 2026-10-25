using UnityEngine;

// Reads render-time replicated state; never plays from predicted/resimulated movement ticks.
[DisallowMultipleComponent]
public sealed class CombatAudioFeedback : MonoBehaviour
{
    private Player owner;
    private bool initialized, alive, boosting;
    private int hit, death, parryStart, parry, portal, slot;
    private MagicType channel;
    private BattleAudio.Loop boostLoop, channelLoop;
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
            parry = owner.ParrySequence; portal = owner.PortalExitSequence; slot = owner.CurrentMagicSlot;
            alive = owner.IsAlive;
            for (int i = 0; i < statuses.Length; i++) statuses[i] = owner.HasStatusEffect(statusTypes[i]);
            initialized = true;
        }
        if (hit != owner.HitSequence) { hit = owner.HitSequence; Sound(BattleSound.DamageReceived); }
        if (death != owner.DeathSequence) { death = owner.DeathSequence; Sound(BattleSound.Death); }
        if (parryStart != owner.ParryStartSequence) { parryStart = owner.ParryStartSequence; Sound(BattleSound.ParryStart); }
        if (parry != owner.ParrySequence) { parry = owner.ParrySequence; Sound(BattleSound.ParrySuccess); }
        if (portal != owner.PortalExitSequence) { portal = owner.PortalExitSequence; Sound(BattleSound.Portal); }
        if (!alive && owner.IsAlive) Sound(BattleSound.Respawn);
        alive = owner.IsAlive;
        if (slot != owner.CurrentMagicSlot)
        {
            slot = owner.CurrentMagicSlot;
            if (IsLocal) BattleAudio.Play(BattleSound.MagicSlotChanged, local: true);
        }
        bool gameplay = owner.IsAlive && BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive;
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
            bool active = gameplay && owner.HasStatusEffect(statusTypes[i]);
            if (active && !statuses[i]) Sound(statusSounds[i]);
            statuses[i] = active;
        }
    }
    private void Sound(BattleSound sound) => BattleAudio.Play(sound, owner.LockAimPoint, transform, IsLocal);
    private void StopLoops() { boostLoop.Stop(); channelLoop.Stop(); boosting = false; channel = MagicType.None; }
    private void OnDisable() { StopLoops(); initialized = false; }
}
