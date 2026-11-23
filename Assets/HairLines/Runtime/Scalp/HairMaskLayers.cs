using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace HairLines
{
    internal struct HairMaskLayers : IDisposable
    {
        public NativeArray<byte> Pixels;
        public NativeArray<int4> Layers;

        public static HairMaskLayers Build(IHairDensityMask mask)
        {
            var sources = new List<Layer>();
            Collect(mask, false, sources);

            int total = 0;

            for (int i = 0; i < sources.Count; i++)
            {
                total += sources[i].Pixels.Length;
            }

            var built = new HairMaskLayers
            {
                Pixels = new NativeArray<byte>(math.max(1, total), Allocator.TempJob,
                                               NativeArrayOptions.UninitializedMemory),
                Layers = new NativeArray<int4>(math.max(1, sources.Count), Allocator.TempJob,
                                               NativeArrayOptions.ClearMemory),
            };

            int cursor = 0;

            for (int i = 0; i < sources.Count; i++)
            {
                Layer layer = sources[i];
                Color32[] pixels = layer.Pixels;

                for (int p = 0; p < pixels.Length; p++)
                {
                    built.Pixels[cursor + p] = pixels[p].r;
                }

                built.Layers[i] = new int4(cursor, layer.Width, layer.Height, layer.Claimed ? 1 : 0);
                cursor += pixels.Length;
            }

            if (sources.Count == 0)
            {
                built.Layers[0] = new int4(0, 0, 0, 0);
            }

            return built;
        }

        public int Count => Layers.Length > 0 && Layers[0].y > 0 ? Layers.Length : 0;

        public void Dispose()
        {
            if (Pixels.IsCreated)
            {
                Pixels.Dispose();
            }

            if (Layers.IsCreated)
            {
                Layers.Dispose();
            }
        }

        public static float Evaluate(in NativeArray<byte> pixels, in NativeArray<int4> layers,
                                     int layerCount, float2 uv)
        {
            float free = 1f;
            float own = 1f;

            for (int i = 0; i < layerCount; i++)
            {
                int4 layer = layers[i];
                float value = Sample(pixels, layer, uv);

                if (layer.w != 0)
                {
                    free *= 1f - math.saturate(value);

                    if (free <= 0f)
                    {
                        return 0f;
                    }
                }
                else
                {
                    own = math.saturate(value);
                }
            }

            return free * own;
        }

        private static float Sample(in NativeArray<byte> pixels, in int4 layer, float2 uv)
        {
            int width = layer.y;
            int height = layer.z;

            if (width <= 0 || height <= 0)
            {
                return 1f;
            }

            float x = math.saturate(uv.x) * (width - 1);
            float y = math.saturate(uv.y) * (height - 1);

            int x0 = (int)math.floor(x);
            int y0 = (int)math.floor(y);
            int x1 = math.min(x0 + 1, width - 1);
            int y1 = math.min(y0 + 1, height - 1);

            float fractionX = x - x0;
            float fractionY = y - y0;

            float bottom = math.lerp(Red(pixels, layer, x0, y0), Red(pixels, layer, x1, y0), fractionX);
            float top = math.lerp(Red(pixels, layer, x0, y1), Red(pixels, layer, x1, y1), fractionX);
            return math.lerp(bottom, top, fractionY);
        }

        private static float Red(in NativeArray<byte> pixels, in int4 layer, int x, int y)
        {
            int index = layer.x + y * layer.y + x;
            return index >= layer.x && index < pixels.Length ? pixels[index] / 255f : 1f;
        }

        private static void Collect(IHairDensityMask mask, bool claimed, List<Layer> sources)
        {
            switch (mask)
            {
                case null:
                    return;

                case HairMaskStack stack:
                    foreach (IHairDensityMask above in stack.ClaimedAbove)
                    {
                        Collect(above, true, sources);
                    }

                    Collect(stack.Own, claimed, sources);
                    return;

                case HairDensityMask density:
                    if (density.TryGetPixels(out Color32[] pixels, out int width, out int height))
                    {
                        sources.Add(new Layer
                        {
                            Pixels = pixels,
                            Width = width,
                            Height = height,
                            Claimed = claimed,
                        });
                    }

                    return;
            }
        }

        private struct Layer
        {
            public Color32[] Pixels;
            public int Width;
            public int Height;
            public bool Claimed;
        }
    }
}
