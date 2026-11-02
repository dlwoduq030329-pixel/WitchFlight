using UnityEngine;

// Dedicated cosmetic copy: no CatAvatar, summon AI, hit points or target registration.
public sealed class FestivalCatVisual : MonoBehaviour
{
    [SerializeField] private GameObject catPrefab;
    [SerializeField] private GameObject planePrefab;
    private void Awake()
    {
        var cat = Instantiate(catPrefab, transform);
        var plane = Instantiate(planePrefab, transform);
        plane.transform.localPosition = Vector3.zero;
        plane.transform.localRotation = Quaternion.identity;
        plane.transform.localScale = Vector3.one * .36f;
        var animator = cat.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            animator.Play("cat - sitting idle", 0, 0f);
            animator.Update(0f);
        }
        cat.transform.localPosition = Vector3.zero;
        cat.transform.localRotation = Quaternion.identity;
        if (CatSeatGeometry.TryBounds(cat.transform, out Bounds bounds))
        {
            float scale = .32f / Mathf.Max(.001f, bounds.size.y);
            cat.transform.localScale *= scale;
            float surface = CatSeatGeometry.SurfaceHeight(plane.transform, transform.position, transform.rotation, 0, -.04f, 0);
            cat.transform.localPosition = new Vector3(0, surface - bounds.min.y * scale + .003f, -.04f);
        }
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
