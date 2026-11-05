using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Attach to the customization preview RawImage, not the lobby preview.
[DisallowMultipleComponent]
[RequireComponent(typeof(RawImage))]
[DefaultExecutionOrder(200)]
public sealed class CustomizationPreviewRotation : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    [SerializeField] private Transform characterRoot;
    [SerializeField, Min(0f)] private float degreesPerPreviewWidth = 360f;

    [SerializeField] private Transform tabRoot;
    [SerializeField] private Camera previewCamera;
    [SerializeField, Min(1f)] private float faceZoom = 2.5f;

    private RawImage previewImage;
    private Rect originalUvRect;
    private Transform head;
    private Button[] tabButtons;
    private float targetZoom = 1f;
    private float currentZoom = 1f;
    private const float ZoomPerScrollStep = .05f;
    private const float ZoomResponse = 6f;
    private RectTransform previewRect;
    private Quaternion originalRotation;
    private Vector2 lastPointerPosition;
    private float yaw;
    private int pointerId;
    private bool captured, dragging, rotated;

    private void Awake()
    {
        previewRect = (RectTransform)transform;
        previewImage = GetComponent<RawImage>();
    }

    private void OnEnable()
    {
        dragging = rotated = false;
        yaw = 0f;
        captured = characterRoot != null;
        if (captured) originalRotation = characterRoot.localRotation;
        originalUvRect = previewImage.uvRect;
        targetZoom = currentZoom = 1f;
        ResolveHead();
        tabButtons = tabRoot != null ? tabRoot.GetComponentsInChildren<Button>(true) : null;
        if (tabButtons != null)
            foreach (Button button in tabButtons) button.onClick.AddListener(ResetPreview);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!isActiveAndEnabled || !captured || characterRoot == null ||
            !characterRoot.gameObject.activeInHierarchy ||
            eventData.button != PointerEventData.InputButton.Left) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                previewRect, eventData.position, eventData.pressEventCamera, out lastPointerPosition)) return;
        pointerId = eventData.pointerId;
        dragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging || eventData.pointerId != pointerId ||
            eventData.button != PointerEventData.InputButton.Left) return;
        if (characterRoot == null || !characterRoot.gameObject.activeInHierarchy)
        {
            dragging = false;
            return;
        }
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                previewRect, eventData.position, eventData.pressEventCamera, out Vector2 point)) return;
        float delta = point.x - lastPointerPosition.x;
        lastPointerPosition = point;
        yaw = Mathf.Repeat(yaw - delta / Mathf.Max(1f, previewRect.rect.width) * degreesPerPreviewWidth, 360f);
        rotated = true;
        ApplyRotation();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.pointerId == pointerId) dragging = false;
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (!isActiveAndEnabled || characterRoot == null ||
            !characterRoot.gameObject.activeInHierarchy || previewCamera == null) return;
        ResolveHead();
        if (head == null) return;
        targetZoom = Mathf.Clamp(targetZoom + eventData.scrollDelta.y * ZoomPerScrollStep,
            1f, Mathf.Max(1f, faceZoom));
    }

    // Runtime tab listeners run after the existing authored tab/camera events.
    public void ResetPreview()
    {
        if (captured && characterRoot != null) characterRoot.localRotation = originalRotation;
        dragging = rotated = false;
        targetZoom = currentZoom = 1f;
        yaw = 0f;
        if (previewImage != null) previewImage.uvRect = originalUvRect;
    }

    private void ResolveHead()
    {
        if (head != null || characterRoot == null) return;
        Animator animator = characterRoot.GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman && animator.avatar != null && animator.avatar.isValid)
            head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null)
            foreach (Transform bone in characterRoot.GetComponentsInChildren<Transform>(true))
                if (bone.name == "Head") { head = bone; break; }
    }

    private void LateUpdate()
    {
        // Keep the inspection angle while the existing character animation runs.
        if (rotated && characterRoot != null && characterRoot.gameObject.activeInHierarchy)
            ApplyRotation();
        UpdateZoom(Time.unscaledDeltaTime);
    }

    private void UpdateZoom(float deltaTime)
    {
        currentZoom = Mathf.Lerp(currentZoom, targetZoom, 1f - Mathf.Exp(-ZoomResponse * deltaTime));
        if (Mathf.Abs(currentZoom - targetZoom) < .001f) currentZoom = targetZoom;
        if (currentZoom <= 1f)
        {
            previewImage.uvRect = originalUvRect;
            return;
        }
        if (head == null || previewCamera == null || characterRoot == null ||
            !characterRoot.gameObject.activeInHierarchy) return;
        Vector3 focus = previewCamera.WorldToViewportPoint(head.position);
        if (focus.z <= 0f) return;
        float width = originalUvRect.width / currentZoom;
        float height = originalUvRect.height / currentZoom;
        // Keep the face's horizontal placement so the customization panel stays clear.
        float x = focus.x - (focus.x - originalUvRect.x) / currentZoom;
        float y = focus.y - height * .55f;
        x = Mathf.Clamp(x, originalUvRect.xMin, originalUvRect.xMax - width);
        y = Mathf.Clamp(y, originalUvRect.yMin, originalUvRect.yMax - height);
        previewImage.uvRect = new Rect(x, y, width, height);
    }

    private void ApplyRotation() =>
        characterRoot.localRotation = Quaternion.AngleAxis(yaw, Vector3.up) * originalRotation;

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) dragging = false;
    }

    private void OnDisable()
    {
        ResetPreview();
        captured = false;
        if (tabButtons != null)
            foreach (Button button in tabButtons)
                if (button != null) button.onClick.RemoveListener(ResetPreview);
        tabButtons = null;
    }
}
