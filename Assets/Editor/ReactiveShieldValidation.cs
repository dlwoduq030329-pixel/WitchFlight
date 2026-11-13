using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class ReactiveShieldValidation
{
    [MenuItem("Tools/WitchFlight/Validate Reactive Shield")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before validation.");
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture texture = null;
        Texture2D pixels = null;
        try
        {
            for (int level = 0; level <= 3; level++)
            {
                Mesh mesh = ReactiveShieldVfx.BuildMesh(level,.045f,.015f);
                try
                {
                    int cells = 10*(1<<(2*level))+2;
                    if (mesh.vertexCount != (cells*4-12)*24) throw new Exception("Incorrect fragment topology at level "+level);
                    Vector3[] vertices=mesh.vertices, normals=mesh.normals; int[] indices=mesh.triangles;
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        int a=indices[i],b=indices[i+1],c=indices[i+2];
                        Vector3 cross=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
                        if(cross.sqrMagnitude<1e-12f) throw new Exception("Degenerate triangle");
                        if(Vector3.Dot(cross,normals[a])<=0f) throw new Exception("Inverted triangle winding");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
            }
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VFX/ReactiveShield/ReactiveShield.prefab");
            if(prefab==null) throw new Exception("Shield prefab failed to load");
            var shieldObject=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            var shield=shieldObject.GetComponent<ReactiveShieldVfx>();shield.Rebuild();
            var renderer=shieldObject.GetComponent<MeshRenderer>();
            Shader shader=renderer.sharedMaterial.shader;
            var cameraObject=new GameObject("Validation camera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-8);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.012f,.021f,.04f);
            camera.fieldOfView=42;camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.allowHDR=true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            texture=new RenderTexture(960,720,24,RenderTextureFormat.ARGB32);texture.Create();camera.targetTexture=texture;
            pixels=new Texture2D(960,720,TextureFormat.RGB24,false);
            Directory.CreateDirectory("Temp/ReactiveShieldValidation");
            void Capture(string name)
            {
                camera.Render();
                if(ShaderUtil.ShaderHasError(shader)) throw new Exception("Reactive Shield shader compilation failed");
                RenderTexture previous=RenderTexture.active;
                try { RenderTexture.active=texture; pixels.ReadPixels(new Rect(0,0,960,720),0,0);pixels.Apply(); }
                finally { RenderTexture.active=previous; }
                File.WriteAllBytes("Temp/ReactiveShieldValidation/"+name+".png",pixels.EncodeToPNG());
            }
            Capture("idle");
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
            var hits=new Vector4[8];var timings=new Vector4[8];
            hits[0]=new Vector4(.25f,.15f,-.956f,1.5f);timings[0]=new Vector4(Time.time-.22f,.6f,1.15f,1f);
            block.SetVectorArray("_ShieldHits",hits);block.SetVectorArray("_ShieldTimings",timings);renderer.SetPropertyBlock(block);
            Capture("impact");
            File.WriteAllText("Temp/ReactiveShieldValidation/result.txt","PASS: geometry levels 0-3, prefab and GPU shader rendering.");
            Debug.Log("Reactive Shield validation passed. Captures: Temp/ReactiveShieldValidation");
        }
        finally
        {
            if(texture!=null){texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
            if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
