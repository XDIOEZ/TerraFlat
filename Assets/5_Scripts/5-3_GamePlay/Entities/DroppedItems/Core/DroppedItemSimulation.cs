using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.DroppedItems
{
    /// <summary>真正的 Entities 存储与生命周期；不引用 Item、GameObject、资源目录或背包。</summary>
    public sealed class DroppedItemSimulation : IDisposable
    {
        private readonly World world;
        private readonly Dictionary<int, Entity> entities = new();
        private readonly DroppedMotionSystem motion;
        public int Count => entities.Count;
        public bool IsCreated => world.IsCreated;
        public IEnumerable<int> Ids => entities.Keys;
        public EntityManager Manager => world.EntityManager;

        public DroppedItemSimulation()
        {
            world = new World("FlatWorld 掉落物 ECS");
            motion = world.GetOrCreateSystemManaged<DroppedMotionSystem>();
        }

        #region 实体数据

        public void Create(DroppedBody body, DroppedFlight? flight = null, DroppedWaterTransition? water = null)
        {
            if (body.Id == 0 || entities.ContainsKey(body.Id))
                throw new ArgumentException("掉落物 ID 为空或重复。", nameof(body));
            if (!math.all(math.isfinite(body.Position)) || !math.all(math.isfinite(body.Scale)) ||
                !math.all(math.isfinite(new float4(body.Amount, body.Rotation, body.VisualHeight, body.WaterDepth))) ||
                body.Amount <= 0f || body.WaterKind > 2 || !math.isfinite(body.SubmergedProgress) ||
                body.SubmergedProgress < 0f || body.SubmergedProgress > 1f)
                throw new ArgumentException("掉落物热数据包含无效坐标、数量或水态。", nameof(body));
            if (flight.HasValue)
            {
                DroppedFlight value = flight.Value;
                if (!math.all(math.isfinite(value.Start)) || !math.all(math.isfinite(value.End)) ||
                    !math.all(math.isfinite(value.Control)) || value.Duration <= 0f || value.Elapsed < 0f ||
                    !math.all(math.isfinite(new float4(value.Duration, value.Elapsed, value.ArcHeight, value.RotationSpeed))))
                    throw new ArgumentException("掉落物轨迹数据无效。", nameof(flight));
            }
            if (water.HasValue)
            {
                DroppedWaterTransition value = water.Value;
                if (value.Duration <= 0f || value.Elapsed < 0f || value.RecedeDuration < 0f ||
                    !math.isfinite(value.RecedeDuration) ||
                    !math.all(math.isfinite(new float4(value.StartDepth, value.TargetDepth, value.Duration, value.Elapsed))))
                    throw new ArgumentException("掉落物水线过渡数据无效。", nameof(water));
            }
            Entity entity = Manager.CreateEntity(typeof(DroppedBody));
            Manager.SetComponentData(entity, body);
            if (flight.HasValue) Manager.AddComponentData(entity, flight.Value);
            if (water.HasValue) Manager.AddComponentData(entity, water.Value);
            entities.Add(body.Id, entity);
        }

        public bool Contains(int id) => entities.ContainsKey(id);
        public DroppedBody Get(int id) => Manager.GetComponentData<DroppedBody>(entities[id]);
        public void Set(DroppedBody body) => Manager.SetComponentData(entities[body.Id], body);
        public bool TryGetFlight(int id, out DroppedFlight flight) => TryRead(id, out flight);
        public bool TryGetWater(int id, out DroppedWaterTransition water) => TryRead(id, out water);

        private bool TryRead<T>(int id, out T value) where T : unmanaged, IComponentData
        {
            if (entities.TryGetValue(id, out Entity entity) && Manager.HasComponent<T>(entity))
            {
                value = Manager.GetComponentData<T>(entity);
                return true;
            }
            value = default;
            return false;
        }

        public void SetWater(int id, DroppedWaterTransition? transition)
        {
            Entity entity = entities[id];
            if (!transition.HasValue) Manager.RemoveComponent<DroppedWaterTransition>(entity);
            else if (Manager.HasComponent<DroppedWaterTransition>(entity)) Manager.SetComponentData(entity, transition.Value);
            else Manager.AddComponentData(entity, transition.Value);
        }

        public void Remove(int id)
        {
            if (!entities.TryGetValue(id, out Entity entity)) return;
            Manager.DestroyEntity(entity);
            entities.Remove(id);
        }

        #endregion

        #region 系统驱动

        /// <summary>每个世界只驱动一次；落地事件之后由玩法层确认水体，再开放拾取。</summary>
        public void Step(float deltaTime, WorldTopologyDomain domain, List<DroppedChange> changes)
        {
            changes.Clear();
            if (deltaTime <= 0f || entities.Count == 0) return;
            motion.Domain = domain;
            motion.StepSeconds = deltaTime;
            motion.Update();
            motion.Complete();
            for (int i = 0; i < motion.Changes.Length; i++)
            {
                DroppedChange change = motion.Changes[i];
                if (!entities.TryGetValue(change.Id, out Entity entity)) continue;
                if (change.Kind == 1) Manager.RemoveComponent<DroppedFlight>(entity);
                if (change.Kind == 2 || change.Kind == 3) Manager.RemoveComponent<DroppedWaterTransition>(entity);
                changes.Add(change);
            }
        }

        public void Dispose()
        {
            if (world.IsCreated) world.Dispose();
            entities.Clear();
        }

        #endregion
    }
}
