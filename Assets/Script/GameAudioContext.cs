using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Runtime bindings keep existing UI prefabs and their designer events intact.
// Clips and volumes remain editable in Resources/BattleAudioTable.
public sealed class GameAudioContext : MonoBehaviour
{
    private sealed class ButtonBinding
    {
        public Button button;
        public UnityAction callback;
        public Button.ButtonClickedEvent events;
    }
    private sealed class Panel
    {
        public Transform transform;
        public bool wasVisible;
        public BattleSound? openSound;
    }
    private readonly List<ButtonBinding> buttons = new();
    private readonly HashSet<Button> boundButtons = new();
    private readonly List<Panel> panels = new();
    private readonly HashSet<Transform> knownPanels = new();
    private BattleAudioTable table;
    private AudioSource music;
    private GameMusic context;
    private GameMusicEntry requested;
    private LoginFlowController login;
    private bool lobbyReady;
    private bool scanned;
    private float nextScan;

    private void Awake()
    {
        table = Resources.Load<BattleAudioTable>("BattleAudioTable");
        music = gameObject.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.spatialBlend = 0f;
        music.volume = 0f;
        music.outputAudioMixerGroup = GameAudioSettings.Group(GameAudioCategory.BGM);
        SceneManager.sceneLoaded += OnSceneLoaded;
        Scan();
    }
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Scan();

