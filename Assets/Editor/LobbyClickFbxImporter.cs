using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

[CustomEditor(typeof(LobbyCharacterClickInteraction))]
public sealed class LobbyCharacterClickEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "regions");
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || serializedObject.FindProperty("originalFbx").objectReferenceValue == null))
            if (GUILayout.Button("원본 FBX에서 Avatar 가져오기"))
            {
                serializedObject.ApplyModifiedProperties();
                Object owner = target;
                EditorApplication.delayCall += () => LobbyClickFbxImporter.ImportAvatar(owner);
            }
        EditorGUILayout.HelpBox("Original FBX: Mixamo 원본 골격 → Avatar 가져오기. 각 부위의 Animation FBX를 넣고 등록하면 Animation State에서 선택할 수 있습니다. 마녀의 Avatar는 유지됩니다.", MessageType.Info);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("regions"), true);
        serializedObject.ApplyModifiedProperties();
    }
}

// Explicit editor buttons only: never imports at startup or modifies the shared battle controller.
internal static class LobbyClickFbxImporter
{
    private static string ModelPath(Object asset)
    {
        string path = AssetDatabase.GetAssetPath(asset);
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
            !(AssetImporter.GetAtPath(path) is ModelImporter))
            throw new InvalidOperationException("Project 창의 FBX 원본 에셋을 지정해주세요. 씬 오브젝트는 사용할 수 없습니다.");
        return path;
    }

    private static Avatar PrepareAvatar(SerializedObject data)
    {
        string path = ModelPath(data.FindProperty("originalFbx").objectReferenceValue);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (importer.animationType != ModelImporterAnimationType.Human || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.SaveAndReimport();
        }
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault(a => a.isValid && a.isHuman);
        if (avatar == null) throw new InvalidOperationException("원본 FBX의 Humanoid Avatar가 유효하지 않습니다. 해당 FBX의 Rig > Configure에서 본 매핑을 확인해주세요.");
        data.FindProperty("sourceAvatar").objectReferenceValue = avatar;
        data.ApplyModifiedProperties();
        return avatar;
    }

    public static void ImportAvatar(Object owner)
    {
        if (owner == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try { PrepareAvatar(new SerializedObject(owner)); Debug.Log("로비 상호작용: 원본 FBX의 Avatar 연결 완료.", owner); }
        catch (Exception error) { Debug.LogError(error.Message, owner); }
    }

    public static void ImportAnimation(Object owner, string regionPath)
    {
        if (owner == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            var data = new SerializedObject(owner);
            var region = data.FindProperty(regionPath);
            if (region == null) return;
            var animator = data.FindProperty("character").objectReferenceValue as Animator;
            if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
                throw new InvalidOperationException("대상 캐릭터에 유효한 Humanoid Animator/Avatar를 지정해주세요.");
            string path = ModelPath(region.FindPropertyRelative("animationFbx").objectReferenceValue);
            Avatar avatar = data.FindProperty("sourceAvatar").objectReferenceValue as Avatar;
            if (data.FindProperty("originalFbx").objectReferenceValue != null) avatar = PrepareAvatar(data);
            if (avatar == null || !avatar.isHuman || !avatar.isValid)
                throw new InvalidOperationException("먼저 Original FBX를 지정하고 Avatar를 가져오세요.");
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            bool sameModel = AssetDatabase.GetAssetPath(avatar) == path;
            var setup = sameModel ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.CopyFromOther;
            Avatar copySource = sameModel ? null : avatar;
            if (importer.animationType != ModelImporterAnimationType.Human || importer.avatarSetup != setup ||
                importer.sourceAvatar != copySource || !importer.importAnimation)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = setup;
                importer.sourceAvatar = copySource;
                importer.importAnimation = true;
                importer.SaveAndReimport();
            }
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal) && c.isHumanMotion).ToArray();
            if (clips.Length == 0) throw new InvalidOperationException("Humanoid 애니메이션을 읽지 못했습니다. 원본과 애니메이션 FBX의 골격 및 Rig 설정을 확인해주세요.");
            AnimatorController controller = EnsureLocalController(data, animator);
            var machine = controller.layers[0].stateMachine;
            var group = machine.stateMachines.FirstOrDefault(s => s.stateMachine.name == "LobbyClicks").stateMachine;
            if (group == null) group = machine.AddStateMachine("LobbyClicks");
            string firstState = null;
            foreach (AnimationClip clip in clips)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long fileId);
                string stateName = clip.name.Replace('.', '_').Replace('/', '_') + "_" + guid.Substring(0, 8) + "_" + fileId;
                var state = group.states.FirstOrDefault(s => s.state.name == stateName).state;
                if (state == null) state = group.AddState(stateName);
                state.motion = clip;
                state.writeDefaultValues = true;
                EditorUtility.SetDirty(state);
                if (firstState == null) firstState = controller.layers[0].name + ".LobbyClicks." + stateName;
            }
            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(controller);
            // Reacquire after import: Unity may have reserialized the inspector's target.
            data.Update();
            region = data.FindProperty(regionPath);
            if (region == null) return;
            region.FindPropertyRelative("state").stringValue = firstState;
            region.FindPropertyRelative("layer").intValue = 0;
            region.FindPropertyRelative("seconds").floatValue = Mathf.Max(.1f, clips[0].length);
            data.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(controller);
            Debug.Log($"로비 전용 애니메이션 {clips.Length}개 등록 완료. Animation State에서 선택하세요.", owner);
        }
        catch (Exception error) { Debug.LogError(error.Message, owner); }
    }

    private static AnimatorController EnsureLocalController(SerializedObject data, Animator animator)
    {
        RuntimeAnimatorController local = data.FindProperty("interactionController").objectReferenceValue as RuntimeAnimatorController;
        AnimatorController controller = BaseController(local);
        if (controller == null)
        {
            RuntimeAnimatorController original = animator.runtimeAnimatorController;
            AnimatorController source = BaseController(original);
            if (source == null) throw new InvalidOperationException("캐릭터의 Animator Controller를 확인해주세요.");
            const string folder = "Assets/Animations/LobbyClickGenerated";
            if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Animations", "LobbyClickGenerated");
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/LobbyCharacterClick.controller");
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path)) throw new InvalidOperationException("로비 전용 컨트롤러 복사에 실패했습니다.");
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            local = controller;
            if (original is AnimatorOverrideController existing)
            {
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                existing.GetOverrides(overrides);
                var copy = new AnimatorOverrideController(controller);
                copy.ApplyOverrides(overrides);
                AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/LobbyCharacterClick.overrideController"));
                local = copy;
            }
            data.FindProperty("interactionController").objectReferenceValue = local;
            data.ApplyModifiedProperties();
        }
        if (animator.runtimeAnimatorController != local)
        {
            Undo.RecordObject(animator, "Connect lobby click controller");
            animator.runtimeAnimatorController = local;
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            EditorUtility.SetDirty(animator);
        }
        return controller;
    }

    private static AnimatorController BaseController(RuntimeAnimatorController runtime)
    {
        while (runtime is AnimatorOverrideController overrides) runtime = overrides.runtimeAnimatorController;
        return runtime as AnimatorController;
    }
}
