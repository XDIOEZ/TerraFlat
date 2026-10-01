using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using FlatWorld.Networking;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    public static partial class NaturalEntityEcsService
    {
        #region 空间索引与无外壳目标

        private sealed partial class Record : IWorldInteractionTarget, ISpatialInteractionShape, IWorldInteractionPreview
        {
            public readonly List<Vector2Int> SpatialCells = new();
            public ulong LastSpatialQuery;
            public Bounds HitBounds, BodyBounds, VisualBounds;
            public bool BlocksMovement;
            public bool HitColliderEnabled;
            public Vector3 WorldPosition => WorldLocalPresentation.ProjectPosition(Snapshot.transform.position);
            public int TargetGuid => Snapshot.Guid;
            public bool IsValid => Contains(Handle) && simulation.GetBody(Handle.Id).Dead == 0;
            public bool CanInteract(Item actor) => actor != null && IsValid && CanHarvest(Handle);
            public bool ContainsInteractionPoint(Vector2 point) => ContainsPoint(this, point);
            public void OnInteractStart(Item actor) => TryHarvest(Handle, actor);
            public void OnInteractCancel(Item actor) { }
            public void SetInteractionHighlighted(bool highlighted) { Highlighted = highlighted; RefreshPresentation(Handle); }
        }

        private static readonly Dictionary<Vector2Int, HashSet<int>> spatialCells = new();
        private static readonly Stack<List<Record>> queryLists = new();
        private static ulong spatialQuerySequence;
        public enum PhysicsBodyChangeReason : byte { Registered, Removed, VisualRevision, SnapshotRefresh }
        public static event Action<Bounds> PhysicsBodyChanged;
        public static event Action<Bounds, PhysicsBodyChangeReason, int, string> PhysicsBodyChangedWithReason;
        public static event Action PhysicsBodiesReset;
        // 原生静态阻挡的投影通知，与公开玩法通知分离，使用当前 World 的运行时身份。
        internal static event Action<BlockingBodySnapshot, bool, PhysicsBodyChangeReason> BlockingBodyChanged;

        internal readonly struct BlockingBodySnapshot
        {
            public readonly int RuntimeId, Guid;
            public readonly string DefinitionId;
            public readonly Bounds Bounds;
            public BlockingBodySnapshot(int runtimeId, int guid, string definitionId, Bounds bounds)
            { RuntimeId = runtimeId; Guid = guid; DefinitionId = definitionId; Bounds = bounds; }
        }

        private static BlockingBodySnapshot CaptureBlockingBody(Record record)
            => new(record.Handle.Id, record.Snapshot.Guid, record.Profile.Definition.Id, record.BodyBounds);

        private static void PublishPhysicsBodyChanged(Record record, PhysicsBodyChangeReason reason, bool exists)
        {
            PhysicsBodyChanged?.Invoke(record.BodyBounds);
            PhysicsBodyChangedWithReason?.Invoke(record.BodyBounds, reason, record.Snapshot.Guid, record.Profile.Definition.Id);
            BlockingBodyChanged?.Invoke(CaptureBlockingBody(record), exists, reason);
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
                if (root == cell) return true;
            }
            return false;
        }

        /// <summary>给低频鸟类栖息地快照追加当前世界仍存活的 ECS 树木，不创建 Item 代理。</summary>
        public static void AppendTreeHabitatPositions(List<Vector2> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (simulation == null) return;
            foreach (Record record in records.Values)
                if (record.Profile.Definition.HasTag(Tag.Tree) && record.IsValid)
                    destination.Add(record.Snapshot.transform.position);
        }

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
            Matrix4x4 root = BodyMatrix(body);
            ItemColliderDefinitionDto collider = record.Profile.Definition.Visual?.Collider;
            record.BlocksMovement = collider?.Enabled != false && collider?.IsTrigger == false;
            record.BodyBounds = TransformBounds(root, collider?.Offset ?? Vector2.zero, collider?.Size ?? Vector2.one);
            JObject health = record.Profile.HealthParameters;
            JObject hit = health?["$collider2D"] as JObject;
            record.HitColliderEnabled = hit?["enabled"]?.Value<bool>() != false;
            Vector2 hitOffset = hit?["offset"]?.ToObject<Vector2>() ?? collider?.Offset ?? Vector2.zero;
            Vector2 hitSize = hit?["size"]?.ToObject<Vector2>() ?? collider?.Size ?? Vector2.one;
            JObject transform = health?["$transform"] as JObject;
            Matrix4x4 hitMatrix = root * Matrix4x4.TRS(transform?["localPosition"]?.ToObject<Vector3>() ?? Vector3.zero,
                Quaternion.Euler(transform?["localEulerAngles"]?.ToObject<Vector3>() ?? Vector3.zero),
                transform?["localScale"]?.ToObject<Vector3>() ?? Vector3.one);
            record.HitBounds = TransformBounds(hitMatrix, hitOffset, hitSize);
            ResolveBodyVisual(record, body, out Sprite sprite, out Matrix4x4 matrix, out _, out _);
            record.VisualBounds = sprite != null ? TransformBounds(matrix, sprite.bounds.center, sprite.bounds.size) : record.HitBounds;
            Bounds bounds = record.HitBounds;
            bounds.Encapsulate(record.BodyBounds); bounds.Encapsulate(record.VisualBounds);
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
            record.IndexedRevision = body.VisualVersion;
            if (notifyPhysics && record.BlocksMovement)
                PublishPhysicsBodyChanged(record, reason, true);
        }

        private static void UnregisterSpatial(Record record, PhysicsBodyChangeReason reason = PhysicsBodyChangeReason.Removed, bool notifyPhysics = true)
        {
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

        /// <summary>仅首次绑定枚举阻挡实体，后续碰撞更新直接消费单实体通知。</summary>
        internal static void CollectBlockingBodies(BoundsInt area, List<BlockingBodySnapshot> output)
        {
            output.Clear();
            if (simulation == null) return;
            Vector2 center = new(area.xMin + area.size.x * 0.5f, area.yMin + area.size.y * 0.5f);
            Vector2 extents = new(area.size.x * 0.5f, area.size.y * 0.5f);
            using ResourceQuery query = QueryBounds(center, extents);
            foreach (Record record in query)
                if (record.BlocksMovement && record.IsValid) output.Add(CaptureBlockingBody(record));
        }

        private static ResourceQuery QueryBounds(Vector2 center, Vector2 extents)
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
            if (((long)maxX - minX + 1) * ((long)maxY - minY + 1) > 16384L) return query;
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                if (spatialCells.TryGetValue(WorldTopologyRuntime.NormalizeCell(new Vector2Int(x, y)), out HashSet<int> bucket))
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
                defense = kind switch
                {
                    CombatDamageKind.Cutting => values.Cutting,
                    CombatDamageKind.Piercing => values.Piercing,
                    CombatDamageKind.Chopping => values.Chopping,
                    CombatDamageKind.Blunt => values.Blunt,
                    _ => 0f
                };
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
        {
            if (actor == null || records.Count == 0 || distance < 0f) return;
            Vector2 origin = actor.transform.position;
            Vector2 center = pointer ?? origin;
            using ResourceQuery query = QueryBounds(center, pointer.HasValue ? Vector2.zero : Vector2.one * distance);
            foreach (Record record in query)
            {
                if (WorldEntityRuntime.DimensionId != record.DimensionId || !record.CanInteract(actor) ||
                    WorldTopologyRuntime.Distance(origin, record.Snapshot.transform.position) > distance) continue;
                if (pointer.HasValue && !ContainsPoint(record, pointer.Value)) continue;
                if (!output.Contains(record)) output.Add(record);
            }
        }

        private static bool ContainsPoint(Record record, Vector2 pointer)
        {
            Vector2 delta = WorldTopologyRuntime.ShortestDelta(record.VisualBounds.center, pointer);
            return Mathf.Abs(delta.x) <= record.VisualBounds.extents.x && Mathf.Abs(delta.y) <= record.VisualBounds.extents.y;
        }

        private static Bounds TransformBounds(Matrix4x4 matrix, Vector2 center, Vector2 size)
        {
            Vector3 x = matrix.MultiplyVector(new Vector3(Mathf.Abs(size.x) * 0.5f, 0f));
            Vector3 y = matrix.MultiplyVector(new Vector3(0f, Mathf.Abs(size.y) * 0.5f));
            return new Bounds(matrix.MultiplyPoint3x4(center), new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y), 0.01f) * 2f);
        }

        #endregion

        #region 共享伤害数值与采集命令

        private static void QueryWeapon(Mod_Damage weapon, AttackShape2D shape, CombatDamageContext context)
        {
            if (!GameNetwork.HasStateAuthority || weapon == null || weapon.RemainingAttackTargets <= 0 ||
                (context.DeliveryCapabilities & CombatDeliveryCapabilities.AirborneOnly) != 0) return;
            var recordsByCollider = new List<Record>();
            var colliderHits = new List<CombatColliderHit2D>();
            using (CombatColliderQuery2D physics = CombatColliderQuery2D.Rent())
            {
                using ResourceQuery candidates = QueryBounds(shape.BoundsCenter, shape.BoundsExtents);
                foreach (Record record in candidates)
                {
                    if (record.Profile.HealthModuleName == null || !record.HitColliderEnabled || !record.IsValid) continue;
                    Vector2 center = WorldTopologyRuntime.NearestImagePosition((Vector2)shape.BoundsCenter,
                        record.HitBounds.center);
                    physics.Add(recordsByCollider.Count, PerceptionShape2D.Aabb(center,
                        new float2(record.HitBounds.extents.x, record.HitBounds.extents.y)));
                    recordsByCollider.Add(record);
                }
                physics.Query(shape, colliderHits);
            }
            var candidatesByDistance = new List<(Record Record, float Fraction, float Distance, Vector2 Point)>();
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
                if (!candidate.Record.IsValid) continue;
                var target = new CombatIdentity { Backend = CombatBackend.Entity, Value = (1UL << 63) | (uint)candidate.Record.Handle.Id,
                    Generation = (uint)candidate.Record.Handle.Generation, World = context.Attack.Source.World, Dimension = context.Attack.Source.Dimension };
                if (!weapon.TryReserveExternalTarget(target)) continue;
                CombatDamageContext hit = context;
                hit.HitPoint = candidate.Point;
                float damage = ApplyDamage(candidate.Record.Handle, hit);
                if (damage >= 0f && weapon != null) weapon.PublishExternalDamage(hit, damage);
            }
        }

        /// <summary>生命只写回同一 Entity，MOD 可修补这个命令边界；不会创建 Mod_DamageReceiver。</summary>
        public static float ApplyDamage(NaturalEntityHandle handle, in CombatDamageContext context)
        {
            if (!GameNetwork.HasStateAuthority || !Contains(handle) || !context.Attack.Source.IsValid ||
                !math.all(math.isfinite(context.Damage)) || !math.isfinite(context.Clock.Time)) return -1f;
            Record record = records[handle.Id];
            if (record.Busy || record.Profile.HealthModuleName == null ||
                !simulation.TryGet(handle.Id, out AiecsVital vital) || vital.Dead != 0 || vital.Hp <= 0f) return -1f;
            float resourceMultiplier = 1f;
            if (context.IsTrueDamage == 0 && simulation.TryGet(handle.Id, out EntityHarvestRequirement requirement))
            {
                resourceMultiplier = Mod_ResourceHarvest.ResolveAffinityMultiplier(
                    (ResourceToolKind)requirement.ToolKind, requirement.MinimumTier,
                    (ResourceToolKind)context.ResourceToolKind, context.ResourceToolTier, context.ResourceToolEfficiency);
                if (!math.isfinite(resourceMultiplier)) return -1f;
            }
            if (context.IsTrueDamage == 0 && context.Clock.Time - vital.LastDamageTime < vital.DamageInterval) return -1f;
            float4 damage = context.IsTrueDamage != 0 ? math.max(0f, context.Damage) :
                CombatRules.Resolve(context.Damage, GameplayCombatBridge.Values(record.Profile.HealthDefaults.DefenseValues),
                    GameplayCombatBridge.Difficulty().Resolve(context.SourceIsPlayer != 0, false), vital.ReceivedMultiplier * resourceMultiplier);
            float previous = vital.Hp;
            vital.Hp = math.max(0f, vital.Hp - math.csum(damage));
            if (context.IsTrueDamage == 0) vital.LastDamageTime = context.Clock.Time;
            vital.LastAttacker = context.Attack.Source; vital.LastCredit = context.Credit;
            if (vital.Hp <= 0f) { vital.Dead = 1; vital.DeathTime = context.Clock.Time; record.DeathByDamage = true; }
            simulation.Set(handle.Id, vital);
            record.FlashUntil = Time.time + 0.15f;
            RefreshPresentation(handle);
            return previous - vital.Hp;
        }

        public static bool CanHarvest(NaturalEntityHandle handle)
        {
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

        /// <summary>产出先成功生成再提交库存或收获状态，失败时回滚本次生成，避免丢物或重复扣库存。</summary>
        public static bool TryHarvest(NaturalEntityHandle handle, Item actor)
        {
            if (!GameNetwork.HasStateAuthority || actor == null || !CanHarvest(handle)) return false;
            Record record = records[handle.Id];
            ChunkMgr chunks = ChunkMgr.ExistingInstance;
            Mod_InteractSender sender = actor.GetComponentInChildren<Mod_InteractSender>(true);
            float reach = sender != null ? sender.maxInteractDistance : Mod_InteractSender.DefaultMaxInteractDistance;
            if (chunks == null || chunks.ResolveWorldAddress(actor.transform.position).DimensionId != record.DimensionId ||
                WorldTopologyRuntime.Distance(actor.transform.position, record.Snapshot.transform.position) > reach) return false;
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
                    return true;
                }
                if (record.Profile.HarvestOutputs != null)
                {
                    float multiplier = Mathf.Max(0f, GameDifficultyService.Current.World.LootAmountMultiplier);
                    foreach (CropYieldEntry output in record.Profile.HarvestOutputs)
                    {
                        if (UnityEngine.Random.value > Mathf.Clamp01(output.probability * multiplier)) continue;
                        int amount = GameDifficultyService.ScaleRandomizedAmount(UnityEngine.Random.Range(output.minAmount, output.maxAmount + 1), multiplier);
                        if (amount > 0) spawned.Add(DroppedItemService.SpawnLoot(output.itemId, position, amount));
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(record.Profile.TreeSeedId)) spawned.Add(DroppedItemService.SpawnLoot(record.Profile.TreeSeedId, position));
                    int amount = GameDifficultyService.ScaleRandomizedAmount(UnityEngine.Random.Range(record.Profile.TreeFoodMinimum,
                        record.Profile.TreeFoodMaximum + 1), GameDifficultyService.Current.World.LootAmountMultiplier);
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
            if (!FinishCanopy(record, record.DeathByDamage)) return;
            // 死亡先锁存；即使表现/第三方回调抛异常，也不能再次发放战利品。
            vital.DeathPublished = 1; simulation.Set(record.Handle.Id, vital);
            try
            {
                if (record.DeathByDamage && !string.IsNullOrWhiteSpace(record.Profile.Definition.LootTableId) &&
                    GameRes.ExistingInstance.TryGetLootTable(record.Profile.Definition.LootTableId, out RuntimeLootTable table))
                    foreach (RuntimeLootTableEntry entry in table.Entries)
                    {
                        float multiplier = GameDifficultyService.Current.World.LootAmountMultiplier * YieldMultiplier(record, entry.ItemId);
                        if (UnityEngine.Random.value > Mathf.Clamp01(entry.Probability * GameDifficultyService.Current.World.LootAmountMultiplier)) continue;
                        int count = GameDifficultyService.ScaleRandomizedAmount(UnityEngine.Random.Range(entry.MinAmount, entry.MaxAmount + 1), multiplier);
                        if (count > 0) DroppedItemService.SpawnLoot(entry.ItemId, record.Snapshot.transform.position, count);
                    }
            }
            finally { CompleteRemoval(record); }
        }

        private static float YieldMultiplier(Record record, string itemId)
        {
            var rule = record.Profile.TemperatureYield;
            if (rule == null || !string.Equals(rule.OutputItemId, itemId, StringComparison.OrdinalIgnoreCase)) return 1f;
            if (!TemperatureMgr.Instance.TryGetAmbientTemperature(record.Snapshot.transform.position, out float temperature))
                throw new InvalidOperationException("资源所在地温度未就绪，不能结算温度产量。");
            return Mathf.Lerp(rule.ColdMultiplier, rule.WarmMultiplier,
                Mathf.InverseLerp(rule.ColdTemperatureCelsius, rule.WarmTemperatureCelsius, temperature));
        }

        private static void CompleteRemoval(Record record)
        {
            try { record.Removed?.Invoke(record.Handle); }
            finally { Remove(record.Handle); }
        }

        #endregion
    }
}
