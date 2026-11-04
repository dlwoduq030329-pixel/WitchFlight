using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Render the existing local character with an independent blend-shape-only expression layer.
[DefaultExecutionOrder(10000)]
public sealed class BattleFacePortrait : MonoBehaviour
{
    [SerializeField] private RawImage faceImage;
    [SerializeField] private Camera vsCamera;
    [SerializeField] private AnimationClip killClip, hitClip, hurtClip, missClip, deathClip;
    [System.Serializable]
    public sealed class FaceCurve
    {
        public string shape;
        public AnimationCurve curve;
        [System.NonSerialized] public int index;
    }
    [System.Serializable]
    public sealed class FaceExpression
    {
        public AnimationClip clip;
        public FaceCurve[] curves;
    }
    [SerializeField, HideInInspector] private FaceExpression[] expressions;
    [SerializeField, Min(.1f)] private float halfHeight = .15f;
    [SerializeField, Range(0f, .15f)] private float maximumShake = .025f;
    [SerializeField, Range(128, 512)] private int resolution = 256;
    [SerializeField, Range(10, 60)] private int framesPerSecond = 30;
    private static BattleFacePortrait active;
    private Player player;
    private Animator character;
    private Transform head, leftEye, rightEye;
    private Vector3 headLocalForward, headLocalUp;
    private SkinnedMeshRenderer face;
    private Renderer[] renderers;
    private int[] layers;
    private bool[] hidden, portraitExcluded;
    private List<Material>[] normalMaterials, savedMaterials;
    private MaterialPropertyBlock[][] normalBlocks, savedBlocks;
    private bool restoringDeathMaterials;
    private float[] weights, displayedWeights, transitionWeights;
    private bool faceApplied, hasDisplayedWeights;
    private FaceExpression currentExpression;
    private RenderTexture texture, originalTarget;
    private Texture originalImage;
    private AnimationClip current;
    private float started, holdUntil, shakeUntil, nextFrame, retryAt;
    [SerializeField, Min(.01f)] private float transitionSeconds = .2f;
    [SerializeField, Min(0f)] private float holdSeconds = 2f;
    [SerializeField, Min(.01f)] private float returnSeconds = .2f;
    private int hitSequence, deathSequence;
    private bool wasAlive, sampling;
    private const int FaceLayer = 31;

