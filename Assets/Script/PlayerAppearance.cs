using UnityEngine;

public class PlayerAppearance : MonoBehaviour
{
    [SerializeField] private Transform hairRoot;
    [SerializeField, Min(0)] private int maxHairLength = 10;
    [SerializeField, Min(0.01f)] private float shortestScaleY = 0.5f;
    [SerializeField, Min(0.01f)] private float longestScaleY = 1.5f;

    private Vector3 defaultHairScale;
    private bool hasCachedDefaultScale;

    public void ApplyHairLength(int hairLength)
    {
        if (hairRoot == null)
            return;

        if (!hasCachedDefaultScale)
        {
            defaultHairScale = hairRoot.localScale;
            hasCachedDefaultScale = true;
        }

        float normalizedLength = maxHairLength <= 0
            ? 0f
            : Mathf.Clamp01((float)hairLength / maxHairLength);

        Vector3 scale = defaultHairScale;
        scale.y = defaultHairScale.y *
                  Mathf.Lerp(shortestScaleY, longestScaleY, normalizedLength);
        hairRoot.localScale = scale;
    }
}