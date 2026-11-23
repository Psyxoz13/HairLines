using UnityEngine;

namespace HairLines.EditorTools
{
    public class HairTexelBuffer
    {
        public int Width { get; }
        public int Height { get; }
        public bool HasPendingTexels => _minX <= _maxX && _minY <= _maxY;
        public bool IsValid => _texture != null && Width > 0 && Height > 0 && _pixels.Length == Width * Height;

        private readonly Texture2D _texture;
        private readonly Color32[] _pixels;
        private readonly Color32[] _original;

        private int _minX, _minY, _maxX, _maxY;
        private int _strokeMinX, _strokeMinY, _strokeMaxX, _strokeMaxY;

        public HairTexelBuffer(Texture2D texture)
        {
            _texture = texture;
            Width = texture != null ? texture.width : 0;
            Height = texture != null ? texture.height : 0;
            _pixels = texture != null ? texture.GetPixels32() : new Color32[0];
            _original = (Color32[])_pixels.Clone();
            ResetDirtyRect();
            _strokeMinX = Width;
            _strokeMinY = Height;
            _strokeMaxX = -1;
            _strokeMaxY = -1;
        }

        public Color32 Read(int index)
        {
            return index >= 0 && index < _pixels.Length ? _pixels[index] : default;
        }

        public bool Write(int index, Color32 value)
        {
            if (index < 0 || index >= _pixels.Length || Width <= 0 || Height <= 0)
            {
                return false;
            }

            Color32 before = _pixels[index];

            if (before.r == value.r && before.g == value.g && before.b == value.b)
            {
                return false;
            }

            _pixels[index] = value;

            int x = index % Width;
            int y = index / Width;
            _minX = Mathf.Min(_minX, x);
            _minY = Mathf.Min(_minY, y);
            _maxX = Mathf.Max(_maxX, x);
            _maxY = Mathf.Max(_maxY, y);
            _strokeMinX = Mathf.Min(_strokeMinX, x);
            _strokeMinY = Mathf.Min(_strokeMinY, y);
            _strokeMaxX = Mathf.Max(_strokeMaxX, x);
            _strokeMaxY = Mathf.Max(_strokeMaxY, y);
            return true;
        }

        public void Apply()
        {
            if (!IsValid || !HasPendingTexels)
            {
                return;
            }

            int width = _maxX - _minX + 1;
            int height = _maxY - _minY + 1;
            var block = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    block[y * width + x] = _pixels[(_minY + y) * Width + _minX + x];
                }
            }

            _texture.SetPixels32(_minX, _minY, width, height, block);
            _texture.Apply(false);
            ResetDirtyRect();
        }

        public bool TryTakeStroke(out RectInt region, out Color32[] before, out Color32[] after)
        {
            region = default;
            before = null;
            after = null;

            if (_strokeMinX > _strokeMaxX || _strokeMinY > _strokeMaxY)
            {
                return false;
            }

            region = new RectInt(_strokeMinX, _strokeMinY,
                                 _strokeMaxX - _strokeMinX + 1, _strokeMaxY - _strokeMinY + 1);
            before = new Color32[region.width * region.height];
            after = new Color32[region.width * region.height];

            for (int y = 0; y < region.height; y++)
            {
                for (int x = 0; x < region.width; x++)
                {
                    int source = (region.y + y) * Width + region.x + x;
                    int destination = y * region.width + x;
                    before[destination] = _original[source];
                    after[destination] = _pixels[source];
                }
            }

            _strokeMinX = Width;
            _strokeMinY = Height;
            _strokeMaxX = -1;
            _strokeMaxY = -1;
            return true;
        }

        public float MeanRed()
        {
            if (_pixels.Length == 0)
            {
                return 0f;
            }

            float sum = 0f;

            for (int i = 0; i < _pixels.Length; i++)
            {
                sum += _pixels[i].r;
            }

            return sum / (_pixels.Length * 255f);
        }

        private void ResetDirtyRect()
        {
            _minX = Width;
            _minY = Height;
            _maxX = -1;
            _maxY = -1;
        }
    }
}
