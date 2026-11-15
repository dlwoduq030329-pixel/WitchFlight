namespace PixeLadder.EasyLiquidFill
{
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// The core visual component that handles the rendering and geometry of the liquid effect.
    /// This component is controlled by the LiquidAnimationController.
    /// </summary>
    [ExecuteAlways]
    public class LiquidAnimator : MonoBehaviour
    {
        #region Fields
        [Header("UI References")]
        [SerializeField] private RectTransform liquid;
        [SerializeField] private Image fillImage;
        [SerializeField] private Image surfaceImage;
        [SerializeField] private Image surfaceMask;
        [SerializeField] private RectTransform fillMasked;
        [SerializeField] private RectTransform surfaceMasked;

        [Header("Surface Color Settings")]
        [Tooltip("How strongly the surface color is tinted. 0 is pure black/white, 1 is the full color.")]
        [SerializeField, Range(0f, 1f)] private float surfaceTintStrength = 0.9f;
        [Tooltip("How sensitive the surface highlight is to the container's angle.")]
        [SerializeField, Range(0f, 10f)] private float surfaceAngleMultiplier = 4.0f;

        // Private State
        private RectTransform rt;
        private Image fillMaskedImage;
        private Image surfaceMaskedImage;
        private Color surfaceColor;
        private float currentFill = 1f;
        private float currentAngleX = 30f;
        private float currentAngleZ = 0f;
        #endregion

        #region Private Struct
        private struct SurfaceData
        {
            public float width;
            public float height;
            public float yPos;
            public float theta;
        }
        #endregion

        #region Unity Lifecycle
        private void Start()
        {
            rt = transform as RectTransform;
            if (fillMasked) fillMaskedImage = fillMasked.GetComponent<Image>();
            if (surfaceMasked) surfaceMaskedImage = surfaceMasked.GetComponent<Image>();
        }

        private void Update()
        {
            if (rt == null) rt = transform as RectTransform;
            if (fillImage == null) return;

            SurfaceData surfaceData = CalculateSurfaceData();

            UpdateSurfaceGeometry(surfaceData);
            UpdateFillAndRotation(surfaceData);
            UpdateMasks();
            UpdateSurfaceColor(surfaceData);
        }
        #endregion

        #region Private Logic
        private SurfaceData CalculateSurfaceData()
        {
            SurfaceData surfaceData = new SurfaceData();
            Rect fillRect = fillImage.rectTransform.rect;
            float radius = fillRect.width * 0.5f;
            surfaceData.theta = currentAngleX * Mathf.Deg2Rad;
            float clipped = Mathf.Clamp01(1f - Mathf.Pow(2f * currentFill - 1f, 2f));
            surfaceData.width = 2f * radius * Mathf.Sqrt(clipped);
            surfaceData.height = surfaceData.width * Mathf.Sin(surfaceData.theta);
            surfaceData.yPos = (currentFill - 0.5f) * fillRect.height * Mathf.Cos(surfaceData.theta);
            return surfaceData;
        }

        private void UpdateSurfaceGeometry(SurfaceData surfaceData)
        {
            if (!surfaceImage || !surfaceMask) return;
            bool isSurfaceVisible = currentAngleX >= 0f;
            surfaceImage.gameObject.SetActive(isSurfaceVisible);
            surfaceMask.gameObject.SetActive(!isSurfaceVisible);
            if (isSurfaceVisible)
            {
                surfaceImage.rectTransform.sizeDelta = new Vector2(surfaceData.width, surfaceData.height);
                surfaceImage.rectTransform.anchoredPosition = new Vector2(0f, surfaceData.yPos);
            }
            else
            {
                surfaceMask.rectTransform.sizeDelta = new Vector2(surfaceData.width, Mathf.Abs(surfaceData.height));
                surfaceMask.rectTransform.anchoredPosition = new Vector2(0f, surfaceData.yPos);
            }
        }

        private void UpdateFillAndRotation(SurfaceData surfaceData)
        {
            if (!fillImage || !liquid) return;
            fillImage.fillAmount = CalculateFillAmount(currentFill, surfaceData.theta);
            liquid.rotation = Quaternion.Euler(0f, 0f, currentAngleZ);
        }

        private void UpdateMasks()
        {
            if (rt == null) return;
            if (fillMasked)
            {
                fillMasked.transform.rotation = transform.rotation;
                fillMasked.sizeDelta = rt.rect.size;
            }
            if (surfaceMasked)
            {
                surfaceMasked.position = transform.position;
                surfaceMasked.sizeDelta = rt.rect.size;
            }
        }

        private void UpdateSurfaceColor(SurfaceData surfaceData)
        {
            if (!surfaceImage) return;
            var darkColor = Color.Lerp(Color.black, surfaceColor, surfaceTintStrength);
            var lightColor = Color.Lerp(Color.white, surfaceColor, surfaceTintStrength);
            var t = (currentAngleX + currentAngleZ) / 90f * surfaceAngleMultiplier;
            surfaceImage.color = Color.Lerp(lightColor, darkColor, t);
        }

        private float CalculateFillAmount(float fill, float theta)
        {
            const float EPS = 1e-6f;
            float s = Mathf.Cos(theta);
            if (s <= EPS)
            {
                if (fill < 0.5f) return 0f;
                if (fill > 0.5f) return 1f;
                return 0.5f;
            }
            float low = 0.5f - 0.5f * s;
            float high = 0.5f + 0.5f * s;
            if (fill <= low) return 0f;
            if (fill >= high) return 1f;
            return (fill - low) / (high - low);
        }
        #endregion

        #region Public API
        public void SetFill(float value) => currentFill = Mathf.Clamp01(value);
        public void SetInclination(float angleX, float angleZ)
        {
            currentAngleX = Mathf.Clamp(angleX, -90f, 90f);
            currentAngleZ = Mathf.Clamp(angleZ, -90f, 90f);
        }
        public void SetFillColor(Color color)
        {
            if (fillMaskedImage) fillMaskedImage.color = color;
            if (surfaceMaskedImage) surfaceMaskedImage.color = color;
        }
        public void SetSurfaceColor(Color color) => surfaceColor = color;
        #endregion
    }
}