using UnityEngine;
using UnityEngine.UI;

namespace Lovatto.Crosshair
{
    public class bl_MarkerRig : MonoBehaviour
    {

        [Header("Settings")]
        public string Name = "default";
        [Tooltip("Machine State name in the animator.")]
        public string AnimationName = "Hit";

        public bool RandomRotation = true;
        public bool ignoreTintColor = false;
        public bool crossfadedAnimation = false;
        [Range(0, 90)] public float MaxAngleVariation = 15;

        [Header("References")]
        public GameObject content = null;
        public Animator animator = null;
        public RectTransform root = null;

        private Graphic[] graphics;


        /// <summary>
        /// 
        /// </summary>
        public void OnHit(Color tint = default(Color))
        {
            content.SetActive(true);
            // randomly rotate the root wach time this is called
            if (RandomRotation)
            {
                root.rotation = Quaternion.Euler(0, 0, Random.Range(-MaxAngleVariation, MaxAngleVariation));
            }
            if (animator != null)
            {
                if (crossfadedAnimation)
                {
                    animator.CrossFade(AnimationName, 0.1f, 0, 0);
                }
                else
                    animator.Play(AnimationName, 0, 0);
            }
            SetColor(tint);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="color"></param>
        public void SetColor(Color color)
        {
            if (ignoreTintColor) return;
            if (graphics == null)
            {
                graphics = root.GetComponentsInChildren<Graphic>(true);
            }
            foreach (var item in graphics)
            {
                item.color = color;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="active"></param>
        public void SetActive(bool active)
        {
            content.SetActive(active);
        }
    }
}