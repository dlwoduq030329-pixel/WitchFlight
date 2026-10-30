using System.Collections.Generic;
using Fusion;
using UnityEngine;

// Viewer-local audio. Authority events arrive through existing snapshots/RPCs.
// No AudioListener is created: the scene's existing listener determines 3D attenuation.
public sealed class BattleAudio : MonoBehaviour
{
    public struct Loop
    {
        internal BattleAudio host;
        internal int slot, generation;
        public void Stop()
        {
            if (host != null) host.Stop(slot, generation);
            host = null;
        }
    }
    private sealed class Voice
    {
        public AudioSource source;
        public Transform follow;
        public bool following;
        public int generation;
        public float started;
    }
    private static BattleAudio instance;
    private BattleAudioTable table;
    private readonly List<Voice> voices = new();
    private readonly Dictionary<(BattleSoundCue, int), float> nextAllowed = new();
    private BattleFlag flag;
    private BattleStartPhase phase;
    private PlayerRef carrier;
    private int countdown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static void Initialize()
    {
        if (instance != null) return;
        instance = new GameObject("Battle Audio (generated)").AddComponent<BattleAudio>();
        instance.table = Resources.Load<BattleAudioTable>("BattleAudioTable");
    }
    public static void Play(BattleSound sound, Vector3 position = default, Transform follow = null, bool local = false)
    {
        Initialize();
        instance.StartVoice(instance.table != null ? instance.table.Get(sound) : null, position, follow, local, false);
        BattleSound? voice = sound switch
        {
            BattleSound.Death => BattleSound.DeathVoice,
            BattleSound.HealingReceived => BattleSound.HealVoice,
            BattleSound.ParryStart => BattleSound.ParryVoice,
            BattleSound.MatchIntro => BattleSound.VSVoice,
            BattleSound.MatchVictory => BattleSound.VictoryVoice,
            BattleSound.MatchDefeat => BattleSound.DefeatVoice,
            _ => null
        };
        if (voice.HasValue) Play(voice.Value, position, follow, local);
    }
    public static void PlayMagic(MagicType magic, MagicSound sound, Vector3 position, Transform follow = null, bool local = false)
    {
        Initialize();
        instance.StartVoice(instance.table != null ? instance.table.Get(magic, sound) : null, position, follow, local, false);
        if (sound == MagicSound.Cast)
        {
            Play(BattleSound.MagicCast, position, follow, local);
            if (magic != MagicType.Healing) Play(BattleSound.AttackVoice, position, follow, local);
        }
        if (sound == MagicSound.SummonAttack) Play(BattleSound.CatAttackVoice, position, follow, local);
    }
    public static Loop StartLoop(BattleSound sound, Transform follow, bool local = false)
    {
        Initialize();
        return instance.StartVoice(instance.table != null ? instance.table.Get(sound) : null,
            follow != null ? follow.position : Vector3.zero, follow, local, true);
    }
    public static Loop StartMagicLoop(MagicType magic, MagicSound sound, Transform follow)
    {
        Initialize();
        return instance.StartVoice(instance.table != null ? instance.table.Get(magic, sound) : null,
            follow != null ? follow.position : Vector3.zero, follow, false, true);
    }
    private Loop StartVoice(BattleSoundCue cue, Vector3 position, Transform follow, bool local, bool loop)
    {
        if (cue == null || table == null || table.masterVolume <= 0f || cue.volume <= 0f)
            return default;
        var key = (cue, local || follow == null ? 0 : follow.GetInstanceID());
        if (!loop && nextAllowed.TryGetValue(key, out float next) && Time.unscaledTime < next) return default;
        AudioClip clip = cue.ChooseClip();
        if (clip == null) return default;
        int index = -1;
        for (int i = 0; i < voices.Count; i++)
            if (!voices[i].source.isPlaying) { index = i; break; }
        if (index < 0 && voices.Count < Mathf.Clamp(table.maxVoices, 8, 128))
        {
            var child = new GameObject("Sound");
            child.transform.SetParent(transform);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            voices.Add(new Voice { source = source });
            index = voices.Count - 1;
        }
        if (index < 0)
        {
            float oldest = float.MaxValue;
            for (int i = 0; i < voices.Count; i++)
                if (!voices[i].source.loop && voices[i].started < oldest)
                { index = i; oldest = voices[i].started; }
        }
        if (index < 0) return default;
        Voice voice = voices[index];
        voice.source.Stop();
        voice.generation++;
        voice.follow = follow;
        voice.following = follow != null;
        voice.started = Time.unscaledTime;
        AudioSource audio = voice.source;
        audio.transform.position = position;
        audio.clip = clip;
        audio.volume = Mathf.Clamp01(cue.volume * table.masterVolume);
        audio.pitch = Mathf.Clamp(cue.pitch, .25f, 3f);
        audio.spatialBlend = local ? 0f : Mathf.Clamp01(cue.spatialBlend);
        audio.minDistance = Mathf.Max(.1f, cue.minDistance);
        audio.maxDistance = Mathf.Max(audio.minDistance + .1f, cue.maxDistance);
        audio.rolloffMode = AudioRolloffMode.Linear;
        audio.dopplerLevel = 0f;
        audio.outputAudioMixerGroup = GameAudioSettings.Group(cue.category);
        audio.loop = loop;
        audio.Play();
        if (nextAllowed.Count > 4096) nextAllowed.Clear();
        if (!loop) nextAllowed[key] = Time.unscaledTime + Mathf.Max(0f, cue.minimumInterval);
        return new Loop { host = this, slot = index, generation = voice.generation };
    }
    private void Stop(int index, int generation)
    {
        if (index >= 0 && index < voices.Count && voices[index].generation == generation)
            voices[index].source.Stop();
    }
    private void LateUpdate()
    {
        foreach (Voice voice in voices)
        {
            if (!voice.source.isPlaying || !voice.following) continue;
            if (voice.follow == null || !voice.follow.gameObject.activeInHierarchy) voice.source.Stop();
            else voice.source.transform.position = voice.follow.position;
        }
        UpdateMatchSounds();
    }
    private void UpdateMatchSounds()
    {
        BattleFlag current = BattleFlag.Instance;
        if (current == null || current.Object == null || !current.Object.IsValid) { flag = null; return; }
        if (flag != current)
        {
            flag = current; phase = flag.Phase; carrier = flag.Carrier; countdown = -1;
            if (phase == BattleStartPhase.Intro) Play(BattleSound.MatchIntro, local: true);
            return; // Never replay historical results when joining a running game.
        }
        if (phase != flag.Phase)
        {
            phase = flag.Phase;
            if (phase == BattleStartPhase.Intro) Play(BattleSound.MatchIntro, local: true);
            if (phase == BattleStartPhase.Playing) Play(BattleSound.RoundStart, local: true);
            if (Player.LocalPlayer != null && Player.LocalPlayer.Object != null && Player.LocalPlayer.Object.IsValid &&
                (phase == BattleStartPhase.RoundResult || phase == BattleStartPhase.Ended))
            {
                int winner = phase == BattleStartPhase.Ended ? flag.WinningTeam : flag.LastRoundWinner;
                if (winner > 0)
                {
                    bool won = winner == Player.LocalPlayer.TeamIndex;
                    Play(phase == BattleStartPhase.Ended ? (won ? BattleSound.MatchVictory : BattleSound.MatchDefeat)
                        : (won ? BattleSound.RoundWin : BattleSound.RoundLose), local: true);
                }
            }
        }
        int seconds = phase == BattleStartPhase.Countdown ? Mathf.CeilToInt(flag.PhaseRemainingSeconds) : -1;
        if (seconds != countdown && seconds > 0 && seconds <= 3) Play(BattleSound.Countdown, local: true);
        countdown = seconds;
        if (carrier != flag.Carrier)
        {
            if (phase == BattleStartPhase.Playing)
                Play(flag.Carrier == PlayerRef.None ? BattleSound.FlagDrop : BattleSound.FlagPickup, local: true);
            carrier = flag.Carrier;
        }
    }
    private void OnDestroy() { if (instance == this) instance = null; }
}
