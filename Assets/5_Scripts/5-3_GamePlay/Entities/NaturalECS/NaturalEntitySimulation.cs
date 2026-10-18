using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.NaturalEntities
{
    #region 自然生成身份与桥接快照

    /// <summary>运行时句柄包含实体世界代际，旧区块回调不能操作新世界中复用的整数 ID。</summary>
    public readonly struct NaturalEntityHandle : IEquatable<NaturalEntityHandle>
    {
        public NaturalEntityHandle(int id, ulong generation) { Id = id; Generation = generation; }
        public int Id { get; }
        public ulong Generation { get; }
        public bool IsValid => Id != 0 && Generation != 0;
        public bool Equals(NaturalEntityHandle other) => Id == other.Id && Generation == other.Generation;
        public override bool Equals(object obj) => obj is NaturalEntityHandle other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, Generation);
        public static bool operator ==(NaturalEntityHandle left, NaturalEntityHandle right) => left.Equals(right);
        public static bool operator !=(NaturalEntityHandle left, NaturalEntityHandle right) => !left.Equals(right);
    }

    [Flags]
    internal enum NaturalEntityCapability : byte { None = 0, Health = 1, Growth = 2, Climate = 4, Harvest = 8 }

    /// <summary>只有生成来源和静态位置是资源适配数据，生命、生长、耐候与表现均复用通用组件。</summary>
    internal struct NaturalEntityLocation : IComponentData
    {
        public int RuntimeId, NaturalGuid;
        public float2 Position;
    }

    /// <summary>主线程边界的一次性值快照，不在 ECS 内保存第二份生命或表现权威。</summary>
    internal struct NaturalEntityBody
    {
        public int RuntimeId, NaturalGuid;
        public float2 Position, Scale;
        public float Rotation;
        public uint VisualVersion;
        public byte Dead, Suspended;
    }

    public readonly struct NaturalEntityPresentationState
    {
        public NaturalEntityPresentationState(float2 position, float2 scale, float rotation,
            uint visualVersion, bool dead, bool suspended, byte growthStage)
        {
            Position = position; Scale = scale; Rotation = rotation; VisualVersion = visualVersion;
            Dead = dead; Suspended = suspended; GrowthStage = growthStage;
        }
        public float2 Position { get; }
        public float2 Scale { get; }
        public float Rotation { get; }
        public uint VisualVersion { get; }
        public bool Dead { get; }
        public bool Suspended { get; }
        public byte GrowthStage { get; }
    }

    #endregion

    /// <summary>自然生成到共享 EntityManager 的薄适配，不创建 World，也不单独调度能力 Job。</summary>
    internal sealed class NaturalEntitySimulation : IDisposable
    {
        #region 借用世界与资源身份

        private readonly World world;
        private readonly Dictionary<int, Entity> entities = new();
        private readonly Dictionary<NaturalEntityEcsProfile, EntityArchetype> archetypes = new();
        private readonly Dictionary<int, NaturalEntityBody> bodySnapshots = new();
        private readonly EntityQuery bodyQuery;
        private bool readingBodyBatch;
        public event Action<int> Changed;
        public NaturalEntitySimulation(World sharedWorld)
        {
            world = sharedWorld ?? throw new ArgumentNullException(nameof(sharedWorld));
            if (!world.IsCreated) throw new InvalidOperationException("实体世界已释放。");
            bodyQuery = Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<NaturalEntityLocation>(),
                    ComponentType.ReadOnly<EntityModuleAppearance>(), ComponentType.ReadOnly<EntityModuleActive>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });
        }
        public bool IsCreated => world.IsCreated;
        public int Count => entities.Count;
        public EntityManager Manager => world.EntityManager;
        public bool Contains(int id) => IsCreated && entities.TryGetValue(id, out Entity entity) && Manager.Exists(entity);
        public World World => world;
        public Entity GetEntity(int id) => entities[id];

        public DynamicBuffer<T> Buffer<T>(int id) where T : unmanaged, IBufferElementData
        {
            Entity entity = entities[id];
            return Manager.HasComponent<T>(entity) ? Manager.GetBuffer<T>(entity) : Manager.AddBuffer<T>(entity);
        }

        public void Create(NaturalEntityEcsProfile profile, int runtimeId, NaturalEntityBody body, AiecsVital? health,
            EntityGrowth? growth, EntityClimate? climate, EntityHarvestRequirement? harvest)
        {
            Complete();
            if (runtimeId == 0 || entities.ContainsKey(runtimeId))
                throw new ArgumentException("自然物运行时 ID 为空或重复。", nameof(runtimeId));
            if (!math.all(math.isfinite(body.Position)) || !math.all(math.isfinite(body.Scale)) ||
                !math.isfinite(body.Rotation)) throw new ArgumentException("自然物姿态包含无效数值。");
            // 模板缓存完整组件组合，新资源不再逐个追加能力造成多次结构搬移。
            if (!archetypes.TryGetValue(profile, out EntityArchetype archetype))
            {
                archetype = Manager.CreateArchetype(profile.ComponentTypes);
                archetypes.Add(profile, archetype);
            }
            Entity entity = Manager.CreateEntity(archetype);
            try
            {
                Manager.SetComponentData(entity, new NaturalEntityLocation
                { RuntimeId = runtimeId, NaturalGuid = body.NaturalGuid, Position = body.Position });
                Manager.SetComponentData(entity, new EntityModuleAppearance
                { Scale = body.Scale, Rotation = body.Rotation, Revision = body.VisualVersion });
                Manager.SetComponentEnabled<EntityModuleActive>(entity, body.Suspended == 0);
                if (health.HasValue)
                {
                    AiecsVital vital = health.Value;
                    if (body.Dead != 0) { vital.Dead = 1; vital.Hp = 0f; }
                    Manager.SetComponentData(entity, vital);
                }
                if (growth.HasValue) Manager.SetComponentData(entity, growth.Value);
                if (climate.HasValue) Manager.SetComponentData(entity, climate.Value);
                if (harvest.HasValue) Manager.SetComponentData(entity, harvest.Value);
                entities.Add(runtimeId, entity);
            }
            catch { Manager.DestroyEntity(entity); throw; }
        }

        public void Remove(int id)
        {
            if (!entities.TryGetValue(id, out Entity entity)) return;
            bodySnapshots.Remove(id);
            if (IsCreated) { Complete(); if (Manager.Exists(entity)) Manager.DestroyEntity(entity); }
            entities.Remove(id);
        }

        public void Dispose()
        {
            if (IsCreated)
            {
                Complete();
                using var removed = new NativeList<Entity>(entities.Count, Allocator.Temp);
                foreach (Entity entity in entities.Values)
                    if (Manager.Exists(entity)) removed.Add(entity);
                if (removed.Length > 0) Manager.DestroyEntity(removed.AsArray());
            }
            entities.Clear();
            archetypes.Clear();
            bodySnapshots.Clear();
            readingBodyBatch = false;
            if (IsCreated) bodyQuery.Dispose();
            Changed = null;
        }

        #endregion

        #region 组件边界

        internal readonly struct BodyReadBatch : IDisposable
        {
            private readonly NaturalEntitySimulation owner;
            internal BodyReadBatch(NaturalEntitySimulation owner) => this.owner = owner;
            public void Dispose()
            {
                if (owner == null) return;
                owner.readingBodyBatch = false;
                owner.bodySnapshots.Clear();
            }
        }

        // 回调前复制批量只读值，不持有跨结构变更的 Chunk/NativeArray 引用。
        internal BodyReadBatch BeginBodyReadBatch()
        {
            if (readingBodyBatch) throw new InvalidOperationException("自然物发布读取不允许重入。");
            bodySnapshots.Clear();
            Manager.CompleteDependencyBeforeRO<NaturalEntityLocation>();
            Manager.CompleteDependencyBeforeRO<EntityModuleAppearance>();
            Manager.CompleteDependencyBeforeRO<EntityModuleActive>();
            Manager.CompleteDependencyBeforeRO<AiecsVital>();
            var locations = Manager.GetComponentTypeHandle<NaturalEntityLocation>(true);
            var appearances = Manager.GetComponentTypeHandle<EntityModuleAppearance>(true);
            var active = Manager.GetComponentTypeHandle<EntityModuleActive>(true);
            var vitals = Manager.GetComponentTypeHandle<AiecsVital>(true);
            var entityType = Manager.GetEntityTypeHandle();
            using var chunks = bodyQuery.ToArchetypeChunkArray(Allocator.Temp);
            foreach (ArchetypeChunk chunk in chunks)
            {
                var sources = chunk.GetNativeArray(ref locations);
                var visuals = chunk.GetNativeArray(ref appearances);
                var health = chunk.GetNativeArray(ref vitals);
                var identities = chunk.GetNativeArray(entityType);
                for (int index = 0; index < chunk.Count; index++)
                {
                    NaturalEntityLocation source = sources[index];
                    if (!entities.TryGetValue(source.RuntimeId, out Entity owned) || owned != identities[index]) continue;
                    EntityModuleAppearance visual = visuals[index];
                    bodySnapshots[source.RuntimeId] = new NaturalEntityBody
                    {
                        RuntimeId = source.RuntimeId, NaturalGuid = source.NaturalGuid, Position = source.Position,
                        Scale = visual.Scale, Rotation = visual.Rotation, VisualVersion = visual.Revision,
                        Dead = health.IsCreated ? health[index].Dead : (byte)0,
                        Suspended = (byte)(chunk.IsComponentEnabled(ref active, index) ? 0 : 1)
                    };
                }
            }
            readingBodyBatch = true;
            return new BodyReadBatch(this);
        }

        /// <summary>少量发布目标只冻结所需实体，避免少量树冠触发整个 ECS 世界遍历。</summary>
        internal BodyReadBatch BeginBodyReadBatch(IReadOnlyCollection<int> requestedIds)
        {
            if (requestedIds == null) throw new ArgumentNullException(nameof(requestedIds));
            if (readingBodyBatch) throw new InvalidOperationException("自然物发布读取不允许重入。");
            bodySnapshots.Clear();
            Manager.CompleteDependencyBeforeRO<NaturalEntityLocation>();
            Manager.CompleteDependencyBeforeRO<EntityModuleAppearance>();
            Manager.CompleteDependencyBeforeRO<EntityModuleActive>();
            Manager.CompleteDependencyBeforeRO<AiecsVital>();
            foreach (int id in requestedIds)
            {
                if (!Contains(id)) continue;
                bodySnapshots[id] = GetBody(id);
            }
            readingBodyBatch = true;
            return new BodyReadBatch(this);
        }

        internal int CapturedBodyCount => bodySnapshots.Count;

        internal void InvalidateBodySnapshot(int id) => bodySnapshots.Remove(id);

        public NaturalEntityBody GetBody(int id)
        {
            if (readingBodyBatch && bodySnapshots.TryGetValue(id, out NaturalEntityBody cached)) return cached;
            // EntityManager 按组件完成写依赖，读取单棵树不再等待整个 World 的所有 Job。
            Entity entity = entities[id];
            NaturalEntityLocation source = Manager.GetComponentData<NaturalEntityLocation>(entity);
            EntityModuleAppearance visual = Manager.GetComponentData<EntityModuleAppearance>(entity);
            return new NaturalEntityBody
            {
                RuntimeId = source.RuntimeId, NaturalGuid = source.NaturalGuid, Position = source.Position,
                Scale = visual.Scale, Rotation = visual.Rotation, VisualVersion = visual.Revision,
                Dead = Manager.HasComponent<AiecsVital>(entity) ? Manager.GetComponentData<AiecsVital>(entity).Dead : (byte)0,
                Suspended = (byte)(Manager.IsComponentEnabled<EntityModuleActive>(entity) ? 0 : 1)
            };
        }

        public void SetBody(int id, NaturalEntityBody body)
        {
            bodySnapshots.Remove(id);
            Entity entity = entities[id];
            Manager.SetComponentData(entity, new NaturalEntityLocation
            { RuntimeId = id, NaturalGuid = body.NaturalGuid, Position = body.Position });
            Manager.SetComponentData(entity, new EntityModuleAppearance
            { Scale = body.Scale, Rotation = body.Rotation, Revision = body.VisualVersion });
            Manager.SetComponentEnabled<EntityModuleActive>(entity, body.Suspended == 0);
            Changed?.Invoke(id);
        }

        public bool TryGet<T>(int id, out T value) where T : unmanaged, IComponentData
        {
            if (IsCreated && entities.TryGetValue(id, out Entity entity) && Manager.HasComponent<T>(entity))
            { value = Manager.GetComponentData<T>(entity); return true; }
            value = default;
            return false;
        }

        public void Set<T>(int id, T value, bool notifyChanged = true) where T : unmanaged, IComponentData
        {
            if (typeof(T) == typeof(AiecsVital) || typeof(T) == typeof(EntityModuleAppearance) ||
                typeof(T) == typeof(NaturalEntityLocation)) bodySnapshots.Remove(id);
            // EntityManager 自行完成对应组件的读写依赖，结构变更仍使用其同步边界。
            Entity entity = entities[id];
            if (Manager.HasComponent<T>(entity)) Manager.SetComponentData(entity, value);
            else Manager.AddComponentData(entity, value);
            if (notifyChanged) Changed?.Invoke(id);
        }

        public void RemoveComponent<T>(int id) where T : unmanaged, IComponentData
        {
            bodySnapshots.Remove(id);
            Entity entity = entities[id];
            if (Manager.HasComponent<T>(entity))
            {
                Manager.RemoveComponent<T>(entity);
                Changed?.Invoke(id);
            }
        }

        public void Complete()
        {
            if (IsCreated) Manager.CompleteAllTrackedJobs();
        }

        #endregion
    }
}
