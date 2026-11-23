using UnityEngine;

namespace HairLines
{
    [AddComponentMenu("Hair Lines/Hair Pin")]
    public class HairPin : MonoBehaviour
    {
        public float Radius { get => _radius; set => _radius = Mathf.Max(MinimumRadius, value); }
        public float Compliance { get => _compliance; set => _compliance = Mathf.Max(0f, value); }
        public SkinnedMeshRenderer Surface { get => _surface; set => _surface = value; }

        private const float MinimumRadius = 0.001f;

        [Tooltip("Strand vertices whose rest position lies inside this sphere, in metres around the pin, " +
                 "are held by it.")]
        [SerializeField, Min(MinimumRadius)]
        private float _radius = 0.02f;

        [Tooltip("Metres per Newton. 0 holds the vertices exactly; larger values let them swing around " +
                 "the pin like a loose elastic.")]
        [SerializeField, Min(0f)]
        private float _compliance;

        [Tooltip("Empty: the held vertices follow this transform. Set: every held vertex is skinned with " +
                 "the bone weights of the closest point on this mesh, so it moves with the skin.")]
        [SerializeField]
        private SkinnedMeshRenderer _surface;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
