using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Uses authored VoiceChat rows; never creates or rearranges scene UI.
public sealed class TeamVoiceRosterUI : MonoBehaviour
{
    private sealed class Row
    {
        public GameObject root;
        public TMP_Text text;
        public Text legacyText;
        public string lastName;
    }
    private readonly List<List<Row>> panels = new();
    private readonly List<PlayerData> players = new(10);
    private NetworkRunner runner;
    private TeamVoiceChat voice;
    private PlayerData publishedOwner;
    private bool publishedValue;
    private float nextPublishRetry, nextRefresh;
    private bool needsBind = true;

    private void Awake()
    {
        runner = GetComponent<NetworkRunner>();
        voice = GetComponent<TeamVoiceChat>();
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => needsBind = true;
    private void OnSceneUnloaded(Scene scene) => needsBind = true;

    private void Bind()
    {
        panels.Clear();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name != "VoiceChat") continue;
                    var rows = new List<Row>();
                    // Hierarchy order is the order authored by the scene designer.
                    foreach (Transform child in candidate)
                    {
                        if (!child.name.StartsWith("Voice01", System.StringComparison.Ordinal)) continue;
                        var label = child.Find("NickNameImage/Text") ?? child.Find("NickNameImage/Text (TMP)");
                        rows.Add(new Row { root = child.gameObject,
                            text = label != null ? label.GetComponent<TMP_Text>() : null,
                            legacyText = label != null ? label.GetComponent<Text>() : null });
                        child.gameObject.SetActive(false);
                    }
                    if (rows.Count > 0) panels.Add(rows);
                }
        }
        needsBind = false;
    }

    private void LateUpdate()
    {
        var local = PlayerData.Local;
        bool validLocal = local != null && local.Object != null && local.Object.IsValid &&
            local.Runner == runner && local.Object.HasInputAuthority;
        bool enabled = voice != null && voice.IsVoiceConnected && voice.IsTransmitting;
        if (validLocal && (publishedOwner != local || publishedValue != enabled ||
            (Time.unscaledTime >= nextPublishRetry && (bool)local.VoiceMicrophoneEnabled != enabled)))
        {
            local.RPC_SetVoiceMicrophoneEnabled(enabled);
            publishedOwner = local;
            publishedValue = enabled;
            nextPublishRetry = Time.unscaledTime + 1f;
        }
        if (needsBind) Bind();
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.1f;
        var manager = NetworkGameManager.Instance;
        bool lobby = manager != null && manager.IsWaitingInCodeRoom;
        bool battle = manager != null && manager.IsBattleSceneLoaded && !manager.IsDeathMatch;
        players.Clear();
        if (runner != null && runner.IsRunning && (lobby || (battle && validLocal)))
            foreach (var player in PlayerData.PerkPlayers)
            {
                if (player == null || player.Object == null || !player.Object.IsValid ||
                    player.Runner != runner || player.IsMatchBot) continue;
                if (!lobby && (local.teamIndex <= 0 || player.teamIndex != local.teamIndex)) continue;
                players.Add(player);
            }
        players.Sort((a, b) => a.Object.InputAuthority.RawEncoded.CompareTo(b.Object.InputAuthority.RawEncoded));
        foreach (var rows in panels)
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.root == null) continue;
                var player = i < players.Count ? players[i] : null;
                string nickname = player != null ? player.playerName.ToString() : string.Empty;
                if (row.lastName != nickname)
                {
                    if (row.text != null) { row.text.richText = false; row.text.text = nickname; }
                    if (row.legacyText != null) { row.legacyText.supportRichText = false; row.legacyText.text = nickname; }
                    row.lastName = nickname;
                }
                bool show = player != null && (player == local ? enabled : (bool)player.VoiceMicrophoneEnabled);
                if (row.root.activeSelf != show) row.root.SetActive(show);
            }
    }
    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        foreach (var rows in panels)
            foreach (var row in rows)
                if (row.root != null) row.root.SetActive(false);
    }
}
