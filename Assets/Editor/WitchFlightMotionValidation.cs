using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class WitchFlightMotionValidation
{
    const string Dir="Temp/WitchFlightMotionVerification";
    [MenuItem("Tools/WitchFlight/Verify Back Flag and Summon Flight %#F2")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play mode first.");
        Directory.CreateDirectory(Dir);var report=new StringBuilder();
        try
        {
            var prefab=PrefabUtility.LoadPrefabContents("Assets/FlagOBJ.prefab");
            try
            {
                var so=new SerializedObject(prefab.GetComponent<BattleFlag>());
                so.FindProperty("carriedVisualOffset").vector3Value=new Vector3(-.12f,-.3f,-.24f);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(prefab,"Assets/FlagOBJ.prefab");
            }
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
            VerifySummon(report);
            VerifyFlag(report);
            report.AppendLine("PASS: all motion checks.");
        }
        catch(Exception e){report.AppendLine("FAIL: "+e);Debug.LogException(e);}
        File.WriteAllText(Dir+"/report.txt",report.ToString());Debug.Log(report.ToString());
    }
    static void Require(bool ok,string text){if(!ok)throw new Exception(text);}
    static void VerifySummon(StringBuilder report)
    {
        foreach(int fps in new[]{30,60,120})
        {
            float dt=1f/fps;
            foreach(Vector3 ownerVelocity in new[]{Vector3.zero,Vector3.forward*60,Vector3.back*20,new Vector3(8,10,-30)})
            {
                var flight=new SummonFlightMotion();flight.Initialize(ownerVelocity,Vector3.forward,28,120,18,60,80);
                Vector3 heading=flight.Heading,pos=Vector3.zero,target=new Vector3(-30,15,-50);
                float launchSpeed=flight.Speed;
                Require(launchSpeed>ownerVelocity.magnitude,"Launch slower than caster");
                for(int i=0;i<fps;i++)pos+=flight.Step(target-pos,true,4,dt);
                Require((pos-heading*launchSpeed).magnitude<.01f,"Launch leg must last exactly one second");
                Require(Vector3.Angle(heading,flight.Heading)<.01f,"Launch steered prematurely");
                Vector3 before=flight.Heading;
                pos+=flight.Step(target-pos,true,4,dt);
                Require(Vector3.Angle(before,flight.Heading)<=120*dt+.01f,"Abrupt pursuit turn");
                for(int i=0;i<fps*12;i++)pos+=flight.Step(target-pos,true,4,dt);
                Require(Vector3.Distance(pos,target)<6,"Pursuit did not settle near target: "+Vector3.Distance(pos,target));
                for(int i=0;i<fps;i++)pos+=flight.Step(Vector3.zero,false,4,dt);
                Require(flight.Speed<.001f,"Target loss must brake");
            }
            Quaternion q=Quaternion.identity;
            var first=SummonFlightMotion.SmoothLook(q,Vector3.back,dt);
            Require(Quaternion.Angle(q,first)>0 && Quaternion.Angle(first,Quaternion.LookRotation(Vector3.back))>90,"Look snapped");
            for(int i=0;i<fps*4;i++)q=SummonFlightMotion.SmoothLook(q,Vector3.back,dt);
            Require(Quaternion.Angle(q,Quaternion.LookRotation(Vector3.back))<1.5f,"Look convergence failed");
            report.AppendLine("PASS: summon "+fps+" Hz: stationary/forward/reverse/vertical launch, 1-second leg, curved pursuit, braking, smooth gaze.");
        }
        Require(Mathf.Approximately(SummonFlightMotion.VisualScale,2f),"Summon scale");
    }
    static void VerifyFlag(StringBuilder report)
    {
        var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FlagOBJ.prefab"));
        GameObject character=null;
        try
        {
            var so=new SerializedObject(root.GetComponent<BattleFlag>());
            var visual=(Transform)so.FindProperty("flagVisualRoot").objectReferenceValue;
            visual.localPosition=Vector3.zero;visual.localRotation=Quaternion.Euler(0,-15,35);
            float heldScale=so.FindProperty("carriedVisualScale").floatValue;
            visual.localScale*=heldScale;
            var motion=root.GetComponentInChildren<FlagClothMotion>();
            Vector3[] rest=motion.cloth.sharedMesh.vertices;Vector2[] uv=motion.cloth.sharedMesh.uv;
            motion.Initialize();
            foreach(int fps in new[]{30,60,120})
            {
                float dt=1f/fps;root.transform.position=Vector3.zero;motion.ResetMotion();motion.AnimateMotion(true,false,dt);
                for(int i=0;i<fps*2;i++){root.transform.position+=Vector3.forward*60*dt;motion.AnimateMotion(true,false,dt);}
                float moving=motion.MeanWorldOffset.z, peak=float.NegativeInfinity;
                for(int i=0;i<fps;i++){motion.AnimateMotion(true,false,dt);peak=Mathf.Max(peak,motion.MeanWorldOffset.z);}
                report.AppendLine("cloth "+fps+" Hz: moving offset="+moving+" braking peak="+peak);
                Require(moving<-.005f,"Cloth should trail actual forward movement");
                Require(peak>.005f,"Braking should swing cloth forward");
                for(int i=0;i<fps;i++){root.transform.position+=Vector3.back*60*dt;motion.AnimateMotion(true,false,dt);}
                Require(motion.MeanWorldOffset.z>.005f,"Reverse airflow failed");
                root.transform.position+=new Vector3(100,80,-100);motion.ResetMotion();motion.AnimateMotion(true,false,dt);
                Require(motion.MeasuredSpeed==0,"Teleport reset failed");
                for(int i=0;i<fps;i++){root.transform.position+=Vector3.right*60*dt;motion.AnimateMotion(true,false,dt);}
                var verts=motion.cloth.sharedMesh.vertices;
                for(int i=0;i<verts.Length;i++)
                {
                    Require(!float.IsNaN(verts[i].sqrMagnitude) && verts[i].magnitude<10,"Cloth diverged");
                    if(uv[i].x<.001f)Require((verts[i]-rest[i]).magnitude<.002f,"Pinned edge moved");
                }
                root.transform.position+=Vector3.right*60*dt;motion.AnimateMotion(true,true,dt);
                var audio=motion.GetComponent<AudioSource>();
                Require(audio.volume>.2f && audio.pitch>1.7f && motion.flutterClip!=null,"Speed audio response failed");
                motion.AnimateMotion(false,false,dt);Require(audio.volume==0&&!audio.isPlaying,"Drop sound failed");
                report.AppendLine("PASS: flag "+fps+" Hz: acceleration, braking, reverse, sideways, pinned edge, teleport reset, audio.");
            }
            character=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Ch/ChPrefab.prefab"));
            character.transform.position=Vector3.zero;
            var animator=character.GetComponentInChildren<Animator>();
            if(animator!=null){animator.Rebind();animator.Update(0);}
            var mount=new FlagBackMount();mount.Resolve(character.transform);
            Require(mount.Chest!=null,"Actual player chest bone missing");
            mount.Pose(out Vector3 p,out Quaternion r);
            Vector3 local=character.transform.InverseTransformPoint(p);
            character.transform.rotation=Quaternion.Euler(12,110,18);mount.Pose(out Vector3 rotated,out Quaternion rr);
            Require((character.transform.InverseTransformPoint(rotated)-local).magnitude<.001f,"Mount detached while turning");
            character.transform.rotation=Quaternion.identity;mount.Pose(out p,out r);
            root.transform.position=Vector3.zero;root.transform.rotation=Quaternion.identity;
            visual.position=p+r*so.FindProperty("carriedVisualOffset").vector3Value;
            visual.rotation=r*Quaternion.Euler(so.FindProperty("carriedVisualEuler").vector3Value);
            motion.SetTorso(mount.Chest);motion.ResetMotion();motion.AnimateMotion(true,false,1f/60);
            report.AppendLine("PASS: actual ChPrefab torso mount; left tilt retained; held scale="+heldScale+"; chest="+mount.Chest.name);
            RenderMounted(root,character,p);
        }
        finally{UnityEngine.Object.DestroyImmediate(root);if(character!=null)UnityEngine.Object.DestroyImmediate(character);}
    }
    static void RenderMounted(GameObject flag,GameObject character,Vector3 chest)
    {
        var camObject=new GameObject("Motion check camera");var lamp=new GameObject("Motion check light");
        var rt=new RenderTexture(800,800,24);var old=RenderTexture.active;Texture2D texture=null;
        try
        {
            foreach(var t in flag.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            foreach(var t in character.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            var cam=camObject.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.orthographicSize=1.15f;
            cam.cullingMask=1<<31;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.18f,.22f,.28f);
            cam.transform.position=chest+new Vector3(0,.15f,-4);cam.transform.LookAt(chest+Vector3.up*.15f);
            var light=lamp.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.cullingMask=1<<31;
            lamp.transform.rotation=Quaternion.Euler(25,-160,0);
            RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            RenderTexture.active=rt;texture=new Texture2D(800,800,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,800,800),0,0);texture.Apply();File.WriteAllBytes(Dir+"/back-mounted.png",texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=old;if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);
            rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(camObject);UnityEngine.Object.DestroyImmediate(lamp);
        }
    }
}
