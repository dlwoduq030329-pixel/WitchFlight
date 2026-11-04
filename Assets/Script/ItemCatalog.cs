using System;
using System.Collections.Generic;
using UnityEngine;

public enum ItemCategory { Hair = 0, Animation = 1, Emote = 2, Voice = 3 }

[Serializable]
public sealed class ItemVoiceCue
{
    public string eventId;
    public AudioClip clip;
}

[Serializable]
public sealed class ItemDefinition
{
    [Tooltip("저장에 사용하는 고유 ID. 예: hair_001. 출시 후 변경하거나 다른 상품에 재사용하지 마세요.")]
    public string id;
    public ItemCategory category;
    public string displayName;
    [TextArea] public string description;
    public Sprite icon;
    [Min(0)] public int price;
    [Header("헤어")]
    public GameObject hairPrefab;
    [Header("애니메이션")]
    public AnimationClip animationClip;
    public string animatorStateName;
    [Header("이모티콘: 기존 EmoteTable의 ID")]
    [Min(0)] public int emoteId;
    [Header("목소리: 이벤트 이름별 음성")]
    public ItemVoiceCue[] voiceCues = Array.Empty<ItemVoiceCue>();
    [Tooltip("Basic_Princess와 동일한 파일명으로 대응하는 캐릭터 음성 묶음입니다.")]
    public CharacterVoicePack voicePack;
}

[CreateAssetMenu(fileName = "ItemCatalog", menuName = "WitchFlight/Items/Item Catalog")]
public sealed class ItemCatalog : ScriptableObject
{
    [SerializeField] private List<ItemDefinition> items = new();
    private static ItemCatalog defaultCatalog;
    public static ItemCatalog Default => defaultCatalog != null ? defaultCatalog :
        (defaultCatalog = Resources.Load<ItemCatalog>("ItemCatalog"));
    public IReadOnlyList<ItemDefinition> Items => items;

    public bool TryGet(string id, out ItemDefinition result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(id) || items == null) return false;
        foreach (var item in items)
        {
            if (item == null || item.id != id) continue;
            if (result != null) { result = null; return false; } // Ambiguous IDs must never grant the wrong item.
            result = item;
        }
        return result != null && Enum.IsDefined(typeof(ItemCategory), result.category) && result.price >= 0;
    }

    private void OnValidate()
    {
        if (items == null) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
            if (item != null && (string.IsNullOrWhiteSpace(item.id) || item.id != item.id.Trim() || !ids.Add(item.id)))
                Debug.LogWarning("ItemCatalog: 비어 있지 않고 중복 없는 고정 ID를 지정해주세요.", this);
    }
}
