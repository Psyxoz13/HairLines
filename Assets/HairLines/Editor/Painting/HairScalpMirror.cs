using UnityEngine;

namespace HairLines.EditorTools
{
    public readonly struct HairScalpMirror
    {
        private readonly Matrix4x4 _reflection;

        public HairScalpMirror(Renderer scalp, Mesh mesh) : this(scalp, mesh != null ? mesh.bounds.center.x : 0f)
        {
        }

        public HairScalpMirror(Renderer scalp, float axis)
        {
            Matrix4x4 flip = Matrix4x4.TRS(new Vector3(2f * axis, 0f, 0f), Quaternion.identity,
                                           new Vector3(-1f, 1f, 1f));
            _reflection = scalp.transform.localToWorldMatrix * flip * scalp.transform.worldToLocalMatrix;
        }

        public Vector3 Point(Vector3 world)
        {
            return _reflection.MultiplyPoint3x4(world);
        }

        public Vector3 Direction(Vector3 world)
        {
            return _reflection.MultiplyVector(world);
        }

        public Ray Ray(Ray world)
        {
            return new Ray(Point(world.origin), Direction(world.direction));
        }

        public HairRootFrame Frame(in HairRootFrame world)
        {
            return new HairRootFrame(Point(world.Position), Direction(world.Normal).normalized,
                                     Direction(world.Tangent).normalized, world.Uv);
        }
    }
}
