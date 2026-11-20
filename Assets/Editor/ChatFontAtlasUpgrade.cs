using TMPro;
using UnityEditor;
using UnityEngine;

// Share source font + settings, not a generated 8192 texture cache.
[InitializeOnLoad]
internal static class ChatFontAtlasUpgrade
{
    internal const string FontPath = "Assets/UI/Font/Pretendard-Regular SDF.asset";
    static ChatFontAtlasUpgrade() => EditorApplication.delayCall += Upgrade;

    [MenuItem("Tools/Chat/Prepare Font For Commit")]
    private static void Upgrade()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Font/Pretendard-Regular.otf");
        if (font == null || source == null) return;
        var serialized = new SerializedObject(font);
        serialized.FindProperty("m_SourceFontFile").objectReferenceValue = source;
        serialized.FindProperty("m_AtlasWidth").intValue = 8192;
        serialized.FindProperty("m_AtlasHeight").intValue = 8192;
        serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        font.isMultiAtlasTexturesEnabled = true;
        ClearGeneratedCache(font);
        AssetDatabase.SaveAssets();
    }

    internal static void ClearGeneratedCache(TMP_FontAsset font)
    {
        if (font == null || font.atlasPopulationMode != AtlasPopulationMode.Dynamic ||
            font.sourceFontFile == null) return;
        // Keep configured capacity, but serialize a 1x1 empty texture. TMP recreates
        // glyphs from the included OTF as text is displayed, also in player builds.
        font.ClearFontAssetData(true);
        EditorUtility.SetDirty(font);
        EditorUtility.SetDirty(font.material);
        foreach (var texture in font.atlasTextures)
            if (texture != null) EditorUtility.SetDirty(texture);
    }
}

internal sealed class ChatFontCacheSaveProcessor : AssetModificationProcessor
{
    private static string[] OnWillSaveAssets(string[] paths)
    {
        foreach (var path in paths)
        {
            if (path != ChatFontAtlasUpgrade.FontPath) continue;
            ChatFontAtlasUpgrade.ClearGeneratedCache(
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path));
            break;
        }
        return paths;
    }
}
