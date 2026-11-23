using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairCombMap
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

        [Tooltip("Two-channel map in the UV space of the scalp. Red and green hold the combed " +
                 "direction in the tangent frame of the root; 0.5 in both means uncombed.")]
        [SerializeField]
        private Texture2D _texture;

        private Texture2D _sampledTexture;
        private Color32[] _pixels;

        private int _width;
        private int _height;

        public const byte Neutral = 128;

        public Vector2 Evaluate(Vector2 uv)
        {
            if (!EnsurePixels())
            {
                return Vector2.zero;
            }

            float x = Mathf.Clamp01(uv.x) * (_width - 1);
            float y = Mathf.Clamp01(uv.y) * (_height - 1);

            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            int x1 = Mathf.Min(x0 + 1, _width - 1);
            int y1 = Mathf.Min(y0 + 1, _height - 1);

            float fractionX = x - x0;
            float fractionY = y - y0;

            Vector2 bottom = Vector2.Lerp(Decode(x0, y0), Decode(x1, y0), fractionX);
            Vector2 top = Vector2.Lerp(Decode(x0, y1), Decode(x1, y1), fractionX);
            return Vector2.Lerp(bottom, top, fractionY);
        }

        public void Invalidate()
        {
            _pixels = null;
            _sampledTexture = null;
        }

        public static Vector2 Decode(Color32 pixel)
        {
            return new Vector2(pixel.r / 255f * 2f - 1f, pixel.g / 255f * 2f - 1f);
        }

        public static Color32 Encode(Vector2 direction)
        {
            float length = direction.magnitude;

            if (length > 1f)
            {
                direction /= length;
            }

            return new Color32((byte)Mathf.RoundToInt((direction.x * 0.5f + 0.5f) * 255f),
                               (byte)Mathf.RoundToInt((direction.y * 0.5f + 0.5f) * 255f),
                               0, 255);
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

        private Vector2 Decode(int x, int y)
        {
            int index = y * _width + x;
            return index >= 0 && index < _pixels.Length ? Decode(_pixels[index]) : Vector2.zero;
        }
    }
}
