using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class EquippedAccountItem
{
    public ItemCategory category;
    public int slot;
    public string itemId;
}

[Serializable]
public sealed class AccountItemInventory
{
    public int version = 1;
    public List<string> ownedItemIds = new();
    public List<EquippedAccountItem> equippedItems = new();
}

// Ownership and equipment selections only. Purchases and gameplay appearance consumers
// can use these APIs later without changing the account storage format.
public static class ItemInventory
{
    public const string EmptyJson = "{\"version\":1,\"ownedItemIds\":[],\"equippedItems\":[]}";
    public const string BasicVoiceItemId = "3.1";
    public const string InitialJson = "{\"version\":1,\"ownedItemIds\":[\"3.1\"],\"equippedItems\":[{\"category\":3,\"slot\":0,\"itemId\":\"3.1\"}]}";
    private static string cachedJson;
    private static AccountItemInventory cached;
    private static bool Ready => DatabaseManager.Instance != null && DatabaseManager.Instance.CanModifyAccountData;

    public static AccountItemInventory Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}") return new AccountItemInventory();
        var data = JsonUtility.FromJson<AccountItemInventory>(json);
        if (data == null || data.version != 1 || data.ownedItemIds == null || data.equippedItems == null)
            throw new InvalidOperationException("itemInventoryJson 형식/버전을 확인해주세요.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in data.ownedItemIds)
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                throw new InvalidOperationException("보유 아이템 ID가 비어 있거나 중복되었습니다.");
        var slots = new HashSet<string>();
        foreach (var entry in data.equippedItems)
            if (entry == null || !ValidSlot(entry.category, entry.slot) || !ids.Contains(entry.itemId) ||
                !slots.Add(entry.category + ":" + entry.slot))
                throw new InvalidOperationException("아이템 장착 슬롯 또는 보유 정보를 확인해주세요.");
        return data;
    }

    private static AccountItemInventory Current
    {
        get
        {
            if (cached == null || cachedJson != DataConfig.itemInventoryJson)
            {
                cached = Parse(DataConfig.itemInventoryJson);
                cachedJson = DataConfig.itemInventoryJson;
            }
            return cached;
        }
    }

    public static string WithDefaultVoice(string json)
    {
        var data = Parse(json);
        if (!data.ownedItemIds.Contains(BasicVoiceItemId)) data.ownedItemIds.Add(BasicVoiceItemId);
        if (!data.equippedItems.Exists(entry => entry.category == ItemCategory.Voice))
            data.equippedItems.Add(new EquippedAccountItem { category = ItemCategory.Voice, slot = 0, itemId = BasicVoiceItemId });
        return JsonUtility.ToJson(data);
    }

    private static bool ValidSlot(ItemCategory category, int slot) =>
        Enum.IsDefined(typeof(ItemCategory), category) && slot >= 0 && slot < (category == ItemCategory.Emote ? 4 : 1);
    private static void Save(AccountItemInventory data) => DataConfig.itemInventoryJson = JsonUtility.ToJson(data);

    public static bool HasItem(string id) => Current.ownedItemIds.Contains(id);
    public static string[] GetOwnedItemIds() => Current.ownedItemIds.ToArray();
    public static string GetEquippedItemId(ItemCategory category, int slot = 0)
    {
        foreach (var entry in Current.equippedItems)
            if (entry.category == category && entry.slot == slot) return entry.itemId;
        return string.Empty;
    }

    // No currency is deducted. Call this only after a grant/purchase is authorized.
    public static bool TryGrantItem(string id)
    {
        if (!Ready || ItemCatalog.Default == null || !ItemCatalog.Default.TryGet(id, out _)) return false;
        var data = Parse(DataConfig.itemInventoryJson);
        if (data.ownedItemIds.Contains(id)) return false;
        data.ownedItemIds.Add(id); Save(data); return true;
    }

    public static bool TryEquipItem(string id, int slot = 0)
    {
        if (!Ready || ItemCatalog.Default == null || !ItemCatalog.Default.TryGet(id, out var item) ||
            !ValidSlot(item.category, slot)) return false;
        var data = Parse(DataConfig.itemInventoryJson);
        if (!data.ownedItemIds.Contains(id)) return false;
        data.equippedItems.RemoveAll(entry => entry.category == item.category && entry.slot == slot);
        data.equippedItems.Add(new EquippedAccountItem { category = item.category, slot = slot, itemId = id });
        Save(data);
        if (item.category == ItemCategory.Voice) PlayerData.Local?.SyncEquippedVoice();
        return true;
    }

    public static bool TryUnequipItem(ItemCategory category, int slot = 0)
    {
        if (!Ready || !ValidSlot(category, slot)) return false;
        if (category == ItemCategory.Voice)
            return GetEquippedItemId(category) != BasicVoiceItemId && TryEquipItem(BasicVoiceItemId);
        var data = Parse(DataConfig.itemInventoryJson);
        if (data.equippedItems.RemoveAll(entry => entry.category == category && entry.slot == slot) == 0) return false;
        Save(data);
        if (category == ItemCategory.Voice) PlayerData.Local?.SyncEquippedVoice();
        return true;
    }
}
