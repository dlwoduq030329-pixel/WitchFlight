using System;
using BackEnd;
using UnityEngine;

public partial class DatabaseManager
{
    public bool IsVoicePurchaseInProgress { get; private set; }

    // One UpdateV2 writes currency + ownership in the SAME account row, preserving equipment.
    // Do not compose this operation from TrySpendMoney and TryGrantItem.
    public bool TryPurchaseVoice(string itemId, out string message)
    {
        message = string.Empty;
        if (!CanAutoSave) { message = "로그인 및 계정 데이터 불러오기를 완료해주세요."; return false; }
        if (IsVoicePurchaseInProgress || IsAutoSavePending) { message = "계정 정보 저장 중입니다. 잠시 후 다시 눌러주세요."; return false; }
        if (ItemCatalog.Default == null || !ItemCatalog.Default.TryGet(itemId, out var item) ||
            item.category != ItemCategory.Voice || item.id.Length > 64 || item.voicePack == null || !item.voicePack.HasClips)
        { message = "목소리 상품 설정을 확인해주세요."; return false; }

        IsVoicePurchaseInProgress = true;
        try
        {
            // Re-read before every attempt, including retry after a lost response.
            var read = Backend.GameData.GetMyData(TABLE_NAME, new Where());
            if (!read.IsSuccess()) { message = "보유 정보 조회 실패: " + read.GetMessage(); return false; }
            var rows = read.FlattenRows();
            if (rows == null || !rows.IsArray || rows.Count != 1 ||
                ReadString(rows[0], "inDate", "") != currentPlayerSettingInDate)
                throw new InvalidOperationException("구매할 계정 데이터 행을 확인할 수 없습니다.");
            var server = ReadPlayerSetting(rows[0]);
            ApplyEconomySnapshot(server.money, server.itemInventoryJson);
            var inventory = ItemInventory.Parse(server.itemInventoryJson);
            if (inventory.ownedItemIds.Contains(itemId)) { message = "이미 보유한 목소리입니다. 장착 버튼을 눌러주세요."; return false; }
            if (server.money < item.price) { message = "재화가 부족합니다."; return false; }

            inventory.ownedItemIds.Add(itemId);
            string json = JsonUtility.ToJson(inventory);
            int balance = server.money - item.price;
            var param = new Param(); param.Add("money", balance); param.Add("itemInventoryJson", json);
            LastSaveSucceeded = false;
            var saved = Backend.GameData.UpdateV2(TABLE_NAME, currentPlayerSettingInDate, loadedUserInDate, param);
            if (!saved.IsSuccess()) throw new InvalidOperationException(saved.GetMessage());
            ApplyEconomySnapshot(balance, json);
            LastSaveSucceeded = true; LastError = string.Empty;
            message = "구매 완료! 장착 버튼을 눌러 목소리를 변경해주세요.";
            return true;
        }
        catch (Exception e)
        {
            // The write might have reached the server before the response was lost.
            // Block stale full-profile autosaves until the account is reloaded.
            IsDataConfigReady = false; LastSaveSucceeded = false;
            message = "구매 결과를 확인할 수 없습니다. 다시 로그인하여 보유 목록을 확인해주세요. " + e.Message;
            LastError = message;
            Debug.LogWarning(message);
            return false;
        }
        finally { IsVoicePurchaseInProgress = false; }
    }

    private void ApplyEconomySnapshot(int balance, string inventory)
    {
        currentPlayerSetting.money = balance;
        currentPlayerSetting.itemInventoryJson = inventory;
        using (DataConfig.BeginChangeBatch(false))
        {
            DataConfig.money = balance;
            DataConfig.itemInventoryJson = inventory;
        }
        PlayerData.Local?.SyncEquippedVoice();
    }
}
