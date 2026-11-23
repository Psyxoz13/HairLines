using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairSkyRayMarchSettings
    {
        [Tooltip("Steps each of the six rays takes through the density volume.")]
        [SerializeField, Range(4, 64)]
        private int _steps = 24;

        [Tooltip("How far along each ray other hair still occludes the sky, in centimetres.")]
        [SerializeField, Range(1f, 200f)]
        private float _distance = 100f;

        public int Steps { get => _steps; set => _steps = Mathf.Clamp(value, 4, 64); }
        public float Distance { get => _distance; set => _distance = Mathf.Clamp(value, 1f, 200f); }
    }

    [Serializable]
    public class HairSkyConeTraceSettings
    {
        [Tooltip("Full opening angle of each of the six cones, in degrees. Wider cones reach " +
                 "further in fewer steps and blur the occlusion more.")]
        [SerializeField, Range(10f, 120f)]
        private float _angle = 90f;

        [Tooltip("Samples each cone takes at most. Each step is as long as half the cone's width " +
                 "there, so the distance covered grows with every step.")]
        [SerializeField, Range(1, 16)]
        private int _steps = 8;

        [Tooltip("How far along each cone other hair still occludes the sky, in centimetres.")]
        [SerializeField, Range(1f, 200f)]
        private float _distance = 100f;

        public float Angle { get => _angle; set => _angle = Mathf.Clamp(value, 10f, 120f); }
        public int Steps { get => _steps; set => _steps = Mathf.Clamp(value, 1, 16); }
        public float Distance { get => _distance; set => _distance = Mathf.Clamp(value, 1f, 200f); }
    }

    [Serializable]
    public class HairSkyGridSettings
    {
        [Tooltip("Cells of the precomputed grid along each axis of a groom's volume.")]
        [SerializeField, Range(4, 128)]
        private int _resolution = 16;

        [Tooltip("Full opening angle of the cones traced from each cell, in degrees.")]
        [SerializeField, Range(10f, 120f)]
        private float _angle = 90f;

        [Tooltip("Samples each cone takes at most.")]
        [SerializeField, Range(1, 16)]
        private int _steps = 8;

        [Tooltip("How far along each cone other hair still occludes the sky, in centimetres.")]
        [SerializeField, Range(1f, 200f)]
        private float _distance = 100f;

        public int Resolution { get => _resolution; set => _resolution = Mathf.Clamp(value, 4, 128); }
        public float Angle { get => _angle; set => _angle = Mathf.Clamp(value, 10f, 120f); }
        public int Steps { get => _steps; set => _steps = Mathf.Clamp(value, 1, 16); }
        public float Distance { get => _distance; set => _distance = Mathf.Clamp(value, 1f, 200f); }
    }
}
