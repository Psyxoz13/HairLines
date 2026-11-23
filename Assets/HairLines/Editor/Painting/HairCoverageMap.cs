using System.Collections.Generic;
using UnityEngine;

namespace HairLines.EditorTools
{
    public static class HairCoverageMap
    {
        private const int Resolution = 256;
        private const float PaintedThreshold = 0.05f;

        public static Texture2D Build(LinePrimitiveRenderer renderer, Texture2D reuse)
        {
            if (renderer == null)
            {
                return reuse;
            }

            Texture2D map = reuse != null && reuse.width == Resolution && reuse.height == Resolution
                ? reuse
                : new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                };

            var pixels = new Color32[Resolution * Resolution];
            IReadOnlyList<HairGroup> groups = renderer.Groups;

            for (int group = 0; group < groups.Count; group++)
            {
                HairGrowSettings settings = groups[group].ResolvedSettings;
                HairDensityMask mask = settings.DensityMask;

                if (mask == null || !mask.IsUsable)
                {
                    StampWholeScalp(pixels);
                    continue;
                }

                for (int y = 0; y < Resolution; y++)
                {
                    for (int x = 0; x < Resolution; x++)
                    {
                        var uv = new Vector2((x + 0.5f) / Resolution, (y + 0.5f) / Resolution);

                        if (mask.Evaluate(uv) <= PaintedThreshold)
                        {
                            continue;
                        }

                        int index = y * Resolution + x;
                        pixels[index].g = 255;
                    }
                }
            }

            StampRoots(renderer, pixels);

            map.SetPixels32(pixels);
            map.Apply(false);
            return map;
        }

        private static void StampWholeScalp(Color32[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i].g = 255;
            }
        }

        private static void StampRoots(LinePrimitiveRenderer renderer, Color32[] pixels)
        {
            LinePrimitiveModel model = renderer.Model;

            if (model == null || model.StrandDebug == null)
            {
                return;
            }

            Vector4[] debug = model.StrandDebug;
            int radius = Mathf.Max(1, Mathf.RoundToInt(Resolution * 0.5f / Mathf.Sqrt(Mathf.Max(debug.Length, 1))));

            for (int drawRange = 0; drawRange < model.DrawRangeCount; drawRange++)
            {
                int group = model.GroupForDrawRange(drawRange);
                byte tag = (byte)Mathf.Clamp(group + 1, 1, 255);
                model.StrandSpanForDrawRange(drawRange, out int start, out int count);

                for (int i = start; i < start + count && i < debug.Length; i++)
                {
                    int cx = Mathf.Clamp(Mathf.RoundToInt(debug[i].x * Resolution), 0, Resolution - 1);
                    int cy = Mathf.Clamp(Mathf.RoundToInt(debug[i].y * Resolution), 0, Resolution - 1);

                    for (int y = -radius; y <= radius; y++)
                    {
                        int py = cy + y;

                        if (py < 0 || py >= Resolution)
                        {
                            continue;
                        }

                        for (int x = -radius; x <= radius; x++)
                        {
                            int px = cx + x;

                            if (px < 0 || px >= Resolution || x * x + y * y > radius * radius)
                            {
                                continue;
                            }

                            pixels[py * Resolution + px].r = tag;
                        }
                    }
                }
            }
        }
    }
}
