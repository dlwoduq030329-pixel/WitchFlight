using System;
using System.Collections.Generic;
using UnityEngine;

public static class DataConfig
{
    // Keep the existing assignment API while notifying the account's save coordinator.
    public static event Action Changed;
    // Presentation-only notification: loading server data must not trigger auto-save.
    public static event Action SnapshotApplied;
    public const int InitialMoney = 20000;
    public static event Action MoneyChanged;
    private static int _money;
    private static string _itemInventoryJson = ItemInventory.EmptyJson;
    public static event Action InventoryChanged;
    public static string itemInventoryJson
    {
        get => _itemInventoryJson;
        internal set
        {
            value ??= ItemInventory.EmptyJson;
            if (_itemInventoryJson == value) return;
            SetValue(ref _itemInventoryJson, value);
            InventoryChanged?.Invoke();
        }
    }
    public static int money
    {
        get => _money;
        internal set
        {
            value = Mathf.Max(0, value);
            if (_money == value) return;
            SetValue(ref _money, value);
            // UI must also refresh on silent server loads and logout resets.
            MoneyChanged?.Invoke();
        }
    }
    private static int _equippedTitleId;
    private static string _missionState = "";
    public static string missionState { get => _missionState; internal set => SetValue(ref _missionState, value ?? ""); }
    private static string _titleProgressJson = "{}";
    public static int equippedTitleId { get => _equippedTitleId; set => SetValue(ref _equippedTitleId, value); }
    public static string titleProgressJson { get => _titleProgressJson; set => SetValue(ref _titleProgressJson, value ?? "{}"); }
    private static int _emoteUpId = 1, _emoteRightId = 2, _emoteDownId = 3, _emoteLeftId = 4;
    public static int emoteUpId { get => _emoteUpId; set => SetValue(ref _emoteUpId, value > 0 ? value : 1); }
    public static int emoteRightId { get => _emoteRightId; set => SetValue(ref _emoteRightId, value > 0 ? value : 2); }
    public static int emoteDownId { get => _emoteDownId; set => SetValue(ref _emoteDownId, value > 0 ? value : 3); }
    public static int emoteLeftId { get => _emoteLeftId; set => SetValue(ref _emoteLeftId, value > 0 ? value : 4); }
    public static int GetEmote(int slot)
    {
        int id = slot switch { 0 => emoteUpId, 1 => emoteRightId, 2 => emoteDownId, 3 => emoteLeftId, _ => 0 };
        if (slot < 0 || slot > 3) return 0;
        return EmoteTable.Default != null && EmoteTable.Default.TryGet(id, out _) ? id : slot + 1;
    }
    public static bool SetEmote(int slot, int id)
    {
        if (slot < 0 || slot > 3 || EmoteTable.Default == null || !EmoteTable.Default.TryGet(id, out _)) return false;
        switch ((EmoteDirection)slot)
        {
            case EmoteDirection.Up: emoteUpId = id; break;
            case EmoteDirection.Right: emoteRightId = id; break;
            case EmoteDirection.Down: emoteDownId = id; break;
            case EmoteDirection.Left: emoteLeftId = id; break;
        }
        return true;
    }
    private static string _playerName;
    private static int _catTutorialCompleted;
    public static int catTutorialCompleted { get => _catTutorialCompleted; set => SetValue(ref _catTutorialCompleted, value == 1 ? 1 : 0); }
    private static int _playerprofile, _hatIndex, _broomIndex, _magic1Index, _magic2Index, _hairStylePreset, _wandIndex;
    private static float _bangsLength, _sideHairLength, _ahogeLength;
    private static float _bangsDirection = 0.5f;
    private static Color _hairColor = Color.white, _clothColor = Color.white, _eyeColor = Color.white;
    private static int batchDepth, silentDepth;
    private static bool batchChanged;

