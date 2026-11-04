using Fusion;

public partial class PlayerData
{
    [Networked] public NetworkString<_64> EquippedVoiceItemId { get; private set; }

    public void SyncEquippedVoice()
    {
        if (Object == null || !Object.IsValid || !Object.HasInputAuthority) return;
        string id = ItemInventory.GetEquippedItemId(ItemCategory.Voice);
        if (id.Length <= 64 && (string.IsNullOrEmpty(id) || ItemInventory.HasItem(id))) RPC_SetVoiceItem(id);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetVoiceItem(string id)
    {
        if (string.IsNullOrEmpty(id)) { EquippedVoiceItemId = string.Empty; return; }
        if (id.Length <= 64 && ItemCatalog.Default != null && ItemCatalog.Default.TryGet(id, out var item) &&
            item.category == ItemCategory.Voice && item.voicePack != null) EquippedVoiceItemId = id;
    }
}
