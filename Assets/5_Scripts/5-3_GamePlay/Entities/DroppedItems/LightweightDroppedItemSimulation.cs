using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace FlatWorld.DroppedItems
{
    /// <summary>轻量掉落物的权威热数据；只保留世界表现、拾取、水体与数量。</summary>
    public struct LightweightDroppedBody
    {
        #region 世界水态与热数据

        public const byte BottomWaterKind = 3;

        public int Id;
        public float2 Position;
        public float2 Scale;
        public float Rotation;
        public float VisualHeight;
        public float Amount;
        public float LiquidDepth;
        public float SubmergedProgress;
        public byte WaterKind; // 0 陆地、1 漂浮、2 下沉、3 河底静止。
        public byte Pickable;

        public bool IsOnBottom => WaterKind == BottomWaterKind;

        #endregion
    }

    /// <summary>只有短期抛掷中的掉落物才持有轨迹。</summary>
    public struct LightweightDroppedFlight
    {
        public float2 Start;
        public float2 End;
        public float2 Control;
        public float Duration;
        public float Elapsed;
        public float ArcHeight;
        public float RotationSpeed;
    }

    /// <summary>轻量掉落物的短期水线过渡。</summary>
    public struct LightweightDroppedWaterTransition
    {
        public float StartDepth;
        public float TargetDepth;
        public float Duration;
        public float Elapsed;
        public float RecedeDuration;
    }

    public struct LightweightDroppedChange
    {
        public int Id;
        public byte Kind;
    }

    /// <summary>
    /// 普通 C# 掉落物模拟器；集中更新，没有 Entity、Job、Burst、逐物品 MonoBehaviour 或 Rigidbody2D。
    /// </summary>
    public sealed class LightweightDroppedItemSimulation : IDisposable
    {
        #region 连续热数据与活动表索引

        private struct DroppedBodyStorageEntry
        {
            public LightweightDroppedBody Body;
            public int FlightIndex;
            public int WaterIndex;
        }

        private struct DroppedFlightStorageEntry
        {
            public int BodyIndex;
            public LightweightDroppedFlight State;
        }

        private struct DroppedWaterStorageEntry
        {
            public int BodyIndex;
            public LightweightDroppedWaterTransition State;
        }

        // ID 只用于外部寻址，运动表通过下标直接读写唯一一份物体状态。
        private readonly Dictionary<int, int> bodyIndices = new();
        private DroppedBodyStorageEntry[] bodies = Array.Empty<DroppedBodyStorageEntry>();
        private DroppedFlightStorageEntry[] flights = Array.Empty<DroppedFlightStorageEntry>();
        private DroppedWaterStorageEntry[] waters = Array.Empty<DroppedWaterStorageEntry>();
        private int bodyCount;
        private int flightCount;
        private int waterCount;
        private bool disposed;

        public int Count => bodyCount;
        public bool IsCreated => !disposed;
        public IEnumerable<int> Ids => bodyIndices.Keys;

        #endregion

        #region 物体和短期运动登记

        public void Create(
            LightweightDroppedBody body,
            LightweightDroppedFlight? flight = null,
            LightweightDroppedWaterTransition? water = null)
        {
            ThrowIfDisposed();
            if (body.Id == 0 || bodyIndices.ContainsKey(body.Id))
                throw new ArgumentException("轻量掉落物 ID 为空或重复。", nameof(body));
            if (!math.all(math.isfinite(body.Position)) || !math.all(math.isfinite(body.Scale)) ||
                !math.all(math.isfinite(new float4(body.Amount, body.Rotation, body.VisualHeight, body.LiquidDepth))) ||
                body.Amount <= 0f || body.WaterKind > LightweightDroppedBody.BottomWaterKind || !math.isfinite(body.SubmergedProgress) ||
                body.SubmergedProgress < 0f || body.SubmergedProgress > 1f)
                throw new ArgumentException("轻量掉落物热数据无效。", nameof(body));

            if (flight.HasValue)
            {
                LightweightDroppedFlight value = flight.Value;
                if (!math.all(math.isfinite(value.Start)) || !math.all(math.isfinite(value.End)) ||
                    !math.all(math.isfinite(value.Control)) || value.Duration <= 0f || value.Elapsed < 0f ||
                    !math.all(math.isfinite(new float4(value.Duration, value.Elapsed, value.ArcHeight, value.RotationSpeed))))
                    throw new ArgumentException("轻量掉落物轨迹无效。", nameof(flight));
            }

            if (water.HasValue)
            {
                LightweightDroppedWaterTransition value = water.Value;
                if (value.Duration <= 0f || value.Elapsed < 0f || value.RecedeDuration < 0f ||
                    !math.isfinite(value.RecedeDuration) ||
                    !math.all(math.isfinite(new float4(value.StartDepth, value.TargetDepth, value.Duration, value.Elapsed))))
                    throw new ArgumentException("轻量掉落物水线过渡无效。", nameof(water));
            }

            EnsureStorageCapacity(ref bodies, bodyCount + 1);
            int bodyIndex = bodyCount++;
            bodyIndices.Add(body.Id, bodyIndex);
            bodies[bodyIndex] = new DroppedBodyStorageEntry { Body = body, FlightIndex = -1, WaterIndex = -1 };
            if (flight.HasValue)
            {
                EnsureStorageCapacity(ref flights, flightCount + 1);
                bodies[bodyIndex].FlightIndex = flightCount;
                flights[flightCount++] = new DroppedFlightStorageEntry { BodyIndex = bodyIndex, State = flight.Value };
            }
            if (water.HasValue) SetWater(body.Id, water);
        }

        public bool Contains(int id) => !disposed && bodyIndices.ContainsKey(id);
        public LightweightDroppedBody Get(int id) => bodies[bodyIndices[id]].Body;
        public void Set(LightweightDroppedBody body)
        {
            ThrowIfDisposed();
            bodies[bodyIndices[body.Id]].Body = body;
        }

        public bool TryGetFlight(int id, out LightweightDroppedFlight flight)
        {
            flight = default;
            if (!bodyIndices.TryGetValue(id, out int bodyIndex) || bodies[bodyIndex].FlightIndex < 0) return false;
            flight = flights[bodies[bodyIndex].FlightIndex].State;
            return true;
        }

        public bool TryGetWater(int id, out LightweightDroppedWaterTransition water)
        {
            water = default;
            if (!bodyIndices.TryGetValue(id, out int bodyIndex) || bodies[bodyIndex].WaterIndex < 0) return false;
            water = waters[bodies[bodyIndex].WaterIndex].State;
            return true;
        }

        public void SetWater(int id, LightweightDroppedWaterTransition? transition)
        {
            ThrowIfDisposed();
            if (!bodyIndices.TryGetValue(id, out int bodyIndex)) return;
            ref DroppedBodyStorageEntry entry = ref bodies[bodyIndex];
            if (!transition.HasValue) { RemoveWaterStorageEntry(entry.WaterIndex); return; }
            if (entry.WaterIndex < 0)
            {
                EnsureStorageCapacity(ref waters, waterCount + 1);
                entry.WaterIndex = waterCount;
                waters[waterCount++] = new DroppedWaterStorageEntry { BodyIndex = bodyIndex };
            }
            waters[entry.WaterIndex].State = transition.Value;
        }

        public void Remove(int id)
        {
            if (disposed || !bodyIndices.Remove(id, out int bodyIndex)) return;
            RemoveFlightStorageEntry(bodies[bodyIndex].FlightIndex);
            RemoveWaterStorageEntry(bodies[bodyIndex].WaterIndex);
            int lastIndex = --bodyCount;
            if (bodyIndex == lastIndex) return;
            bodies[bodyIndex] = bodies[lastIndex];
            ref DroppedBodyStorageEntry moved = ref bodies[bodyIndex];
            bodyIndices[moved.Body.Id] = bodyIndex;
            if (moved.FlightIndex >= 0) flights[moved.FlightIndex].BodyIndex = bodyIndex;
            if (moved.WaterIndex >= 0) waters[moved.WaterIndex].BodyIndex = bodyIndex;
        }

        private static void EnsureStorageCapacity<T>(ref T[] entries, int required)
        {
            if (entries.Length < required)
                Array.Resize(ref entries, Math.Max(required, Math.Max(4, entries.Length * 2)));
        }

        // 活动条目删除后用末项补位，并修正所属物体的反向索引。
        private void RemoveFlightStorageEntry(int index)
        {
            if (index < 0) return;
            bodies[flights[index].BodyIndex].FlightIndex = -1;
            int lastIndex = --flightCount;
            if (index == lastIndex) return;
            flights[index] = flights[lastIndex];
            bodies[flights[index].BodyIndex].FlightIndex = index;
        }

        private void RemoveWaterStorageEntry(int index)
        {
            if (index < 0) return;
            bodies[waters[index].BodyIndex].WaterIndex = -1;
            int lastIndex = --waterCount;
            if (index == lastIndex) return;
            waters[index] = waters[lastIndex];
            bodies[waters[index].BodyIndex].WaterIndex = index;
        }

        #endregion

        #region 飞行和入水过渡连续推进

        public void Step(float deltaTime, WorldTopologyDomain domain, List<LightweightDroppedChange> changes)
        {
            changes.Clear();
            if (disposed || deltaTime <= 0f || bodyCount == 0) return;

            for (int i = 0; i < flightCount;)
            {
                ref DroppedFlightStorageEntry entry = ref flights[i];
                ref LightweightDroppedBody body = ref bodies[entry.BodyIndex].Body;
                ref LightweightDroppedFlight flight = ref entry.State;

                flight.Elapsed = math.min(flight.Duration, flight.Elapsed + deltaTime);
                float t = math.saturate(flight.Elapsed / math.max(0.0001f, flight.Duration));
                float2 ground = math.lerp(flight.Start, flight.End, t);
                float mt = 1f - t;
                float2 visual = mt * mt * flight.Start + 2f * mt * t * flight.Control + t * t * flight.End;
                body.Position = domain.Normalize(ground);
                body.VisualHeight = t >= 1f
                    ? 0f
                    : visual.y - ground.y + math.sin(t * math.PI) * flight.ArcHeight;
                body.Rotation += flight.RotationSpeed * deltaTime;
                bool landed = t >= 1f;
                if (landed) RemoveFlightStorageEntry(i);
                else i++;
                changes.Add(new LightweightDroppedChange { Id = body.Id, Kind = (byte)(landed ? 1 : 0) });
            }

            for (int i = 0; i < waterCount;)
            {
                ref DroppedWaterStorageEntry entry = ref waters[i];
                ref LightweightDroppedBody body = ref bodies[entry.BodyIndex].Body;
                ref LightweightDroppedWaterTransition transition = ref entry.State;

                float endTime = transition.Duration +
                    (body.WaterKind == 2 ? math.max(0f, transition.RecedeDuration) : 0f);
                transition.Elapsed = math.min(endTime, transition.Elapsed + deltaTime);
                float t = math.saturate(transition.Elapsed / math.max(0.0001f, transition.Duration));
                float weight = body.WaterKind == 1 ? t * t * (3f - 2f * t) : t;
                body.LiquidDepth = math.lerp(transition.StartDepth, transition.TargetDepth, weight);
                body.SubmergedProgress = body.WaterKind == 2
                    ? math.saturate((transition.Elapsed - transition.Duration) /
                                    math.max(0.0001f, transition.RecedeDuration))
                    : 0f;
                bool finished = transition.Elapsed >= endTime;
                bool reachedBottom = finished && body.WaterKind == 2;
                // 沉没结束只转为河底静止态，实体、位置和数量继续保留给拾取与存档。
                if (reachedBottom) body.WaterKind = LightweightDroppedBody.BottomWaterKind;
                if (finished) RemoveWaterStorageEntry(i);
                else i++;
                changes.Add(new LightweightDroppedChange
                {
                    Id = body.Id,
                    Kind = (byte)(finished ? (reachedBottom ? 3 : 2) : 0)
                });
            }
        }

        #endregion

        #region 模拟工作区生命周期
        public void Dispose()
        {
            if (disposed) return;
            Clear();
            disposed = true;
        }

        // 冷世界逐次重填工作区，保留容量但不保留上个世界的物品和轨迹。
        public void Clear()
        {
            ThrowIfDisposed();
            bodyIndices.Clear();
            bodyCount = flightCount = waterCount = 0;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(LightweightDroppedItemSimulation));
        }
        #endregion
    }
}