    public static string playerName { get => _playerName; set => SetValue(ref _playerName, value); }
    public static int playerprofile { get => _playerprofile; set => SetValue(ref _playerprofile, value); }
    public static int hatIndex { get => _hatIndex; set => SetValue(ref _hatIndex, value); }
    public static int broomIndex { get => _broomIndex; set => SetValue(ref _broomIndex, value); }
    public static int magic1Index { get => _magic1Index; set => SetValue(ref _magic1Index, value); }
    public static int magic2Index { get => _magic2Index; set => SetValue(ref _magic2Index, value); }
    public static int hairStylePreset { get => _hairStylePreset; set => SetValue(ref _hairStylePreset, value); }
    public static float bangsLength { get => _bangsLength; set => SetValue(ref _bangsLength, value); }
    public static float bangsDirection { get => _bangsDirection; set => SetValue(ref _bangsDirection, value); }
    public static float sideHairLength { get => _sideHairLength; set => SetValue(ref _sideHairLength, value); }
    public static float ahogeLength { get => _ahogeLength; set => SetValue(ref _ahogeLength, value); }
    public static Color hairColor { get => _hairColor; set => SetValue(ref _hairColor, value); }
    public static Color clothColor { get => _clothColor; set => SetValue(ref _clothColor, value); }
    public static Color eyeColor { get => _eyeColor; set => SetValue(ref _eyeColor, value); }
    public static int wandIndex { get => _wandIndex; set => SetValue(ref _wandIndex, value); }

    private static void SetValue<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        if (silentDepth > 0) return;
        batchChanged = true;
        NotifyChanged();
    }

    private static void NotifyChanged()
    {
        if (batchDepth != 0 || !batchChanged) return;
        batchChanged = false;
        Changed?.Invoke();
    }

    // Use the silent form only for server snapshots / session resets, not user edits.
    internal static IDisposable BeginChangeBatch(bool notify = true) => new ChangeBatch(notify);

    private sealed class ChangeBatch : IDisposable
    {
        private readonly bool notify;
        private bool disposed;
        public ChangeBatch(bool notify)
        {
            this.notify = notify;
            batchDepth++;
            if (!notify) silentDepth++;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (!notify) silentDepth--;
            batchDepth--;
            NotifyChanged();
            if (!notify && batchDepth == 0) SnapshotApplied?.Invoke();
        }
    }

    // Compatibility for the existing integer hair slider / PlayerAppearance demo.
    public static int hairLength
    {
        get => Mathf.RoundToInt(bangsLength);
        set => bangsLength = Mathf.Max(0, value);
    }

    public static PlayerConfig GetPlayerConfig() => new PlayerConfig
    {
        hairStylePreset = hairStylePreset, bangsLength = bangsLength,
        bangsDirection = bangsDirection, sideHairLength = sideHairLength, ahogeLength = ahogeLength,
        hairColor = hairColor, clothColor = clothColor, eyeColor = eyeColor,
        hatIndex = hatIndex, broomIndex = broomIndex, wandIndex = wandIndex
    }.Sanitized();

    public static void ApplyPlayerConfig(PlayerConfig config)
    {
        using var batch = BeginChangeBatch();
        config = config.Sanitized();
        hairStylePreset = config.hairStylePreset;
        bangsLength = config.bangsLength;
        bangsDirection = config.bangsDirection;
        sideHairLength = config.sideHairLength;
        ahogeLength = config.ahogeLength;
        hairColor = config.hairColor;
        clothColor = config.clothColor;
        eyeColor = config.eyeColor;
        hatIndex = config.hatIndex;
        broomIndex = config.broomIndex;
        wandIndex = config.wandIndex;
    }

    // Prevent the previous account's appearance leaking into missing/failed loads.
    public static void ResetToDefaults()
    {
        using var batch = BeginChangeBatch(false);
        playerName = string.Empty;
        emoteUpId = 1; emoteRightId = 2; emoteDownId = 3; emoteLeftId = 4;
        money = 0;
        itemInventoryJson = ItemInventory.EmptyJson;
        equippedTitleId = 0;
        titleProgressJson = "{}";
        missionState = "";
        catTutorialCompleted = 0;
        playerprofile = 0;
        magic1Index = (int)MagicType.Fire;
        magic2Index = (int)MagicType.Ice;
        ApplyPlayerConfig(PlayerConfig.Default);
    }

}
