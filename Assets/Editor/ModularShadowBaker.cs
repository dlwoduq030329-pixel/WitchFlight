using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class ModularShadowBaker
{
    [MenuItem("Tools/WitchFlight/Shadows/Bake selected map")]
    private static void BakeSelected()
    {
        if (Application.isPlaying || Selection.activeTransform == null)
        { Debug.LogWarning("Select the map root outside Play mode first."); return; }
        string path = EditorUtility.SaveFilePanelInProject("Bake map shadows", "MapShadows", "asset", "Save shadow bake");
        if (string.IsNullOrEmpty(path)) return;
        Transform root = Selection.activeTransform;
        ModularShadowBake result = Bake(root, path);
        var controller = root.GetComponent<ModularShadowProxy>();
        if (controller == null) controller = Undo.AddComponent<ModularShadowProxy>(root.gameObject);
        Undo.RecordObject(controller, "Assign shadow bake");
        controller.Bake = result;
        EditorUtility.SetDirty(controller);
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Debug.Log($"Shadow bake: {result.batches.Sum(b => b.sources.Length)} wall/roof renderers -> {result.batches.Length} shadow chunks. Original visible meshes and colliders retained.", root);
    }

    private static string PathOf(Transform root, Transform child)
    {
        var indices = new List<int>();
        while (child != root) { indices.Add(child.GetSiblingIndex()); child = child.parent; }
        indices.Reverse();
        return string.Join("/", indices);
    }

    public static ModularShadowBake Bake(Transform root, string assetPath, float cellSize = 20f)
    {
        if (cellSize < 1f) throw new ArgumentOutOfRangeException(nameof(cellSize));
        var groups = new Dictionary<string, List<MeshRenderer>>();
        Matrix4x4 inverse = root.worldToLocalMatrix;
        foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var f = r.GetComponent<MeshFilter>();
            if (!r.enabled || !r.gameObject.activeInHierarchy || f == null || f.sharedMesh == null ||
                r.shadowCastingMode != ShadowCastingMode.On || r.forceRenderingOff || r.HasPropertyBlock() ||
                r.GetComponentInParent<LODGroup>() != null || r.GetComponentInParent<Animator>() != null ||
                r.GetComponentInParent<Rigidbody>() != null) continue;
            string meshPath = AssetDatabase.GetAssetPath(f.sharedMesh).Replace('\\', '/');
            // Limit this first pass to the small, opaque, static modular walls and roofs.
            if (!meshPath.StartsWith("Assets/Map Source/Walls/") && !meshPath.StartsWith("Assets/Map Source/Roofs/")) continue;
            Material[] mats = r.sharedMaterials;
            if (mats.Length != f.sharedMesh.subMeshCount || mats.Any(m => m == null || m.renderQueue > 2500 ||
                (m.HasProperty("_ClippingMode") && m.GetFloat("_ClippingMode") != 0) ||
                (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") != 0))) continue;
            Matrix4x4 matrix = inverse * r.localToWorldMatrix;
            if (matrix.determinant <= 0) continue; // Preserve mirrored geometry's existing culling.
            Vector3 p = inverse.MultiplyPoint3x4(r.bounds.center);
            string path = PathOf(root, r.transform);
            string key = path.Split('/')[0] + ":" + Mathf.FloorToInt(p.x / cellSize) + ":" +
                Mathf.FloorToInt(p.y / cellSize) + ":" + Mathf.FloorToInt(p.z / cellSize) + ":" +
                r.gameObject.layer + ":" + r.renderingLayerMask + ":" + string.Join(",", mats.Select(m => m.GetInstanceID()));
            if (!groups.TryGetValue(key, out var group)) groups.Add(key, group = new List<MeshRenderer>());
            group.Add(r);
        }
        var readable = new Dictionary<Mesh, Mesh>();
        var chunks = new List<ModularShadowBake.Batch>();
        try
        {
            foreach (var group in groups.Values)
            {
                if (group.Count < 4) continue;
                Material[] materials = group[0].sharedMaterials;
                var submeshes = new List<Mesh>();
                try
                {
                    for (int slot = 0; slot < materials.Length; slot++)
                    {
                        var instances = new CombineInstance[group.Count];
                        for (int i = 0; i < group.Count; i++)
                        {
                            Mesh original = group[i].GetComponent<MeshFilter>().sharedMesh;
                            if (!readable.TryGetValue(original, out Mesh copy)) readable.Add(original, copy = ReadMesh(original));
                            instances[i] = new CombineInstance { mesh = copy, subMeshIndex = slot,
                                transform = inverse * group[i].localToWorldMatrix };
                        }
                        var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                        part.CombineMeshes(instances, true, true, false);
                        submeshes.Add(part);
                    }
                    var combined = new Mesh { name = "ShadowChunk_" + chunks.Count.ToString("D3"), indexFormat = IndexFormat.UInt32 };
                    combined.CombineMeshes(submeshes.Select(m => new CombineInstance { mesh = m, transform = Matrix4x4.identity }).ToArray(), false, false);
                    combined.RecalculateBounds();
                    chunks.Add(new ModularShadowBake.Batch { mesh = combined, materials = materials,
                        layer = group[0].gameObject.layer, renderingLayerMask = group[0].renderingLayerMask,
                        sources = group.Select(r => new ModularShadowBake.Source { path = PathOf(root, r.transform),
                            objectName = r.name, mesh = r.GetComponent<MeshFilter>().sharedMesh,
                            matrix = inverse * r.localToWorldMatrix }).ToArray() });
                }
                finally { foreach (Mesh m in submeshes) UnityEngine.Object.DestroyImmediate(m); }
            }
            var result = AssetDatabase.LoadAssetAtPath<ModularShadowBake>(assetPath);
            if (result == null)
            {
                result = ScriptableObject.CreateInstance<ModularShadowBake>();
                AssetDatabase.CreateAsset(result, assetPath);
            }
            else foreach (var old in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                if (old is Mesh) UnityEngine.Object.DestroyImmediate(old, true);
            result.cellSize = cellSize;
            result.batches = chunks.ToArray();
            foreach (var chunk in chunks) AssetDatabase.AddObjectToAsset(chunk.mesh, result);
            EditorUtility.SetDirty(result);
            AssetDatabase.SaveAssets();
            return result;
        }
        finally { foreach (var m in readable.Values) UnityEngine.Object.DestroyImmediate(m); }
    }

    // Editor-only API reads imported meshes without changing Read/Write on all FBX assets.
    private static Mesh ReadMesh(Mesh source)
    {
        using var access = MeshUtility.AcquireReadOnlyMeshData(source);
        var data = access[0];
        var mesh = new Mesh { name = source.name + " shadow bake copy", indexFormat = IndexFormat.UInt32 };
        using (var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
        { data.GetVertices(positions); mesh.SetVertices(positions); }
        if (data.HasVertexAttribute(VertexAttribute.Normal))
            using (var normals = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
            { data.GetNormals(normals); mesh.SetNormals(normals); }
        if (data.HasVertexAttribute(VertexAttribute.Tangent))
            using (var tangents = new NativeArray<Vector4>(data.vertexCount, Allocator.Temp))
            { data.GetTangents(tangents); mesh.SetTangents(tangents); }
        if (data.HasVertexAttribute(VertexAttribute.TexCoord0))
            using (var uv = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp))
            { data.GetUVs(0, uv); mesh.SetUVs(0, uv); }
        mesh.subMeshCount = data.subMeshCount;
        for (int i = 0; i < data.subMeshCount; i++)
            using (var indices = new NativeArray<int>(data.GetSubMesh(i).indexCount, Allocator.Temp))
            { data.GetIndices(indices, i); mesh.SetIndices(indices.ToArray(), data.GetSubMesh(i).topology, i, false); }
        mesh.RecalculateBounds();
        return mesh;
    }
}
