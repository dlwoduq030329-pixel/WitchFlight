using UnityEngine;

public class PlayerAppearance : MonoBehaviour
{
    [SerializeField] private Transform hairRoot;
    [SerializeField] private string[] autoDetectHairRootNames = { "LongHair", "Hair_Back_rurune", "Hair" };
    [SerializeField, Min(0)] private int maxHairLength = 10;
    [SerializeField, Min(0.01f)] private float shortestScaleY = 0.5f;
    [SerializeField, Min(0.01f)] private float longestScaleY = 1.5f;

    private Vector3 defaultHairScale;
    private bool hasCachedDefaultScale;

    public void ApplyHairLength(int hairLength)
    {
        Transform root = ResolveHairRoot();
        if (root == null)
            return;

        if (!hasCachedDefaultScale)
        {
            defaultHairScale = root.localScale;
            hasCachedDefaultScale = true;
        }

        float normalizedLength = maxHairLength <= 0
            ? 0f
            : Mathf.Clamp01((float)hairLength / maxHairLength);

        Vector3 scale = defaultHairScale;
        scale.y = defaultHairScale.y *
                  Mathf.Lerp(shortestScaleY, longestScaleY, normalizedLength);
        // Temporarily disable hair scaling to diagnose missing hair in intro portraits.
        // root.localScale = scale;
    }

    private Transform ResolveHairRoot()
    {
        if (hairRoot != null)
            return hairRoot;

        foreach (Transform candidate in GetComponentsInChildren<Transform>(true))
        {
            if (autoDetectHairRootNames == null)
                continue;

            foreach (string candidateName in autoDetectHairRootNames)
            {
                if (candidate.name == candidateName)
                {
                    hairRoot = candidate;
                    return hairRoot;
                }
            }
        }

        return null;
    }
}
