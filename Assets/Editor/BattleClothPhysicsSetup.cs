using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using EZhex1991.EZSoftBone;

// One-time editor transfer. No runtime prefab creation or scene searches.
internal static class BattleClothPhysicsSetup
{
    private const string Battle = "Assets/Ch/ChPrefab.prefab";
    [InitializeOnLoadMethod] private static void Queue()
    {
        EditorApplication.delayCall += Auto;
        EditorApplication.playModeStateChanged -= OnPlay;
        EditorApplication.playModeStateChanged += OnPlay;
    }
    private static void OnPlay(PlayModeStateChange state)
    { if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Auto; }
    private static void Auto()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Auto; return; }
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Battle);
        if (asset != null && asset.GetComponentInChildren<FlightSkirtShapeLimit>(true) == null) Setup();
    }
    [MenuItem("Tools/Witch Flight/Setup Battle Cloth Physics")]
    private static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var source = PrefabUtility.LoadPrefabContents("Assets/ChPrefabForRobby.prefab");
        var target = PrefabUtility.LoadPrefabContents(Battle);
        try
        {
            if (target.GetComponentInChildren<FlightSkirtShapeLimit>(true) != null) return;
            Transform sourceMount = source.GetComponentsInChildren<LobbyHatSoftBone>(true)
                .First(x => x.hatRenderer.name == "StandardHat").transform.parent.parent;
            Transform targetMount = target.GetComponentsInChildren<Transform>(true).First(x => x.name == "Hat" && x.parent.name == "Hat_Root");
            targetMount.SetParent(targetMount.parent.parent, true);
            foreach (var helper in source.GetComponentsInChildren<LobbyHatSoftBone>(true))
            {
                Transform sourceHat = helper.transform.parent;
                Transform targetHat = targetMount.Find(sourceHat.name);
                if (targetHat == null) throw new InvalidOperationException("Battle hat missing: " + sourceHat.name);
                if (PrefabUtility.IsAnyPrefabInstanceRoot(targetHat.gameObject))
                    PrefabUtility.UnpackPrefabInstance(targetHat.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                CopyTree(sourceHat, targetHat, source.transform, target.transform);
                var copied = targetHat.GetComponentInChildren<LobbyHatSoftBone>(true);
                copied.flightMode = true; copied.fullFlutterSpeed = 60f; copied.flightFlutterStrength = .008f;
                copied.solver.enabled = false;
                var limit = copied.GetComponent<LobbyHatShapeLimit>();
                limit.maxBend = 8f; limit.maxDisplacement = .005f;
                // Do not permit the experimental large arm-contact fold in flight.
                limit.contactBend = 18f;
            }
            var sourceSkirt = source.GetComponentInChildren<LobbySkirtSoftBone>(true);
            string path = AnimationUtility.CalculateTransformPath(sourceSkirt.transform.parent, source.transform);
            Transform targetSkirt = target.transform.Find(path);
            if (targetSkirt == null) throw new InvalidOperationException("Battle skirt missing: " + path);
            var host = new GameObject("FlightSkirtPhysics"); host.layer = sourceSkirt.gameObject.layer;
            host.transform.SetParent(targetSkirt, false);
            CopyTree(sourceSkirt.transform, host.transform, source.transform, target.transform);
            var skirt = host.GetComponent<LobbySkirtSoftBone>();
            var settings = new SerializedObject(skirt);
            settings.FindProperty("flightMode").boolValue = true;
            settings.FindProperty("fullFlutterSpeed").floatValue = 60f;
            settings.FindProperty("maxWind").floatValue = .025f;
            settings.FindProperty("flutterStrength").floatValue = .012f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var solver = host.GetComponent<EZSoftBone>(); solver.enabled = false;
            var guard = host.AddComponent<FlightSkirtShapeLimit>(); guard.solver = solver;
            var roots = new SerializedObject(solver).FindProperty("m_RootBones");
            guard.chainRoots = new Transform[roots.arraySize];
            for (int i = 0; i < roots.arraySize; i++) guard.chainRoots[i] = roots.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
            // Verify copied references do not still point into the lobby prefab contents.
            foreach (var component in target.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!(component is LobbyHatSoftBone) && !(component is LobbyHatShapeLimit) &&
                    !(component is LobbySkirtSoftBone) && !(component is EZSoftBone)) continue;
                var so = new SerializedObject(component); var p = so.GetIterator();
                while (p.Next(true))
                    if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue is Component c && c.transform.IsChildOf(source.transform))
                        throw new InvalidOperationException("Unmapped lobby reference: " + component.name + "/" + p.propertyPath);
            }
            PrefabUtility.SaveAsPrefabAsset(target, Battle); AssetDatabase.SaveAssets();
            Debug.Log("[BattleCloth] Copied lobby skirt and three hats to ChPrefab; bounded flight flutter enabled.");
        }
        catch (Exception e) { Debug.LogException(e); }
        finally { PrefabUtility.UnloadPrefabContents(target); PrefabUtility.UnloadPrefabContents(source); }
    }
    private static void CopyTree(Transform source, Transform target, Transform sourceRoot, Transform targetRoot)
    {
        // Build the full hierarchy and components first so all references can be remapped.
        foreach (var node in source.GetComponentsInChildren<Transform>(true))
        {
            string path = AnimationUtility.CalculateTransformPath(node, source);
            Transform dest = path.Length == 0 ? target : target.Find(path);
            if (dest == null)
            {
                string parentPath = AnimationUtility.CalculateTransformPath(node.parent, source);
                dest = new GameObject(node.name).transform;
                dest.SetParent(parentPath.Length == 0 ? target : target.Find(parentPath), false);
                dest.localPosition = node.localPosition; dest.localRotation = node.localRotation; dest.localScale = node.localScale;
                dest.gameObject.layer = node.gameObject.layer; dest.gameObject.SetActive(node.gameObject.activeSelf);
            }
            foreach (var c in node.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                if (c is SkinnedMeshRenderer && dest.GetComponent<MeshRenderer>() != null)
                {
                    UnityEngine.Object.DestroyImmediate(dest.GetComponent<MeshRenderer>());
                    UnityEngine.Object.DestroyImmediate(dest.GetComponent<MeshFilter>());
                }
                if (dest.GetComponent(c.GetType()) == null) dest.gameObject.AddComponent(c.GetType());
            }
        }
        foreach (var node in source.GetComponentsInChildren<Transform>(true))
        {
            string path = AnimationUtility.CalculateTransformPath(node, source);
            Transform dest = path.Length == 0 ? target : target.Find(path);
            foreach (var c in node.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                var copy = dest.GetComponent(c.GetType()); EditorUtility.CopySerialized(c, copy);
                var so = new SerializedObject(copy); var p = so.GetIterator();
                while (p.Next(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = p.objectReferenceValue;
                    Transform transform = value is GameObject go ? go.transform : (value as Component)?.transform;
                    if (transform == null || !transform.IsChildOf(sourceRoot)) continue;
                    Transform mapped;
                    if (transform.IsChildOf(source))
                    {
                        string local = AnimationUtility.CalculateTransformPath(transform, source);
                        mapped = local.Length == 0 ? target : target.Find(local);
                    }
                    else
                    {
                        string global = AnimationUtility.CalculateTransformPath(transform, sourceRoot);
                        mapped = global.Length == 0 ? targetRoot : targetRoot.Find(global);
                    }
                    if (mapped == null) throw new InvalidOperationException("Cannot map " + transform.name + " for " + p.propertyPath);
                    p.objectReferenceValue = value is GameObject ? (UnityEngine.Object)mapped.gameObject : mapped.GetComponent(value.GetType());
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
