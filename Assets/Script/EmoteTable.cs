using System;
using UnityEngine;

public enum EmoteDirection { Up = 0, Right = 1, Down = 2, Left = 3 }
public enum DefaultEmoteFace { Hello, Happy, Sad, Angry }

[Serializable]
public sealed class EmoteEntry
{
    [Min(1), Tooltip("네트워크에서 공유하는 고유 ID. 기존 ID를 재사용하거나 변경하지 마세요.")]
    public int id;
    public string displayName;
    public Sprite icon;
    [Tooltip("개별 프리팹이 필요한 경우에만 지정합니다. 비우면 테이블의 공용 Display Prefab을 사용합니다.")]
    public GameObject displayPrefab;
    [Tooltip("Animator 상태 이름(예: Happy 또는 Base Layer.Happy), 또는 Animation 컴포넌트에 등록된 클립 이름. 비우면 기본 애니메이션입니다.")]
    public string animationName;
    [Min(0f), Tooltip("0이면 선택한 애니메이션 길이로 자동 종료합니다. 여러 상태/지연/루프를 조합한 프리팹은 전체 재생 시간을 직접 지정하세요.")]
    public float prefabDisplaySeconds;
    [Tooltip("Icon이 비어 있을 때 사용하는 기본 얼굴입니다.")]
    public DefaultEmoteFace fallbackFace;
}

[CreateAssetMenu(fileName = "EmoteTable", menuName = "WitchFlight/UI/Emote Table")]
public sealed class EmoteTable : ScriptableObject
{
    [Tooltip("모든 감정표현이 함께 사용하는 UI 프리팹(RectTransform). 각 항목의 Animation Name으로 재생 상태를 선택합니다.")]
    [SerializeField] private GameObject defaultDisplayPrefab;
    public GameObject DefaultDisplayPrefab => defaultDisplayPrefab;
    [SerializeField] private EmoteEntry[] entries =
    {
        new EmoteEntry { id = 1, displayName = "안녕!", fallbackFace = DefaultEmoteFace.Hello },
        new EmoteEntry { id = 2, displayName = "신나요!", fallbackFace = DefaultEmoteFace.Happy },
        new EmoteEntry { id = 3, displayName = "아쉬워요", fallbackFace = DefaultEmoteFace.Sad },
        new EmoteEntry { id = 4, displayName = "으으!", fallbackFace = DefaultEmoteFace.Angry }
    };
    [Tooltip("연속 사용 최소 간격(초). 호스트에서도 검증하며 게임 플레이에는 영향을 주지 않습니다.")]
    [SerializeField, Min(.1f)] private float minimumInterval = 1f;
    private static EmoteTable defaultTable;
    public static EmoteTable Default => defaultTable != null ? defaultTable :
        (defaultTable = Resources.Load<EmoteTable>("EmoteTable"));
    public float MinimumInterval => Mathf.Max(.1f, minimumInterval);

    public bool TryGet(int id, out EmoteEntry entry)
    {
        if (id > 0 && entries != null)
            foreach (var candidate in entries)
                if (candidate != null && candidate.id == id) { entry = candidate; return true; }
        entry = null;
        return false;
    }

    private void OnValidate()
    {
        minimumInterval = Mathf.Max(.1f, minimumInterval);
        if (entries == null) return;
        var ids = new System.Collections.Generic.HashSet<int>();
        foreach (var entry in entries)
            if (entry != null && (entry.id <= 0 || !ids.Add(entry.id)))
                Debug.LogWarning("EmoteTable: 감정표현 ID는 1 이상의 중복되지 않는 값이어야 합니다.", this);
    }
}
