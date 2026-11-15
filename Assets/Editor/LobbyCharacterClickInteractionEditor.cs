using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[CustomPropertyDrawer(typeof(LobbyCharacterClickInteraction.Region))]
public sealed class LobbyCharacterClickRegionDrawer : PropertyDrawer
{
    private const float Gap = 3f;
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => (EditorGUIUtility.singleLineHeight + Gap) * 12;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        Rect row = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(row, label, EditorStyles.boldLabel);
        row.y += row.height + Gap;
        foreach (var name in new[] { "label", "bone", "anchor", "localCenter", "radius" })
        {
            EditorGUI.PropertyField(row, property.FindPropertyRelative(name));
            row.y += row.height + Gap;
        }
        EditorGUI.PropertyField(row, property.FindPropertyRelative("animationFbx"), new GUIContent("Animation FBX"));
        row.y += row.height + Gap;
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || property.FindPropertyRelative("animationFbx").objectReferenceValue == null))
            if (GUI.Button(row, "FBX 가져오기 → 로비 애니메이션 등록"))
            {
                var target = property.serializedObject.targetObject;
                string path = property.propertyPath;
                property.serializedObject.ApplyModifiedProperties();
                EditorApplication.delayCall += () => LobbyClickFbxImporter.ImportAnimation(target, path);
            }
        row.y += row.height + Gap;
        var animator = property.serializedObject.FindProperty("character")?.objectReferenceValue as Animator;
        RuntimeAnimatorController runtime = animator != null ? animator.runtimeAnimatorController : null;
        while (runtime is AnimatorOverrideController overrides) runtime = overrides.runtimeAnimatorController;
        var names = new List<string> { "미지정 (클릭 반응 없음)" };
        var layers = new List<int> { 0 };
        if (runtime is AnimatorController controller)
            for (int i = 0; i < controller.layers.Length; i++)
                Collect(controller.layers[i].stateMachine, controller.layers[i].name + ".", i, names, layers);
        var state = property.FindPropertyRelative("state");
        var layer = property.FindPropertyRelative("layer");
        int index = string.IsNullOrEmpty(state.stringValue) ? 0 : names.IndexOf(state.stringValue);
        if (index < 0) { index = names.Count; names.Add(state.stringValue + " (현재 컨트롤러에 없음)"); layers.Add(layer.intValue); }
        EditorGUI.BeginChangeCheck();
        int selected = EditorGUI.Popup(row, "Animation State", index, names.ToArray());
        if (EditorGUI.EndChangeCheck())
        {
            state.stringValue = selected == 0 ? "" : names[selected];
            layer.intValue = layers[selected];
        }
        row.y += row.height + Gap;
        EditorGUI.LabelField(row, "Animation Layer", layer.intValue.ToString());
        row.y += row.height + Gap;
        EditorGUI.PropertyField(row, property.FindPropertyRelative("seconds"), new GUIContent("재생 시간 (초)"));
        row.y += row.height + Gap;
        EditorGUI.PropertyField(row, property.FindPropertyRelative("blendSeconds"), new GUIContent("전환 시간 (초)"));
        EditorGUI.EndProperty();
    }

    private static void Collect(AnimatorStateMachine machine, string prefix, int layer, List<string> names, List<int> layers)
    {
        foreach (var state in machine.states) { names.Add(prefix + state.state.name); layers.Add(layer); }
        foreach (var child in machine.stateMachines) Collect(child.stateMachine, prefix + child.stateMachine.name + ".", layer, names, layers);
    }
}
