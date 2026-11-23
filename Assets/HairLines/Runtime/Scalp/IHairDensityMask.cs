using UnityEngine;

namespace HairLines
{
    public interface IHairDensityMask
    {
        float Evaluate(Vector2 uv);
    }
}
