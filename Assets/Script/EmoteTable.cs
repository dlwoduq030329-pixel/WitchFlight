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
    [Tooltip("Icon이 비어 있을 때 사용하는 기본 얼굴입니다.")]
    public DefaultEmoteFace fallbackFace;
}

[CreateAssetMenu(fileName = "EmoteTable", menuName = "WitchFlight/UI/Emote Table")]
public sealed class EmoteTable : ScriptableObject
{
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
