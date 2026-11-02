using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class BladeWeaponVisual : MonoBehaviour
{
    private static Mesh blade;
    private void Awake()
    {
        if (blade == null)
        {
            blade = new Mesh { name = "Violet staff blade" };
            blade.vertices = new[] { Vector3.zero, new Vector3(-.16f,0,1.05f),
                new Vector3(0,.045f,1.05f), new Vector3(.16f,0,1.05f),
                new Vector3(0,-.045f,1.05f), new Vector3(0,0,1.6f) };
            blade.uv = new[] { new Vector2(.5f,0), new Vector2(.4f,0), new Vector2(.5f,.2f),
                new Vector2(.6f,0), new Vector2(.5f,.2f), new Vector2(.5f,0) };
            blade.triangles = new[] {0,1,2,0,2,3,0,3,4,0,4,1,5,2,1,5,3,2,5,4,3,5,1,4};
            blade.RecalculateNormals(); blade.RecalculateBounds(); blade.UploadMeshData(true);
        }
        GetComponent<MeshFilter>().sharedMesh = blade;
    }
}
