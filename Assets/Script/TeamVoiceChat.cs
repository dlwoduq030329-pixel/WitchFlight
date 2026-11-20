using Fusion;
using Photon.Voice.Unity.UtilityScripts;
using System.Collections.Generic;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.UI;

// Session-owned audio: independent of character spawning, death and respawn.
public sealed class TeamVoiceChat : FusionVoiceClient
{
    public enum MicrophoneMode { Hold = 0, Toggle = 1 }
    private const string ModeKey = "WitchFlight.TeamVoice.Mode";
    private static readonly byte[] RemoveGroups = { 1, 2, 3 };
    private static readonly byte[][] ReceiveGroups = { new byte[0], new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 } };
    public static TeamVoiceChat Instance { get; private set; }
    public static int SavedMode => Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, 0), 0, 1);
    public bool IsTransmitting => microphone != null && microphone.TransmitEnabled;
    public bool IsVoiceConnected => Client != null && Client.InRoom;
    public static event System.Action SettingsChanged;
    public static float OutputVolume => Mathf.Clamp(PlayerPrefs.GetFloat("VoiceChat.Output", 50f), 0f, 100f);
    public static float InputVolume => Mathf.Clamp(PlayerPrefs.GetFloat("VoiceChat.Input", 50f), 0f, 100f);
    public static void SetOutputVolume(float value) => SetVolume("VoiceChat.Output", value);
    public static void SetInputVolume(float value) => SetVolume("VoiceChat.Input", value);
    private static void SetVolume(string key, float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return;
        PlayerPrefs.SetFloat(key, Mathf.Clamp(value, 0f, 100f));
        if (Instance != null) Instance.ApplyVolumes();
        SettingsChanged?.Invoke();
    }
    private readonly List<AudioSource> outputs = new List<AudioSource>();
    private MicAmplifier amplifier;
    private void ApplyVolumes()
    {
        if (amplifier != null) amplifier.AmplificationFactor = InputVolume / 50f;
        foreach (var output in outputs) if (output != null) output.volume = OutputVolume / 100f;
    }
    private Recorder microphone;
    private NetworkRunner runner;
    private int subscribedGroup = -1;
    private bool toggled;
    private bool focused = true;
    private bool requireRelease;
    private int mode;

    public static void Attach(NetworkRunner runner)
    {
        if (runner.GetComponent<TeamVoiceChat>() != null) return;
        var voice = runner.gameObject.AddComponent<TeamVoiceChat>();
        runner.AddCallbacks(voice);
    }

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
        runner = GetComponent<NetworkRunner>();
        mode = SavedMode;
        UseFusionAppSettings = true;
        microphone = gameObject.AddComponent<Recorder>();
        microphone.TransmitEnabled = false;
        microphone.InterestGroup = 3; // Never broadcast group 0 (cannot unsubscribe).
        microphone.VoiceDetection = false;
        microphone.StopRecordingWhenPaused = true;
        amplifier = gameObject.AddComponent<MicAmplifier>();
        ApplyVolumes();
        PrimaryRecorder = microphone;
        AddRecorder(microphone);
    }

    // Dropdown value: 0 = hold F, 1 = press F to toggle. Safe to call before joining.
    public static void SaveVoiceMode(int value)
    {
        value = Mathf.Clamp(value, 0, 1);
        PlayerPrefs.SetInt(ModeKey, value);
        PlayerPrefs.Save();
        if (Instance != null) Instance.SetLocalMode(value);
        SettingsChanged?.Invoke();
    }
    public void SetVoiceMode(int value) => SaveVoiceMode(value);
    private void SetLocalMode(int value)
    {
        mode = value;
        StopTalking();
        requireRelease = true;
    }
    private void StopTalking()
    {
        toggled = false;
        if (microphone != null) microphone.TransmitEnabled = false;
    }
    private int DesiredGroup()
    {
        var manager = NetworkGameManager.Instance;
        if (manager == null || manager.IsTraining || runner == null || !runner.IsRunning) return 0;
        if (manager.IsWaitingInCodeRoom) return 3;
        if (!manager.IsBattleSceneLoaded) return 0;
        // Free-for-all has no allied channel.
        if (manager.IsDeathMatch) return 0;
        var data = PlayerData.Local;
        if (data == null || data.Object == null || !data.Object.IsValid || data.Runner != runner) return 0;
        return data.teamIndex == 1 || data.teamIndex == 2 ? data.teamIndex : 0;
    }
    private static bool IsTyping()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null && (selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<InputField>() != null);
    }
    protected override void Update()
    {
        base.Update();
        if (microphone == null) return;
        if (!IsVoiceConnected)
        {
            subscribedGroup = -1;
            StopTalking();
            requireRelease = true;
            return;
        }
        int desired = DesiredGroup();
        if (desired != subscribedGroup)
        {
            StopTalking();
            requireRelease = true;
            if (!Client.LoadBalancingPeer.OpChangeGroups(RemoveGroups, ReceiveGroups[desired])) return;
            microphone.InterestGroup = (byte)(desired == 0 ? 3 : desired);
            subscribedGroup = desired;
        }
        if (!focused || desired == 0 || IsTyping() || CombatPresentation.MenuOpen)
        {
            StopTalking();
            requireRelease = true;
            return;
        }
        if (requireRelease)
        {
            if (!Input.GetKey(KeyCode.F)) requireRelease = false;
            return;
        }
        if (mode == 1 && Input.GetKeyDown(KeyCode.F)) toggled = !toggled;
        microphone.TransmitEnabled = mode == 0 ? Input.GetKey(KeyCode.F) : toggled;
    }
    protected override Speaker InstantiateSpeakerForRemoteVoice(int playerId, byte voiceId, object userData)
    {
        var output = new GameObject("Team voice speaker " + playerId);
        output.transform.SetParent(transform, false);
        var audio = output.AddComponent<AudioSource>();
        audio.spatialBlend = 0f;
        audio.playOnAwake = false;
        outputs.Add(audio);
        ApplyVolumes();
        var speaker = output.AddComponent<Speaker>();
        speaker.OnRemoteVoiceRemoveAction += removed =>
        {
            outputs.Remove(audio);
            if (removed != null) Destroy(removed.gameObject);
        };
        return speaker;
    }
    private void OnApplicationFocus(bool value)
    {
        focused = value;
        if (!value) { StopTalking(); requireRelease = true; }
    }
    protected override void OnDestroy()
    {
        StopTalking();
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }
}
