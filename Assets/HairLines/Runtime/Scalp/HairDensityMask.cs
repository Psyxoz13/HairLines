using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairDensityMask : IHairDensityMask
    {
        public Texture2D Texture
        {
            get => _texture;
            set
            {
                _texture = value;
                _pixels = null;
            }
        }

        public bool HasTexture => _texture != null;
        public bool IsUsable => _texture != null && _texture.isReadable;

        [Tooltip("Single-channel mask in the UV space of the scalp. The red channel scales hair density.")]
        [SerializeField]
        private Texture2D _texture;

        private Texture2D _sampledTexture;
        private Color32[] _pixels;

        private int _width;
        private int _height;

        public float Evaluate(Vector2 uv)
        {
            if (!EnsurePixels())
            {
                return 1f;
            }

            float x = Mathf.Clamp01(uv.x) * (_width - 1);
            float y = Mathf.Clamp01(uv.y) * (_height - 1);

            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            int x1 = Mathf.Min(x0 + 1, _width - 1);
            int y1 = Mathf.Min(y0 + 1, _height - 1);

            float fractionX = x - x0;
            float fractionY = y - y0;

            float bottom = Mathf.Lerp(Red(x0, y0), Red(x1, y0), fractionX);
            float top = Mathf.Lerp(Red(x0, y1), Red(x1, y1), fractionX);
            return Mathf.Lerp(bottom, top, fractionY);
        }

        public void Invalidate()
        {
            _pixels = null;
            _sampledTexture = null;
        }

        internal bool TryGetPixels(out Color32[] pixels, out int width, out int height)
        {
            if (!EnsurePixels())
            {
                pixels = null;
                width = 0;
                height = 0;
                return false;
            }

            pixels = _pixels;
            width = _width;
            height = _height;
            return true;
        }

        private bool EnsurePixels()
        {
            if (_texture != null && _pixels != null && _sampledTexture == _texture &&
                _width == _texture.width && _height == _texture.height &&
                _width > 0 && _height > 0 && _pixels.Length >= _width * _height)
            {
                return true;
            }

            if (!IsUsable)
            {
                _pixels = null;
                _sampledTexture = null;
                return false;
            }

            int width = _texture.width;
            int height = _texture.height;
            Color32[] pixels = _texture.GetPixels32();
            if (width <= 0 || height <= 0 || pixels == null || pixels.Length < width * height)
            {
                _pixels = null;
                _sampledTexture = null;
                return false;
            }

            _pixels = pixels;
            _sampledTexture = _texture;
            _width = width;
            _height = height;
            return true;
        }

        private float Red(int x, int y)
        {
            int index = y * _width + x;
            return index >= 0 && index < _pixels.Length ? _pixels[index].r / 255f : 1f;
        }
    }
}
