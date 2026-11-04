using UnityEngine;
using System.Collections;


namespace Suimono.Core
{
	public class fx_causticObject : MonoBehaviour {

		public bool manualPlacement = false;

		private Suimono.Core.SuimonoModule moduleObject;
		private Suimono.Core.fx_causticModule causticObject;
		private Light lightComponent;
		private float heightMult = 1f;

		void Start () {
			//get master objects
			moduleObject = GameObject.Find("SUIMONO_Module").GetComponent<Suimono.Core.SuimonoModule>();
			causticObject = GameObject.Find("_caustic_effects").GetComponent<Suimono.Core.fx_causticModule>();
			lightComponent = GetComponent<Light>();
		}
		

		void LateUpdate () {
		

			if (causticObject.enableCaustics){

				//get the current light texture from Module
				lightComponent.cookie = causticObject.useTex;
				lightComponent.cullingMask = moduleObject.causticLayer;
				lightComponent.color = causticObject.causticTint;

				//Set Height (manual objects only)
				heightMult = 1f;
				if (manualPlacement) heightMult = (1f-causticObject.heightFac);
				lightComponent.intensity = causticObject.causticIntensity * heightMult;

				lightComponent.cookieSize = causticObject.causticScale;

				//get scene lighting
				if (causticObject.sceneLightObject != null ){

					//set caustic color based on scene lighting
					if (causticObject.inheritLightColor){
						lightComponent.color = causticObject.sceneLightObject.color * causticObject.causticTint;
						lightComponent.intensity = lightComponent.intensity * causticObject.sceneLightObject.intensity;
					} else {
						lightComponent.color = causticObject.causticTint;
					}

					//set caustic direction based on scene light direction
					if (causticObject.inheritLightDirection){
						transform.eulerAngles = causticObject.sceneLightObject.transform.eulerAngles;
					} else {
						transform.eulerAngles = new Vector3(90f,0f,0f);
					}

				}
			}


		}
        bool previousEnabled;
        Light previousSun;
        void OnEnable() { UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += SyncUrpCookie; UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += RestoreUrpCookie; }
        void OnDisable() { UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= SyncUrpCookie; UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= RestoreUrpCookie; }
        void SyncUrpCookie(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera) {
            if (!(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)) return;
            var light=GetComponent<Light>(); if(light==null) return;
            var data=light.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            if(data==null) data=light.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            data.lightCookieSize=Vector2.one*light.cookieSize;
            previousEnabled=light.enabled;previousSun=RenderSettings.sun;
            if(moduleObject==null) return;
            var tool=camera.GetComponent<cameraTools>();
            bool visible=tool==null ? moduleObject.isUnderwater && camera==moduleObject.setCameraComponent :
                tool.cameraType==suiCamToolType.transparentCaustic || (tool.cameraType==suiCamToolType.transparent && !moduleObject.enableCausticsBlending);
            light.enabled=Application.isPlaying && visible && moduleObject.enableCaustics && causticObject!=null && causticObject.enableCaustics;
            if(moduleObject.setLight!=null) {
                light.enabled &= moduleObject.setLight.isActiveAndEnabled;
                RenderSettings.sun=moduleObject.setLight;
            }
        }
        void RestoreUrpCookie(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera) {
            if (!(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)) return;
            var light=GetComponent<Light>();if(light!=null)light.enabled=previousEnabled;RenderSettings.sun=previousSun;
        }

	}
}