    private void Scan()
    {
        nextScan = Time.unscaledTime + .5f;
        if (login == null) login = FindFirstObjectByType<LoginFlowController>();
        for (int i = buttons.Count - 1; i >= 0; i--)
            if (buttons[i].button == null)
            {
                boundButtons.Remove(buttons[i].button);
                buttons.RemoveAt(i);
            }
        foreach (ButtonBinding binding in buttons)
            if (binding.button.onClick != binding.events)
            {
                binding.events.RemoveListener(binding.callback);
                binding.events = binding.button.onClick;
                binding.events.AddListener(binding.callback);
            }
        foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!boundButtons.Add(button)) continue;
            bool explicitAudio = false;
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                if (button.onClick.GetPersistentTarget(i) is BattleAudioEvent) explicitAudio = true;
            if (explicitAudio) continue;
            BattleSound sound = ButtonSound(button);
            UnityAction callback = () => BattleAudio.Play(sound, local: true);
            button.onClick.AddListener(callback);
            buttons.Add(new ButtonBinding { button = button, callback = callback, events = button.onClick });
        }
        for (int i = panels.Count - 1; i >= 0; i--)
            if (panels[i].transform == null)
            {
                knownPanels.Remove(panels[i].transform);
                panels.RemoveAt(i);
            }
        foreach (RectTransform t in FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string name = CleanName(t);
            bool popup = IsPopup(name);
            bool attendance = name == "SuccessPopup" && HasAncestor(t, "ChoolsukUI");
            if (!popup && !attendance && name != "StoreUI" && name != "CustumUI" && name != "IntroUI" && name != "RobbyUI") continue;
            if (!knownPanels.Add(t)) continue;
            // A nested popup container must not replay its parent's opening sound.
            bool nestedPopup = false;
            for (Transform p = t.parent; p != null; p = p.parent)
                if (IsPopup(CleanName(p))) { nestedPopup = true; break; }
            panels.Add(new Panel { transform = t, wasVisible = !scanned && t.gameObject.activeInHierarchy,
                openSound = attendance ? BattleSound.AttendanceSuccess : popup && !nestedPopup ? BattleSound.UIPopup : null });
        }
        scanned = true;
    }

    private static string CleanName(Transform t) => t.name.Replace("(Clone)", "").Trim();
    private static bool IsPopup(string name) => name.StartsWith("PopupUI_", StringComparison.Ordinal) || name == "Popup";
    private static bool HasAncestor(Transform t, string name)
    {
        for (Transform p = t; p != null; p = p.parent)
            if (CleanName(p) == name) return true;
        return false;
    }
    private static BattleSound ButtonSound(Button button)
    {
        string name = button.name.ToLowerInvariant();
        if (name == "xbutton" || name.Contains("back") || name.Contains("cancel") || name.Contains("exit") || name.Contains("reset"))
            return BattleSound.UICancel;
        if (name.Contains("apply") || name.Contains("confirm") || name.Contains("positive")) return BattleSound.UIConfirm;
        if (name.Contains("tab") || HasAncestor(button.transform, "TabUI")) return BattleSound.UITab;
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            string method = button.onClick.GetPersistentMethodName(i);
            if (method == "SelectMagic" || method == "ShowMagic") return BattleSound.MagicSelected;
            if (method == "SelectSlot" || method.EndsWith("ColorPreset", StringComparison.Ordinal)) return BattleSound.UITab;
            if (method == "TryLogin" || method == "TryRegister" || method == "StartRandomMatch" ||
                method == "StartTraining" || method == "CreateRoom" || method == "JoinRoom" || method == "StartRoomGame" || method == "StartMatch")
                return BattleSound.UIImportant;
            if (method == "SetActive" && button.onClick.GetPersistentTarget(i) is GameObject target &&
                (target.name == "StoreUI" || target.name == "CustumUI" || target.name == "MenuUI")) return BattleSound.UITab;
        }
        if (name.Contains("login") || name.Contains("start") || name.Contains("matching")) return BattleSound.UIImportant;
        return BattleSound.UIClick;
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextScan) Scan();
        foreach (Panel panel in panels)
        {
            if (panel.transform == null) continue;
            bool visible = panel.transform.gameObject.activeInHierarchy;
            if (visible && !panel.wasVisible && panel.openSound.HasValue)
                BattleAudio.Play(panel.openSound.Value, local: true);
            panel.wasVisible = visible;
        }
        bool ready = login != null && login.IsLobbyReady;
        if (ready && !lobbyReady) BattleAudio.Play(BattleSound.GreetingVoice, local: true);
        lobbyReady = ready;

        GameMusic next = ResolveMusic();
        if (context != next)
        {
            context = next;
            requested = table != null ? table.GetMusic(next) : null;
        }
        AudioClip nextClip = requested != null ? requested.clip : null;
        if (music.clip != nextClip)
        {
            music.volume = Mathf.MoveTowards(music.volume, 0f, Time.unscaledDeltaTime * .8f);
            if (music.volume > 0f) return;
            music.Stop();
            music.clip = nextClip;
            if (nextClip != null)
            {
                music.loop = requested.loop;
                music.Play();
            }
        }
        else
        {
            float gain = requested != null && table != null ? requested.volume * table.masterVolume : 0f;
            music.volume = Mathf.MoveTowards(music.volume, gain, Time.unscaledDeltaTime * .8f);
        }
    }

    private bool Visible(string name)
    {
        foreach (Panel panel in panels)
            if (panel.transform != null && panel.transform.gameObject.activeInHierarchy && CleanName(panel.transform) == name) return true;
        return false;
    }
    private GameMusic ResolveMusic()
    {
        BattleFlag flag = BattleFlag.Instance;
        if (flag != null && flag.Object != null && flag.Object.IsValid)
        {
            switch (flag.Phase)
            {
                case BattleStartPhase.Intermission: return GameMusic.MagicSelection;
                case BattleStartPhase.WaitingForPlayers: return GameMusic.WaitingRoom;
                case BattleStartPhase.Intro:
                case BattleStartPhase.Countdown: return GameMusic.BattleStart;
                case BattleStartPhase.Playing:
                    // Let the start sting finish once; no battle loop is supplied in the sheet.
                    return context == GameMusic.BattleStart ? GameMusic.BattleStart : GameMusic.None;
                default: return GameMusic.None;
            }
        }
        NetworkGameManager manager = NetworkGameManager.Instance;
        if (manager != null && manager.IsMatching)
            return manager.IsWaitingInCodeRoom ? GameMusic.WaitingRoom : GameMusic.Matching;
        if (Visible("StoreUI")) return GameMusic.Store;
        if (Visible("CustumUI")) return GameMusic.Customization;
        if (login != null) return login.IsLobbyReady ? GameMusic.Lobby : GameMusic.Intro;
        if (Visible("RobbyUI")) return GameMusic.Lobby;
        if (Visible("IntroUI")) return GameMusic.Intro;
        return GameMusic.None;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        foreach (ButtonBinding binding in buttons)
            binding.events.RemoveListener(binding.callback);
    }
}
