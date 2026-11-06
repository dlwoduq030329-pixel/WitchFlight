using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Only the Main lobby portrait owns this component; the shared character prefab is unchanged.
[DefaultExecutionOrder(220)]
public sealed class LobbyCharacterPetting : MonoBehaviour
{
    [SerializeField] private Animator character;
    [SerializeField] private Camera previewCamera;
    [SerializeField] private GameObject customizationPanel;
    [SerializeField] private float strokePixels = 14f;
    [SerializeField] private float reactionSeconds = 1.4f;
    [SerializeField] private float faceRadius = .13f;
    [SerializeField] private float chestRadius = .17f;
    private Transform head, chest;
    private RawImage surface;
    private int faceLayer = -1;
    private int activeFace;
    private bool ownsReaction;
    private bool returningFace;
    private readonly List<FaceValue> faceValues = new List<FaceValue>();
    private sealed class FaceValue { public SkinnedMeshRenderer mesh; public int index; public float value; }
    private float originalFaceWeight;
    private AnimatorStateInfo originalBody, originalFace;
    private Vector3 rootPosition;
    private Quaternion rootRotation;
    private static readonly int NeutralFaceState = Animator.StringToHash("Face.Basic(blink)");
    private static readonly int StandingState = Animator.StringToHash("Ingame.StandingIdle");
    private static readonly int MoguState = Animator.StringToHash("Face.mogumogu");
    private static readonly int GuardFaceState = Animator.StringToHash("Face.ll");
    private readonly List<RaycastResult> hits = new List<RaycastResult>();
    private readonly List<BoneState> bones = new List<BoneState>();
    private Vector2 lastPointer;
    private int dragRegion, reaction;
    private float travel, until, blend, armBlend;
    private struct BoneState { public Transform bone; public Quaternion rotation; }

    private void Start()
    {
        if (character == null || !character.isHuman || character.avatar == null || !character.avatar.isValid) { enabled = false; return; }
        head = character.GetBoneTransform(HumanBodyBones.Head);
        chest = character.GetBoneTransform(HumanBodyBones.UpperChest);
        if (chest == null) chest = character.GetBoneTransform(HumanBodyBones.Chest);
        faceLayer = character.GetLayerIndex("Face");
        if (faceLayer < 0 || !character.HasState(faceLayer, MoguState) ||
            !character.HasState(faceLayer, GuardFaceState) || !character.HasState(0, StandingState))
        {
            Debug.LogError("Lobby petting requires Ingame.StandingIdle and Face.mogumogu / Face.ll on the preview Animator.", this);
            enabled = false;
        }
    }
    private bool Ready => head != null && chest != null && previewCamera != null && previewCamera.isActiveAndEnabled &&
        character.gameObject.activeInHierarchy && (customizationPanel == null || !customizationPanel.activeInHierarchy);