    private void Awake()
    {
        var table = Resources.Load<BattleFaceExpressionTable>("BattleFaceExpressions");
        if (expressions == null || expressions.Length == 0) expressions = table != null ? table.expressions : null;
        else if (table != null)
            for (int i = 0; i < expressions.Length; i++)
                if (expressions[i] != null && (expressions[i].curves == null || expressions[i].curves.Length == 0))
                    foreach (var fallback in table.expressions)
                        if (fallback.clip == expressions[i].clip) { expressions[i] = fallback; break; }
        originalImage = faceImage.texture;
        originalTarget = vsCamera.targetTexture;
        faceImage.raycastTarget = false;
        vsCamera.enabled = false;
        vsCamera.clearFlags = CameraClearFlags.SolidColor;
        vsCamera.backgroundColor = Color.clear;
        vsCamera.cullingMask = 1 << FaceLayer;
        vsCamera.orthographic = true;
        vsCamera.nearClipPlane = .01f; vsCamera.farClipPlane = 4f;
        vsCamera.depth = -90;
        vsCamera.allowHDR = false; vsCamera.allowMSAA = false;
        vsCamera.useOcclusionCulling = false;
        if (vsCamera.TryGetComponent<UniversalAdditionalCameraData>(out var data))
        {
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.allowXRRendering = false;
        }
    }
    private void OnEnable()
    {
        active = this;
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
        RenderPipelineManager.endContextRendering += EndContext;
    }
    private void OnDisable()
    {
        if (active == this) active = null;
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        RenderPipelineManager.endContextRendering -= EndContext;
        RestoreFaceWeights();
        RestoreCharacter();
        if (vsCamera != null) vsCamera.enabled = false;
    }
    public static void ConfirmedHit(Player source, bool lethal)
    {
        if (active == null || source != Player.LocalPlayer) return;
        active.Show(lethal ? active.killClip : active.hitClip);
    }
    public static void Miss(Player source)
    {
        if (active != null && source == Player.LocalPlayer) active.Show(active.missClip);
    }
    private void Show(AnimationClip clip)
    {
        if (player == null || clip == null || expressions == null) return;
        if (current == clip && Time.unscaledTime < holdUntil)
        {
            holdUntil = Mathf.Max(holdUntil, Time.unscaledTime + holdSeconds);
            return;
        }
        FaceExpression next = null;
        foreach (var expression in expressions)
            if (expression != null && expression.clip == clip && expression.curves != null) { next = expression; break; }
        if (next == null) return;
        for (int i = 0; i < transitionWeights.Length; i++)
            transitionWeights[i] = hasDisplayedWeights ? displayedWeights[i] : face.GetBlendShapeWeight(i);
        current = clip; currentExpression = next; started = Time.unscaledTime;
        holdUntil = started + transitionSeconds + holdSeconds;
        nextFrame = 0f;
    }
    private void LateUpdate()
    {
        RestoreCharacter();
        RestoreFaceWeights();
        Player local = Player.LocalPlayer;
        bool visible = faceImage.isActiveAndEnabled && !faceImage.canvasRenderer.cull &&
            BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive &&
            local != null && local.Object != null && local.Object.IsValid;
        vsCamera.enabled = false;
        if (!visible) return;
        if (local != player || head == null)
        {
            if (Time.unscaledTime < retryAt) return;
            retryAt = Time.unscaledTime + .5f;
            if (!Bind(local)) return;
        }
        if (deathSequence != player.DeathSequence || (wasAlive && !player.IsAlive))
        {
            Show(deathClip); deathSequence = player.DeathSequence;
        }
        if (!wasAlive && player.IsAlive) { current = null; currentExpression = null; hasDisplayedWeights = false; }
        if (hitSequence != player.HitSequence)
        {
            hitSequence = player.HitSequence;
            shakeUntil = Time.unscaledTime + .35f;
            if (player.IsAlive) Show(hurtClip);
        }
        wasAlive = player.IsAlive;
        if (Time.unscaledTime < nextFrame) return;
        nextFrame = Time.unscaledTime + 1f / framesPerSecond;
        if (current != null && Time.unscaledTime >= holdUntil + returnSeconds)
        { current = null; currentExpression = null; hasDisplayedWeights = false; }
        for (int i = 0; i < weights.Length; i++) weights[i] = face.GetBlendShapeWeight(i);
        faceApplied = true;
        ApplyFaceExpression();
        UpdateCameraFrame();
        vsCamera.enabled = true;
    }
    private bool Bind(Player source)
    {
        character = source.GetComponentInChildren<Animator>(true);
        if (character == null || !character.isHuman) return false;
        head = character.GetBoneTransform(HumanBodyBones.Head);
        face = character.transform.Find("Body")?.GetComponent<SkinnedMeshRenderer>();
        if (head == null || face == null || face.sharedMesh == null) return false;
        leftEye = character.GetBoneTransform(HumanBodyBones.LeftEye);
        rightEye = character.GetBoneTransform(HumanBodyBones.RightEye);
        // Mesh bind poses provide the head axes without depending on the current flight pose.
        headLocalForward = head.InverseTransformDirection(character.transform.forward);
        headLocalUp = head.InverseTransformDirection(character.transform.up);
        var skinBones = face.bones;
        var bindPoses = face.sharedMesh.bindposes;
        // Bind poses consume mesh-local vectors. Body is authored with a -90 degree
        // X rotation, so raw mesh +Z points upward rather than toward the face.
        Vector3 meshForward = face.transform.InverseTransformVector(character.transform.forward);
        Vector3 meshUp = face.transform.InverseTransformVector(character.transform.up);
        for (int i = 0; i < skinBones.Length && i < bindPoses.Length; i++)
        {
            if (skinBones[i] != head) continue;
            headLocalForward = bindPoses[i].MultiplyVector(meshForward).normalized;
            headLocalUp = bindPoses[i].MultiplyVector(meshUp).normalized;
            break;
        }
        // Resolve blend-shape names only when the local character changes.
        if (expressions != null)
            foreach (var expression in expressions)
            {
                if (expression == null || expression.curves == null) continue;
                foreach (var curve in expression.curves)
                    curve.index = face.sharedMesh.GetBlendShapeIndex(curve.shape);
            }
        weights = new float[face.sharedMesh.blendShapeCount];
        displayedWeights = new float[weights.Length]; transitionWeights = new float[weights.Length];
        var visible = new List<Renderer>();
        foreach (var renderer in character.GetComponentsInChildren<Renderer>(true))
            if ((renderer is SkinnedMeshRenderer || renderer is MeshRenderer) &&
                renderer.GetComponentInParent<CatAvatar>() == null && renderer.GetComponentInParent<Canvas>() == null)
                visible.Add(renderer);
        renderers = visible.ToArray(); layers = new int[renderers.Length]; hidden = new bool[renderers.Length];
        portraitExcluded = new bool[renderers.Length];
        var equipment = source.GetComponentInChildren<PlayerEquipment>(true);
        for (int i = 0; i < renderers.Length; i++)
            portraitExcluded[i] = equipment != null && equipment.IsFlightEquipment(renderers[i].transform);
        normalMaterials = new List<Material>[renderers.Length]; savedMaterials = new List<Material>[renderers.Length];
        normalBlocks = new MaterialPropertyBlock[renderers.Length][]; savedBlocks = new MaterialPropertyBlock[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
        {
            normalMaterials[i] = new List<Material>(); renderers[i].GetSharedMaterials(normalMaterials[i]);
            savedMaterials[i] = new List<Material>(normalMaterials[i].Count);
            normalBlocks[i] = new MaterialPropertyBlock[normalMaterials[i].Count];
            savedBlocks[i] = new MaterialPropertyBlock[normalMaterials[i].Count];
            for (int slot = 0; slot < normalMaterials[i].Count; slot++)
            {
                normalBlocks[i][slot] = new MaterialPropertyBlock();
                savedBlocks[i][slot] = new MaterialPropertyBlock();
                renderers[i].GetPropertyBlock(normalBlocks[i][slot], slot);
            }
        }
        float aspect = Mathf.Max(.2f, faceImage.rectTransform.rect.width / Mathf.Max(1f, faceImage.rectTransform.rect.height));
        if (texture == null)
        {
            texture = new RenderTexture(resolution, Mathf.RoundToInt(resolution / aspect), 24, RenderTextureFormat.ARGB32);
            texture.name = "Battle face"; texture.Create();
        }
        faceImage.texture = texture; vsCamera.targetTexture = texture; vsCamera.aspect = aspect;
        player = source; hitSequence = source.HitSequence; deathSequence = source.DeathSequence;
        wasAlive = source.IsAlive; current = null; currentExpression = null; hasDisplayedWeights = false;
        if (!source.IsAlive) Show(deathClip);
        return true;
    }
    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != vsCamera || player == null || head == null || face == null) return;
        sampling = true;
        restoringDeathMaterials = !player.IsAlive;
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i]; if (renderer == null) continue;
            layers[i] = renderer.gameObject.layer; hidden[i] = renderer.forceRenderingOff;
            renderer.gameObject.layer = FaceLayer; renderer.forceRenderingOff = portraitExcluded[i];
            if (restoringDeathMaterials)
            {
                renderer.GetSharedMaterials(savedMaterials[i]);
                for (int slot = 0; slot < normalBlocks[i].Length; slot++)
                {
                    renderer.GetPropertyBlock(savedBlocks[i][slot], slot);
                    renderer.SetPropertyBlock(normalBlocks[i][slot], slot);
                }
                renderer.SetSharedMaterials(normalMaterials[i]);
            }
        }
        UpdateCameraFrame();
    }
    private void UpdateCameraFrame()
    {
        // Position before URP prepares camera data, then refresh after facial sampling.
        // Follow all casting/boost head motion exactly; only the bounded hit shake remains.
        Vector3 forward = head.TransformDirection(headLocalForward).normalized;
        Vector3 up = Vector3.ProjectOnPlane(head.TransformDirection(headLocalUp), forward).normalized;
        Vector3 right = Vector3.Cross(up, forward).normalized;
        float scale = Mathf.Max(.01f, Mathf.Abs(player.transform.lossyScale.y));
        Vector3 center = leftEye != null && rightEye != null
            ? (leftEye.position + rightEye.position) * .5f - up * (.025f * scale)
            : head.position + up * (.06f * scale);
        float shake = Mathf.Clamp01((shakeUntil - Time.unscaledTime) / .35f);
        center += right * (Mathf.Sin(Time.unscaledTime * 65f) * maximumShake * .5f * scale * shake);
        float distance = .65f * scale;
        vsCamera.orthographicSize = halfHeight * scale;
        float depthPadding = Mathf.Max(.28f, halfHeight) * scale;
        vsCamera.nearClipPlane = Mathf.Max(.01f, distance - depthPadding);
        vsCamera.farClipPlane = distance + depthPadding * 1.5f;
        vsCamera.transform.SetPositionAndRotation(center + forward * distance, Quaternion.LookRotation(-forward, up));
        vsCamera.ResetWorldToCameraMatrix();
        vsCamera.ResetProjectionMatrix();
    }
    private void EndCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera == vsCamera) RestoreCharacter();
    }
    private void RestoreCharacter()
    {
        if (!sampling) return;
        sampling = false;
        for (int i = renderers.Length - 1; i >= 0; i--)
            if (renderers[i] != null)
            {
                renderers[i].gameObject.layer = layers[i]; renderers[i].forceRenderingOff = hidden[i];
                if (restoringDeathMaterials)
                {
                    renderers[i].SetSharedMaterials(savedMaterials[i]);
                    for (int slot = 0; slot < savedBlocks[i].Length; slot++)
                        renderers[i].SetPropertyBlock(savedBlocks[i][slot], slot);
                }
            }

    }
    private void EndContext(ScriptableRenderContext context, List<Camera> cameras) => RestoreFaceWeights();
    private void RestoreFaceWeights()
    {
        if (!faceApplied) return;
        faceApplied = false;
        if (face != null)
            for (int i = 0; i < weights.Length; i++) face.SetBlendShapeWeight(i, weights[i]);
    }
    private void ApplyFaceExpression()
    {
        if (currentExpression == null) return;
        float elapsed = Time.unscaledTime - started;
        float entering = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / transitionSeconds));
        float leaving = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - holdUntil) / returnSeconds));
        System.Array.Copy(weights, displayedWeights, weights.Length);
        foreach (var curve in currentExpression.curves)
        {
            if (curve.index < 0) continue;
            float target = curve.curve.Evaluate(Mathf.Min(elapsed, current.length));
            float value = Mathf.Lerp(transitionWeights[curve.index], target, entering);
            value = Mathf.Lerp(value, weights[curve.index], leaving);
            face.SetBlendShapeWeight(curve.index, value);
            displayedWeights[curve.index] = value;
        }
        hasDisplayedWeights = true;
    }
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying) return;
        var clips = new[] { killClip, hitClip, hurtClip, missClip, deathClip };
        expressions = new FaceExpression[clips.Length];
        for (int i = 0; i < clips.Length; i++)
        {
            var curves = new List<FaceCurve>();
            if (clips[i] != null)
                foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clips[i]))
                {
                    if (binding.path != "Body" || binding.type != typeof(SkinnedMeshRenderer) ||
                        !binding.propertyName.StartsWith("blendShape.")) continue;
                    curves.Add(new FaceCurve {
                        shape = binding.propertyName.Substring("blendShape.".Length),
                        curve = UnityEditor.AnimationUtility.GetEditorCurve(clips[i], binding)
                    });
                }
            expressions[i] = new FaceExpression { clip = clips[i], curves = curves.ToArray() };
        }
    }
#endif
    private void OnDestroy()
    {
        RestoreFaceWeights();
        RestoreCharacter();
        if (vsCamera != null) { vsCamera.enabled = false; vsCamera.targetTexture = originalTarget; }
        if (faceImage != null) faceImage.texture = originalImage;
        if (texture != null) { texture.Release(); Destroy(texture); }
    }
}
