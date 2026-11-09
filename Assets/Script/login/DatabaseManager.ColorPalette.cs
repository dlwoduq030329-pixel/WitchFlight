using System;
using BackEnd;
using UnityEngine;

public enum ColorPaletteTarget { Clothes, Hair, Eyes }

public partial class DatabaseManager
{
    public const int ClothPalettePrice = 5000;
    public bool IsPalettePurchaseInProgress { get; private set; }

    // The preview never changes DataConfig. Commit money and color together in one row.
    public void PurchaseClothColor(Color color, Action<bool, string> completed)
        => PurchasePaletteColor(ColorPaletteTarget.Clothes, color, Color.white, completed);

    public void PurchasePaletteColor(ColorPaletteTarget target, Color color, Color eyeShapeKey, Action<bool, string> completed)
    {
        if (target < ColorPaletteTarget.Clothes || target > ColorPaletteTarget.Eyes)
        { completed(false, "색상 변경 대상을 확인해주세요."); return; }
        if (!CanAutoSave) { completed(false, "로그인 정보를 확인해주세요."); return; }
        if (IsPalettePurchaseInProgress || IsAttendanceInProgress || IsItemPurchaseInProgress || claimingMission || IsAutoSavePending)
        { completed(false, "계정 정보 저장 중입니다. 잠시 후 다시 눌러주세요."); return; }
        if (float.IsNaN(color.r) || float.IsNaN(color.g) || float.IsNaN(color.b) ||
            float.IsInfinity(color.r) || float.IsInfinity(color.g) || float.IsInfinity(color.b))
        { completed(false, "색상 값을 확인해주세요."); return; }
        color.a = 1;
        Color selected = (Color32)color;
        string hex = "#" + ColorUtility.ToHtmlStringRGBA(selected);
        string column = target == ColorPaletteTarget.Hair ? "hairColor" : target == ColorPaletteTarget.Eyes ? "eyePaletteColor" : "clothColor";
        Color current = target == ColorPaletteTarget.Hair ? DataConfig.hairColor : target == ColorPaletteTarget.Eyes ? DataConfig.eyePaletteColor : DataConfig.clothColor;
        if (target == ColorPaletteTarget.Eyes)
        {
            var table = EyeTexturePresetTable.Default;
            if (table == null || !table.TryGetPaletteShape(true, out var fourWhite) || !table.TryGetPaletteShape(false, out var sparkling) ||
                (ColorUtility.ToHtmlStringRGBA(eyeShapeKey) != ColorUtility.ToHtmlStringRGBA(fourWhite.savedColorKey) &&
                 ColorUtility.ToHtmlStringRGBA(eyeShapeKey) != ColorUtility.ToHtmlStringRGBA(sparkling.savedColorKey)))
            { completed(false, "눈 모양 설정을 확인해주세요."); return; }
        }
        if (hex == "#" + ColorUtility.ToHtmlStringRGBA(current) &&
            (target != ColorPaletteTarget.Eyes || ColorUtility.ToHtmlStringRGBA(eyeShapeKey) == ColorUtility.ToHtmlStringRGBA(DataConfig.eyeColor)))
        { completed(false, "현재와 다른 색상을 선택해주세요."); return; }

        int session = dataSessionVersion;
        string row = currentPlayerSettingInDate, owner = loadedUserInDate;
        IsPalettePurchaseInProgress = true;
        LastSaveSucceeded = false;
        bool Current() => this != null && session == dataSessionVersion && HasLoadedProfile &&
            owner == loadedUserInDate && row == currentPlayerSettingInDate;
        void Finish(bool success, string message, bool uncertain = false)
        {
            if (!Current()) return;
            if (uncertain) IsDataConfigReady = false; // Never overwrite an uncertain server write.
            IsPalettePurchaseInProgress = false;
            LastSaveSucceeded = success;
            LastError = success ? string.Empty : message;
            completed(success, message);
            DataConfigReadyChanged?.Invoke(); // Resume reward/save clients that waited for this purchase.
        }
        void Uncertain() => Finish(false, "저장 결과를 확인할 수 없습니다. 다시 로그인해 색상과 재화를 확인해주세요.", true);
        try
        {
            Backend.GameData.GetMyData(TABLE_NAME, new Where(), read => saveCompletions.Enqueue(() =>
            {
                if (!Current()) return;
                try
                {
                    if (read == null || !read.IsSuccess())
                    { Finish(false, "재화 조회에 실패했습니다. 다시 시도해주세요."); return; }
                    var rows = read.FlattenRows();
                    if (rows == null || rows.Count != 1 || ReadString(rows[0], "inDate", "") != row)
                    { Finish(false, "계정 데이터 행을 확인할 수 없습니다."); return; }
                    int money = ReadPlayerSetting(rows[0]).money;
                    if (money < ClothPalettePrice)
                    { Finish(false, "재화가 부족합니다. 5,000 재화가 필요합니다."); return; }
                    int balance = money - ClothPalettePrice;
                    var param = new Param();
                    param.Add("money", balance);
                    param.Add(column, hex);
                    if (target == ColorPaletteTarget.Eyes) param.Add("eyeColor", "#" + ColorUtility.ToHtmlStringRGBA(eyeShapeKey));
                    try
                    {
                        Backend.GameData.UpdateV2(TABLE_NAME, row, owner, param, saved => saveCompletions.Enqueue(() =>
                        {
                            if (!Current()) return;
                            if (saved == null || !saved.IsSuccess()) { Uncertain(); return; }
                            currentPlayerSetting.money = balance;
                            switch (target)
                            {
                                case ColorPaletteTarget.Clothes: currentPlayerSetting.clothColor = selected; break;
                                case ColorPaletteTarget.Hair: currentPlayerSetting.hairColor = selected; break;
                                case ColorPaletteTarget.Eyes:
                                    currentPlayerSetting.eyePaletteColor = selected;
                                    currentPlayerSetting.eyeColor = eyeShapeKey; break;
                            }
                            // Notify the preview and network appearance subscribers only after success.
                            using (DataConfig.BeginChangeBatch())
                            {
                                DataConfig.money = balance;
                                switch (target)
                                {
                                    case ColorPaletteTarget.Clothes: DataConfig.clothColor = selected; break;
                                    case ColorPaletteTarget.Hair: DataConfig.hairColor = selected; break;
                                    case ColorPaletteTarget.Eyes:
                                        DataConfig.eyePaletteColor = selected;
                                        DataConfig.eyeColor = eyeShapeKey; break;
                                }
                            }
                            Finish(true, "색상이 변경되었습니다.");
                        }));
                    }
                    catch (Exception) { Uncertain(); }
                }
                catch (Exception) { Finish(false, "계정 데이터를 확인할 수 없습니다. 다시 로그인해주세요."); }
            }));
        }
        catch (Exception) { Finish(false, "재화 조회를 시작할 수 없습니다. 다시 시도해주세요."); }
    }
}
