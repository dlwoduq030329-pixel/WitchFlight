using System;
using UnityEngine;
using UnityEngine.Rendering;

// Editor-baked geometry only. Visible renderers, colliders and prefab instances stay intact.
public sealed class ModularShadowBake : ScriptableObject
{
    [Serializable] public sealed class Source
    {
        public string path;
        public string objectName;
        public Mesh mesh;
        public Matrix4x4 matrix;
    }

    [Serializable] public sealed class Batch
    {
        public Mesh mesh;
        public Material[] materials;
        public int layer;
        public uint renderingLayerMask;
        public Source[] sources;
    }

    public float cellSize = 20f;
    public Batch[] batches = Array.Empty<Batch>();

    public static bool SameMatrix(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > .0002f) return false;
        return true;
    }
}
