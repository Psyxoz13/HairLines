using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Profiling;
using UnityEngine;

namespace HairLines
{
    public static class HairGroomBlob
    {
        public const int Magic = 0x4D524748;
        public const int Version = 1;

        private static readonly ProfilerMarker _readMarker =
            new ProfilerMarker("HairLines.Grooming.ReadGroomBlob");

        public static byte[] Write(LinePrimitiveModel model)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(model.StrandCount);
            writer.Write(model.VertexCount);

            Bounds bounds = model.LocalBounds;
            writer.Write(bounds.center.x);
            writer.Write(bounds.center.y);
            writer.Write(bounds.center.z);
            writer.Write(bounds.size.x);
            writer.Write(bounds.size.y);
            writer.Write(bounds.size.z);

            WriteArray(writer, model.Positions);
            WriteArray(writer, model.Colors);
            WriteArray(writer, model.VertexGroups);
            WriteArray(writer, model.Strands);
            WriteArray(writer, model.EdgeOpacity);

            writer.Flush();
            return stream.ToArray();
        }

        public static LinePrimitiveModel Read(byte[] blob)
        {
            using ProfilerMarker.AutoScope scope = _readMarker.Auto();
            if (blob == null || blob.Length < 8)
            {
                throw new InvalidDataException("Groom blob is empty.");
            }

            using var stream = new MemoryStream(blob, false);
            using var reader = new BinaryReader(stream);

            if (reader.ReadInt32() != Magic)
            {
                throw new InvalidDataException("Not a HairLines groom blob.");
            }

            int version = reader.ReadInt32();

            if (version != Version)
            {
                throw new InvalidDataException($"Groom blob version {version} is not supported, expected {Version}.");
            }

            reader.ReadInt32();
            reader.ReadInt32();

            var center = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var size = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

            Vector3[] positions = ReadArray<Vector3>(reader);
            Vector4[] colors = ReadArray<Vector4>(reader);
            uint[] groups = ReadArray<uint>(reader);
            Vector2Int[] strands = ReadArray<Vector2Int>(reader);
            float[] opacity = ReadArray<float>(reader);

            var model = new LinePrimitiveModel(positions, colors, BuildIndices(strands), opacity, strands, groups)
            {
                LocalBounds = new Bounds(center, size),
            };

            return model;
        }

        public static int[] BuildIndices(Vector2Int[] strands)
        {
            int edges = 0;

            foreach (Vector2Int strand in strands)
            {
                edges += Mathf.Max(0, strand.y - 1);
            }

            var indices = new int[edges * 2];
            int cursor = 0;

            foreach (Vector2Int strand in strands)
            {
                for (int i = 0; i + 1 < strand.y; i++)
                {
                    indices[cursor++] = strand.x + i;
                    indices[cursor++] = strand.x + i + 1;
                }
            }

            return indices;
        }

        private static void WriteArray<T>(BinaryWriter writer, T[] values) where T : unmanaged
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<T>(values));
            writer.Write(values.Length);
            writer.Write(bytes);
        }

        private static T[] ReadArray<T>(BinaryReader reader) where T : unmanaged
        {
            int count = reader.ReadInt32();
            var values = new T[count];
            Span<byte> bytes = MemoryMarshal.AsBytes(new Span<T>(values));
            int read = reader.Read(bytes);

            if (read != bytes.Length)
            {
                throw new InvalidDataException($"Groom blob ended early: wanted {bytes.Length} bytes, got {read}.");
            }

            return values;
        }
    }
}
