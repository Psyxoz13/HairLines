using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    public class HairMaskUndoRecord : ScriptableObject
    {
        [SerializeField] private Texture2D _target;
        [SerializeField] private int _x;
        [SerializeField] private int _y;
        [SerializeField] private int _width;
        [SerializeField] private int _height;
        [SerializeField] private byte[] _before;
        [SerializeField] private byte[] _after;
        [SerializeField] private bool _applied;

        private bool _synced;

        internal void Fill(Texture2D target, RectInt region, Color32[] before, Color32[] after)
        {
            _target = target;
            _x = region.x;
            _y = region.y;
            _width = region.width;
            _height = region.height;
            _before = Pack(before);
            _after = Pack(after);
            _applied = false;
            _synced = false;
        }

        internal void MarkApplied()
        {
            _applied = true;
            _synced = true;
        }

        internal bool Sync()
        {
            if (_target == null || _before == null || _after == null)
            {
                return false;
            }

            if (_applied == _synced)
            {
                return true;
            }

            _synced = _applied;
            _target.SetPixels32(_x, _y, _width, _height, Unpack(_applied ? _after : _before));
            _target.Apply(false);
            HairMaskFactory.Save(_target);
            return true;
        }

        private static byte[] Pack(Color32[] pixels)
        {
            var packed = new byte[pixels.Length * 4];

            for (int i = 0; i < pixels.Length; i++)
            {
                packed[i * 4] = pixels[i].r;
                packed[i * 4 + 1] = pixels[i].g;
                packed[i * 4 + 2] = pixels[i].b;
                packed[i * 4 + 3] = pixels[i].a;
            }

            return packed;
        }

        private static Color32[] Unpack(byte[] packed)
        {
            var pixels = new Color32[packed.Length / 4];

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(packed[i * 4], packed[i * 4 + 1],
                                        packed[i * 4 + 2], packed[i * 4 + 3]);
            }

            return pixels;
        }
    }

    public static class HairMaskUndo
    {
        private const int MaxRecords = 96;

        private static readonly List<HairMaskUndoRecord> _records = new List<HairMaskUndoRecord>();
        private static bool _hooked;

        public static void Register(Texture2D target, RectInt region, Color32[] before, Color32[] after,
                                    string name)
        {
            if (target == null || region.width <= 0 || region.height <= 0)
            {
                return;
            }

            Hook();
            Trim();

            var record = ScriptableObject.CreateInstance<HairMaskUndoRecord>();
            record.hideFlags = HideFlags.HideAndDontSave;
            record.Fill(target, region, before, after);

            Undo.RegisterCompleteObjectUndo(record, name);
            record.MarkApplied();
            _records.Add(record);
        }

        private static void Hook()
        {
            if (_hooked)
            {
                return;
            }

            Undo.undoRedoPerformed += SyncAll;
            _hooked = true;
        }

        private static void SyncAll()
        {
            for (int i = _records.Count - 1; i >= 0; i--)
            {
                if (_records[i] == null || !_records[i].Sync())
                {
                    _records.RemoveAt(i);
                }
            }

            SceneView.RepaintAll();
        }

        private static void Trim()
        {
            while (_records.Count >= MaxRecords)
            {
                HairMaskUndoRecord oldest = _records[0];
                _records.RemoveAt(0);

                if (oldest != null)
                {
                    Object.DestroyImmediate(oldest);
                }
            }
        }
    }
}
