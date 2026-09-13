using UnityEngine;
using UnityEngine.UI;

namespace Lovatto.DamagePointer
{
    /// <summary>
    /// Base type for indicator visuals. Derive this to implement custom indicator behavior.
    /// </summary>
    public abstract class DamageIndicatorBase : MonoBehaviour
    {
        protected RectTransform Rect { get; private set; }
        protected Image Image { get; private set; }
        protected Material RuntimeMaterial { get; set; }

        /// <summary>
        /// Called once when the pooled slot is created.
        /// </summary>
        public virtual void Initialize(RectTransform rect, Image image)
        {
            Rect = rect;
            Image = image;
        }

        /// <summary>
        /// Called whenever this pooled slot is activated.
        /// </summary>
        public abstract void Activate(in DamageIndicatorActivationContext context);

        /// <summary>
        /// Called every frame while active. Return false to deactivate this slot.
        /// </summary>
        public abstract bool Tick(in DamageIndicatorTickContext context);

        /// <summary>
        /// Called when the slot is deactivated but kept in the pool.
        /// </summary>
        public virtual void Deactivate()
        {
        }

        /// <summary>
        /// Called when the pool is being destroyed.
        /// </summary>
        public virtual void Cleanup()
        {
            if (RuntimeMaterial != null)
            {
                Object.Destroy(RuntimeMaterial);
                RuntimeMaterial = null;
            }
        }
    }
}