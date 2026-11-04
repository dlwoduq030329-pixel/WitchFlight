using UnityEngine;
namespace ZakhanStylizedLootDrops
{
        public class ItemRotation : MonoBehaviour
    {
        [SerializeField] private Vector3 Axis;

        [SerializeField] private float Velocity = 50f;
        private void OnEnable()
        {
            transform.rotation = Quaternion.identity;
        }
        void FixedUpdate()
        {
            transform.RotateAround(transform.position, Axis, Velocity * Time.fixedDeltaTime);
        }
    }
}
