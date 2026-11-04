using UnityEngine;
using UnityEngine.Timeline;

namespace Lovatto.Crosshair
{
    public sealed class bl_HitMarker : bl_HitMarkerBase
    {
        [SerializeField] private GameObject content = null;
        public bl_MarkerRig[] markers;

        /// <summary>
        /// 
        /// </summary>
        private void Awake()
        {
            SetActive(false);
            SetActiveChild(false);
        }

        /// <summary>
        /// 
        /// </summary>
        public override void OnHit(string markerName = "default", Color tint = default(Color))
        {
            if (tint == default(Color))
            {
                tint = Color.white;
            }

            content.SetActive(true);

            bool found = false;
            foreach (var item in markers)
            {
                if (item == null) continue;

                if (item.Name == markerName)
                {
                    item.OnHit(tint);
                    found = true;
                }
                else
                {
                    item.SetActive(false);
                }
            }

            if (!found)
            {
                markers[0].OnHit(tint);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="color"></param>
        public void SetColor(Color color)
        {
            foreach (var item in markers)
            {
                if (item == null) continue;

                item.SetColor(color);
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="active"></param>
        public void SetActiveChild(bool active)
        {
            foreach (var item in markers)
            {
                if (item == null) continue;

                item.SetActive(active);
            }
        }
    }

    
    }