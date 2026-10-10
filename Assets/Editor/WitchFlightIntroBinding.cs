using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// One-use editor migration; never opens or saves a user's scene.
internal static class WitchFlightIntroBinding
{
    [InitializeOnLoadMethod]
    private static void Init() => EditorApplication.update += Tick;

    private static void Tick()
    {
        const string request = "Temp/WitchFlightIntroBinding.request";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(request)) return;
        EditorApplication.update -= Tick;
        File.Delete(request);
        GameObject contents = null;
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before binding intro UI.");
            const string path = "Assets/UI/Prefab/BattleUI.prefab";
            contents = PrefabUtility.LoadPrefabContents(path);
            linkuserinfo bridge = contents.GetComponentsInChildren<linkuserinfo>(true).Single();
            Transform local = bridge.transform.Cast<Transform>().Single(t => t.name.Trim() == "PlayerProfile");
            Transform opponent = bridge.transform.Cast<Transform>().Single(t => t.name.Trim() == "EnemyProfile");
            var data = new SerializedObject(bridge);
            Bind<TMP_Text>(data, "localNameText", local, "Nickname");
            Bind<TMP_Text>(data, "opponentNameText", opponent, "Nickname");
            Bind<Image>(data, "localProfileImage", local, "ProfileFrame/ProfileImage");
            Bind<Image>(data, "opponentProfileImage", opponent, "ProfileFrame/ProfileImage");
            Bind<Image>(data, "localMagic1Image", local, "MagicIcon01");
            Bind<Image>(data, "localMagic2Image", local, "MagicIcon02");
            Bind<Image>(data, "opponentMagic1Image", opponent, "MagicIcon01");
            Bind<Image>(data, "opponentMagic2Image", opponent, "MagicIcon02");
            data.FindProperty("confirmRandomMatchPreview").boolValue = false;
            data.FindProperty("magicTable").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MagicStatTable>("Assets/Resources/MagicStatTable.asset");
            ProfileImageTable profileTable = AssetDatabase.LoadAssetAtPath<ProfileImageTable>("Assets/Resources/ProfileImageTable.asset");
            if (profileTable == null) throw new InvalidOperationException("Shared profile image table missing.");
            data.FindProperty("profileTable").objectReferenceValue = profileTable;
            data.ApplyModifiedPropertiesWithoutUndo();
            string backup = "Temp/BattleUI-before-intro-binding-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".prefab";
            File.Copy(path, backup);
            PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);
            if (!saved) throw new InvalidOperationException("Prefab save failed.");
            var verified = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<linkuserinfo>(true));
            string[] fields = { "localNameText", "opponentNameText", "localProfileImage", "opponentProfileImage", "localMagic1Image", "localMagic2Image", "opponentMagic1Image", "opponentMagic2Image", "profileTable", "magicTable" };
            foreach (string field in fields)
                if (verified.FindProperty(field).objectReferenceValue == null) throw new InvalidOperationException("Saved reference missing: " + field);
            File.WriteAllText("Temp/WitchFlightIntroBinding.report.txt", "VERIFIED: 8 intro UI references; shared MagicStatTable and ProfileImageTable. Existing portrait RawImages and scenes untouched. Backup: " + backup);
        }
        catch (Exception e)
        {
            File.WriteAllText("Temp/WitchFlightIntroBinding.report.txt", "FAILED: " + e);
            Debug.LogException(e);
        }
        finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
    }

    private static void Bind<T>(SerializedObject data, string field, Transform root, string path) where T : Component
    {
        Transform target = root.Find(path);
        T component = target != null ? target.GetComponent<T>() : null;
        if (component == null) throw new InvalidOperationException("Missing " + field + " at " + root.name + "/" + path);
        data.FindProperty(field).objectReferenceValue = component;
    }
}
