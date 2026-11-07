using System;
using System.Collections.Generic;
using UnityEngine;

public enum MissionPeriod { Daily = 0, Weekly = 1 }
public enum MissionObjective
{
    MatchComplete = 0, DamageDealt = 1, KillOrAssist = 2, ItemCollected = 3,
    MatchWin = 4, CombatMinutes = 5, ParrySuccess = 6,
    DistinctMagicHits = 7, DistinctPlayDays = 8, MatchesWithKillOrAssist = 9
}

[Serializable]
public sealed class MissionDefinition
{
    [Tooltip("저장용 고유 ID. 이름과 설명은 바꿔도 되지만, 운영 후 이 ID는 변경하거나 재사용하지 마세요.")]
    public string missionId;
    public MissionPeriod period;
    [Tooltip("MissionUI 버튼 하위 Name에 표시할 미션 이름입니다.")]
    public string displayName;
    [Tooltip("미션 버튼 하위 MissionIMG에 표시할 Sprite입니다. 비워두면 MissionIMG의 기존 이미지를 사용합니다.")]
    public Sprite sprite;
    [TextArea(2, 4), Tooltip("{target}은 목표 수치, {requiredHitsPerMagic}은 마법 종류별 요구 적중 횟수로 표시할 예정입니다. 진척률은 UI에서 별도로 붙입니다.")]
    public string description;
    [Tooltip("로비 MissionPanel에 표시할 짧은 설명입니다. 비우면 기본 설명을 사용하며 진척률은 자동으로 붙습니다.")]
    public string shortDescription;
    public MissionObjective objective;
    [Min(1), Tooltip("목표 횟수 또는 수치. CombatMinutes는 분, DistinctMagicHits는 서로 다른 마법 종류 수입니다.")]
    public int target = 1;
    [Min(0), Tooltip("완료 후 수령할 재화입니다.")]
    public int rewardCurrency = 100;
    [Min(0), Tooltip("일일 표시 순서 / 주간 순환 순서. 작은 값부터 사용합니다.")]
    public int sortOrder;
    public bool enabled = true;
    [Min(1), Tooltip("DistinctMagicHits 미션에서만 사용: 각 마법으로 요구하는 적중 횟수입니다.")]
    public int requiredHitsPerMagic = 10;
}

// Definitions only. No scene hooks, progress tracking, backend calls or rewards.
[CreateAssetMenu(fileName = "MissionCatalog", menuName = "WitchFlight/Missions/Mission Catalog")]
public sealed class MissionCatalog : ScriptableObject
{
    [SerializeField] private List<MissionDefinition> missions = new();
    public IReadOnlyList<MissionDefinition> Missions => missions;

    private void OnValidate()
    {
        if (missions == null) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mission in missions)
        {
            if (mission == null || string.IsNullOrWhiteSpace(mission.missionId) ||
                mission.missionId != mission.missionId.Trim() || !ids.Add(mission.missionId))
                Debug.LogWarning("MissionCatalog: 비어 있지 않고 중복 없는 고정 ID를 지정해주세요.", this);
        }
    }
}
