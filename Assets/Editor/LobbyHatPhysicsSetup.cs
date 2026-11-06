using System;
using System.Collections.Generic;
using System.Linq;
using EZhex1991.EZSoftBone;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

internal static class LobbyHatPhysicsSetup
{
    private const string PrefabPath = "Assets/ChPrefabForRobby.prefab";
    private const string MeshPath = "Assets/Script/LobbyAsHatRig.asset";
    [InitializeOnLoadMethod] private static void Queue()
    {
        EditorApplication.delayCall += AutoSetup;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }
    private static void OnPlayMode(PlayModeStateChange state)
    { if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += AutoSetup; }
    private static void AutoSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += AutoSetup; return; }
        Setup();
    }
    [MenuItem("Tools/Witch Flight/Setup Lobby Hat Physics")]
    private static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (asset == null) return;
        if (asset.GetComponentsInChildren<LobbyHatSoftBone>(true).Length > 0)
        {
            UpgradeDefaultBrim();
            return;
        }
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Animator animator = root.GetComponentInChildren<Animator>(true);
            var player = root.GetComponents<MonoBehaviour>().First(x => x != null && x.GetType().Name == "PlayerRobby");
            var property = new SerializedObject(player).FindProperty("lobbyHats");
            var hats = new GameObject[property.arraySize];
            for (int i = 0; i < hats.Length; i++) hats[i] = property.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            if (hats.Length < 4 || hats[1] == null || hats[2] == null || hats[3] == null || animator == null)
                throw new InvalidOperationException("Lobby hat slots 1-3 and Animator are required.");
            foreach (var hat in hats)
                if (hat != null && PrefabUtility.IsAnyPrefabInstanceRoot(hat))
                    PrefabUtility.UnpackPrefabInstance(hat, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            Transform hatRoot = root.GetComponentsInChildren<Transform>(true).First(x => x.name == "Hat_Root");
            // The equipment container is not a deforming bone. Move it out of the
            // solver subtree while preserving its exact placement under the head.
            Transform mount = hatRoot.Cast<Transform>().FirstOrDefault(x => x.name == "Hat");
            if (mount != null) mount.SetParent(hatRoot.parent, true);
            AddPhysics(root, hats[1], animator, hatRoot, true);
            Transform sortingRoot = hats[2].GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "SortingHatBone");
            // Identify by contents as slot order may differ between projects.
            GameObject sorting = hats.FirstOrDefault(x => x != null && x.GetComponentsInChildren<Transform>(true).Any(t => t.name == "SortingHatBone"));
            if (sorting == null) throw new InvalidOperationException("SortingHat rig was not found.");
            sortingRoot = sorting.GetComponentsInChildren<Transform>(true).First(x => x.name == "SortingHatBone");
            AddPhysics(root, sorting, animator, sortingRoot, false);
            GameObject plain = hats.FirstOrDefault(x => x != null && x.GetComponentInChildren<MeshFilter>(true) != null);
            if (plain == null) throw new InvalidOperationException("AsHat mesh was not found.");
            Transform generatedRoot = RigAsHat(root, plain);
            AddPhysics(root, plain, animator, generatedRoot, false);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            EditorApplication.delayCall += UpgradeDefaultBrim;
            Debug.Log("[LobbyHat] Installed default/SortingHat soft bones and a lobby-only AsHat brim rig. Battle assets unchanged.");
        }
        catch (Exception e) { Debug.LogException(e); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void UpgradeDefaultBrim()
    {
        const string path = "Assets/Script/LobbyDefaultHatRig.asset";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var oldHelper = asset.GetComponentsInChildren<LobbyHatSoftBone>(true).FirstOrDefault(x => x.hatRenderer != null && x.hatRenderer.name == "StandardHat");
        if (oldHelper == null || !(oldHelper.hatRenderer is SkinnedMeshRenderer oldRenderer) ||
            AssetDatabase.GetAssetPath(oldRenderer.sharedMesh) == path) return;
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var helper = root.GetComponentsInChildren<LobbyHatSoftBone>(true).First(x => x.hatRenderer != null && x.hatRenderer.name == "StandardHat");
            var renderer = (SkinnedMeshRenderer)helper.hatRenderer;
            var mesh = CopyMesh(renderer.sharedMesh);
            var baked = new Mesh(); renderer.BakeMesh(baked);
            mesh.vertices = baked.vertices; mesh.normals = baked.normals;
            if (baked.tangents.Length == baked.vertexCount) mesh.tangents = baked.tangents;
            UnityEngine.Object.DestroyImmediate(baked);
            mesh.RecalculateBounds(); mesh.name = "LobbyDefaultHatRig";
            Vector3 up = renderer.transform.InverseTransformDirection(root.transform.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
            if (right.sqrMagnitude < .1f) right = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
            Vector3 forward = Vector3.Cross(right, up).normalized;
            // The original brim joint pivots identify the crown base, independently
            // of ribbons and the crooked tip extending the mesh bounds.
            var originalBrim = helper.GetComponent<LobbyHatShapeLimit>().bones
                .Where(x => x != null && x.name.StartsWith("Hat_Brim_") && x.childCount > 0).ToArray();
            Vector3 center = Vector3.zero;
            foreach (var b in originalBrim) center += renderer.transform.InverseTransformPoint(b.position);
            if (originalBrim.Length == 0) throw new InvalidOperationException("Default brim pivots missing.");
            center /= originalBrim.Length;
            Vector3[] vertices = mesh.vertices;
            float radius = 0f;
            foreach (var v in vertices) radius = Mathf.Max(radius, Vector3.ProjectOnPlane(v - center, up).magnitude);
            var rig = new GameObject("LobbyDefaultHatRoot").transform;
            rig.SetParent(renderer.transform, false);
            const int sectors = 24;
            var skinBones = new Transform[sectors + 1]; skinBones[0] = rig;
            float inner = radius * .38f;
            for (int i = 0; i < sectors; i++)
            {
                float angle = i * Mathf.PI * 2f / sectors;
                Vector3 radial = right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
                var bone = new GameObject("LobbyBrim_" + i).transform; bone.SetParent(rig, false);
                bone.localPosition = center + radial * inner;
                var tip = new GameObject("LobbyBrimEnd_" + i).transform; tip.SetParent(bone, false);
                tip.localPosition = radial * (radius - inner);
                skinBones[i + 1] = bone;
            }
            var weights = new BoneWeight[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 delta = vertices[i] - center;
                float radial = Vector3.ProjectOnPlane(delta, up).magnitude;
                float h = Vector3.Dot(delta, up);
                float influence = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(inner * .9f, inner * 1.25f, radial)) *
                    (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(radius * .12f, radius * .35f, h)));
                float sector = Mathf.Repeat(Mathf.Atan2(Vector3.Dot(delta, forward), Vector3.Dot(delta, right)) / (2 * Mathf.PI), 1) * sectors;
                int a = Mathf.FloorToInt(sector) % sectors; float t = sector - Mathf.Floor(sector);
                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 - influence,
                    boneIndex1 = a + 1, weight1 = influence * (1 - t), boneIndex2 = (a + 1) % sectors + 1, weight2 = influence * t };
            }
            mesh.boneWeights = weights;
            mesh.bindposes = skinBones.Select(x => x.worldToLocalMatrix * renderer.transform.localToWorldMatrix).ToArray();
            // Reject a bad bind pose before replacing the prefab's renderer.
            Matrix4x4[] bind = mesh.bindposes;
            for (int i = 0; i < vertices.Length; i++)
            {
                BoneWeight w = weights[i]; Vector3 v = vertices[i];
                Vector3 actual = skinBones[w.boneIndex0].TransformPoint(bind[w.boneIndex0].MultiplyPoint3x4(v)) * w.weight0 +
                    skinBones[w.boneIndex1].TransformPoint(bind[w.boneIndex1].MultiplyPoint3x4(v)) * w.weight1 +
                    skinBones[w.boneIndex2].TransformPoint(bind[w.boneIndex2].MultiplyPoint3x4(v)) * w.weight2;
                if ((actual - renderer.transform.TransformPoint(v)).sqrMagnitude > .000001f)
                    throw new InvalidOperationException("Lobby hat bind pose does not preserve the original shape.");
            }
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
            renderer.sharedMesh = mesh; renderer.bones = skinBones; renderer.rootBone = rig; renderer.localBounds = mesh.bounds;
            var so = new SerializedObject(helper.solver);
            var roots = so.FindProperty("m_RootBones"); roots.arraySize = 1; roots.GetArrayElementAtIndex(0).objectReferenceValue = rig;
            so.ApplyModifiedPropertiesWithoutUndo();
            var limit = helper.GetComponent<LobbyHatShapeLimit>();
            limit.bones = rig.GetComponentsInChildren<Transform>(true).Where(x => x != rig).ToArray();
            limit.contactBend = 70f;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); AssetDatabase.SaveAssets();
            Debug.Log("[LobbyHat] Default brim upgraded to 24 weighted sectors (lobby-only mesh copy).");
        }
        catch (Exception e) { Debug.LogException(e); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void AddPhysics(GameObject player, GameObject hat, Animator animator, Transform boneRoot, bool soft)
    {
        var host = new GameObject("LobbyHatPhysics"); host.layer = 2;
        host.transform.SetParent(hat.transform, false);
        var solver = host.AddComponent<EZSoftBone>();
        // Configure the serialized lists before the first runtime Awake.
        var data = new SerializedObject(solver);
        var roots = data.FindProperty("m_RootBones"); roots.arraySize = 1; roots.GetArrayElementAtIndex(0).objectReferenceValue = boneRoot;
        data.FindProperty("m_EndBones").arraySize = 0;
        data.ApplyModifiedPropertiesWithoutUndo();
        solver.startDepth = 0;
        solver.iterations = 2;
        solver.radius = .012f;
        solver.collisionLayers = 4;
        solver.simulateSpace = player.transform;
        solver.siblingConstraints = EZSoftBone.UnificationMode.None;
        solver.sleepThreshold = .00001f;
        string materialPath = soft ? "Assets/Script/LobbyHatSoft.asset" : "Assets/Script/LobbyHatFirm.asset";
        var material = AssetDatabase.LoadAssetAtPath<EZSoftBoneMaterial>(materialPath);
        if (material == null)
        {
            material = ScriptableObject.CreateInstance<EZSoftBoneMaterial>();
            material.damping = .65f; material.stiffness = soft ? .55f : .8f;
            material.resistance = .95f; material.slackness = .01f;
            AssetDatabase.CreateAsset(material, materialPath);
        }
        solver.sharedMaterial = material;
        solver.enabled = false;
        var helper = host.AddComponent<LobbyHatSoftBone>();
        helper.solver = solver; helper.bodyAnimator = animator; helper.motionRoot = player.transform;
        helper.hatRenderer = hat.GetComponentInChildren<Renderer>(true); helper.softMovement = soft;
        var limit = host.AddComponent<LobbyHatShapeLimit>();
        limit.solver = solver;
        limit.bones = boneRoot.GetComponentsInChildren<Transform>(true).Where(x => x != boneRoot).ToArray();
        limit.maxBend = soft ? 12f : 8f; limit.maxDisplacement = .008f;
    }

    private static Transform RigAsHat(GameObject player, GameObject hat)
    {
        MeshFilter filter = hat.GetComponentInChildren<MeshFilter>(true);
        MeshRenderer old = filter.GetComponent<MeshRenderer>();
        Mesh mesh = CopyMesh(filter.sharedMesh);
        mesh.name = "LobbyAsHatRig";
        Vector3[] vertices = mesh.vertices;
        Vector3 up = filter.transform.InverseTransformDirection(player.transform.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
        if (right.sqrMagnitude < .1f) right = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
        Vector3 forward = Vector3.Cross(right, up).normalized;
        Vector3 center = mesh.bounds.center;
        float low = float.PositiveInfinity, high = float.NegativeInfinity, radius = 0f;
        foreach (Vector3 v in vertices)
        {
            float h = Vector3.Dot(v - center, up); low = Mathf.Min(low, h); high = Mathf.Max(high, h);
            radius = Mathf.Max(radius, Vector3.ProjectOnPlane(v - center, up).magnitude);
        }
        const int sectors = 12;
        Transform rig = new GameObject("AsHatBrimRoot").transform; rig.SetParent(filter.transform, false);
        var bones = new Transform[sectors + 1]; bones[0] = rig;
        float brimHeight = low + (high - low) * .16f;
        for (int i = 0; i < sectors; i++)
        {
            float a = i * Mathf.PI * 2f / sectors;
            Vector3 direction = right * Mathf.Cos(a) + forward * Mathf.Sin(a);
            Transform bone = new GameObject("AsHatBrim_" + i).transform; bone.SetParent(rig, false);
            bone.localPosition = center + up * brimHeight + direction * radius * .3f;
            Transform end = new GameObject("AsHatBrimEnd_" + i).transform; end.SetParent(bone, false);
            end.localPosition = direction * radius * .7f;
            bones[i + 1] = bone;
        }
        var weights = new BoneWeight[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i] - center;
            float radial = Vector3.ProjectOnPlane(v, up).magnitude / Mathf.Max(radius, .0001f);
            float h = (Vector3.Dot(v, up) - low) / Mathf.Max(high - low, .0001f);
            float influence = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.3f, .85f, radial)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.25f, .5f, h)));
            float sector = Mathf.Repeat(Mathf.Atan2(Vector3.Dot(v, forward), Vector3.Dot(v, right)) / (Mathf.PI * 2f), 1f) * sectors;
            int a = Mathf.FloorToInt(sector) % sectors; float t = sector - Mathf.Floor(sector);
            weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f - influence,
                boneIndex1 = a + 1, weight1 = influence * (1f - t), boneIndex2 = (a + 1) % sectors + 1, weight2 = influence * t };
        }
        mesh.boneWeights = weights;
        mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * filter.transform.localToWorldMatrix).ToArray();
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (existing == null) AssetDatabase.CreateAsset(mesh, MeshPath);
        else { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
        Material[] materials = old.sharedMaterials; ShadowCastingMode shadows = old.shadowCastingMode; bool receive = old.receiveShadows;
        GameObject target = filter.gameObject;
        UnityEngine.Object.DestroyImmediate(old); UnityEngine.Object.DestroyImmediate(filter);
        var renderer = target.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = mesh; renderer.sharedMaterials = materials; renderer.bones = bones; renderer.rootBone = rig;
        renderer.localBounds = mesh.bounds; renderer.shadowCastingMode = shadows; renderer.receiveShadows = receive;
        return rig;
    }

    private static Mesh CopyMesh(Mesh source)
    {
        // Editor-only read also supports model meshes with Read/Write disabled.
        using var data = MeshUtility.AcquireReadOnlyMeshData(source);
        var d = data[0]; var mesh = new Mesh { indexFormat = source.indexFormat };
        using (var v = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp)) { d.GetVertices(v); mesh.SetVertices(v); }
        if (d.HasVertexAttribute(VertexAttribute.Normal))
            using (var n = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp)) { d.GetNormals(n); mesh.SetNormals(n); }
        if (d.HasVertexAttribute(VertexAttribute.Tangent))
            using (var t = new NativeArray<Vector4>(d.vertexCount, Allocator.Temp)) { d.GetTangents(t); mesh.SetTangents(t); }
        for (int channel = 0; channel < 8; channel++)
            if (d.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel)))
                using (var uv = new NativeArray<Vector4>(d.vertexCount, Allocator.Temp)) { d.GetUVs(channel, uv); mesh.SetUVs(channel, uv); }
        if (d.HasVertexAttribute(VertexAttribute.Color))
            using (var c = new NativeArray<Color>(d.vertexCount, Allocator.Temp)) { d.GetColors(c); mesh.SetColors(c); }
        mesh.subMeshCount = d.subMeshCount;
        for (int i = 0; i < d.subMeshCount; i++)
        {
            var sub = d.GetSubMesh(i);
            using var indices = new NativeArray<int>(sub.indexCount, Allocator.Temp);
            d.GetIndices(indices, i); mesh.SetIndices(indices.ToArray(), sub.topology, i);
        }
        mesh.RecalculateBounds(); return mesh;
    }
}

