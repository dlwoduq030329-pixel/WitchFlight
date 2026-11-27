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
    private sealed class Row
    {
        public GameObject root;
        public TMP_Text name, explanation, reward;
        public Image icon;
        public Sprite defaultIcon;
        public Slider slider;
        public Button button;
        public string id;
    }
    private readonly List<Row> dayRows = new(), weekRows = new();
    private Coroutine resetTimer;

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
        Present(dayContent, dayTemplate, dayRows, daily);
        Present(weekContent, weekTemplate, weekRows, weekly);
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
            row.slider = root.GetComponentInChildren<Slider>(true);
            if (row.slider != null)
            {
                row.slider.minValue = 0; row.slider.maxValue = 1;
                row.slider.wholeNumbers = false; row.slider.interactable = false;
            }
            // Keep the authored click actions: open the existing mission/reward window.
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
            int progress = Mathf.Clamp(Mathf.FloorToInt(state.progress), 0, mission.target);
            if (row.name != null) row.name.text = $"[{(mission.period == MissionPeriod.Weekly ? "주간" : "일간")}] {mission.displayName}";
            if (row.explanation != null) row.explanation.text =
                (mission.description ?? "").Replace("{target}", mission.target.ToString())
                .Replace("{requiredHitsPerMagic}", mission.requiredHitsPerMagic.ToString()) + $" ({progress} / {mission.target})";
            if (row.reward != null) row.reward.text = mission.rewardCurrency.ToString();
            if (row.icon != null) row.icon.sprite = mission.sprite != null ? mission.sprite : row.defaultIcon;
            if (row.slider != null) row.slider.SetValueWithoutNotify(mission.target > 0 ? Mathf.Clamp01(state.progress / mission.target) : 0);
            if (row.button != null) row.button.interactable = true;
        }
    }
}
