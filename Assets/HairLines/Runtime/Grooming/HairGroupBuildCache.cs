using System;
using UnityEngine;

namespace HairLines
{
    internal readonly struct HairMaskKey : IEquatable<HairMaskKey>
    {
        private readonly int _texture;
        private readonly Hash128 _contents;
        private readonly int _flags;

        private HairMaskKey(Texture2D texture, bool enabled)
        {
            _texture = texture != null ? texture.GetInstanceID() : 0;
            _flags = enabled ? 1 : 0;

#if UNITY_EDITOR
            _contents = texture != null ? texture.imageContentsHash : default;
#else
            _contents = texture != null ? new Hash128(texture.updateCount, 0u, 0u, 0u) : default;
#endif
        }

        public static HairMaskKey Of(HairDensityMask mask, bool enabled)
        {
            return new HairMaskKey(mask != null && mask.IsUsable ? mask.Texture : null, enabled);
        }

        public static HairMaskKey Of(HairCombMap map)
        {
            return new HairMaskKey(map != null && map.IsUsable ? map.Texture : null, true);
        }

        public static HairMaskKey Of(HairColorMap map)
        {
            return new HairMaskKey(map != null && map.IsUsable ? map.Texture : null, true);
        }

        public bool Equals(HairMaskKey other)
        {
            return _texture == other._texture && _contents == other._contents && _flags == other._flags;
        }

        public override bool Equals(object other)
        {
            return other is HairMaskKey key && Equals(key);
        }

        public override int GetHashCode()
        {
            return _texture ^ (_flags * 397);
        }
    }

    internal readonly struct HairRootKey : IEquatable<HairRootKey>
    {
        private readonly int _surface;
        private readonly int _submesh;
        private readonly int _seed;
        private readonly HairMaskKey _mask;
        private readonly Matrix4x4 _toWorld;
        private readonly Vector4 _sphere;
        private readonly int _guides;

        public HairRootKey(HairSurface surface, int submesh, HairDensityMask mask, int seed,
                           HairRootFrame[] guideFrames)
        {
            _surface = surface.Fingerprint;
            _submesh = submesh;
            _seed = seed;
            _mask = HairMaskKey.Of(mask, true);
            _toWorld = surface.LocalToWorld;
            _sphere = Vector4.zero;
            _guides = Fingerprint(guideFrames);
        }

        public HairRootKey(HairScalpSettings scalp, int seed)
        {
            _surface = 0;
            _submesh = 0;
            _seed = seed;
            _mask = default;
            _toWorld = Matrix4x4.identity;
            _sphere = new Vector4(scalp.SphereCenter.x, scalp.SphereCenter.y, scalp.SphereCenter.z,
                                  scalp.SphereRadius * 1024f + scalp.SphereCoverage);
            _guides = 0;
        }

        private static int Fingerprint(HairRootFrame[] frames)
        {
            if (frames == null || frames.Length == 0)
            {
                return 0;
            }

            int hash = frames.Length;

            for (int i = 0; i < frames.Length; i++)
            {
                hash = hash * 397 ^ frames[i].Position.GetHashCode();
                hash = hash * 397 ^ frames[i].Uv.GetHashCode();
            }

            return hash;
        }

        public bool Equals(HairRootKey other)
        {
            return _surface == other._surface && _submesh == other._submesh && _seed == other._seed
                   && _mask.Equals(other._mask) && _toWorld == other._toWorld && _sphere == other._sphere
                   && _guides == other._guides;
        }

        public override bool Equals(object other)
        {
            return other is HairRootKey key && Equals(key);
        }

        public override int GetHashCode()
        {
            return _surface ^ (_seed * 397) ^ _submesh ^ _mask.GetHashCode();
        }
    }

    internal sealed class HairGroupBuildCache
    {
        public HairRootBinding[] Bindings { get; private set; }
        public HairRootFrame[] Roots { get; private set; }
        public Vector2[] Comb { get; private set; }
        public float[] OperatorWeights { get; private set; }
        public uint[] Colors { get; private set; }

        private HairRootKey _bindingKey;
        private int _bindingVersion;

        private HairRootKey _rootKey;
        private int _rootVersion = -1;
        private int _rootCount = -1;
        private Matrix4x4 _rootMatrix;

        private HairMaskKey _combKey;
        private HairMaskKey[] _operatorKeys;
        private HairMaskKey _rootColorKey;
        private HairMaskKey _tipColorKey;
        private HairMaskKey _tintKey;
        private bool _colorsValid;

        public bool NeedsBindings(in HairRootKey key, int count)
        {
            return Bindings == null || Bindings.Length < count || !_bindingKey.Equals(key);
        }

        public void SetBindings(in HairRootKey key, HairRootBinding[] bindings)
        {
            _bindingKey = key;
            Bindings = bindings;
            _bindingVersion++;
            Roots = null;
        }

        public bool NeedsRoots(in HairRootKey key, int count, in Matrix4x4 toLocal)
        {
            return Roots == null || Roots.Length != count || _rootVersion != _bindingVersion
                   || _rootCount != count || _rootMatrix != toLocal || !_rootKey.Equals(key);
        }

        public void SetRoots(in HairRootKey key, int count, in Matrix4x4 toLocal, HairRootFrame[] roots)
        {
            _rootKey = key;
            _rootVersion = _bindingVersion;
            _rootCount = count;
            _rootMatrix = toLocal;
            Roots = roots;
            Comb = null;
            OperatorWeights = null;
            Colors = null;
            _colorsValid = false;
        }

        public bool NeedsColors(in HairMaskKey root, in HairMaskKey tip, in HairMaskKey tint)
        {
            return !_colorsValid || !_rootColorKey.Equals(root) || !_tipColorKey.Equals(tip) ||
                   !_tintKey.Equals(tint);
        }

        public void SetColors(in HairMaskKey root, in HairMaskKey tip, in HairMaskKey tint, uint[] colors)
        {
            _rootColorKey = root;
            _tipColorKey = tip;
            _tintKey = tint;
            _colorsValid = true;
            Colors = colors;
        }

        public bool NeedsComb(in HairMaskKey key)
        {
            return Comb == null || Comb.Length != (Roots != null ? Roots.Length : 0)
                   || !_combKey.Equals(key);
        }

        public void SetComb(in HairMaskKey key, Vector2[] comb)
        {
            _combKey = key;
            Comb = comb;
        }

        public bool NeedsWeights(HairMaskKey[] keys, int count)
        {
            if (OperatorWeights == null || _operatorKeys == null || _operatorKeys.Length != count)
            {
                return true;
            }

            for (int i = 0; i < count; i++)
            {
                if (!_operatorKeys[i].Equals(keys[i]))
                {
                    return true;
                }
            }

            return OperatorWeights.Length != (Roots != null ? Roots.Length : 0) * count;
        }

        public void SetWeights(HairMaskKey[] keys, int count, float[] weights)
        {
            if (_operatorKeys == null || _operatorKeys.Length != count)
            {
                _operatorKeys = new HairMaskKey[count];
            }

            Array.Copy(keys, _operatorKeys, count);
            OperatorWeights = weights;
        }
    }
}
