using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Main lobby preview only. No colliders, shared prefab changes or network gameplay code.
[DisallowMultipleComponent]
public sealed class LobbyCharacterClickInteraction : MonoBehaviour
{
    [Serializable]
    public sealed class Region
    {
        public string label = "부위";
        public HumanBodyBones bone = HumanBodyBones.Head;
        [Tooltip("비워두면 위의 Humanoid Bone을 사용합니다.")] public Transform anchor;
        public Vector3 localCenter;
        [Min(.001f)] public float radius = .12f;
#if UNITY_EDITOR
        // Import sources are editor-only; builds need the retargeted clips, not the Mixamo model meshes.
        [Tooltip("이 부위에서 재생할 Mixamo 애니메이션 FBX. Inspector의 가져오기 버튼으로 Humanoid 설정 후 로비 전용 상태에 등록합니다.")]
        public GameObject animationFbx;
#endif
        [Tooltip("Animator의 전체 상태 경로. Inspector의 목록에서 선택하세요.")] public string state;
        public int layer;
        [Min(.1f)] public float seconds = 2f;
        [Min(0f)] public float blendSeconds = .15f;
    }

    [SerializeField] private Animator character;
#if UNITY_EDITOR
    [Tooltip("Avatar를 가져올 Mixamo 원본 모델 FBX. 애니메이션 FBX와 같은 골격이어야 합니다.")]
    [SerializeField] private GameObject originalFbx;
    [Tooltip("원본 FBX에서 읽은 Humanoid Avatar. 애니메이션 임포트용이며 마녀 Animator의 Avatar는 교체하지 않습니다.")]
    [SerializeField] private Avatar sourceAvatar;
    [SerializeField, HideInInspector] private RuntimeAnimatorController interactionController;
#endif
    [SerializeField] private Camera previewCamera;
    [Tooltip("캐릭터 RenderTexture를 표시하는 RawImage. 비워두면 이 UI 아래에서 클릭 시 찾습니다.")]
    [SerializeField] private RawImage previewImage;
    [SerializeField] private GameObject customizationPanel;
    [SerializeField] private Region[] regions = {
        new Region { label = "머리", bone = HumanBodyBones.Head, radius = .14f },
        new Region { label = "몸통", bone = HumanBodyBones.Chest, radius = .18f },
        new Region { label = "왼손", bone = HumanBodyBones.LeftHand, radius = .09f },
        new Region { label = "오른손", bone = HumanBodyBones.RightHand, radius = .09f }
    };
    private Transform[] anchors;
    private readonly List<RaycastResult> uiHits = new();
    private SkinnedMeshRenderer[] meshes;
    private float[][] originalShapes;
    private bool reacting, returning, originalRootMotion;
    private float until, originalWeight, fade;
    private int reactionLayer;
    private AnimatorStateInfo originalState;
    private Vector3 originalPosition;
    private Quaternion originalRotation;

    private void Awake()
    {
        CacheAnchors();
        if (character == null) return;
        meshes = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        originalShapes = new float[meshes.Length][];
        for (int i = 0; i < meshes.Length; i++)
            originalShapes[i] = new float[meshes[i].sharedMesh != null ? meshes[i].sharedMesh.blendShapeCount : 0];
    }

    private void CacheAnchors()
    {
        anchors = new Transform[regions.Length];
        for (int i = 0; i < regions.Length; i++) anchors[i] = ResolveAnchor(regions[i]);
    }

    private Transform ResolveAnchor(Region region)
    {
        if (region == null) return null;
        if (region.anchor != null) return region.anchor;
        return character != null && character.isHuman && character.avatar != null && character.avatar.isValid &&
            region.bone >= 0 && region.bone < HumanBodyBones.LastBone ? character.GetBoneTransform(region.bone) : null;
    }

    private bool Ready => character != null && character.isActiveAndEnabled && previewCamera != null &&
        previewCamera.isActiveAndEnabled && (customizationPanel == null || !customizationPanel.activeInHierarchy);

