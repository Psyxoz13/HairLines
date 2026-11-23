using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairScalpSettings
    {
        public HairScalpMode Mode { get => _mode; set => _mode = value; }
        public Renderer Renderer { get => _renderer; set => _renderer = value; }
        public int Submesh { get => _submesh; set => _submesh = Mathf.Max(-1, value); }

        public float SphereRadius { get => _sphereRadius; set => _sphereRadius = Mathf.Max(0.001f, value); }
        public float SphereCoverage { get => _sphereCoverage; set => _sphereCoverage = Mathf.Clamp(value, 0.02f, 1f); }
        public Vector3 SphereCenter { get => _sphereCenter; set => _sphereCenter = value; }

        [Tooltip("Sphere grows hair on an analytic cap. Mesh grows it on the polygons of a mesh renderer.")]
        [SerializeField]
        private HairScalpMode _mode = HairScalpMode.Sphere;

        [Tooltip("Mesh or skinned mesh renderer the roots are bound to.")]
        [SerializeField]
        private Renderer _renderer;

        [Tooltip("Submesh the roots are sampled from. -1 uses every submesh.")]
        [SerializeField, Min(-1)]
        private int _submesh = -1;

        [Tooltip("Radius of the analytic scalp sphere, in metres.")]
        [SerializeField, Min(0.001f)]
        private float _sphereRadius = 0.10f;

        [Tooltip("Fraction of the sphere covered by hair, measured down from the crown.")]
        [SerializeField, Range(0.02f, 1f)]
        private float _sphereCoverage = 0.5f;

        [Tooltip("Centre of the analytic scalp sphere, in the local space of this object.")]
        [SerializeField]
        private Vector3 _sphereCenter = Vector3.zero;
    }
}
