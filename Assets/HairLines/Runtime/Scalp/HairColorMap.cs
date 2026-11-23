using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairColorMap
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

        [Tooltip("Colour texture in the UV space of the scalp, read at the root of every strand.")]
        [SerializeField]
        private Texture2D _texture;

        private Texture2D _sampledTexture;
        private Color32[] _pixels;
        private bool _linear;

        private int _width;
        private int _height;

        public uint EvaluatePacked(Vector2 uv)
        {
            if (!EnsurePixels())
            {
                return 0u;
            }

            float x = Mathf.Clamp01(uv.x) * (_width - 1);
            float y = Mathf.Clamp01(uv.y) * (_height - 1);

            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            int x1 = Mathf.Min(x0 + 1, _width - 1);
            int y1 = Mathf.Min(y0 + 1, _height - 1);

            float fractionX = x - x0;
            float fractionY = y - y0;

            Color bottom = Color.Lerp(_pixels[y0 * _width + x0], _pixels[y0 * _width + x1], fractionX);
            Color top = Color.Lerp(_pixels[y1 * _width + x0], _pixels[y1 * _width + x1], fractionX);
            Color color = Color.Lerp(bottom, top, fractionY);

            if (_linear)
            {
                color = color.gamma;
            }

            return (uint)Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f)
                   | ((uint)Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f) << 8)
                   | ((uint)Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f) << 16)
                   | (255u << 24);
        }

        public void Invalidate()
        {
            _pixels = null;
            _sampledTexture = null;
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
            _linear = !_texture.isDataSRGB;
            return true;
        }
    }
}