    private void Update()
    {
        if (!Ready || !Application.isFocused) { RestoreImmediately(); return; }
        if (reacting)
        {
            if (Time.unscaledTime >= until)
            {
                if (!returning)
                {
                    returning = true;
                    character.CrossFadeInFixedTime(originalState.fullPathHash, fade, reactionLayer, originalState.normalizedTime * originalState.length);
                    until = Time.unscaledTime + fade;
                }
                else FinishReaction();
            }
            return; // Do not restart the animation repeatedly while clicking.
        }
        if (Cursor.lockState == CursorLockMode.Locked || !Input.GetMouseButtonDown(0)) return;
        if (!TryPointerRay(out Ray ray)) return;
        int selected = -1;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < regions.Length; i++)
        {
            Region region = regions[i];
            Transform anchor = anchors[i];
            if (anchor == null || region == null || string.IsNullOrWhiteSpace(region.state)) continue;
            Vector3 scale = anchor.lossyScale;
            float radius = region.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (RaySphereDistance(ray, anchor.TransformPoint(region.localCenter), radius, out float distance) && distance < nearest)
            { nearest = distance; selected = i; }
        }
        if (selected >= 0) Play(regions[selected]);
    }

    public static bool RaySphereDistance(Ray ray, Vector3 center, float radius, out float distance)
    {
        distance = 0;
        if (radius <= 0f) return false;
        Vector3 offset = center - ray.origin;
        float along = Vector3.Dot(offset, ray.direction);
        float discriminant = radius * radius - (offset.sqrMagnitude - along * along);
        if (discriminant < 0f) return false;
        float root = Mathf.Sqrt(discriminant);
        if (along + root < 0f) return false;
        distance = Mathf.Max(0f, along - root);
        return true;
    }

    private bool TryPointerRay(out Ray ray)
    {
        ray = default;
        if (previewImage == null || previewImage.texture != previewCamera.targetTexture)
        {
            previewImage = null;
            foreach (var image in GetComponentsInChildren<RawImage>(false))
                if (previewCamera.targetTexture != null && image.texture == previewCamera.targetTexture) { previewImage = image; break; }
        }
        if (previewImage == null || !previewImage.isActiveAndEnabled || previewImage.canvas == null) return false;
        if (EventSystem.current != null)
        {
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Input.mousePosition }, uiHits);
            if (uiHits.Count > 0)
            {
                RawImage hitImage = uiHits[0].gameObject.GetComponent<RawImage>();
                if (hitImage == null || !hitImage.transform.IsChildOf(transform) || hitImage.texture != previewCamera.targetTexture) return false;
                previewImage = hitImage;
            }
        }
        Canvas canvas = previewImage.canvas;
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(previewImage.rectTransform, Input.mousePosition, uiCamera, out Vector2 p)) return false;
        Rect rect = previewImage.rectTransform.rect;
        if (rect.width <= 0 || rect.height <= 0 || !rect.Contains(p)) return false;
        Rect uv = previewImage.uvRect;
        ray = previewCamera.ViewportPointToRay(new Vector3(uv.x + (p.x - rect.x) / rect.width * uv.width,
            uv.y + (p.y - rect.y) / rect.height * uv.height));
        return true;
    }

    private void Play(Region region)
    {
        int state = Animator.StringToHash(region.state);
        if (region.layer < 0 || region.layer >= character.layerCount || !character.HasState(region.layer, state))
        { Debug.LogWarning($"로비 클릭 영역 '{region.label}': Animator 상태 '{region.state}'를 확인하세요.", this); return; }
        reactionLayer = region.layer;
        originalState = character.IsInTransition(reactionLayer) ? character.GetNextAnimatorStateInfo(reactionLayer) : character.GetCurrentAnimatorStateInfo(reactionLayer);
        originalWeight = character.GetLayerWeight(reactionLayer);
        originalRootMotion = character.applyRootMotion;
        originalPosition = character.transform.localPosition;
        originalRotation = character.transform.localRotation;
        for (int i = 0; i < meshes.Length; i++)
            if (meshes[i] != null && meshes[i].sharedMesh != null)
                for (int j = 0; j < Mathf.Min(originalShapes[i].Length, meshes[i].sharedMesh.blendShapeCount); j++)
                    originalShapes[i][j] = meshes[i].GetBlendShapeWeight(j);
        fade = Mathf.Max(0f, region.blendSeconds);
        reacting = true;
        returning = false;
        character.applyRootMotion = false;
        character.SetLayerWeight(reactionLayer, 1f);
        character.CrossFadeInFixedTime(state, fade, reactionLayer, 0f);
        until = Time.unscaledTime + Mathf.Max(.1f, region.seconds);
    }

    private void LateUpdate()
    {
        if (!reacting || character == null) return;
        character.transform.localPosition = originalPosition;
        character.transform.localRotation = originalRotation;
    }

    private void RestoreImmediately()
    {
        if (!reacting || character == null) return;
        if (character.isActiveAndEnabled && character.HasState(reactionLayer, originalState.fullPathHash))
            character.Play(originalState.fullPathHash, reactionLayer, originalState.normalizedTime);
        FinishReaction();
    }

    private void FinishReaction()
    {
        if (character != null)
        {
            character.SetLayerWeight(reactionLayer, originalWeight);
            character.applyRootMotion = originalRootMotion;
            character.transform.localPosition = originalPosition;
            character.transform.localRotation = originalRotation;
        }
        if (meshes != null)
            for (int i = 0; i < meshes.Length; i++)
                if (meshes[i] != null && meshes[i].sharedMesh != null)
                    for (int j = 0; j < Mathf.Min(originalShapes[i].Length, meshes[i].sharedMesh.blendShapeCount); j++)
                        meshes[i].SetBlendShapeWeight(j, originalShapes[i][j]);
        reacting = returning = false;
    }

    private void OnDisable() => RestoreImmediately();
    private void OnValidate() { if (Application.isPlaying) CacheAnchors(); }
    private void OnDrawGizmosSelected()
    {
        if (regions == null) return;
        Gizmos.color = Color.cyan;
        foreach (Region region in regions)
        {
            Transform anchor = ResolveAnchor(region);
            if (anchor == null) continue;
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = anchor.localToWorldMatrix;
            Gizmos.DrawWireSphere(region.localCenter, region.radius);
            Gizmos.matrix = old;
        }
    }
}
