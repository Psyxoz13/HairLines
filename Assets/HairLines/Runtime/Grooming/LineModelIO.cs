using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace HairLines
{
    public static class LineModelIO
    {
        private const uint HasSegments = 1;
        private const uint HasPoints = 2;
        private const uint HasThickness = 4;
        private const uint HasTransparency = 8;
        private const uint HasColors = 16;
        private const int CyHairInfoBytes = 88;
        private const int BytesPerFloat = 4;

        public static LinePrimitiveModel Load(string path, HairImportSettings settings)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Empty path");
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Line model not found", path);
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();

            switch (extension)
            {
                case ".hair":
                    return LoadCyHair(path, settings);

                case ".obj":
                    return LoadObjPolylines(path, settings);

                default:
                    throw new NotSupportedException("Unsupported line model extension: " + extension);
            }
        }

        public static LinePrimitiveModel LoadCyHair(string path, HairImportSettings settings)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            byte[] signature = reader.ReadBytes(4);

            if (signature.Length != 4 || signature[0] != 'H' || signature[1] != 'A'
                || signature[2] != 'I' || signature[3] != 'R')
            {
                throw new InvalidDataException("Not a cyHair file (missing HAIR signature): " + path);
            }

            uint strandCount = reader.ReadUInt32();
            uint pointCount = reader.ReadUInt32();
            uint arrays = reader.ReadUInt32();
            uint defaultSegments = reader.ReadUInt32();

            reader.ReadSingle();
            reader.ReadSingle();

            float defaultRed = reader.ReadSingle();
            float defaultGreen = reader.ReadSingle();
            float defaultBlue = reader.ReadSingle();
            reader.ReadBytes(CyHairInfoBytes);

            if ((arrays & HasPoints) == 0)
            {
                throw new InvalidDataException("cyHair file carries no point array: " + path);
            }

            var segments = new int[strandCount];

            for (int i = 0; i < strandCount; i++)
            {
                segments[i] = (arrays & HasSegments) != 0 ? reader.ReadUInt16() : (int)defaultSegments;
            }

            var points = new Vector3[pointCount];

            for (int i = 0; i < pointCount; i++)
            {
                points[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            if ((arrays & HasThickness) != 0)
            {
                reader.ReadBytes((int)pointCount * BytesPerFloat);
            }

            if ((arrays & HasTransparency) != 0)
            {
                reader.ReadBytes((int)pointCount * BytesPerFloat);
            }

            Color[] colors = null;

            if ((arrays & HasColors) != 0)
            {
                colors = new Color[pointCount];

                for (int i = 0; i < pointCount; i++)
                {
                    colors[i] = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), 1f);
                }
            }

            Color fallback = (arrays & HasColors) != 0
                ? new Color(defaultRed, defaultGreen, defaultBlue, 1f)
                : settings.DefaultColor;

            var builder = new LinePrimitiveBuilder();
            builder.Reserve((int)pointCount, (int)strandCount);

            int cursor = 0;
            int stride = settings.StrandStride;

            for (int strand = 0; strand < strandCount; strand++)
            {
                int count = segments[strand] + 1;

                if (strand % stride == 0 && cursor + count <= pointCount)
                {
                    builder.BeginStrand();

                    for (int k = 0; k < count; k++)
                    {
                        float alongStrand = count > 1 ? k / (float)(count - 1) : 0f;
                        Color color = colors != null ? colors[cursor + k] : fallback;
                        builder.AddVertex(settings.Apply(points[cursor + k]),
                                          new Vector4(color.r, color.g, color.b, alongStrand));
                    }

                    builder.EndStrand(1f, settings.TipOpacity);
                }

                cursor += count;
            }

            return builder.Build();
        }

        public static LinePrimitiveModel LoadObjPolylines(string path, HairImportSettings settings)
        {
            var positions = new List<Vector3>();
            var vertexColors = new List<Color>();
            var polylines = new List<int[]>();
            bool anyColor = false;

            ReadObj(path, settings, positions, vertexColors, polylines, ref anyColor);

            if (polylines.Count == 0)
            {
                throw new InvalidDataException("OBJ contains no 'l' polyline elements: " + path);
            }

            var builder = new LinePrimitiveBuilder();
            builder.Reserve(positions.Count, polylines.Count);

            int stride = settings.StrandStride;

            for (int p = 0; p < polylines.Count; p++)
            {
                if (p % stride != 0)
                {
                    continue;
                }

                int[] line = polylines[p];
                builder.BeginStrand();

                for (int k = 0; k < line.Length; k++)
                {
                    int vertex = line[k];

                    if (vertex < 0 || vertex >= positions.Count)
                    {
                        continue;
                    }

                    float alongStrand = line.Length > 1 ? k / (float)(line.Length - 1) : 0f;
                    Color color = anyColor ? vertexColors[vertex] : settings.DefaultColor;
                    builder.AddVertex(settings.Apply(positions[vertex]),
                                      new Vector4(color.r, color.g, color.b, alongStrand));
                }

                builder.EndStrand(1f, settings.TipOpacity);
            }

            return builder.Build();
        }

        private static void ReadObj(string path, HairImportSettings settings, List<Vector3> positions,
                            List<Color> vertexColors, List<int[]> polylines, ref bool anyColor)
        {
            var separators = new[] { ' ', '\t' };
            using var reader = new StreamReader(path);
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length < 2 || line[0] == '#')
                {
                    continue;
                }

                bool separated = line[1] == ' ' || line[1] == '\t';

                if (line[0] == 'v' && separated)
                {
                    string[] tokens = line.Split(separators, StringSplitOptions.RemoveEmptyEntries);

                    if (tokens.Length < 4)
                    {
                        continue;
                    }

                    positions.Add(new Vector3(ParseFloat(tokens[1]), ParseFloat(tokens[2]), ParseFloat(tokens[3])));

                    if (tokens.Length >= 7)
                    {
                        anyColor = true;
                        vertexColors.Add(new Color(ParseFloat(tokens[4]), ParseFloat(tokens[5]),
                                                   ParseFloat(tokens[6]), 1f));
                    }
                    else
                    {
                        vertexColors.Add(settings.DefaultColor);
                    }
                }
                else if (line[0] == 'l' && separated)
                {
                    string[] tokens = line.Split(separators, StringSplitOptions.RemoveEmptyEntries);

                    if (tokens.Length < 3)
                    {
                        continue;
                    }

                    polylines.Add(ParsePolyline(tokens, positions.Count));
                }
            }
        }

        private static int[] ParsePolyline(string[] tokens, int vertexCount)
        {
            var indices = new int[tokens.Length - 1];

            for (int i = 1; i < tokens.Length; i++)
            {
                int slash = tokens[i].IndexOf('/');
                string raw = slash >= 0 ? tokens[i].Substring(0, slash) : tokens[i];
                int index = int.Parse(raw, CultureInfo.InvariantCulture);
                indices[i - 1] = index > 0 ? index - 1 : vertexCount + index;
            }

            return indices;
        }

        private static float ParseFloat(string value)
        {
            return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
