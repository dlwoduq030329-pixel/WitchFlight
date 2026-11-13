using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Independent visual shield. ReportHit changes presentation only, never damage or collision.</summary>
[ExecuteAlways, DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class ReactiveShieldVfx : MonoBehaviour
{
    [Header("Shape (local units)")]
    [SerializeField] private Material shieldMaterial;
    [SerializeField, Range(0, 3)] private int subdivisions = 2;
    [SerializeField, Min(.1f)] private float radius = 2f;
    [SerializeField, Range(0f, .3f)] private float cellGap = .045f;
    [SerializeField, Range(0f, .1f)] private float thickness = .015f;
    [Header("Energy")]
    [SerializeField, ColorUsage(true, true)] private Color energyColor = new Color(.12f, 1.5f, 2.5f, 1f);
    [SerializeField, ColorUsage(true, true)] private Color impactColor = new Color(4f, .28f, .08f, 1f);
    [SerializeField, Range(0f, 1f)] private float surfaceOpacity = .08f;
    [SerializeField, Range(.002f, .15f)] private float edgeWidth = .025f;
    [SerializeField, Range(0f, 2f)] private float ambientEnergy = .35f;
    [Header("Hit and reconstruction")]
    [SerializeField, Min(.05f)] private float hitRadius = .9f;
    [SerializeField, Min(.1f)] private float recoverySeconds = 1.15f;
    [SerializeField, Range(0f, 1f)] private float outwardDisplacement = .38f;
    [SerializeField, Range(0f, 90f)] private float fragmentRotation = 24f;

    private const int MaxImpacts = 8;
    private static readonly int HitsId = Shader.PropertyToID("_ShieldHits");
    private static readonly int TimingsId = Shader.PropertyToID("_ShieldTimings");
    private static readonly int RadiusId = Shader.PropertyToID("_ShieldRadius");
    private static readonly int EnergyId = Shader.PropertyToID("_EnergyColor");
    private static readonly int ImpactId = Shader.PropertyToID("_ImpactColor");
    private static readonly int OpacityId = Shader.PropertyToID("_SurfaceOpacity");
    private static readonly int EdgeId = Shader.PropertyToID("_EdgeWidth");
    private static readonly int AmbientId = Shader.PropertyToID("_AmbientEnergy");
    private static readonly int DisplacementId = Shader.PropertyToID("_Displacement");
    private static readonly int RotationId = Shader.PropertyToID("_FragmentRotation");
    private readonly Vector4[] hits = new Vector4[MaxImpacts];
    private readonly Vector4[] timings = new Vector4[MaxImpacts];
    private MaterialPropertyBlock properties;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh generatedMesh;
    private int nextImpact;
    public float Radius => radius;
    public int CellCount => 10 * (1 << (2 * Mathf.Clamp(subdivisions, 0, 3))) + 2;

    private void OnEnable()
    {
        if (generatedMesh == null) Rebuild();
        ClearHits();
    }

    // No Update, physics polling, fragment GameObjects or runtime material copies.
    public void ReportHit(Vector3 worldPosition, float strength = 1f)
    {
        if (!isActiveAndEnabled || !Application.isPlaying || generatedMesh == null) return;
        if (!float.IsFinite(strength) || strength <= 0f || !Finite(worldPosition)) return;
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        if (!Finite(local) || local.sqrMagnitude < .000001f) return;
        local = local.normalized; // Pin the impact to the moving shield's surface.
        hits[nextImpact] = new Vector4(local.x, local.y, local.z, Mathf.Clamp(strength, 0f, 2f));
        timings[nextImpact] = new Vector4(Time.time, hitRadius / radius, recoverySeconds, 1f);
        nextImpact = (nextImpact + 1) % MaxImpacts;
        UploadProperties();
    }

    public void ClearHits()
    {
        nextImpact = 0;
        for (int i = 0; i < MaxImpacts; i++) { hits[i] = Vector4.zero; timings[i] = Vector4.zero; }
        UploadProperties();
    }

    [ContextMenu("Rebuild shield / Apply settings")]
    public void Rebuild()
    {
        radius = Mathf.Max(.1f, radius);
        hitRadius = Mathf.Max(.05f, hitRadius);
        recoverySeconds = Mathf.Max(.1f, recoverySeconds);
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        ReleaseMesh();
        generatedMesh = BuildMesh(Mathf.Clamp(subdivisions, 0, 3), Mathf.Clamp01(cellGap), thickness);
        generatedMesh.hideFlags = HideFlags.HideAndDontSave;
        // Vertex displacement occurs on the GPU, so supply conservative CPU culling bounds.
        float extent = radius * (1f + thickness) + outwardDisplacement * 4f + radius;
        generatedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * (extent * 2f));
        meshFilter.sharedMesh = generatedMesh;
        if (shieldMaterial != null) meshRenderer.sharedMaterial = shieldMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        ClearHits();
    }

    private void UploadProperties()
    {
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (properties == null) properties = new MaterialPropertyBlock();
        properties.SetVectorArray(HitsId, hits);
        properties.SetVectorArray(TimingsId, timings);
        properties.SetFloat(RadiusId, radius);
        properties.SetColor(EnergyId, energyColor);
        properties.SetColor(ImpactId, impactColor);
        properties.SetFloat(OpacityId, surfaceOpacity);
        properties.SetFloat(EdgeId, edgeWidth);
        properties.SetFloat(AmbientId, ambientEnergy);
        properties.SetFloat(DisplacementId, outwardDisplacement);
        properties.SetFloat(RotationId, fragmentRotation * Mathf.Deg2Rad);
        meshRenderer.SetPropertyBlock(properties);
    }

    private void OnDestroy() => ReleaseMesh();
    private void ReleaseMesh()
    {
        if (generatedMesh == null) return;
        if (meshFilter != null && meshFilter.sharedMesh == generatedMesh) meshFilter.sharedMesh = null;
        if (Application.isPlaying) Destroy(generatedMesh); else DestroyImmediate(generatedMesh);
        generatedMesh = null;
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    // Dual of a subdivided icosahedron: twelve pentagons, remaining cells hexagonal.
    public static Mesh BuildMesh(int level, float gap, float depth)
    {
        level = Mathf.Clamp(level, 0, 3);
        gap = Mathf.Clamp(gap, 0f, .3f);
        depth = Mathf.Clamp(depth, 0f, .1f);
        float t = (1f + Mathf.Sqrt(5f)) * .5f;
        var points = new List<Vector3> {
            new(-1,t,0),new(1,t,0),new(-1,-t,0),new(1,-t,0),new(0,-1,t),new(0,1,t),
            new(0,-1,-t),new(0,1,-t),new(t,0,-1),new(t,0,1),new(-t,0,-1),new(-t,0,1) };
        for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized;
        var faces = new List<int> {0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,
            3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
        for (int l = 0; l < level; l++)
        {
            var midpoint = new Dictionary<ulong, int>();
            var refined = new List<int>(faces.Count * 4);
            int Mid(int a, int b)
            {
                ulong key = ((ulong)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                if (midpoint.TryGetValue(key, out int found)) return found;
                int index = points.Count; points.Add((points[a] + points[b]).normalized);
                midpoint.Add(key, index); return index;
            }
            for (int i = 0; i < faces.Count; i += 3)
            {
                int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                int ab = Mid(a,b), bc = Mid(b,c), ca = Mid(c,a);
                refined.AddRange(new[] {a,ab,ca,b,bc,ab,c,ca,bc,ab,bc,ca});
            }
            faces = refined;
        }
        var rings = new List<Vector3>[points.Count];
        for (int i = 0; i < rings.Length; i++) rings[i] = new List<Vector3>(6);
        for (int i = 0; i < faces.Count; i += 3)
        {
            Vector3 center = (points[faces[i]] + points[faces[i+1]] + points[faces[i+2]]).normalized;
            for (int j = 0; j < 3; j++) rings[faces[i+j]].Add(center);
        }
        var positions = new List<Vector3>(); var normals = new List<Vector3>();
        var barycentrics = new List<Vector3>(); var centers = new List<Vector4>();
        var shards = new List<Vector4>(); var edges = new List<Vector4>(); var indices = new List<int>();
        for (int cell = 0; cell < points.Count; cell++)
        {
            Vector3 normal = points[cell];
            Vector3 tangent = Vector3.Cross(Mathf.Abs(normal.y) < .9f ? Vector3.up : Vector3.right, normal).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            List<Vector3> ring = rings[cell];
            ring.Sort((a,b) => Mathf.Atan2(Vector3.Dot(a,bitangent),Vector3.Dot(a,tangent))
                .CompareTo(Mathf.Atan2(Vector3.Dot(b,bitangent),Vector3.Dot(b,tangent))));
            for (int i = 0; i < ring.Count; i++) ring[i] = Vector3.Lerp(ring[i], normal, gap);
            for (int i = 1; i < ring.Count - 1; i++)
            {
                Vector3 a = ring[0], b = ring[i], c = ring[i+1], shardCenter = (a+b+c)/3f;
                Vector4 cellData = new Vector4(normal.x,normal.y,normal.z,cell * .6180339f);
                Vector4 shardData = new Vector4(shardCenter.x,shardCenter.y,shardCenter.z,(cell*7+i)*.754877f);
                Vector4 outerEdges = new Vector4(1f,i == ring.Count-2 ? 1f : 0f,i == 1 ? 1f : 0f,0f);
                void Tri(Vector3 x,Vector3 y,Vector3 z,Vector3 n,Vector4 flags)
                {
                    int start = positions.Count;
                    positions.Add(x);positions.Add(y);positions.Add(z);
                    barycentrics.Add(Vector3.right);barycentrics.Add(Vector3.up);barycentrics.Add(Vector3.forward);
                    for(int k=0;k<3;k++){normals.Add(n);centers.Add(cellData);shards.Add(shardData);edges.Add(flags);indices.Add(start+k);}
                }
                Tri(a,b,c,normal,outerEdges);
                if (depth <= 0f) continue;
                Vector3 back = normal * depth;
                Tri(c-back,b-back,a-back,-normal,new Vector4(outerEdges.z,outerEdges.y,outerEdges.x,0));
                void Side(Vector3 x, Vector3 y, bool outer)
                {
                    Vector3 n = Vector3.Cross(y-x,-normal).normalized;
                    var flags = new Vector4(0,0,0,outer ? 1f : .2f);
                    Tri(x,y-back,x-back,n,flags);Tri(x,y,y-back,n,flags);
                }
                Side(a,b,i==1);Side(b,c,true);Side(c,a,i==ring.Count-2);
            }
        }
        var mesh = new Mesh {name="Reactive Shield Cells", indexFormat=positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16};
        mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetUVs(0,barycentrics);
        mesh.SetUVs(1,centers);mesh.SetUVs(2,shards);mesh.SetUVs(3,edges);mesh.SetTriangles(indices,0);
        mesh.RecalculateBounds();return mesh;
    }
}
