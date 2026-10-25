using System;
using UnityEngine;
using UnityEngine.Audio;

public enum BattleSound
{
    ParryReady, ParryStart, ParrySuccess, DamageReceived, HitConfirmed, Death, Respawn,
    HealingReceived, MagicSlotChanged, BoostStart, BoostLoop, Portal, HealthPotion,
    ManaPotion, BoosterPickup, Updraft, InsufficientMana, PerkAvailable, PerkSelected,
    Emote, KillNotice, FlagPickup, FlagDrop, MatchIntro, Countdown, RoundStart,
    RoundWin, RoundLose, MatchVictory, MatchDefeat, Binding, Slow, HitStun, Burn,
    WallCrash, UIClick, UIConfirm, UICancel
}

public enum MagicSound { LockComplete, Cast, ProjectileLoop, Impact, ChannelLoop, ChannelEnd, FollowUp, AreaWarning, SummonAttack, EffectLoop }

[Serializable]
public sealed class BattleSoundCue
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Range(.25f, 3f)] public float pitch = 1f;
    [Tooltip("같은 소리의 최소 재생 간격. 패링 타이밍 알림은 여러 발이 동시에 들어와도 겹치지 않습니다.")]
    [Min(0f)] public float minimumInterval = .06f;
    [Range(0f, 1f)] public float spatialBlend = 1f;
    [Min(.1f)] public float minDistance = 4f;
    [Min(1f)] public float maxDistance = 80f;
}

[Serializable]
public sealed class BattleSoundEntry
{
    public string label;
    public BattleSound sound;
    public BattleSoundCue audio = new();
}

[Serializable]
public sealed class MagicSoundEntry
{
    public string label;
    public MagicType magic;
    public BattleSoundCue lockComplete = new();
    public BattleSoundCue cast = new();
    public BattleSoundCue projectileLoop = new();
    public BattleSoundCue impact = new();
    public BattleSoundCue channelLoop = new();
    public BattleSoundCue channelEnd = new();
    public BattleSoundCue followUp = new();
    public BattleSoundCue areaWarning = new();
    public BattleSoundCue summonAttack = new();
    public BattleSoundCue effectLoop = new();

    public BattleSoundCue Get(MagicSound sound) => sound switch
    {
        MagicSound.LockComplete => lockComplete, MagicSound.Cast => cast,
        MagicSound.ProjectileLoop => projectileLoop, MagicSound.Impact => impact,
        MagicSound.ChannelLoop => channelLoop, MagicSound.ChannelEnd => channelEnd,
        MagicSound.FollowUp => followUp, MagicSound.AreaWarning => areaWarning,
        MagicSound.SummonAttack => summonAttack, MagicSound.EffectLoop => effectLoop, _ => null
    };
}

// Resources/BattleAudioTable is the single sound assignment point; empty clips stay silent.
[CreateAssetMenu(menuName = "WitchFlight/Battle Audio Table")]
public sealed class BattleAudioTable : ScriptableObject
{
    [Range(0f, 1f)] public float masterVolume = 1f;
    public AudioMixerGroup outputMixer;
    [Range(8, 128)] public int maxVoices = 48;
    [Header("Local flight speed sounds")]
    public AudioClip flightWindClip;
    [Range(0f, 1f)] public float flightWindVolume = .18f;
    [Tooltip("비워두면 비행 바람을 짧게 부풀렸다 줄이는 소리로 사용합니다.")]
    public AudioClip flightPassClip;
    [Range(0f, 1f)] public float flightPassVolume = .3f;
    public BattleSoundEntry[] commonSounds = Array.Empty<BattleSoundEntry>();
    public MagicSoundEntry[] magicSounds = Array.Empty<MagicSoundEntry>();

    public BattleSoundCue Get(BattleSound sound)
    {
        foreach (var entry in commonSounds)
            if (entry != null && entry.sound == sound) return entry.audio;
        return null;
    }
    public BattleSoundCue Get(MagicType magic, MagicSound sound)
    {
        foreach (var entry in magicSounds)
            if (entry != null && entry.magic == magic) return entry.Get(sound);
        return null;
    }
}
