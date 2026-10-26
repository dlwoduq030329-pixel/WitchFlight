using UnityEngine;

// Local presentation only: no player colliders, damage, input or network authority.
public sealed class CatAvatar : MonoBehaviour
{
    public Transform Cat { get; private set; }
    public Transform Plane { get; private set; }
    public int BodyIndex { get; private set; } = 1;
    private Animator animator;
    private WitchCatAppearance appearance;
    private CatCompanionTable table;
    private Renderer[] renderers;
    private Transform[] children;
    private int currentLayer = -1;
    private float idleAt;
    private bool pendingIdle;
    private SkinnedMeshRenderer eyeRenderer;
    private int closedEyeShape = -1, groomingStateHash, kneadingStateHash;
    private bool wasClosingEyes;
    private Transform lookHead;
    private Quaternion animatedHeadRotation;
    private bool cursorLookApplied;
    private Vector2 cursorLook;

    public static CatAvatar Create(Transform parent, int body, int eyes, bool plane, GameObject existingCat = null)
    {
        var table = CatCompanionTable.Default;
        if (table == null || table.catPrefab == null) return null;
        var root = new GameObject("WitchCat companion");
        var avatar = root.AddComponent<CatAvatar>();
        avatar.table = table;
        var cat = existingCat != null ? existingCat : Instantiate(table.catPrefab);
        cat.transform.SetParent(root.transform, false);
        cat.transform.localPosition = Vector3.zero;
        cat.transform.localRotation = Quaternion.Euler(table.catEuler);
        cat.transform.localScale = Vector3.one;
        cat.SetActive(true);
        avatar.Cat = cat.transform;
        avatar.animator = cat.GetComponentInChildren<Animator>(true);
        if (avatar.animator != null)
        {
            avatar.animator.applyRootMotion = false; avatar.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            avatar.animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            var idle = table.GetAnimation(CatAnimation.SittingIdle);
            if (idle != null) { avatar.animator.Play(idle.stateName, 0, 0); avatar.animator.Update(0); }
        }
        foreach (Collider collider in cat.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        avatar.appearance = cat.GetComponent<WitchCatAppearance>();
        avatar.SetAppearance(body, eyes);
        if (CatSeatGeometry.TryBounds(cat.transform, out Bounds bounds))
        {
            float size = Mathf.Max(.0001f, bounds.size.y);
            cat.transform.localScale *= table.catHeight / size;
            // Imported skinned-renderer bounds include animation padding: use the actual idle mesh.
            cat.transform.localPosition = new Vector3(table.catSeatOffset.x, -bounds.min.y * (table.catHeight / size), table.catSeatOffset.z);
        }
        if (table.paperAirplanePrefab != null)
        {
            avatar.Plane = Instantiate(table.paperAirplanePrefab, root.transform).transform;
            avatar.Plane.localPosition = Vector3.zero;
            avatar.Plane.localRotation = Quaternion.identity;
            avatar.Plane.localScale = table.planeScale;
            avatar.Plane.gameObject.SetActive(plane);
            if (plane)
            {
                float surface = CatSeatGeometry.SurfaceHeight(avatar.Plane, root.transform.position, root.transform.rotation,
                    table.catSeatOffset.x, table.catSeatOffset.z, 0);
                cat.transform.localPosition += Vector3.up * (surface + table.catSeatOffset.y);
            }
        }
        avatar.renderers = root.GetComponentsInChildren<Renderer>(true);
        avatar.children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in avatar.children)
            if (child.name == "head") { avatar.lookHead = child; break; }
        foreach (var skin in cat.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (skin.sharedMesh != null && skin.sharedMesh.GetBlendShapeIndex("eye") >= 0)
            { avatar.eyeRenderer = skin; avatar.closedEyeShape = skin.sharedMesh.GetBlendShapeIndex("eye"); break; }
        var grooming = table.GetAnimation(CatAnimation.Grooming);
        if (grooming != null) avatar.groomingStateHash = Animator.StringToHash(grooming.stateName);
        var kneading = table.GetAnimation(CatAnimation.Knead);
        if (kneading != null) avatar.kneadingStateHash = Animator.StringToHash(kneading.stateName);
        root.transform.SetParent(parent, false);
        avatar.Play(CatAnimation.SittingIdle, false);
        return avatar;
    }

    public void SetAppearance(int body, int eyes)
    {
        BodyIndex = Mathf.Clamp(body, 1, 7);
        appearance?.ApplyAccountIndices(body, eyes);
    }

    public void Play(CatAnimation animation, bool returnToIdle = true)
    {
        var entry = table.GetAnimation(animation);
        if (entry == null || animator == null || !animator.HasState(0, Animator.StringToHash(entry.stateName))) return;
        animator.CrossFadeInFixedTime(entry.stateName, .12f, 0, 0);
        pendingIdle = returnToIdle;
        idleAt = Time.unscaledTime + Mathf.Max(.15f, entry.seconds);
    }
    private void Update()
    {
        RestoreCursorLook();
        if (pendingIdle && Time.unscaledTime >= idleAt) Play(CatAnimation.SittingIdle, false);
    }
    // Called after the lobby's orbit positioning and Animator evaluation. Only the
    // head moves: the paper plane and petting hit area keep their existing motion.
    public void FollowLobbyCursor(Camera camera, Ray pointer, bool follow)
    {
        RestoreCursorLook();
        if (lookHead == null || camera == null) return;
        Vector2 desired = Vector2.zero;
        if (follow && table.lobbyFollowCursor && !pendingIdle)
        {
            var plane = new Plane(camera.transform.forward, lookHead.position);
            if (plane.Raycast(pointer, out float distance))
            {
                Vector3 offset = pointer.GetPoint(distance) - lookHead.position;
                float depth = Mathf.Max(.2f, Mathf.Abs(Vector3.Dot(lookHead.position - camera.transform.position, camera.transform.forward)) * .5f);
                desired = new Vector2(
                    Mathf.Clamp(Mathf.Atan2(Vector3.Dot(offset, camera.transform.right), depth) * Mathf.Rad2Deg, -table.lobbyHeadYaw, table.lobbyHeadYaw),
                    Mathf.Clamp(Mathf.Atan2(Vector3.Dot(offset, camera.transform.up), depth) * Mathf.Rad2Deg, -table.lobbyHeadPitch, table.lobbyHeadPitch));
            }
        }
        cursorLook = Vector2.Lerp(cursorLook, desired, 1f - Mathf.Exp(-table.lobbyLookResponse * Time.unscaledDeltaTime));
        animatedHeadRotation = lookHead.localRotation;
        lookHead.rotation = Quaternion.AngleAxis(-cursorLook.x, camera.transform.up) *
            Quaternion.AngleAxis(cursorLook.y, camera.transform.right) * lookHead.rotation;
        cursorLookApplied = true;
    }
    public void StopCursorLook()
    {
        RestoreCursorLook();
        cursorLook = Vector2.zero;
    }
    private void RestoreCursorLook()
    {
        if (cursorLookApplied && lookHead != null) lookHead.localRotation = animatedHeadRotation;
        cursorLookApplied = false;
    }
    private void OnDisable() => StopCursorLook();
    private void LateUpdate()
    {
        if (animator == null || eyeRenderer == null || closedEyeShape < 0) return;
        bool current = ClosesEyes(animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
        float closing = current ? 1f : 0f;
        if (animator.IsInTransition(0))
        {
            bool next = ClosesEyes(animator.GetNextAnimatorStateInfo(0).shortNameHash);
            closing = Mathf.Lerp(current ? 1f : 0f, next ? 1f : 0f,
                Mathf.Clamp01(animator.GetAnimatorTransitionInfo(0).normalizedTime));
        }
        // Petting/kneading and grooming close the eyes; releasing petting blends back to open idle.
        // Override after Animator because the clips also write the eye blend shape.
        if (closing > 0 || wasClosingEyes) eyeRenderer.SetBlendShapeWeight(closedEyeShape, closing * 100f);
        wasClosingEyes = closing > 0;
    }
    private bool ClosesEyes(int stateHash) => stateHash != 0 &&
        (stateHash == groomingStateHash || stateHash == kneadingStateHash);
    public void SetVisible(bool visible)
    {
        if (renderers != null) foreach (var renderer in renderers) if (renderer != null) renderer.enabled = visible;
    }
    public bool Intersects(Ray ray)
    {
        foreach (var renderer in renderers)
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.bounds.IntersectRay(ray)) return true;
        return false;
    }
    public void SetLayer(int layer)
    {
        if (currentLayer == layer) return;
        currentLayer = layer;
        foreach (Transform child in children) if (child != null) child.gameObject.layer = layer;
    }
}
