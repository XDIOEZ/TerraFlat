using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace FlatWorld.DroppedItems
{
    /// <summary>轻量掉落物的权威热数据；只保留世界表现、拾取、水体与数量。</summary>
    public struct LightweightDroppedBody
    {
        public int Id;
        public float2 Position;
        public float2 Scale;
        public float Rotation;
        public float VisualHeight;
        public float Amount;
        public float LiquidDepth;
        public float SubmergedProgress;
        public byte WaterKind;
        public byte Pickable;
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
        private readonly Dictionary<int, LightweightDroppedBody> bodies = new();
        private readonly Dictionary<int, LightweightDroppedFlight> flights = new();
        private readonly Dictionary<int, LightweightDroppedWaterTransition> waters = new();
        private readonly List<int> flightScratch = new();
        private readonly List<int> waterScratch = new();
        private bool disposed;

        public int Count => bodies.Count;
        public bool IsCreated => !disposed;
        public IEnumerable<int> Ids => bodies.Keys;

        public void Create(
            LightweightDroppedBody body,
            LightweightDroppedFlight? flight = null,
            LightweightDroppedWaterTransition? water = null)
        {
            ThrowIfDisposed();
            if (body.Id == 0 || bodies.ContainsKey(body.Id))
                throw new ArgumentException("轻量掉落物 ID 为空或重复。", nameof(body));
            if (!math.all(math.isfinite(body.Position)) || !math.all(math.isfinite(body.Scale)) ||
                !math.all(math.isfinite(new float4(body.Amount, body.Rotation, body.VisualHeight, body.LiquidDepth))) ||
                body.Amount <= 0f || body.WaterKind > 2 || !math.isfinite(body.SubmergedProgress) ||
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

            bodies.Add(body.Id, body);
            if (flight.HasValue) flights.Add(body.Id, flight.Value);
            if (water.HasValue) waters.Add(body.Id, water.Value);
        }

        public bool Contains(int id) => !disposed && bodies.ContainsKey(id);
        public LightweightDroppedBody Get(int id) => bodies[id];
        public void Set(LightweightDroppedBody body)
        {
            ThrowIfDisposed();
            if (!bodies.ContainsKey(body.Id))
                throw new KeyNotFoundException($"轻量掉落物不存在：{body.Id}");
            bodies[body.Id] = body;
        }

        public bool TryGetFlight(int id, out LightweightDroppedFlight flight) => flights.TryGetValue(id, out flight);
        public bool TryGetWater(int id, out LightweightDroppedWaterTransition water) => waters.TryGetValue(id, out water);

        public void SetWater(int id, LightweightDroppedWaterTransition? transition)
        {
            ThrowIfDisposed();
            if (!bodies.ContainsKey(id)) return;
            if (transition.HasValue) waters[id] = transition.Value;
            else waters.Remove(id);
        }

        public void Remove(int id)
        {
            if (disposed) return;
            bodies.Remove(id);
            flights.Remove(id);
            waters.Remove(id);
        }

        public void Step(float deltaTime, WorldTopologyDomain domain, List<LightweightDroppedChange> changes)
        {
            changes.Clear();
            if (disposed || deltaTime <= 0f || bodies.Count == 0) return;

            flightScratch.Clear();
            flightScratch.AddRange(flights.Keys);
            for (int i = 0; i < flightScratch.Count; i++)
            {
                int id = flightScratch[i];
                if (!bodies.TryGetValue(id, out LightweightDroppedBody body) ||
                    !flights.TryGetValue(id, out LightweightDroppedFlight flight))
                    continue;

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
                bodies[id] = body;

                bool landed = t >= 1f;
                if (landed) flights.Remove(id);
                else flights[id] = flight;
                changes.Add(new LightweightDroppedChange { Id = id, Kind = (byte)(landed ? 1 : 0) });
            }

            waterScratch.Clear();
            waterScratch.AddRange(waters.Keys);
            for (int i = 0; i < waterScratch.Count; i++)
            {
                int id = waterScratch[i];
                if (!bodies.TryGetValue(id, out LightweightDroppedBody body) ||
                    !waters.TryGetValue(id, out LightweightDroppedWaterTransition transition))
                    continue;

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
                bodies[id] = body;

                bool finished = transition.Elapsed >= endTime;
                if (finished) waters.Remove(id);
                else waters[id] = transition;
                changes.Add(new LightweightDroppedChange
                {
                    Id = id,
                    Kind = (byte)(finished ? (body.WaterKind == 2 ? 3 : 2) : 0)
                });
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            bodies.Clear();
            flights.Clear();
            waters.Clear();
            flightScratch.Clear();
            waterScratch.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(LightweightDroppedItemSimulation));
        }
    }
}
