using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace Suimono.Core
{
    public static class SuimonoUrpSetup
    {
        [MenuItem("Tools/SUIMONO/Configure URP")]
        public static void Configure()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets" }))
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                pipeline.supportsCameraDepthTexture = true;
                pipeline.supportsCameraOpaqueTexture = true;
                EditorUtility.SetDirty(pipeline);
                var serialized = new SerializedObject(pipeline);
                serialized.FindProperty("m_SupportsLightCookies").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var renderers = serialized.FindProperty("m_RendererDataList");
                for (int i = 0; i < renderers.arraySize; i++)
                {
                    var renderer = renderers.GetArrayElementAtIndex(i).objectReferenceValue as UniversalRendererData;
                    if (renderer == null) continue;
                    var feature = renderer.rendererFeatures.OfType<SuimonoUrpFeature>().FirstOrDefault();
                    bool isNew = feature == null;
                    if (isNew) feature = ScriptableObject.CreateInstance<SuimonoUrpFeature>();
                    feature.name = "Suimono URP";
                    var settings = new SerializedObject(feature);
                    settings.FindProperty("underwaterShader").objectReferenceValue = Shader.Find("Hidden/Suimono/UnderwaterURP");
                    settings.FindProperty("captureShader").objectReferenceValue = Shader.Find("Hidden/Suimono/CaptureURP");
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    if (isNew) AssetDatabase.AddObjectToAsset(feature, renderer);
                    if (isNew) renderer.rendererFeatures.Add(feature);
                    feature.Create(); EditorUtility.SetDirty(feature);
                    renderer.SetDirty(); EditorUtility.SetDirty(renderer);
                    AssetDatabase.SaveAssetIfDirty(renderer);
                }
                AssetDatabase.SaveAssetIfDirty(pipeline);
            }
            int errors = 0;
            foreach (var name in new[] { "Suimono2/Demo/Flora_DoubleSided", "Suimono2/Demo/Flora_SingleSided", "Reflective/Bumped Specular", "Suimono2/Demo/HairSpec", "Suimono2/Demo/Skin", "Suimono2/Bumped Specular Double", "Hidden/Suimono/CaptureURP", "Suimono2/EffectNormalBlend", "Suimono2/EffectWakeMask", "Suimono2/Suimono2_FX_ShorelineObject", "Suimono2/surface", "Suimono2/surface_scale", "Suimono2/surface_under", "Hidden/Suimono/UnderwaterURP", "Suimono2/particle_Alpha", "Suimono2/particle_AlphaOverlay", "Suimono2/effect_splash" })
            {
                var shader = Shader.Find(name);
                if (shader == null) { Debug.LogError("Missing shader: " + name); errors++; continue; }
                var mat = new Material(shader);
                for(int pass=0;pass<mat.passCount;pass++) mat.SetPass(pass);
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) { Debug.LogError(name + ": " + message.message); errors++; }
                Object.DestroyImmediate(mat);
            }
            Debug.Log("SUIMONO_URP_VALIDATION errors=" + errors);
            if (Application.isBatchMode) EditorApplication.Exit(errors == 0 ? 0 : 1);
        }
    }
}

