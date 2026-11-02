using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The entire face (rings, marks, Roman glyphs and rising light) shares one mesh.
public sealed class TimeClockVisual : MonoBehaviour
{
    [SerializeField] private Material emissionMaterial;
    private Transform hand;
    private static Mesh faceMesh, handMesh;
    private static readonly string[] numerals = { "XII", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI" };
    private void Awake()
    {
        if (faceMesh == null) faceMesh = BuildFace();
        var face = new GameObject("Emissive clock face", typeof(MeshFilter), typeof(MeshRenderer));
        face.transform.SetParent(transform, false);
        face.GetComponent<MeshFilter>().sharedMesh = faceMesh;
        var renderer = face.GetComponent<MeshRenderer>(); renderer.sharedMaterial = emissionMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        if (handMesh == null)
        {
            handMesh = new Mesh { name = "Clock second hand" };
            handMesh.vertices = new[] { new Vector3(-.003f,.001f,-.04f), new Vector3(.003f,.001f,-.04f),
                new Vector3(.003f,.001f,.67f), new Vector3(.012f,.001f,.67f), new Vector3(0,.001f,.74f),
                new Vector3(-.012f,.001f,.67f), new Vector3(-.003f,.001f,.67f) };
            handMesh.uv = new Vector2[7];
            handMesh.triangles = new[] {0,1,2,0,2,6,3,4,5};
            handMesh.RecalculateBounds(); handMesh.UploadMeshData(true);
        }
        var needle = new GameObject("Second hand", typeof(MeshFilter), typeof(MeshRenderer));
        needle.transform.SetParent(transform, false); hand = needle.transform;
        needle.GetComponent<MeshFilter>().sharedMesh = handMesh;
        var handRenderer = needle.GetComponent<MeshRenderer>(); handRenderer.sharedMaterial = emissionMaterial;
        handRenderer.shadowCastingMode = ShadowCastingMode.Off; handRenderer.receiveShadows = false;
    }
    private static Mesh BuildFace()
    {
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool rising)
        {
            int n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            uv.Add(new Vector2(rising ? 1 : 0, 0)); uv.Add(new Vector2(rising ? 1 : 0, 0));
            uv.Add(new Vector2(rising ? 1 : 0, rising ? 1 : 0)); uv.Add(new Vector2(rising ? 1 : 0, rising ? 1 : 0));
            triangles.Add(n); triangles.Add(n+1); triangles.Add(n+2); triangles.Add(n); triangles.Add(n+2); triangles.Add(n+3);
        }
        void Stroke(Vector3 a, Vector3 b, float width, bool rise = false)
        {
            Vector3 side = Vector3.Cross(Vector3.up, b-a).normalized * width * .5f;
            Quad(a-side, b-side, b+side, a+side, false);
            if (rise) Quad(a, b, b + Vector3.up*.06f, a + Vector3.up*.06f, true);
        }
        foreach (float radius in new[] { 1f, .74f, .06f })
            for (int i=0;i<96;i++)
            {
                float a=i*Mathf.PI*2/96, b=(i+1)*Mathf.PI*2/96;
                Stroke(new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*radius,
                    new Vector3(Mathf.Sin(b),0,Mathf.Cos(b))*radius,.0025f, radius==1f);
            }
        for (int i=0;i<60;i++)
        {
            float angle=i*Mathf.PI*2/60; Vector3 axis=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
            Stroke(axis*(i%5==0?.91f:.95f),axis*.985f,.0015f);
        }
        for (int i=0;i<12;i++)
        {
            float angle=i*Mathf.PI/6; Vector3 center=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*.84f;
            string text=numerals[i];
            for(int c=0;c<text.Length;c++)
            {
                Vector3 o=center+Vector3.right*((c-(text.Length-1)*.5f)*.042f);
                Vector3 tl=o+new Vector3(-.016f,0,.04f), tr=o+new Vector3(.016f,0,.04f);
                Vector3 bl=o+new Vector3(-.016f,0,-.04f), br=o+new Vector3(.016f,0,-.04f);
                if(text[c]=='I')
                {
                    Stroke(o+Vector3.forward*.04f,o-Vector3.forward*.04f,.003f,true);
                    Stroke(tl,tr,.002f,true); Stroke(bl,br,.002f,true);
                }
                else if(text[c]=='V') { Stroke(tl,o-Vector3.forward*.04f,.003f,true); Stroke(o-Vector3.forward*.04f,tr,.003f,true); }
                else { Stroke(tl,br,.003f,true); Stroke(tr,bl,.003f,true); }
            }
        }
        var mesh=new Mesh {name="Clock face XII-I-XI"}; mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
        mesh.RecalculateBounds(); mesh.UploadMeshData(true); return mesh;
    }
    public void Show(float radius, float progress)
    {
        transform.localScale=Vector3.one*radius;
        if(hand!=null) hand.localRotation=Quaternion.Euler(0,Mathf.Clamp01(progress)*360,0);
    }
}
