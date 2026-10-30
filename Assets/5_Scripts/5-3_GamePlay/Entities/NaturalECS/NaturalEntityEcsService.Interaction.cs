using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    /// <summary>只读自然实体目标投影，保留正式交互身份与实体代际，不创建 Item 外壳。</summary>
    public readonly struct NaturalEntityTargetObservation
    {
        public readonly NaturalEntityHandle Handle;
        public readonly IWorldInteractionTarget Target;
        public readonly string ItemId;
        public readonly Bounds AimBounds;
        public readonly bool HasHealth;
        public readonly Vector2 Health;
        public NaturalEntityTargetObservation(NaturalEntityHandle handle, IWorldInteractionTarget target,
            string itemId, Bounds aimBounds, bool hasHealth, Vector2 health)
        { Handle = handle; Target = target; ItemId = itemId; AimBounds = aimBounds; HasHealth = hasHealth; Health = health; }
    }

    public static partial class NaturalEntityEcsService
    {
        #region 空间索引与无外壳目标

        private sealed partial class Record : IWorldInteractionTarget, ISpatialInteractionShape, IWorldInteractionPreview,
            IGameplayPhysicsDamageTarget
        {
            public readonly List<Vector2Int> SpatialCells = new();
            public ulong LastSpatialQuery;
            public Bounds HitBounds, BodyBounds, VisualBounds;
            public Bounds InteractionBounds;
            public bool HasConfiguredInteractionBounds;
            public bool BlocksMovement;
            public bool HitColliderEnabled;
            public int ManualHarvestProgress;
            public Vector3 WorldPosition
            {
                get { using var scope = UseHandle(Handle); return WorldLocalPresentation.ProjectPosition(Snapshot.transform.position); }
            }
            public int TargetGuid => Snapshot.Guid;
            public string ItemDefinitionId => Snapshot.IDName;
            public bool IsValid
            {
                get { using var scope = UseHandle(Handle); return Contains(Handle) && simulation.GetBody(Handle.Id).Dead == 0; }
            }
            public bool CanInteract(Item actor) => actor != null &&
                TryGetActorPosition(actor, WorldKey, out _) && IsValid && CanHarvest(Handle);
            // 资源采集保留鼠标点选，不经过设备面板入口。
            public bool CanPointerInteract(Item actor) => true;
            public bool ContainsInteractionPoint(Vector2 point) => ContainsPoint(this, point);
            public void OnInteractStart(Item actor) => TryHarvest(Handle, actor);
            public void OnInteractCancel(Item actor) { }
            public void SetInteractionHighlighted(bool highlighted) { Highlighted = highlighted; RefreshPresentation(Handle); }
            public void ReceivePhysicsDamage(Mod_Damage weapon, in CombatDamageContext context)
                => ApplyWeaponHit(this, weapon, context);
        }

        private static Dictionary<Vector2Int, HashSet<int>> spatialCells => State.spatialCells;
        private static HashSet<Record> habitatTreeRecords => State.habitatTreeRecords;
        private static readonly ProfilerMarker TreeHabitatMarker = new("NaturalEntityRuntime.TreeHabitat");
        private static readonly Stack<List<Record>> queryLists = new();
        private static ulong spatialQuerySequence { get => State.spatialQuerySequence; set => State.spatialQuerySequence = value; }
        public static ulong SpatialRevision => State.SpatialRevision;
        public enum PhysicsBodyChangeReason : byte { Registered, Removed, VisualRevision, SnapshotRefresh }
        public static event Action<Bounds> PhysicsBodyChanged;
        public static event Action<Bounds, PhysicsBodyChangeReason, int, string> PhysicsBodyChangedWithReason;
        public static event Action PhysicsBodiesReset;
        private static void PublishPhysicsBodyChanged(Record record, PhysicsBodyChangeReason reason, bool exists)
        {
            PhysicsBodyChanged?.Invoke(record.BodyBounds);
            PhysicsBodyChangedWithReason?.Invoke(record.BodyBounds, reason, record.Snapshot.Guid, record.Profile.Definition.Id);
        }

        private readonly struct ResourceQuery : IDisposable
        {
            private readonly List<Record> items;
            public ResourceQuery(List<Record> value) => items = value;
            public List<Record>.Enumerator GetEnumerator() => items.GetEnumerator();
            public void Dispose() { items.Clear(); queryLists.Push(items); }
        }

        /// <summary>种植占格和 AI 资源搜索均查询 Entity，不依赖旧 Item 空间索引。</summary>
        public static bool HasResourceAtCell(Vector2Int cell)
        {
            cell = WorldTopologyRuntime.NormalizeCell(cell);
            if (IsCultivatedCellOccupied(cell)) return true;
            if (!spatialCells.TryGetValue(cell, out var bucket)) return false;
            foreach (int id in bucket)
            {
                if (!records.TryGetValue(id, out Record record) || !record.IsValid) continue;
                NaturalEntityBody body = simulation.GetBody(id);
                Vector2Int root = WorldTopologyRuntime.NormalizeCell(new Vector2Int(Mathf.FloorToInt(body.Position.x), Mathf.FloorToInt(body.Position.y)));
                if (OccupiesCell(record, root, cell)) return true;
            }
            return false;
        }

        /// <summary>按生成身份和位置读取实时生命，不写回存档快照。</summary>
        public static bool TryGetHealthAtPosition(int guid, Vector2 position, out Vector2 health)
        {
            health = default;
            if (simulation == null) return false;
            using ResourceQuery query = QueryBounds(position, Vector2.one);
            foreach (Record record in query)
                if (record.Snapshot.Guid == guid && record.IsValid &&
                    WorldTopologyRuntime.Distance(record.Snapshot.transform.position, position) < .01f &&
                    simulation.TryGet(record.Handle.Id, out AiecsVital vital))
                { health = new Vector2(vital.Hp, vital.MaxHp); return true; }
            return false;
        }

        /// <summary>鼠标格只读查询不要求资源已经成熟或可采集，复用自然实体空间桶。</summary>
        public static bool TryGetWorldItemDefinitionAtCell(Vector2Int cell, out string itemId)
        {
            itemId = null;
            if (simulation == null) return false;
            cell = WorldTopologyRuntime.NormalizeCell(cell);
            if (!spatialCells.TryGetValue(cell, out var bucket)) return false;
            Record selected = null;
            foreach (int id in bucket)
            {
                if (!records.TryGetValue(id, out Record record) || !record.IsValid ||
                    record.DimensionId != WorldEntityRuntime.DimensionId) continue;
                NaturalEntityBody body = simulation.GetBody(id);
                Vector2Int root = WorldTopologyRuntime.NormalizeCell(new Vector2Int(Mathf.FloorToInt(body.Position.x), Mathf.FloorToInt(body.Position.y)));
                if (OccupiesCell(record, root, cell) && (selected == null || record.Snapshot.Guid < selected.Snapshot.Guid)) selected = record;
            }
            itemId = selected?.Snapshot.IDName;
            return !string.IsNullOrEmpty(itemId);
        }

        /// <summary>按实际空间桶观察当前世界存活实体，未激活生成点不会成为可操作目标。</summary>
        public static void QueryTargetObservations(Item actor, float radius, List<NaturalEntityTargetObservation> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.Clear();
            if (actor == null || !float.IsFinite(radius) || radius <= 0f || simulation == null) return;
            using ResourceQuery query = QueryBounds(actor.transform.position, Vector2.one * radius);
            foreach (Record record in query)
            {
                if (!record.IsValid || record.DimensionId != WorldEntityRuntime.DimensionId ||
                    SqrDistanceToBounds(actor.transform.position, record.HitBounds) > radius * radius) continue;
                bool hasHealth = simulation.TryGet(record.Handle.Id, out AiecsVital vital);
                result.Add(new NaturalEntityTargetObservation(record.Handle, record, record.Profile.Definition.Id,
                    record.HitBounds, hasHealth, hasHealth ? new Vector2(vital.Hp, vital.MaxHp) : default));
            }
        }

        /// <summary>动作前按带代际句柄重新读取目标，删除、卸载或换代后立即拒绝。</summary>
        public static bool TryGetTargetObservation(NaturalEntityHandle handle, out NaturalEntityTargetObservation observation)
        {
            using var scope = UseHandle(handle);
            observation = default;
            if (!Contains(handle) || !records.TryGetValue(handle.Id, out Record record) || !record.IsValid) return false;
            bool hasHealth = simulation.TryGet(handle.Id, out AiecsVital vital);
            observation = new NaturalEntityTargetObservation(handle, record, record.Profile.Definition.Id,
                record.HitBounds, hasHealth, hasHealth ? new Vector2(vital.Hp, vital.MaxHp) : default);
            return true;
        }

        #region 树木栖息地查询

        /// <summary>树木身份在注册、卸载和定义刷新时维护，查询不再遍历所有自然资源。</summary>
        private static void UpdateTreeHabitatIndex(Record record)
        {
            State.SpatialRevision++;
            if (record.Profile?.Definition?.HasTag(Tag.Tree) == true)
                habitatTreeRecords.Add(record);
            else
                habitatTreeRecords.Remove(record);
        }

        /// <summary>给低频鸟类栖息地快照追加当前世界仍存活的 ECS 树木，不创建 Item 代理。</summary>
        public static void AppendTreeHabitatPositions(List<Vector2> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (simulation == null) return;
            using (TreeHabitatMarker.Auto())
            {
                foreach (Record record in habitatTreeRecords)
                    if (record.IsValid)
                        destination.Add(record.Snapshot.transform.position);
            }
        }

        #region 附近自然环境声源

        // 复用实际空间桶和声明标签，树、灌木等来源不需要创建音源或 Item 外壳。
        public static int FindNearestTaggedAmbientSource(Vector2 listenerPosition, float radius, string sourceTag,
            out Vector2 sourcePosition)
        {
            sourcePosition = default;
            if (simulation == null || radius <= 0f) return 0;
            WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
            float nearest = radius * radius;
            int count = 0;
            using ResourceQuery query = QueryBounds(listenerPosition, Vector2.one * radius, topology);
            foreach (Record record in query)
            {
                if (!record.IsValid || !record.Profile.Definition.HasTag(sourceTag)) continue;
                NaturalEntityBody body = simulation.GetBody(record.Handle.Id);
                Vector2 candidate = new(body.Position.x, body.Position.y);
                float distance = topology.SqrDistance(new float2(listenerPosition.x, listenerPosition.y),
                    new float2(candidate.x, candidate.y));
                if (distance >= radius * radius) continue;
                count++;
                if (distance >= nearest) continue;
                nearest = distance;
                sourcePosition = candidate;
            }
            return count;
        }

        #endregion

        #endregion

        public static bool TryFindForage(Vector2 origin, float radius, string tag, Func<Vector2, bool> canReach,
            out Vector2 position, out int guid)
        {
            position = default; guid = 0;
            if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius)) return false;
            float best = radius * radius;
            using ResourceQuery query = QueryBounds(origin, Vector2.one * radius);
            foreach (Record record in query)
            {
                if (!record.IsValid || !record.Profile.HasCrop || !record.Profile.Definition.HasTag(tag)) continue;
                Vector2 candidate = record.Snapshot.transform.position;
                float distance = WorldTopologyRuntime.SqrDistance(origin, candidate);
                if (distance >= best || (canReach != null && !canReach(candidate))) continue;
                position = candidate; guid = record.Snapshot.Guid; best = distance;
            }
            return guid != 0;
        }

        public static bool IsForageAvailable(Vector2 position, int guid, string tag)
        {
            using ResourceQuery query = QueryBounds(position, Vector2.one * 0.6f);
            foreach (Record record in query)
                if (record.Snapshot.Guid == guid && record.IsValid && record.Profile.HasCrop &&
                    record.Profile.Definition.HasTag(tag) &&
                    WorldTopologyRuntime.SqrDistance(record.Snapshot.transform.position, position) <= 0.36f) return true;
            return false;
        }

        private static void RegisterSpatial(Record record, PhysicsBodyChangeReason reason = PhysicsBodyChangeReason.Registered, bool notifyPhysics = true)
        {
            NaturalEntityBody body = simulation.GetBody(record.Handle.Id);
            ResolveSpatialBounds(record, body);
            RegisterSpatialCells(record);
            if (notifyPhysics && record.BlocksMovement)
                PublishPhysicsBodyChanged(record, reason, true);
        }

        private static void ResolveSpatialBounds(Record record, NaturalEntityBody body)
        {
            Matrix4x4 root = BodyMatrix(body);
            NaturalEntitySpatialGeometry geometry = record.Profile.SpatialGeometry;
            record.BlocksMovement = geometry.BlocksMovement;
            record.HitColliderEnabled = geometry.HitColliderEnabled;
            geometry.Resolve(root, out record.BodyBounds, out record.HitBounds);
            ResolveBodyVisual(record, body, out Sprite sprite, out Matrix4x4 matrix, out _, out _,
                out Vector3 rendererScale, out _);
            RefreshDefaultNaturalInteractionRegionGeometry(record, sprite, rendererScale);
            ResolveConfiguredPhysicsBounds(record);
            record.VisualBounds = sprite != null ? TransformBounds(matrix, sprite.bounds.center, sprite.bounds.size) : record.HitBounds;
            record.IndexedRevision = body.VisualVersion;
        }

        // 外观变色、库存及果实数量不改变几何时，保留空间桶与物理登记。
        private static void RefreshSpatial(Record record, NaturalEntityBody body)
        {
            Bounds previousBody = record.BodyBounds, previousHit = record.HitBounds, previousVisual = record.VisualBounds;
            Bounds previousInteraction = record.InteractionBounds;
            bool previousBlocking = record.BlocksMovement;
            ResolveSpatialBounds(record, body);
            if (previousBody.Equals(record.BodyBounds) && previousHit.Equals(record.HitBounds) &&
                previousVisual.Equals(record.VisualBounds) && previousInteraction.Equals(record.InteractionBounds) &&
                previousBlocking == record.BlocksMovement) return;
            UnregisterSpatial(record, PhysicsBodyChangeReason.VisualRevision, notifyPhysics: false);
            RegisterSpatialCells(record);
            if (previousBlocking == record.BlocksMovement && previousBody.Equals(record.BodyBounds)) return;
            if (previousBlocking)
            {
                PhysicsBodyChanged?.Invoke(previousBody);
                PhysicsBodyChangedWithReason?.Invoke(previousBody, PhysicsBodyChangeReason.VisualRevision,
                    record.Snapshot.Guid, record.Profile.Definition.Id);
            }
            if (record.BlocksMovement) PublishPhysicsBodyChanged(record, PhysicsBodyChangeReason.VisualRevision, true);
        }

        private static void RegisterSpatialCells(Record record)
        {
            State.SpatialRevision++;
            Bounds bounds = record.HitBounds;
            bounds.Encapsulate(record.BodyBounds); bounds.Encapsulate(record.VisualBounds);
            if (record.HasConfiguredInteractionBounds) bounds.Encapsulate(record.InteractionBounds);
            int firstX = Mathf.FloorToInt(bounds.min.x), lastX = Mathf.FloorToInt(bounds.max.x);
            int firstY = Mathf.FloorToInt(bounds.min.y), lastY = Mathf.FloorToInt(bounds.max.y);
            if ((long)(lastX - firstX + 1) * (lastY - firstY + 1) > 16384L)
                throw new InvalidOperationException("资源实体的空间范围过大，拒绝无界注册。");
            for (int y = firstY; y <= lastY; y++)
            for (int x = firstX; x <= lastX; x++)
            {
                Vector2Int cell = WorldTopologyRuntime.NormalizeCell(new Vector2Int(x, y));
                if (!spatialCells.TryGetValue(cell, out HashSet<int> bucket)) spatialCells.Add(cell, bucket = new());
                if (bucket.Add(record.Handle.Id)) record.SpatialCells.Add(cell);
            }
            RefreshContactDamageSource(record);
        }

        private static void UnregisterSpatial(Record record, PhysicsBodyChangeReason reason = PhysicsBodyChangeReason.Removed, bool notifyPhysics = true)
        {
            State.SpatialRevision++;
            if (notifyPhysics && record.BlocksMovement && record.SpatialCells.Count > 0)
                PublishPhysicsBodyChanged(record, reason, false);
            foreach (Vector2Int cell in record.SpatialCells)
                if (spatialCells.TryGetValue(cell, out HashSet<int> bucket))
                {
                    bucket.Remove(record.Handle.Id);
                    if (bucket.Count == 0) spatialCells.Remove(cell);
                }
            record.SpatialCells.Clear();
        }

        /// <summary>给已加载区块提供纯数据障碍外形，物理组件不反向持有实体。</summary>
        public static void CollectBlockingBounds(BoundsInt area, List<Bounds> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            if (simulation == null) return;
            Vector2 center = new(area.xMin + area.size.x * 0.5f, area.yMin + area.size.y * 0.5f);
            Vector2 extents = new(area.size.x * 0.5f, area.size.y * 0.5f);
            using ResourceQuery query = QueryBounds(center, extents);
            foreach (Record record in query)
                if (record.BlocksMovement && record.IsValid)
                    output.Add(record.BodyBounds);
        }

        private static ResourceQuery QueryBounds(Vector2 center, Vector2 extents)
            => QueryBounds(center, extents, WorldTopologyRuntime.GetActiveDomain());

        private static ResourceQuery QueryBounds(Vector2 center, Vector2 extents, WorldTopologyDomain topology)
        {
            List<Record> result = queryLists.Count > 0 ? queryLists.Pop() : new List<Record>(32);
            var query = new ResourceQuery(result);
            if (!math.all(math.isfinite(new float4(center.x, center.y, extents.x, extents.y))) ||
                extents.x < 0f || extents.y < 0f) return query;
            if (++spatialQuerySequence == 0)
            {
                foreach (Record record in records.Values) record.LastSpatialQuery = 0;
                spatialQuerySequence = 1;
            }
            int minX = Mathf.FloorToInt(center.x - extents.x), maxX = Mathf.FloorToInt(center.x + extents.x);
            int minY = Mathf.FloorToInt(center.y - extents.y), maxY = Mathf.FloorToInt(center.y + extents.y);
            if (((long)maxX - minX + 1) * ((long)maxY - minY + 1) > 16384L)
            {
                // 大范围改遍历已激活实体，保留调用方精筛，不能因网格预算静默返回空目标。
                foreach (Record record in records.Values)
                {
                    Bounds bounds = record.HitBounds;
                    bounds.Encapsulate(record.BodyBounds); bounds.Encapsulate(record.VisualBounds);
                    if (record.HasConfiguredInteractionBounds) bounds.Encapsulate(record.InteractionBounds);
                    Vector2 delta = topology.ShortestDelta((Vector2)bounds.center, center);
                    if (Mathf.Abs(delta.x) <= extents.x + bounds.extents.x &&
                        Mathf.Abs(delta.y) <= extents.y + bounds.extents.y) result.Add(record);
                }
                return query;
            }
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                if (spatialCells.TryGetValue(new Vector2Int(topology.NormalizeX(x), topology.NormalizeY(y)), out HashSet<int> bucket))
                    foreach (int id in bucket)
                        if (records.TryGetValue(id, out Record record) && record.LastSpatialQuery != spatialQuerySequence)
                        { record.LastSpatialQuery = spatialQuerySequence; result.Add(record); }
            return query;
        }

        /// <summary>按世界命中点读取最近自然实体的对应类型防御，供无 GameObject 外壳的命中后行为复用。</summary>
        public static bool TryGetCombatDefenseAtPoint(Vector2 point, CombatDamageKind kind,
            out float defense, out int runtimeId)
        {
            defense = 0f;
            runtimeId = 0;
            if (simulation == null || kind == CombatDamageKind.None)
                return false;

            const float tolerance = 0.18f;
            float bestDistance = float.PositiveInfinity;
            using ResourceQuery query = QueryBounds(point, Vector2.one * tolerance);
            foreach (Record record in query)
            {
                if (!record.IsValid || record.Profile.HealthDefaults?.DefenseValues == null ||
                    !record.HitColliderEnabled)
                {
                    continue;
                }

                float distance = Mathf.Min(
                    SqrDistanceToBounds(point, record.HitBounds),
                    SqrDistanceToBounds(point, record.BodyBounds));
                if (distance > tolerance * tolerance || distance >= bestDistance)
                    continue;

                CombatDefense values = record.Profile.HealthDefaults.DefenseValues;
                defense = values.Physical;
                runtimeId = record.Handle.Id;
                bestDistance = distance;
            }

            return bestDistance < float.PositiveInfinity;
        }

        /// <summary>用环形世界的最短位移计算点到 AABB 的平方距离。</summary>
        private static float SqrDistanceToBounds(Vector2 point, Bounds bounds)
        {
            Vector2 delta = WorldTopologyRuntime.ShortestDelta(bounds.center, point);
            float dx = Mathf.Max(0f, Mathf.Abs(delta.x) - bounds.extents.x);
            float dy = Mathf.Max(0f, Mathf.Abs(delta.y) - bounds.extents.y);
            return dx * dx + dy * dy;
        }

        public static void QueryInteractions(Item actor, float distance, Vector2? pointer, List<IInteractable> output)
            => QueryInteractions(actor, distance, pointer, output, WorldTopologyRuntime.GetActiveDomain());

        /// <summary>空间桶与精筛共享本轮拓扑，避免每个候选和网格重新解析活动世界。</summary>
        internal static void QueryInteractions(Item actor, float distance, Vector2? pointer,
            List<IInteractable> output, WorldTopologyDomain topology)
        {
            if (actor == null || records.Count == 0 || distance < 0f) return;
            Vector2 origin = actor.transform.position;
            Vector2 center = pointer ?? origin;
            using ResourceQuery query = QueryBounds(center, pointer.HasValue ? Vector2.zero : Vector2.one * distance, topology);
            foreach (Record record in query)
            {
                if (WorldEntityRuntime.DimensionId != record.DimensionId || !record.CanInteract(actor) ||
                    topology.Distance(origin, (Vector2)record.Snapshot.transform.position) > distance) continue;
                if (pointer.HasValue && !ContainsPoint(record, pointer.Value, topology)) continue;
                if (!output.Contains(record)) output.Add(record);
            }
        }

        private static bool ContainsPoint(Record record, Vector2 pointer)
            => ContainsPoint(record, pointer, WorldTopologyRuntime.GetActiveDomain());

        private static bool ContainsPoint(Record record, Vector2 pointer, WorldTopologyDomain topology)
        {
            if (record.PhysicsProxy?.IsValid == true)
                return ContainsConfiguredPhysicsPoint(record, pointer, WorldPhysicsPurpose2D.Interaction);
            Vector2 delta = topology.ShortestDelta((Vector2)record.VisualBounds.center, pointer);
            return Mathf.Abs(delta.x) <= record.VisualBounds.extents.x && Mathf.Abs(delta.y) <= record.VisualBounds.extents.y;
        }

        internal static Bounds TransformBounds(Matrix4x4 matrix, Vector2 center, Vector2 size)
        {
            Vector3 x = matrix.MultiplyVector(new Vector3(Mathf.Abs(size.x) * 0.5f, 0f));
            Vector3 y = matrix.MultiplyVector(new Vector3(0f, Mathf.Abs(size.y) * 0.5f));
            return new Bounds(matrix.MultiplyPoint3x4(center), new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y), 0.01f) * 2f);
        }

        #endregion

        #region 共享伤害数值与采集命令

        private sealed class WeaponQueryBuffers
        {
            public readonly List<Record> Records = new(32);
            public readonly List<CombatColliderHit2D> Hits = new(32);
            public readonly List<(Record Record, float Fraction, float Distance, Vector2 Point)> Candidates = new(32);
            public void Clear() { Records.Clear(); Hits.Clear(); Candidates.Clear(); }
        }

        private static readonly Stack<WeaponQueryBuffers> weaponQueryPool = new();

        private static void QueryWeapon(Mod_Damage weapon, AttackShape2D shape, CombatDamageContext context)
        {
            if (!GameNetwork.HasStateAuthority || weapon == null || weapon.RemainingAttackTargets <= 0 ||
                (context.DeliveryCapabilities & CombatDeliveryCapabilities.AirborneOnly) != 0) return;
            // 一次查询独占一组缓冲，嵌套攻击和异常退出都不会覆盖其它查询。
            WeaponQueryBuffers buffers = weaponQueryPool.Count > 0 ? weaponQueryPool.Pop() : new WeaponQueryBuffers();
            try
            {
                var recordsByCollider = buffers.Records;
                var colliderHits = buffers.Hits;
                using (CombatColliderQuery2D physics = CombatColliderQuery2D.Rent())
                {
                    using ResourceQuery candidates = QueryBounds(shape.BoundsCenter, shape.BoundsExtents);
                    foreach (Record record in candidates)
                    {
                        if (record.Profile.HealthModuleName == null || !record.HitColliderEnabled || !record.IsValid ||
                            record.PhysicsProxy?.IsValid == true) continue;
                        Vector2 center = WorldTopologyRuntime.NearestImagePosition((Vector2)shape.BoundsCenter,
                            record.HitBounds.center);
                        physics.Add(recordsByCollider.Count, PerceptionShape2D.Aabb(center,
                            new float2(record.HitBounds.extents.x, record.HitBounds.extents.y)));
                        recordsByCollider.Add(record);
                    }
                    physics.Query(shape, colliderHits);
                }
                var candidatesByDistance = buffers.Candidates;
                AppendConfiguredPhysicsWeaponCandidates(shape, candidatesByDistance);
                for (int i = 0; i < colliderHits.Count; i++)
                {
                    CombatColliderHit2D hit = colliderHits[i];
                    Record record = recordsByCollider[hit.CandidateId];
                    candidatesByDistance.Add((record, hit.Fraction,
                        WorldTopologyRuntime.SqrDistance((Vector2)shape.BoundsCenter, hit.Point), hit.Point));
                }
                candidatesByDistance.Sort((a, b) => a.Fraction != b.Fraction ? a.Fraction.CompareTo(b.Fraction) :
                    a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Record.Handle.Id.CompareTo(b.Record.Handle.Id));
                foreach (var candidate in candidatesByDistance)
                {
                    if (weapon == null || weapon.RemainingAttackTargets <= 0) break;
                    CombatDamageContext hit = context;
                    hit.HitPoint = candidate.Point;
                    ApplyWeaponHit(candidate.Record, weapon, hit);
                }
            }
            finally { buffers.Clear(); weaponQueryPool.Push(buffers); }
        }

        /// <summary>受击盒查询与真实物理接触共用资格、窗口预约和 ECS 生命提交，禁止重复扣血。</summary>
        private static void ApplyWeaponHit(Record record, Mod_Damage weapon, in CombatDamageContext context)
        {
            if (!GameNetwork.HasStateAuthority || weapon == null || record.Busy || !record.IsValid ||
                !record.HitColliderEnabled || record.Profile.HealthModuleName == null ||
                WorldEntityRuntime.DimensionId != record.DimensionId ||
                (context.DeliveryCapabilities & CombatDeliveryCapabilities.AirborneOnly) != 0) return;
            var target = new CombatIdentity { Backend = CombatBackend.Entity, Value = (1UL << 63) | (uint)record.Handle.Id,
                Generation = (uint)record.Handle.Generation, World = context.Attack.Source.World, Dimension = context.Attack.Source.Dimension };
            if (!weapon.TryReserveExternalTarget(target)) return;
            weapon.ConsumeWeaponDurabilityForHit();
            float damage = ApplyDamage(record.Handle, context, weapon);
            if (damage >= 0f && weapon != null) weapon.PublishExternalDamage(context, damage);
        }

        /// <summary>生命只写回同一 Entity，MOD 可修补这个命令边界；不会创建 Mod_DamageReceiver。</summary>
        public static float ApplyDamage(NaturalEntityHandle handle, in CombatDamageContext context, Mod_Damage sender = null)
            => ApplyDamageInternal(handle, context, false, sender);

        private static float ApplyDamageInternal(NaturalEntityHandle handle, in CombatDamageContext context, bool environmentalDamage,
            Mod_Damage sender = null)
        {
            using var handleScope = UseHandle(handle);
            if (!GameNetwork.HasStateAuthority || !Contains(handle) || !context.Attack.Source.IsValid ||
                !math.all(math.isfinite(context.Damage)) || !math.isfinite(context.Clock.Time)) return -1f;
            Record record = records[handle.Id];
            if (record.Busy || record.Profile.HealthModuleName == null ||
                !simulation.TryGet(handle.Id, out AiecsVital vital) || vital.Dead != 0 || vital.Hp <= 0f) return -1f;
            float resourceMultiplier = 1f;
            // 中立环境伤害沿正式生命入口结算物理防御，跳过采集工具资格与普通攻击间隔。
            if (context.IsTrueDamage == 0 && !environmentalDamage)
                resourceMultiplier = Mod_ResourceHarvest.ResolveAffinityMultiplier(
                    (ResourceToolKind)(record.Profile.HealthParameters.Value<int?>("weakTool") ?? 0), 1,
                    (ResourceToolKind)context.ResourceToolKind, context.ResourceToolTier, context.ResourceToolEfficiency);
            if (context.IsTrueDamage == 0 && !environmentalDamage && simulation.TryGet(handle.Id, out EntityHarvestRequirement requirement))
            {
                resourceMultiplier = Mod_ResourceHarvest.ResolveAffinityMultiplier(
                    (ResourceToolKind)requirement.ToolKind, requirement.MinimumTier,
                    (ResourceToolKind)context.ResourceToolKind, context.ResourceToolTier, context.ResourceToolEfficiency);
                if (!math.isfinite(resourceMultiplier)) return -1f;
            }
            if (context.IsTrueDamage == 0 && !environmentalDamage && context.Clock.Time - vital.LastDamageTime < vital.DamageInterval) return -1f;
            float4 damage = context.IsTrueDamage != 0 ? math.max(0f, context.Damage) :
                CombatRules.Resolve(context.Damage, GameplayCombatBridge.Values(record.Profile.HealthDefaults.DefenseValues),
                    GameplayCombatBridge.Tuning().Resolve(context.SourceIsPlayer != 0, false), vital.ReceivedMultiplier * resourceMultiplier);
            float previous = vital.Hp;
            vital.Hp = math.max(0f, vital.Hp - math.csum(damage));
            if (context.IsTrueDamage == 0 && !environmentalDamage) vital.LastDamageTime = context.Clock.Time;
            vital.LastAttacker = context.Attack.Source; vital.LastCredit = context.Credit;
            if (vital.Hp <= 0f) { vital.Dead = 1; vital.DeathTime = context.Clock.Time; record.DeathByDamage = true; }
            simulation.Set(handle.Id, vital);
            record.FlashUntil = Time.time + 0.15f;
            RefreshPresentation(handle);
            float actualDamage = previous - vital.Hp;
            RememberResourceHit(record, context.Origin, actualDamage);
            // 批量资源仅偏移提交的视觉矩阵，物理投影、占格与掉落坐标仍使用原位置。
            if (sender != null && CombatHitFeedback.HasImpact(actualDamage))
            {
                record.HitShakeStartedAt = Time.time;
                record.HitShakeDuration = CombatHitFeedback.ShakeDuration(sender.HitShakeDuration);
                record.HitShakeDistance = CombatHitFeedback.ShakeDistance(sender.HitShakeDistance);
                record.HitShakeDirection = CombatHitFeedback.ShakeDirection(
                    WorldTopologyRuntime.ShortestDelta((Vector2)context.Origin, record.WorldPosition));
                if (record.HitShakeDuration > 0f && record.HitShakeDistance > 0f)
                    flashPresentationWatch.Add(handle.Id);
            }
            ResourceHarvestHitBonus.TrySpawn(context, actualDamage, resourceMultiplier,
                record.Profile.Definition.LootTableId, ResourceLootPosition(record));
            if (actualDamage > 0f) PublishNetworkStateCommitted(record);
            // 显式声明的声音跟随有效命中发声，零伤按配置处理，致命命中也在移除前播放一次。
            if (!environmentalDamage && context.Attack.Source.Backend != CombatBackend.Environment &&
                !string.IsNullOrWhiteSpace(record.Profile.HurtAudioCueId))
                CombatAudioRouter.PlayImpact(record.Snapshot, record.Profile.ImpactAudioMaterial,
                    record.Profile.HurtAudioCueId, actualDamage, record.WorldPosition, sender,
                    playOnBlockedHit: record.Profile.PlayHurtAudioOnBlockedHit);
            return actualDamage;
        }

        public static bool CanHarvest(NaturalEntityHandle handle)
        {
            using var handleScope = UseHandle(handle);
            if (!Contains(handle)) return false;
            Record record = records[handle.Id];
            if (record.Busy || simulation.GetBody(handle.Id).Dead != 0) return false;
            if (simulation.TryGet(handle.Id, out EntityPlantLifecycle plant))
            {
                if (plant.Harvested != 0 || plant.Initialized == 0 || !ReadClock(out _, out double now) || now - plant.LastWorldTime > 1d) return false;
                if (!record.Profile.HasCrop && (plant.Cultivated == 0 || !record.Profile.AllowCultivatedHarvest)) return false;
            }
            if (simulation.TryGet(handle.Id, out EntityGrowth growth) && growth.Progress < growth.MaxProgress) return false;
            if (simulation.TryGet(handle.Id, out EntityClimate climate) && (climate.CaughtUp == 0 || climate.Dead != 0)) return false;
            if (record.Profile.Collection != null)
                return simulation.TryGet(handle.Id, out EntityResourceStock stock) && stock.Count > 0;
            return record.Profile.HarvestOutputs?.Count > 0 ||
                (record.Profile.AllowCultivatedHarvest && !string.IsNullOrWhiteSpace(record.Profile.TreeFoodId));
        }

        // 网络请求只定位服务端本世界的真实实体，采集规则和掉落仍复用同一入口。
        public static event Action<string, int, Vector2, string, int, int, ItemData> NetworkStateCommitted;
        public static event Action<string, string, int, int, int, Vector2, ItemData> IndependentPhysicsStateCommitted;

        // 休眠前交付最后快照，删除和迁出则明确发送空状态。
        internal static void PublishIndependentPhysicsStateSnapshot(NaturalEntityHandle handle, ItemData snapshot)
        {
            using var scope = UseHandle(handle);
            if (Contains(handle) && records[handle.Id].HasIndependentPhysicsSource)
                PublishIndependentPhysicsState(records[handle.Id], snapshot);
        }

        private static void PublishIndependentPhysicsState(Record record, ItemData snapshot)
            => PublishIndependentPhysicsState(record.WorldKey, record.PhysicsSourceWorldKey,
                record.PhysicsSourceChunkX, record.PhysicsSourceChunkY, record.Snapshot.Guid,
                record.Snapshot.transform.position, snapshot);

        internal static void PublishIndependentPhysicsState(string worldKey, string sourceWorldKey,
            int sourceX, int sourceY, int guid, Vector2 position, ItemData snapshot)
        {
            if (IndependentPhysicsStateCommitted == null) return;
            foreach (Action<string, string, int, int, int, Vector2, ItemData> callback in IndependentPhysicsStateCommitted.GetInvocationList())
                try { callback(worldKey, sourceWorldKey, sourceX, sourceY, guid, position, snapshot); }
                catch (Exception exception) { Debug.LogException(exception); }
        }

        private static void PublishNaturalStateForNetwork(Record record, ItemData snapshot)
        {
            if (NetworkStateCommitted == null) return;
            foreach (Action<string, int, Vector2, string, int, int, ItemData> callback in NetworkStateCommitted.GetInvocationList())
                try { callback(record.WorldKey, record.Snapshot.Guid, record.Snapshot.transform.position,
                    record.HasGeneratedNaturalSource ? record.PhysicsSourceWorldKey : null,
                    record.PhysicsSourceChunkX, record.PhysicsSourceChunkY, snapshot); }
                catch (Exception exception) { Debug.LogException(exception); }
        }

        private static void PublishNetworkStateCommitted(Record record)
        {
            if (!record.HasIndependentPhysicsSource && NetworkStateCommitted == null) return;
            ItemData snapshot = null;
            if (Contains(record.Handle) && !TryCapture(record.Handle, out snapshot)) return;
            if (record.HasIndependentPhysicsSource)
                PublishIndependentPhysicsState(record, snapshot);
            else PublishNaturalStateForNetwork(record, snapshot);
        }
        public static bool TryCaptureNetworkState(string worldKey, int guid, Vector2 position, out ItemData snapshot)
        {
            snapshot = null;
            if (!GameNetwork.HasStateAuthority || !GameplayWorldRegistry.TryGet(worldKey, out _) ||
                !float.IsFinite(position.x) || !float.IsFinite(position.y)) return false;
            using var worldScope = GameplayWorldRegistry.UseWorld(worldKey);
            Record record = FindNetworkRecord(guid, position, out _);
            return record != null && TryCapture(record.Handle, out snapshot);
        }

        // 客户端同步正式生态差量，卸载后再次生成仍沿用同一删除和库存状态。
        public static void ApplyNetworkState(string worldKey, int guid, Vector2 position, ItemData snapshot,
            string sourceWorldKey = null, int sourceChunkX = 0, int sourceChunkY = 0)
        {
            if (GameNetwork.HasStateAuthority || guid == 0 || !GameplayWorldRegistry.TryGet(worldKey, out _) ||
                !float.IsFinite(position.x) || !float.IsFinite(position.y) ||
                snapshot != null && (snapshot.Guid != guid || snapshot.transform == null)) return;
            if (sourceWorldKey != null && (string.IsNullOrWhiteSpace(sourceWorldKey) || sourceWorldKey != worldKey)) return;
            using var worldScope = GameplayWorldRegistry.UseWorld(worldKey);
            ChunkMgr chunks = ChunkMgr.ExistingInstance;
            PlanetData planet = SaveDataMgr.Instance?.Active_PlanetData;
            if (chunks == null || planet == null) return;
            var address = sourceWorldKey != null
                ? new FlatWorld.WorldModel.WorldAddress(global::WorldAddress.FromWorldKey(worldKey).DimensionId,
                    new Int2(sourceChunkX, sourceChunkY))
                : chunks.ResolveWorldAddress(position);
            planet.Ecology ??= new EcologyWorldSaveData();
            Record record;
            if (sourceWorldKey != null)
                State.generatedNaturalSources.TryGetValue((sourceWorldKey, sourceChunkX, sourceChunkY, guid), out record);
            else
            {
                record = FindNetworkRecord(guid, position, out bool ambiguous);
                if (ambiguous) return;
            }
            if (snapshot == null)
            {
                planet.Ecology.MarkRemoved(address.ChunkOrigin.X, address.ChunkOrigin.Y, guid);
                chunks.NotifyNaturalItemRemovedToWorldOwners(address, guid);
                if (record != null) Remove(record.Handle);
                return;
            }
            if (WorldTopologyRuntime.Distance(position, snapshot.transform.position) >= .01f) return;
            planet.Ecology.CaptureChangedItem(address.ChunkOrigin.X, address.ChunkOrigin.Y, snapshot);
            if (record == null) return;
            float baseline = simulation.TryGet(record.Handle.Id, out EntityClimate climate) ? climate.BaselineCelsius : 0f;
            ApplySnapshot(record.Handle, snapshot, baseline);
        }

        private static Record FindNetworkRecord(int guid, Vector2 position, out bool ambiguous)
        {
            ambiguous = false;
            if (simulation == null) return null;
            // 客户端挂起实体也要接收状态，不能借只包含活跃对象的交互空间索引。
            Record match = null;
            foreach (Record record in records.Values)
                if (record.Snapshot.Guid == guid &&
                    WorldTopologyRuntime.Distance(position, record.Snapshot.transform.position) < .01f)
                {
                    if (match != null) { ambiguous = true; return null; }
                    match = record;
                }
            return match;
        }

        public static bool TryHarvestNetwork(Player actor, string worldKey, int guid, Vector2 position,
            string sourceWorldKey = null, int sourceChunkX = 0, int sourceChunkY = 0)
        {
            if (!GameNetwork.HasStateAuthority || actor == null || guid == 0 ||
                !float.IsFinite(position.x) || !float.IsFinite(position.y) ||
                !GameplayWorldRegistry.TryGet(worldKey, out _)) return false;
            using var worldScope = GameplayWorldRegistry.UseWorld(worldKey);
            if (!TryGetActorPosition(actor, worldKey, out _) || simulation == null) return false;
            if (sourceWorldKey != null)
            {
                if (string.IsNullOrWhiteSpace(sourceWorldKey) ||
                    !State.generatedNaturalSources.TryGetValue((sourceWorldKey, sourceChunkX, sourceChunkY, guid), out Record source) ||
                    !source.IsValid || source.WorldKey != worldKey) return false;
                return TryHarvest(source.Handle, actor);
            }
            Record match = null;
            foreach (Record record in records.Values)
                if (record.Snapshot.Guid == guid && record.IsValid && record.WorldKey == worldKey &&
                    WorldTopologyRuntime.Distance(position, record.Snapshot.transform.position) < .01f)
                {
                    if (match != null) return false;
                    match = record;
                }
            return match != null && TryHarvest(match.Handle, actor);
        }

        /// <summary>产出先成功生成再提交库存或收获状态，失败时回滚本次生成，避免丢物或重复扣库存。</summary>
        public static bool TryHarvest(NaturalEntityHandle handle, Item actor)
        {
            using var handleScope = UseHandle(handle);
            if (actor == null || !Contains(handle)) return false;
            Record record = records[handle.Id];
            if (!GameNetwork.HasStateAuthority)
                return actor is Player player && NaturalInteractionNetworkBridge.RequestHarvest(player,
                    record.WorldKey, record.Snapshot.Guid, record.Snapshot.transform.position,
                    record.HasGeneratedNaturalSource ? record.PhysicsSourceWorldKey : null,
                    record.PhysicsSourceChunkX, record.PhysicsSourceChunkY);
            if (!CanHarvest(handle)) return false;
            if (!TryGetActorPosition(actor, record.WorldKey, out Vector2 actorPosition)) return false;
            ChunkMgr chunks = ChunkMgr.ExistingInstance;
            Mod_InteractSender sender = actor.GetComponentInChildren<Mod_InteractSender>(true);
            float reach = sender != null ? sender.maxInteractDistance : Mod_InteractSender.DefaultMaxInteractDistance;
            if (chunks == null || chunks.ResolveWorldAddress(actorPosition).DimensionId != record.DimensionId ||
                WorldTopologyRuntime.Distance(actorPosition, record.Snapshot.transform.position) > reach) return false;
            // 仅权威有效点击推进；未完成时不生成产物、不扣采集库存。
            if (!ManualHarvestWork.TryPayStamina(actor)) return false;
            record.HarvestShakeStartedAt = Time.time;
            flashPresentationWatch.Add(handle.Id);
            RefreshPresentation(handle);
            if (!ManualHarvestWork.Advance(ref record.ManualHarvestProgress, record.Profile.ManualHarvestWorkSteps)) return true;
            record.Busy = true;
            var spawned = new List<DroppedItemHandle>();
            try
            {
                Vector2 position = record.Snapshot.transform.position;
                if (record.Profile.Collection is { } collection)
                {
                    spawned.Add(DroppedItemService.SpawnLoot(collection.CollectItemId, position, 1f, collection.SpawnRadius,
                        collection.ThrowDuration, bezierOffset: collection.ThrowBezierOffset, arcHeight: collection.ThrowArcHeight));
                    simulation.TryGet(handle.Id, out EntityResourceStock stock);
                    stock.Count--; simulation.Set(handle.Id, stock);
                    RefreshPresentation(handle);
                    PublishNetworkStateCommitted(record);
                    return true;
                }
                if (record.Profile.HarvestOutputs != null)
                {
                    float multiplier = Mathf.Max(0f, GameRuleConfiguration.Current.World.LootAmountMultiplier);
                    foreach (CropYieldEntry output in record.Profile.HarvestOutputs)
                    {
                        if (UnityEngine.Random.value > Mathf.Clamp01(output.probability * multiplier)) continue;
                        int amount = GameRuleMath.ScaleRandomizedAmount(UnityEngine.Random.Range(output.minAmount, output.maxAmount + 1), multiplier);
                        if (amount > 0) spawned.Add(DroppedItemService.SpawnLoot(output.itemId, position, amount));
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(record.Profile.TreeSeedId)) spawned.Add(DroppedItemService.SpawnLoot(record.Profile.TreeSeedId, position));
                    int amount = GameRuleMath.ScaleRandomizedAmount(UnityEngine.Random.Range(record.Profile.TreeFoodMinimum,
                        record.Profile.TreeFoodMaximum + 1), GameRuleConfiguration.Current.World.LootAmountMultiplier);
                    if (amount > 0) spawned.Add(DroppedItemService.SpawnLoot(record.Profile.TreeFoodId, position, amount));
                }
            }
            catch
            {
                foreach (DroppedItemHandle drop in spawned) DroppedItemService.Remove(drop);
                throw;
            }
            finally { record.Busy = false; }
            if (simulation.TryGet(handle.Id, out EntityPlantLifecycle plant))
            { plant.Harvested = 1; plant.Status = EntityPlantGrowthStatus.Harvested; simulation.Set(handle.Id, plant); }
            CompleteRemoval(record);
            return true;
        }

        private static void PublishDeath(Record record)
        {
            if (!Contains(record.Handle)) return;
            simulation.TryGet(record.Handle.Id, out AiecsVital vital);
            if (vital.DeathPublished != 0) return;
            if (!FinishCanopy(record, record.DeathByDamage && !record.DeathByFire)) return;
            // 死亡先锁存；即使表现/第三方回调抛异常，也不能再次发放战利品。
            vital.DeathPublished = 1; simulation.Set(record.Handle.Id, vital);
            try
            {
                if (record.DeathByFire) return;
                // 被击杀的成熟作物沿用一次收获产物表，徒手进度不影响死亡产量。
                if (record.DeathByDamage && record.Profile.HasCrop && record.Profile.HarvestOutputs != null &&
                    simulation.TryGet(record.Handle.Id, out EntityPlantLifecycle crop) && crop.Harvested == 0 &&
                    simulation.TryGet(record.Handle.Id, out EntityGrowth growth) && growth.Progress >= growth.MaxProgress)
                {
                    float multiplier = Mathf.Max(0f, GameRuleConfiguration.Current.World.LootAmountMultiplier);
                    foreach (CropYieldEntry output in record.Profile.HarvestOutputs)
                    {
                        if (UnityEngine.Random.value > Mathf.Clamp01(output.probability * multiplier)) continue;
                        int amount = GameRuleMath.ScaleRandomizedAmount(
                            UnityEngine.Random.Range(output.minAmount, output.maxAmount + 1), multiplier);
                        if (amount > 0) DroppedItemService.SpawnLoot(output.itemId, record.Snapshot.transform.position, amount);
                    }
                    if (record.Profile.Collection is { } collection &&
                        simulation.TryGet(record.Handle.Id, out EntityResourceStock stock) && stock.Count > 0)
                    {
                        DroppedItemService.SpawnLoot(collection.CollectItemId, record.Snapshot.transform.position, 1f,
                            collection.SpawnRadius, collection.ThrowDuration, bezierOffset: collection.ThrowBezierOffset, arcHeight: collection.ThrowArcHeight);
                        stock.Count--;
                        simulation.Set(record.Handle.Id, stock);
                    }
                    crop.Harvested = 1;
                    simulation.Set(record.Handle.Id, crop);
                }
                if (record.DeathByDamage && !string.IsNullOrWhiteSpace(record.Profile.Definition.LootTableId) &&
                    GameRes.ExistingInstance.TryGetLootTable(record.Profile.Definition.LootTableId, out RuntimeLootTable table))
                    foreach (RuntimeLootTableEntry entry in table.Entries)
                    {
                        if (simulation.TryGet(record.Handle.Id, out EntityPlantLifecycle harvested) && harvested.Harvested != 0 &&
                            (record.Profile.HarvestOutputs?.Exists(output => string.Equals(output.itemId, entry.ItemId, StringComparison.OrdinalIgnoreCase)) == true ||
                             string.Equals(record.Profile.Collection?.CollectItemId, entry.ItemId, StringComparison.OrdinalIgnoreCase))) continue;
                        float multiplier = GameRuleConfiguration.Current.World.LootAmountMultiplier * YieldMultiplier(record, entry.ItemId);
                        if (UnityEngine.Random.value > Mathf.Clamp01(entry.Probability * GameRuleConfiguration.Current.World.LootAmountMultiplier)) continue;
                        int count = GameRuleMath.ScaleRandomizedAmount(entry.RollAmount(), multiplier);
                        if (count > 0) DroppedItemService.SpawnLoot(entry.ItemId, ResourceLootPosition(record), count);
                    }
            }
            finally { CompleteRemoval(record); }
        }

        private static float YieldMultiplier(Record record, string itemId)
        {
            var rule = record.Profile.TemperatureYield;
            if (rule == null || !string.Equals(rule.OutputItemId, itemId, StringComparison.OrdinalIgnoreCase)) return 1f;
            float temperature;
            if (ItemEnvironmentSources.TryGet(record.WorldKey, record.Snapshot.transform.position, out ItemEnvironmentSample environment))
                temperature = (float)environment.TemperatureCelsius;
            else if (TemperatureMgr.Instance == null ||
                !TemperatureMgr.Instance.TryGetAmbientTemperature(record.Snapshot.transform.position, out temperature))
                throw new InvalidOperationException("资源所在地温度未就绪，不能结算温度产量。");
            return Mathf.Lerp(rule.ColdMultiplier, rule.WarmMultiplier,
                Mathf.InverseLerp(rule.ColdTemperatureCelsius, rule.WarmTemperatureCelsius, temperature));
        }

        private static void CompleteRemoval(Record record)
        {
            try { record.Removed?.Invoke(record.Handle); }
            finally { Remove(record.Handle); PublishNetworkStateCommitted(record); }
        }

        #endregion
    }
}
