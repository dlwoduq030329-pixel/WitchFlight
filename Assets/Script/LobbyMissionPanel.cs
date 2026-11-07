using System;
using System.Collections;
using TMPro;
using UnityEngine;

public sealed class LobbyMissionPanel : MonoBehaviour
{
    [SerializeField] private GameObject weekMission, dayMission;
    [SerializeField] private TMP_Text weekName, weekText, dayName, dayText;
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
        MissionDefinition weekly = null, daily = null;
        if (MissionProgression.Ready)
        {
            foreach (var definition in MissionProgression.Active())
            {
                var state = MissionProgression.Entry(MissionProgression.State, definition.missionId);
                // Completed objectives leave the summary even before the reward is claimed.
                if (state.claimed || state.progress >= definition.target) continue;
                if (definition.period == MissionPeriod.Weekly) weekly = definition;
                else if (daily == null) daily = definition; // First unfinished daily in catalog order.
            }
        }
        Present(weekMission, weekName, weekText, weekly);
        Present(dayMission, dayName, dayText, daily);
    }
    private static void Present(GameObject root, TMP_Text name, TMP_Text text, MissionDefinition definition)
    {
        if (root == null) return;
        bool visible = definition != null;
        if (root.activeSelf != visible) root.SetActive(visible);
        if (!visible) return;
        if (name != null) name.text = definition.displayName;
        if (text == null) return;
        var state = MissionProgression.Entry(MissionProgression.State, definition.missionId);
        string summary = string.IsNullOrWhiteSpace(definition.shortDescription) ? definition.description : definition.shortDescription;
        summary = (summary ?? string.Empty).Replace("{target}", definition.target.ToString())
            .Replace("{requiredHitsPerMagic}", definition.requiredHitsPerMagic.ToString());
        text.text = $"{summary} ({Mathf.Clamp(Mathf.FloorToInt(state.progress), 0, definition.target)}/{definition.target})";
    }
}
