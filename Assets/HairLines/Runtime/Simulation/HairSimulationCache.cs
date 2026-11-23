using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HairLines
{
    public static class HairSimulationCache
    {
        public const int Version = 2;
        private const int HeaderBytes = 28;
        private static readonly byte[] Magic = { (byte)'H', (byte)'L', (byte)'S', (byte)'C' };

        public readonly struct Layout
        {
            public int SlotCount { get; }
            public int PinCount { get; }
            public int VertexCount { get; }
            public int StrandCount { get; }
            public uint SlotHash { get; }

            public Layout(int slotCount, int pinCount, int vertexCount, int strandCount, uint slotHash)
            {
                SlotCount = slotCount;
                PinCount = pinCount;
                VertexCount = vertexCount;
                StrandCount = strandCount;
                SlotHash = slotHash;
            }

            public int PositionBytes => SlotCount * 12;
            public int FrameBytes => sizeof(float) + PositionBytes + PinCount * 16;
        }

        public static uint HashSlots(uint[] controlIndices)
        {
            uint hash = 2166136261u;
            if (controlIndices == null)
            {
                return hash;
            }

            for (int i = 0; i < controlIndices.Length; i++)
            {
                hash = (hash ^ controlIndices[i]) * 16777619u;
            }

            return hash;
        }

        public static string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.Combine(Application.streamingAssetsPath, path);
        }

        public class Writer : IDisposable
        {
            public int FrameCount { get; private set; }
            public long Bytes => _stream?.Length ?? 0L;

            private readonly FileStream _stream;
            private readonly byte[] _frame;
            private readonly int _positionBytes;

            public Writer(string path, Layout layout)
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _frame = new byte[layout.FrameBytes];
                _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                var header = new byte[HeaderBytes];
                Array.Copy(Magic, header, Magic.Length);
                BitConverter.TryWriteBytes(new Span<byte>(header, 4, 4), Version);
                BitConverter.TryWriteBytes(new Span<byte>(header, 8, 4), layout.SlotCount);
                BitConverter.TryWriteBytes(new Span<byte>(header, 12, 4), layout.PinCount);
                BitConverter.TryWriteBytes(new Span<byte>(header, 16, 4), layout.VertexCount);
                BitConverter.TryWriteBytes(new Span<byte>(header, 20, 4), layout.StrandCount);
                BitConverter.TryWriteBytes(new Span<byte>(header, 24, 4), layout.SlotHash);
                _stream.Write(header, 0, header.Length);
                _stream.Flush();
                _positionBytes = layout.PositionBytes;
            }

            public void Append(float time, Vector3[] objectSpacePositions, Vector4[] objectSpacePinTargets)
            {
                BitConverter.TryWriteBytes(new Span<byte>(_frame, 0, sizeof(float)), time);
                MemoryMarshal.AsBytes(new ReadOnlySpan<Vector3>(objectSpacePositions))
                             .CopyTo(new Span<byte>(_frame, sizeof(float), _positionBytes));
                MemoryMarshal.AsBytes(new ReadOnlySpan<Vector4>(objectSpacePinTargets))
                             .CopyTo(new Span<byte>(_frame, sizeof(float) + _positionBytes,
                                                    _frame.Length - sizeof(float) - _positionBytes));
                _stream.Write(_frame, 0, _frame.Length);
                _stream.Flush();
                FrameCount++;
            }

            public void Dispose()
            {
                _stream?.Dispose();
            }
        }

        public class Reader : IDisposable
        {
            public Layout Layout { get; }
            public int FrameCount => _times.Length;
            public float Duration => _times.Length > 0 ? _times[_times.Length - 1] : 0f;

            private readonly FileStream _stream;
            private readonly float[] _times;
            private readonly byte[] _frame;
            private readonly Vector3[] _shownPositions;
            private readonly Vector4[] _shownPinTargets;
            private int _shownFrame = -1;

            public Reader(string path, Layout expected)
            {
                _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                try
                {
                    var header = new byte[HeaderBytes];
                    if (!ReadExactly(header, header.Length) || !new ReadOnlySpan<byte>(header, 0, 4).SequenceEqual(Magic))
                    {
                        throw new InvalidDataException("not a hair simulation cache");
                    }

                    int version = BitConverter.ToInt32(header, 4);
                    if (version != Version)
                    {
                        throw new InvalidDataException($"cache version {version}, expected {Version}; record it again");
                    }

                    Layout = new Layout(BitConverter.ToInt32(header, 8), BitConverter.ToInt32(header, 12),
                                        BitConverter.ToInt32(header, 16), BitConverter.ToInt32(header, 20),
                                        BitConverter.ToUInt32(header, 24));
                    if (Layout.SlotCount <= 0 || Layout.PinCount < 0 || Layout.VertexCount < 0 || Layout.StrandCount < 0)
                    {
                        throw new InvalidDataException("the cache header is damaged");
                    }

                    if (!Layout.Equals(expected))
                    {
                        throw new InvalidDataException(
                            $"it was recorded for {Layout.SlotCount:N0} solver slots and {Layout.PinCount:N0} pinned " +
                            $"vertices over {Layout.VertexCount:N0} vertices, this groom has {expected.SlotCount:N0} " +
                            $"and {expected.PinCount:N0} over {expected.VertexCount:N0} or a different slot layout " +
                            "(skip, pins, guide map); record it again");
                    }

                    long frames = (_stream.Length - HeaderBytes) / Layout.FrameBytes;
                    _frame = new byte[Layout.FrameBytes];
                    _shownPositions = new Vector3[Layout.SlotCount];
                    _shownPinTargets = new Vector4[Layout.PinCount];
                    _times = new float[frames];
                    for (int frame = 0; frame < frames; frame++)
                    {
                        _stream.Position = FrameOffset(frame);
                        if (!ReadExactly(_frame, sizeof(float)))
                        {
                            throw new InvalidDataException("the cache is truncated");
                        }

                        float time = BitConverter.ToSingle(_frame, 0);
                        if (!float.IsFinite(time) || (frame > 0 && time < _times[frame - 1]))
                        {
                            throw new InvalidDataException($"frame {frame} has a damaged time stamp");
                        }

                        _times[frame] = time;
                    }
                }
                catch
                {
                    _stream.Dispose();
                    throw;
                }
            }

            public void ReadShownAt(float time, Vector3[] positions, Vector4[] pinTargets)
            {
                int frame = LastFrameAtOrBefore(time);
                if (_shownFrame != frame)
                {
                    Read(frame);
                    _shownFrame = frame;
                }

                Array.Copy(_shownPositions, positions, _shownPositions.Length);
                Array.Copy(_shownPinTargets, pinTargets, _shownPinTargets.Length);
            }

            public void Dispose()
            {
                _stream?.Dispose();
            }

            private int LastFrameAtOrBefore(float time)
            {
                int low = 0;
                int high = _times.Length;
                while (low < high)
                {
                    int middle = (low + high) >> 1;
                    if (_times[middle] <= time)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                return Mathf.Max(0, low - 1);
            }

            private void Read(int frame)
            {
                _stream.Position = FrameOffset(frame);
                if (!ReadExactly(_frame, _frame.Length))
                {
                    throw new EndOfStreamException("hair simulation cache frame is truncated");
                }

                int positionBytes = Layout.PositionBytes;
                new ReadOnlySpan<byte>(_frame, sizeof(float), positionBytes)
                    .CopyTo(MemoryMarshal.AsBytes(new Span<Vector3>(_shownPositions)));
                new ReadOnlySpan<byte>(_frame, sizeof(float) + positionBytes, _frame.Length - sizeof(float) - positionBytes)
                    .CopyTo(MemoryMarshal.AsBytes(new Span<Vector4>(_shownPinTargets)));
            }

            private bool ReadExactly(byte[] buffer, int count)
            {
                int read = 0;
                while (read < count)
                {
                    int received = _stream.Read(buffer, read, count - read);
                    if (received <= 0)
                    {
                        return false;
                    }

                    read += received;
                }

                return true;
            }

            private long FrameOffset(int frame) => HeaderBytes + (long)frame * Layout.FrameBytes;
        }
    }
}
