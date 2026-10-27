using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class WitchFlagSetup
{
    const string Folder="Assets/Art/WitchFlag/";
    [MenuItem("Tools/WitchFlight/Install and Verify Witch Flag %#F3")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play mode first.");
        var importer=(ModelImporter)AssetImporter.GetAtPath(Folder+"WitchFlag.fbx");
        importer.isReadable=true; importer.importCameras=false; importer.importLights=false;
        importer.materialImportMode=ModelImporterMaterialImportMode.None; importer.SaveAndReimport();
        var wood=MakeMaterial("Wood","WoodGrain.png"); var linen=MakeMaterial("WhiteWitchLinen","WitchLinen.png");
        var root=PrefabUtility.LoadPrefabContents("Assets/FlagOBJ.prefab");
        try
        {
            var flag=root.GetComponent<BattleFlag>(); var so=new SerializedObject(flag);
            var visual=(Transform)so.FindProperty("flagVisualRoot").objectReferenceValue;
            if(visual==null || visual==root.transform) throw new Exception("Missing model-only FlagVisual root.");
            foreach(Transform child in visual.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"WitchFlag.fbx"),visual);
            model.transform.localPosition=Vector3.zero; model.transform.localRotation=Quaternion.identity;
            model.transform.localScale=Vector3.one;
            MeshFilter cloth=null;
            foreach(var mr in model.GetComponentsInChildren<MeshRenderer>())
            {
                bool fabric=mr.name=="FlagCloth"; mr.sharedMaterial=fabric?linen:wood;
                if(fabric) cloth=mr.GetComponent<MeshFilter>();
            }
            if(cloth==null) throw new Exception("FBX cloth mesh missing.");
            var motion=model.AddComponent<FlagClothMotion>(); motion.cloth=cloth;
            motion.flutterClip=AssetDatabase.LoadAssetAtPath<AudioClip>(Folder+"FlagFlutter.wav");
            var source=model.AddComponent<AudioSource>(); source.playOnAwake=false;
            so.FindProperty("carriedVisualOffset").vector3Value=new Vector3(-.12f,-.3f,-.24f);
            so.FindProperty("carriedVisualEuler").vector3Value=new Vector3(0,-15,35);
            // Keep the user's carried scale and authoritative pickup collider as authored.
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/FlagOBJ.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); Verify();
    }
    static Material MakeMaterial(string name,string texture)
    {
        string path=Folder+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        if(name=="WhiteWitchLinen")m.shader=Shader.Find("WitchFlight/Flag Linen");
        m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+texture));
        if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",Color.white);
        if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.15f);
        if(m.HasProperty("_Cull"))m.SetFloat("_Cull",0);
        EditorUtility.SetDirty(m); return m;
    }
    static void Verify() => WitchFlightMotionValidation.Run();
}

