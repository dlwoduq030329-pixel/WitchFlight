using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Suimono.Core {
    // Explicit, reversible play-mode smoke tests. No scene or asset is saved.
    public static class SuimonoUrpValidation {
        [MenuItem("Tools/SUIMONO/Preview underwater (Play mode) %#F10")]
        public static void PreviewUnderwater() {
            if(!Application.isPlaying)return;
            var module=UnityEngine.Object.FindFirstObjectByType<SuimonoModule>();if(module==null||module.setCameraComponent==null)return;
            var controller=GameObject.Find("_CONTROLLER");if(controller!=null)controller.SetActive(false);
            var camera=module.setCameraComponent;var p=camera.transform.position;p.y=module.currentSurfaceLevel-3;
            camera.transform.position=p;camera.transform.rotation=Quaternion.Euler(-30,camera.transform.eulerAngles.y,0);
        }
        [MenuItem("Tools/SUIMONO/Validate runtime %#F9")]
        public static void Run() {
            if(!Application.isPlaying) { Debug.LogError("SUIMONO: runtime validation requires Play mode.");return; }
            int failures=0;
            var module=UnityEngine.Object.FindFirstObjectByType<SuimonoModule>();
            if(module==null || module.setCameraComponent==null) {Debug.LogError("SUIMONO: no active module camera");return;}
            var tools=UnityEngine.Object.FindObjectsByType<cameraTools>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            foreach(var tool in tools) if(tool.isActiveAndEnabled) {
                tool.CameraUpdate();
                Debug.Log("SUIMONO_BUFFER "+tool.cameraType+" "+Stats(tool.renderTexDiff));
            }
            foreach(var test in new[]{(suiCamToolType.normals,"Suimono2/EffectNormalBlend"),(suiCamToolType.wakeEffects,"Suimono2/EffectWakeMask"),(suiCamToolType.shorelineCapture,"Suimono2/Suimono2_FX_ShorelineObject")}) {
                var tool=tools.FirstOrDefault(t=>t.cameraType==test.Item1);
                if(tool==null) {Debug.LogError("SUIMONO_TEST missing camera "+test.Item1);failures++;continue;}
                var fixture=GameObject.CreatePrimitive(PrimitiveType.Quad); fixture.name="SUIMONO temporary capture test";
                var texture=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true);
                var material=new Material(Shader.Find(test.Item2));
                bool advanced=module.enableAdvancedDistort,active=tool.gameObject.activeSelf;
                try {
                    module.enableAdvancedDistort=true;tool.gameObject.SetActive(true);
                    fixture.layer=test.Item1==suiCamToolType.shorelineCapture ? module.layerDepthNum : module.layerScreenFXNum;
                    fixture.transform.SetPositionAndRotation(module.setCameraComponent.transform.position+module.setCameraComponent.transform.forward*5,module.setCameraComponent.transform.rotation);
                    fixture.transform.localScale=Vector3.one*4;
                    texture.SetPixels(Enumerable.Repeat(new Color(.3f,.6f,.8f,1),4).ToArray());texture.Apply();
                    material.SetTexture("_MainTex",texture);material.SetColor("_Color",Color.white);material.SetFloat("_Strength",1);material.SetVector("_Mult",new Vector4(1,1,0,0));
                    fixture.GetComponent<Renderer>().sharedMaterial=material;
                    tool.CameraUpdate();var pixels=Read(tool.renderTexDiff);var center=pixels[(tool.renderTexDiff.height/2)*tool.renderTexDiff.width+tool.renderTexDiff.width/2];
                    bool ok=center.g>.1f && center.b>.2f && Mathf.Abs(center.g-.6f)<.25f;
                    Debug.Log("SUIMONO_TEST "+test.Item1+" "+(ok?"PASS":"FAIL")+" center="+center);
                    if(!ok)failures++;
                } finally {module.enableAdvancedDistort=advanced;tool.gameObject.SetActive(active);UnityEngine.Object.DestroyImmediate(fixture);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(texture);}
            }
            failures+=CheckDepthAndCaustics(module,tools);
            var reflection=tools.FirstOrDefault(t=>t.cameraType==suiCamToolType.localReflection);
            if(reflection!=null && !module.isUnderwater) {
                var blur=reflection.GetComponent<Suimono_DistanceBlur>();
                if(blur!=null){float amount=blur.blurAmt;bool enabled=blur.enabled;
                    try {blur.enabled=true;blur.blurAmt=0;reflection.CameraUpdate();var sharp=Read(reflection.renderTexDiff);
                        blur.blurAmt=1;reflection.CameraUpdate();float delta=Difference(sharp,Read(reflection.renderTexDiff));
                        bool ok=!float.IsNaN(delta)&&delta>0;Debug.Log("SUIMONO_TEST reflectionBlur "+(ok?"PASS":"FAIL")+" delta="+delta);if(!ok)failures++;
                    } finally {blur.blurAmt=amount;blur.enabled=enabled;reflection.CameraUpdate();}
                }
            }
            var camera=module.setCameraComponent;var fog=camera.GetComponent<Suimono_UnderwaterFog>();
            if(fog!=null) {
                Debug.Log($"SUIMONO_FOG color={fog.underwaterColor} light={fog.lightFactor} range={fog.fogStart}/{fog.fogEnd} level={module.currentSurfaceLevel} cameraY={camera.transform.position.y}");
                var target=new RenderTexture(320,180,24,RenderTextureFormat.ARGBHalf); target.Create();
                bool under=module.isUnderwater,enabled=fog.enabled;var transition=fog.UrpTransition;
                try {
                    fog.enabled=true;module.isUnderwater=false;fog.SetUrpTestTransition(new Vector4(1.1f,1.1f,0,0));
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});var above=Read(target);
                    module.isUnderwater=true;
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});var below=Read(target);
                    module.isUnderwater=false;fog.SetUrpTestTransition(new Vector4(.2f,.2f,.3f,.7f));
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});var drops=Read(target);
                    float underwaterDelta=Difference(above,below),transitionDelta=Difference(above,drops);
                    bool ok=underwaterDelta>.001f && transitionDelta>.00001f;
                    Debug.Log($"SUIMONO_TEST underwater/transition {(ok?"PASS":"FAIL")} underwaterDelta={underwaterDelta} transitionDelta={transitionDelta}");
                    if(!ok)failures++;
                } finally {module.isUnderwater=under;fog.enabled=enabled;fog.SetUrpTestTransition(transition);target.Release();UnityEngine.Object.DestroyImmediate(target);}
            }
            foreach(var tool in tools)if(tool!=null && tool.isActiveAndEnabled)tool.CameraUpdate();
            Debug.Log("SUIMONO_RUNTIME_VALIDATION failures="+failures);
        }
        static int CheckDepthAndCaustics(SuimonoModule module,cameraTools[] tools) {
            int errors=0;
            var fixture=GameObject.CreatePrimitive(PrimitiveType.Cube);fixture.name="SUIMONO temporary depth fixture";
            var camObject=new GameObject("SUIMONO temporary depth camera");var cam=camObject.AddComponent<Camera>();cam.enabled=false;
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var oldCamera=module.setCameraComponent;var oldTransform=module.setCamera;
            bool oldCaustics=module.enableCaustics,oldBlend=module.enableCausticsBlending;
            var capture=tools.FirstOrDefault(t=>t.cameraType==suiCamToolType.transparentCaustic);
            var transparent=tools.FirstOrDefault(t=>t.cameraType==suiCamToolType.transparent);
            bool oldActive=capture!=null && capture.gameObject.activeSelf;
            try {
                fixture.layer=module.layerDepthNum;fixture.transform.position=new Vector3(50000,0,50000);fixture.transform.localScale=new Vector3(5,1,5);fixture.GetComponent<Renderer>().sharedMaterial=material;
                material.SetColor("_BaseColor",Color.white);
                cam.transform.position=fixture.transform.position+Vector3.up*10;cam.transform.rotation=Quaternion.Euler(90,0,0);
                cam.orthographic=true;cam.orthographicSize=4;cam.nearClipPlane=.01f;cam.farClipPlane=20;cam.cullingMask=1<<fixture.layer;cam.clearFlags=CameraClearFlags.SolidColor;
                var depth=camObject.AddComponent<SuimonoCamera_depth>();depth._sceneDepth=2;depth._shoreDepth=2;
                var tool=camObject.AddComponent<cameraTools>();tool.cameraType=suiCamToolType.shorelineObject;tool.resolution=64;tool.CameraUpdate();
                var pixels=Read(tool.renderTexDiff);var center=pixels[32*64+32];
                bool ok=Mathf.Abs(center.r-.525f)<.08f && center.g>.9f && center.b<.1f;
                Debug.Log("SUIMONO_TEST automaticShoreDepth "+(ok?"PASS":"FAIL")+" center="+center);if(!ok)errors++;
                UnityEngine.Object.DestroyImmediate(tool);UnityEngine.Object.DestroyImmediate(depth);
                if(capture!=null && transparent!=null) {
                    fixture.layer=0;cam.cullingMask=1;module.setCameraComponent=cam;module.setCamera=cam.transform;module.enableCaustics=true;module.enableCausticsBlending=true;
                    capture.gameObject.SetActive(true);
                    transparent.CameraUpdate();var noCaustic=Read(transparent.renderTexDiff);
                    capture.CameraUpdate();var withCaustic=Read(capture.renderTexDiff);
                    float delta=noCaustic.Length==withCaustic.Length ? Difference(noCaustic,withCaustic) : 0;
                    ok=delta>.00001f;Debug.Log("SUIMONO_TEST caustics "+(ok?"PASS":"FAIL")+" delta="+delta);if(!ok)errors++;
                    var causticLight=module.suimonoModuleLibrary.causticObjectLight;
                    if(causticLight!=null) {int mask=causticLight.cullingMask;
                        try {causticLight.cullingMask=0;capture.CameraUpdate();float excluded=Difference(noCaustic,Read(capture.renderTexDiff));
                            ok=excluded<.00001f;Debug.Log("SUIMONO_TEST causticLayerMask "+(ok?"PASS":"FAIL")+" delta="+excluded);if(!ok)errors++;
                        } finally {causticLight.cullingMask=mask;}
                    }
                }
            } finally {
                module.setCameraComponent=oldCamera;module.setCamera=oldTransform;module.enableCaustics=oldCaustics;module.enableCausticsBlending=oldBlend;
                if(capture!=null)capture.gameObject.SetActive(oldActive);
                UnityEngine.Object.DestroyImmediate(camObject);UnityEngine.Object.DestroyImmediate(fixture);UnityEngine.Object.DestroyImmediate(material);
            }
            return errors;
        }
        static float Difference(Color[] a,Color[] b) {double sum=0;for(int i=0;i<a.Length;i++)sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return (float)(sum/(a.Length*3));}
        static Color[] Read(RenderTexture rt) {
            if(rt==null || !rt.IsCreated())return Array.Empty<Color>();
            var previous=RenderTexture.active;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
            try {RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();return tex.GetPixels();}
            finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(tex);}
        }
        static string Stats(RenderTexture rt) {
            var p=Read(rt);if(p.Length==0)return "EMPTY";var mean=Color.clear;int invalid=0;
            foreach(var c in p) {mean+=c;if(float.IsNaN(c.r)||float.IsInfinity(c.r)||float.IsNaN(c.g)||float.IsNaN(c.b))invalid++;}
            return $"{rt.width}x{rt.height} mean={mean/p.Length} invalid={invalid}";
        }
    }
}
