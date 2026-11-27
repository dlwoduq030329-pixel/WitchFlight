using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;
using UnityEngine;

public sealed class LobbyMissionPanel : MonoBehaviour
{
    [SerializeField] private Transform dayContent, weekContent;
    [SerializeField] private GameObject dayTemplate, weekTemplate;
    [SerializeField] private Sprite activeMissionBackground, completedMissionBackground;
    [SerializeField, Range(0f, 1f)] private float completedDarkness = 0.4f;
    private sealed class Row
    {
        public GameObject root;
        public TMP_Text name, explanation, reward;
        public Image icon, background, dimmer;
        public Sprite defaultIcon;
        public Slider slider;
        public Button button;
        public string id;
    }
    private readonly List<Row> dayRows = new(), weekRows = new();
    private Coroutine resetTimer;
    private bool claimingReward;

    private void OnEnable()
    {
        MissionProgression.Changed += Refresh;
        DatabaseManager.DataConfigReadyChanged += OnReadyChanged;
        OnReadyChanged();
        resetTimer = StartCoroutine(WaitForReset());
    }
    private void OnDisable()
    {
        MissionProgression.Changed -= Refresh;
        DatabaseManager.DataConfigReadyChanged -= OnReadyChanged;
        if (resetTimer != null) StopCoroutine(resetTimer);
        resetTimer = null;
    }
    private void OnReadyChanged()
    {
        if (!MissionProgression.Ready) MissionProgression.Load();
        MissionProgression.EnsurePeriods();
        Refresh();
    }
    private IEnumerator WaitForReset()
    {
        while (true)
        {
            DateTime now = DateTime.UtcNow;
            DateTime next = MissionProgression.PeriodDay(now).AddDays(1).AddHours(-3);
            yield return new WaitForSecondsRealtime(Mathf.Max(1f, (float)(next - now).TotalSeconds));
            MissionProgression.EnsurePeriods();
            Refresh();
        }
    }
    private void OnApplicationFocus(bool focused)
    {
        if (!focused || !isActiveAndEnabled) return;
        MissionProgression.EnsurePeriods();
        Refresh();
    }
    private void Refresh()
    {
        var daily = new List<MissionDefinition>();
        var weekly = new List<MissionDefinition>();
        if (MissionProgression.Ready)
            foreach (var mission in MissionProgression.Active())
                (mission.period == MissionPeriod.Weekly ? weekly : daily).Add(mission);
        // Completed and claimed missions remain visible until their period resets.
        MoveClaimedToEnd(daily);
        MoveClaimedToEnd(weekly);
        Present(dayContent, dayTemplate, dayRows, daily);
        Present(weekContent, weekTemplate, weekRows, weekly);
    }
    private static bool IsClaimed(MissionDefinition mission)
    {
        var state = MissionProgression.Entry(MissionProgression.State, mission.missionId);
        return state.claimed;
    }
    private static void MoveClaimedToEnd(List<MissionDefinition> missions)
    {
        // Stable partition keeps the assigned order within both groups.
        var claimed = missions.FindAll(IsClaimed);
        missions.RemoveAll(IsClaimed);
        missions.AddRange(claimed);
    }
    private void Present(Transform content, GameObject template, List<Row> rows, List<MissionDefinition> missions)
    {
        if (content == null || template == null) return;
        while (rows.Count < missions.Count)
        {
            var root = rows.Count == 0 ? template : Instantiate(template, content, false);
            if (rows.Count > 0) root.name = template.name + " (" + rows.Count + ")";
            var row = new Row { root = root, button = root.GetComponent<Button>() };
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                switch (text.name.Replace(" ", ""))
                {
                    case "MissionName": row.name = text; break;
                    case "MissionExplanation": row.explanation = text; break;
                    case "MissionExplanation(1)": row.reward = text; break;
                }
            }
            foreach (var image in root.GetComponentsInChildren<Image>(true))
                if (image.name == "MissionIcon") { row.icon = image; row.defaultIcon = image.sprite; break; }
            row.background = root.GetComponent<Image>();
            var dimmerTransform = root.transform.Find("CompletedDimmer");
            if (dimmerTransform == null)
            {
                var overlay = new GameObject("CompletedDimmer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                overlay.transform.SetParent(root.transform, false);
                var rect = (RectTransform)overlay.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                dimmerTransform = rect;
            }
            row.dimmer = dimmerTransform.GetComponent<Image>();
            row.dimmer.raycastTarget = false;
            dimmerTransform.SetAsLastSibling();
            row.slider = root.GetComponentInChildren<Slider>(true);
            if (row.slider != null)
            {
                row.slider.minValue = 0; row.slider.maxValue = 1;
                row.slider.wholeNumbers = false; row.slider.interactable = false;
            }
            // Replace persistent navigation actions too; RemoveAllListeners alone would retain them.
            if (row.button != null)
            {
                row.button.onClick = new Button.ButtonClickedEvent();
                row.button.onClick.AddListener(() => ClaimReward(row));
            }
            // The progress slider is display-only and must not intercept the row button click.
            if (row.slider != null)
                foreach (var graphic in row.slider.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            rows.Add(row);
        }
        if (rows.Count == 0) template.SetActive(false);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            row.root.SetActive(i < missions.Count);
            if (i >= missions.Count) continue;
            var mission = missions[i]; row.id = mission.missionId;
            var state = MissionProgression.Entry(MissionProgression.State, mission.missionId);
            bool completed = state.claimed || state.progress >= mission.target;
            var background = completed ? completedMissionBackground : activeMissionBackground;
            if (row.background != null && background != null) row.background.sprite = background;
            row.dimmer.color = new Color(0f, 0f, 0f, completed ? completedDarkness : 0f);
            row.root.transform.SetSiblingIndex(i);
            int progress = Mathf.Clamp(Mathf.FloorToInt(state.progress), 0, mission.target);
            if (row.name != null) row.name.text = $"[{(mission.period == MissionPeriod.Weekly ? "주간" : "일간")}] {mission.displayName}";
            if (row.explanation != null) row.explanation.text =
                (mission.description ?? "").Replace("{target}", mission.target.ToString())
                .Replace("{requiredHitsPerMagic}", mission.requiredHitsPerMagic.ToString()) + $" ({progress} / {mission.target})";
            if (row.reward != null) row.reward.text = mission.rewardCurrency.ToString();
            if (row.icon != null) row.icon.sprite = mission.sprite != null ? mission.sprite : row.defaultIcon;
            if (row.slider != null) row.slider.SetValueWithoutNotify(mission.target > 0 ? Mathf.Clamp01(state.progress / mission.target) : 0);
            if (row.button != null) row.button.interactable = MissionProgression.Ready && !claimingReward && completed && !state.claimed;
        }
    }
    private void ClaimReward(Row row)
    {
        if (claimingReward || !MissionProgression.Ready || string.IsNullOrEmpty(row.id)) return;
        // A reset can refresh/reassign this row, so capture the clicked mission first.
        string id = row.id;
        MissionProgression.EnsurePeriods();
        var definition = MissionProgression.Find(id);
        if (definition == null || !MissionProgression.Active().Exists(d => d.missionId == id)) return;
        var entry = MissionProgression.Entry(MissionProgression.State, id);
        if (entry.claimed || entry.progress < definition.target) return;
        var db = DatabaseManager.Instance;
        if (db == null) return;
        claimingReward = true;
        string failure = null;
        try
        {
            Refresh();
            if (!db.TryClaimMission(id, out string message)) failure = message;
        }
        finally
        {
            claimingReward = false;
            Refresh();
        }
        if (!string.IsNullOrEmpty(failure))
        {
            Debug.Log(failure, this);
            // Keep the reward field numeric; show retry/save errors on the clicked mission.
            ShowClaimError(dayRows, id, failure);
            ShowClaimError(weekRows, id, failure);
        }
    }

    private static void ShowClaimError(List<Row> rows, string id, string message)
    {
        foreach (var row in rows)
            if (row.id == id && row.explanation != null) row.explanation.text += "\n" + message;
    }

}
