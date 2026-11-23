using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace HairLines
{
    public static class HairSampleElimination
    {
        public const int CandidateMultiple = 4;

        private const float Beta = 0.65f;
        private const float Gamma = 1.5f;

        private const float MinimumDensity = 1f / 16f;

        private static readonly ProfilerMarker _orderMarker =
            new ProfilerMarker("HairLines.Scalp.EliminateSamples");

        public static void Order(NativeArray<float3> positions, NativeArray<float> density,
                                 float area, NativeArray<int> order)
        {
            using ProfilerMarker.AutoScope scope = _orderMarker.Auto();
            new EliminateJob
            {
                positions = positions,
                density = density,
                order = order,
                area = math.max(area, 1e-9f),
            }.Run();
        }

        private struct Grid
        {
            public NativeArray<int> starts;
            public NativeArray<int> entries;
            public NativeArray<float3> position;
            public NativeArray<float> reach;
            public int buckets;
            public float cell;
            public float floor;
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
        private struct EliminateJob : IJob
        {
            [ReadOnly] public NativeArray<float3> positions;
            [ReadOnly] public NativeArray<float> density;
            public NativeArray<int> order;
            public float area;

            public void Execute()
            {
                int input = positions.Length;
                int target = order.Length;

                if (target <= 0)
                {
                    return;
                }

                if (input <= target)
                {
                    for (int i = 0; i < target; i++)
                    {
                        order[i] = i < input ? i : 0;
                    }

                    return;
                }

                var alive = new NativeArray<int>(input, Allocator.Temp);

                for (int i = 0; i < input; i++)
                {
                    alive[i] = i;
                }

                int count = Reduce(alive, input, target, -1);

                while (count > 1)
                {
                    count = Reduce(alive, count, count >> 1, count);
                }

                order[0] = alive[0];
                alive.Dispose();
            }

            private int Reduce(NativeArray<int> alive, int count, int target, int writeEnd)
            {
                float reach = 2f * math.sqrt(area / (2f * math.sqrt(3f) * target));
                float floor = reach * (1f - math.pow(target / (float)count, Gamma)) * Beta;

                float thinnest = 1f;

                for (int i = 0; i < count; i++)
                {
                    thinnest = math.min(thinnest, math.max(density[alive[i]], MinimumDensity));
                }

                float cell = reach * math.rsqrt(thinnest);

                int buckets = math.ceilpow2(math.max(16, count * 2));
                var starts = new NativeArray<int>(buckets + 1, Allocator.Temp);
                var entries = new NativeArray<int>(count, Allocator.Temp);

                for (int i = 0; i < count; i++)
                {
                    starts[Bucket(Cell(positions[alive[i]], cell), buckets)]++;
                }

                int running = 0;

                for (int bucket = 0; bucket < buckets; bucket++)
                {
                    int here = starts[bucket];
                    starts[bucket] = running;
                    running += here;
                }

                starts[buckets] = running;

                var cursor = new NativeArray<int>(buckets, Allocator.Temp);
                NativeArray<int>.Copy(starts, cursor, buckets);

                for (int i = 0; i < count; i++)
                {
                    entries[cursor[Bucket(Cell(positions[alive[i]], cell), buckets)]++] = i;
                }

                cursor.Dispose();

                var grid = new Grid
                {
                    starts = starts,
                    entries = entries,
                    position = new NativeArray<float3>(count, Allocator.Temp),
                    reach = new NativeArray<float>(count, Allocator.Temp),
                    buckets = buckets,
                    cell = cell,
                    floor = floor,
                };

                var place = new NativeArray<int>(count, Allocator.Temp);

                for (int entry = 0; entry < count; entry++)
                {
                    int local = entries[entry];
                    int sample = alive[local];
                    grid.position[entry] = positions[sample];
                    grid.reach[entry] = reach * math.rsqrt(math.max(density[sample], MinimumDensity));
                    place[local] = entry;
                }

                var weight = new NativeArray<float>(count, Allocator.Temp);
                var heap = new NativeArray<int>(count, Allocator.Temp);
                var slot = new NativeArray<int>(count, Allocator.Temp);

                for (int entry = 0; entry < count; entry++)
                {
                    weight[entries[entry]] = Crowding(entry, grid);
                }

                for (int i = 0; i < count; i++)
                {
                    heap[i] = i;
                    slot[i] = i;
                }

                for (int i = (count >> 1) - 1; i >= 0; i--)
                {
                    SiftDown(i, count, heap, slot, weight);
                }

                int size = count;
                int write = writeEnd;

                while (size > target)
                {
                    int top = heap[0];
                    size--;
                    Swap(0, size, heap, slot);
                    SiftDown(0, size, heap, slot, weight);
                    slot[top] = -1;

                    if (write > 0)
                    {
                        order[--write] = alive[top];
                    }

                    Uncrowd(place[top], grid, weight, heap, slot, size);
                }

                var survivors = new NativeArray<int>(size, Allocator.Temp);

                for (int i = 0; i < size; i++)
                {
                    survivors[i] = alive[heap[i]];
                }

                for (int i = 0; i < size; i++)
                {
                    alive[i] = survivors[i];
                }

                survivors.Dispose();
                place.Dispose();
                weight.Dispose();
                heap.Dispose();
                slot.Dispose();
                grid.position.Dispose();
                grid.reach.Dispose();
                entries.Dispose();
                starts.Dispose();

                return size;
            }

            private static float Crowding(int index, in Grid grid)
            {
                float3 here = grid.position[index];
                float own = grid.reach[index];
                int3 home = Cell(here, grid.cell);
                float sum = 0f;

                for (int z = -1; z <= 1; z++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            int3 probe = home + new int3(x, y, z);
                            int bucket = Bucket(probe, grid.buckets);

                            for (int entry = grid.starts[bucket]; entry < grid.starts[bucket + 1]; entry++)
                            {
                                if (entry == index)
                                {
                                    continue;
                                }

                                float3 there = grid.position[entry];

                                if (!math.all(Cell(there, grid.cell) == probe))
                                {
                                    continue;
                                }

                                float distance = math.distance(here, there);

                                if (distance >= own)
                                {
                                    continue;
                                }

                                sum += Falloff(math.max(distance, grid.floor) / own);
                            }
                        }
                    }
                }

                return sum;
            }

            private static void Uncrowd(int leaving, in Grid grid, NativeArray<float> weight,
                                        NativeArray<int> heap, NativeArray<int> slot, int size)
            {
                float3 here = grid.position[leaving];
                int3 home = Cell(here, grid.cell);

                for (int z = -1; z <= 1; z++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            int3 probe = home + new int3(x, y, z);
                            int bucket = Bucket(probe, grid.buckets);

                            for (int entry = grid.starts[bucket]; entry < grid.starts[bucket + 1]; entry++)
                            {
                                if (entry == leaving)
                                {
                                    continue;
                                }

                                float3 there = grid.position[entry];

                                if (!math.all(Cell(there, grid.cell) == probe))
                                {
                                    continue;
                                }

                                float own = grid.reach[entry];
                                float distance = math.distance(here, there);

                                if (distance >= own)
                                {
                                    continue;
                                }

                                int other = grid.entries[entry];

                                if (slot[other] < 0)
                                {
                                    continue;
                                }

                                weight[other] = math.max(0f,
                                    weight[other] - Falloff(math.max(distance, grid.floor) / own));
                                SiftDown(slot[other], size, heap, slot, weight);
                            }
                        }
                    }
                }
            }

            private static float Falloff(float ratio)
            {
                float remaining = 1f - ratio;
                float squared = remaining * remaining;
                float fourth = squared * squared;
                return fourth * fourth;
            }

            private static int3 Cell(float3 position, float size)
            {
                return (int3)math.floor(position / size);
            }

            private static int Bucket(int3 cell, int buckets)
            {
                uint hash = (uint)(cell.x * 73856093) ^ (uint)(cell.y * 19349663) ^ (uint)(cell.z * 83492791);
                return (int)(hash & (uint)(buckets - 1));
            }

            private static void SiftDown(int at, int size, NativeArray<int> heap, NativeArray<int> slot,
                                         NativeArray<float> weight)
            {
                while (true)
                {
                    int left = at * 2 + 1;

                    if (left >= size)
                    {
                        return;
                    }

                    int largest = left;
                    int right = left + 1;

                    if (right < size && weight[heap[right]] > weight[heap[left]])
                    {
                        largest = right;
                    }

                    if (weight[heap[largest]] <= weight[heap[at]])
                    {
                        return;
                    }

                    Swap(at, largest, heap, slot);
                    at = largest;
                }
            }

            private static void Swap(int first, int second, NativeArray<int> heap, NativeArray<int> slot)
            {
                int a = heap[first];
                int b = heap[second];
                heap[first] = b;
                heap[second] = a;
                slot[b] = first;
                slot[a] = second;
            }
        }
    }
}