    private void Update()
    {
        RestorePose();
        if (!Ready || !Application.isFocused) { ResetReaction(); return; }
        int region = PointerRegion();
        if (Input.GetMouseButtonDown(0)) { dragRegion = region; travel = 0; lastPointer = Input.mousePosition; }
        if (!Input.GetMouseButton(0) || region == 0 || region != dragRegion) { dragRegion = 0; travel = 0; }
        if (dragRegion != 0)
        {
            Vector2 pointer = Input.mousePosition;
            travel += Vector2.Distance(pointer, lastPointer) * (1080f / Mathf.Max(1, Screen.height));
            lastPointer = pointer;
            if (travel >= strokePixels)
            {
                reaction = dragRegion; until = Time.unscaledTime + reactionSeconds; travel = 0;
                BeginReaction(reaction);
            }
        }
        float target = Time.unscaledTime < until ? 1f : 0f;
        blend = Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime * 4f);
        armBlend = Mathf.MoveTowards(armBlend, reaction == 2 ? target : 0f, Time.unscaledDeltaTime * 4f);
        if (ownsReaction && blend <= 0f && armBlend <= 0f) EndReaction();
    }
    private void BeginReaction(int region)
    {
        if (faceLayer < 0) return;
        if (!ownsReaction)
        {
            originalBody = character.GetCurrentAnimatorStateInfo(0);
            originalFace = character.IsInTransition(faceLayer)
                ? character.GetNextAnimatorStateInfo(faceLayer) : character.GetCurrentAnimatorStateInfo(faceLayer);
            faceValues.Clear();
            foreach (var mesh in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (mesh.sharedMesh != null)
                    foreach (string shape in new[] { "mogu", "mogumogu", "a_ll" })
                    {
                        int index = mesh.sharedMesh.GetBlendShapeIndex(shape);
                        if (index >= 0) faceValues.Add(new FaceValue { mesh = mesh, index = index, value = mesh.GetBlendShapeWeight(index) });
                    }
            originalFaceWeight = character.GetLayerWeight(faceLayer);
            rootPosition = character.transform.localPosition;
            rootRotation = character.transform.localRotation;
            ownsReaction = true;
            if (originalBody.fullPathHash != StandingState)
                character.CrossFadeInFixedTime(StandingState, .2f, 0);
        }
        returningFace = false;
        int state = region == 1 ? MoguState : GuardFaceState;
        if (activeFace == state) return; // Repeated strokes extend the reaction, not restart the Animator.
        activeFace = state;
        character.SetLayerWeight(faceLayer, 1f);
        character.CrossFadeInFixedTime(state, .15f, faceLayer, 0f);
    }
    private int ReturnFaceState => originalFace.fullPathHash != 0 &&
        originalFace.fullPathHash != MoguState && originalFace.fullPathHash != GuardFaceState
        ? originalFace.fullPathHash : NeutralFaceState;

    private void EndReaction()
    {
        if (!ownsReaction || returningFace || character == null) return;
        returningFace = true; activeFace = 0;
        if (!character.isActiveAndEnabled) return;
        // Restart the original expression; a saved normalized time may already be beyond its end.
        character.CrossFadeInFixedTime(ReturnFaceState, .15f, faceLayer, 0f);
        if (originalBody.fullPathHash != 0 && originalBody.fullPathHash != StandingState &&
            character.GetCurrentAnimatorStateInfo(0).fullPathHash == StandingState)
            character.CrossFade(originalBody.fullPathHash, .2f, 0, originalBody.normalizedTime);
    }
    private void CompleteFaceReturn(bool immediate)
    {
        if (!ownsReaction || !returningFace || character == null || !character.isActiveAndEnabled) return;
        if (immediate) character.Play(ReturnFaceState, faceLayer, 0f);
        else if (character.IsInTransition(faceLayer) || character.GetCurrentAnimatorStateInfo(faceLayer).fullPathHash != ReturnFaceState) return;
        // The normal blink clip does not key these three reaction-only shapes.
        foreach (var value in faceValues)
            if (value.mesh != null) value.mesh.SetBlendShapeWeight(value.index, value.value);
        faceValues.Clear();
        character.SetLayerWeight(faceLayer, originalFaceWeight);
        RestoreRoot();
        ownsReaction = returningFace = false; activeFace = reaction = 0;
    }
    private void RestoreRoot()
    {
        if (!ownsReaction || character == null) return;
        character.transform.localPosition = rootPosition;
        character.transform.localRotation = rootRotation;
    }
    private int PointerRegion()
    {
        if (surface == null || !surface.isActiveAndEnabled || surface.texture != previewCamera.targetTexture)
        {
            surface = null;
            foreach (var image in GetComponentsInChildren<RawImage>())
                if (image.isActiveAndEnabled && image.texture == previewCamera.targetTexture) { surface = image; break; }
        }
        if (surface == null || surface.canvas == null) return 0;
        var canvas = surface.canvas;
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(surface.rectTransform, Input.mousePosition, uiCamera, out var p)) return 0;
        Rect rect = surface.rectTransform.rect;
        if (rect.width <= 0 || rect.height <= 0 || !rect.Contains(p)) return 0;
        if (EventSystem.current != null)
        {
            hits.Clear(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Input.mousePosition }, hits);
            if (hits.Count > 0)
            {
                var hit = hits[0].gameObject.transform;
                if (!hit.IsChildOf(transform) || hit.GetComponentInParent<Selectable>() != null) return 0;
            }
        }
        Rect uv = surface.uvRect;
        Ray ray = previewCamera.ViewportPointToRay(new Vector3(uv.x + (p.x - rect.x) / rect.width * uv.width, uv.y + (p.y - rect.y) / rect.height * uv.height));
        float scale = Mathf.Abs(character.transform.lossyScale.y);
        if (Touches(ray, head.position + character.transform.up * (.055f * scale), faceRadius * scale)) return 1;
        if (Touches(ray, chest.position + character.transform.forward * (.06f * scale), chestRadius * scale)) return 2;
        return 0;
    }
    private static bool Touches(Ray ray, Vector3 center, float radius)
    {
        Vector3 offset = center - ray.origin;
        float distance = Vector3.Dot(offset, ray.direction);
        return distance > 0 && (offset - ray.direction * distance).sqrMagnitude <= radius * radius;
    }
    private void LateUpdate()
    {
        if (!Ready) return;
        RestoreRoot();
        CompleteFaceReturn(false);
        if (blend <= 0f) return;
        if (armBlend > 0)
        {
            CrossArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 1);
            CrossArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, -1);
        }
    }
    // Two-bone IK creates a standing X guard without reusing a flying combat pose.
    private void CrossArm(HumanBodyBones upperId, HumanBodyBones lowerId, HumanBodyBones handId, float side)
    {
        Transform upper = character.GetBoneTransform(upperId), lower = character.GetBoneTransform(lowerId), hand = character.GetBoneTransform(handId);
        if (upper == null || lower == null || hand == null) return;
        Quaternion upperBase = upper.localRotation, lowerBase = lower.localRotation;
        bones.Add(new BoneState { bone = upper, rotation = upperBase });
        bones.Add(new BoneState { bone = lower, rotation = lowerBase });
        float scale = Mathf.Abs(character.transform.lossyScale.y);
        Transform root = character.transform;
        Vector3 target = chest.position + root.right * (side * .13f * scale) + root.up * (.13f * scale) + root.forward * ((side > 0 ? .22f : .18f) * scale);
        float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, hand.position);
        if (a < .001f || b < .001f) return;
        Vector3 direction = (target - upper.position).normalized;
        float d = Mathf.Clamp(Vector3.Distance(target, upper.position), Mathf.Abs(a - b) + .001f, a + b - .001f);
        target = upper.position + direction * d;
        Vector3 pole = -root.up - root.right * side * .65f + root.forward * .2f;
        Vector3 bend = Vector3.ProjectOnPlane(pole, direction).normalized;
        float along = (a * a - b * b + d * d) / (2f * d);
        Vector3 elbow = upper.position + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
        upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, elbow - upper.position) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) * lower.rotation;
        Quaternion upperTarget = upper.localRotation, lowerTarget = lower.localRotation;
        upper.localRotation = Quaternion.Slerp(upperBase, upperTarget, armBlend);
        lower.localRotation = Quaternion.Slerp(lowerBase, lowerTarget, armBlend);
    }
    private void RestorePose()
    {
        for (int i = bones.Count - 1; i >= 0; i--) if (bones[i].bone != null) bones[i].bone.localRotation = bones[i].rotation;
        bones.Clear();
    }
    private void ResetReaction()
    {
        RestorePose();
        dragRegion = reaction = 0; travel = until = blend = armBlend = 0;
        EndReaction();
        CompleteFaceReturn(true);
    }
    private void OnDisable() => ResetReaction();
    private void OnDestroy() => ResetReaction();
}
