using UnityEngine;

public static class CharacterVoice
{
    private static CharacterVoicePack basic;
    public static AudioClip Resolve(AudioClip original, Transform speaker, bool local)
    {
        if (original == null) return null;
        if (basic == null) basic = Resources.Load<CharacterVoicePack>("VoiceBasicPrincess");
        // Only replace the character's authored basic clips, never cats, music or spell SFX.
        if (basic == null || !basic.Contains(original)) return original;
        string itemId = string.Empty;
        var player = speaker != null ? speaker.GetComponentInParent<Player>() : null;
        if (player != null && player.Object != null && player.Object.IsValid)
        {
            var data = PlayerData.FindPerkData(player.Runner, player.ParticipantRef);
            if (data != null) itemId = data.EquippedVoiceItemId.ToString();
        }
        else if (local) itemId = ItemInventory.GetEquippedItemId(ItemCategory.Voice);
        return ResolveItem(original, itemId);
    }

    public static AudioClip ResolveItem(AudioClip original, string itemId)
    {
        var catalog = ItemCatalog.Default;
        return catalog != null && catalog.TryGet(itemId, out var item) && item.category == ItemCategory.Voice && item.voicePack != null
            ? item.voicePack.Match(original) : original;
    }
}
