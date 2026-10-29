using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using FlatWorld.Networking;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Pool;

namespace FlatWorld.NaturalEntities
{
    public struct NaturalEntityPhysicsNetworkPose
    {
        #region 自然实体权威运动快照
        public string SourceWorldKey;
        public int SourceChunkX, SourceChunkY, Guid;
        public Vector2 Position, Velocity;
        public float RotationDegrees, AngularVelocity;
        public bool IsDynamic;
        #endregion
    }

    public static partial class NaturalEntityEcsService
    {
        #region 原实体的世界物理代理

        private sealed partial class Record : IWorldPhysicsEntityRegionTriggerReceiver2D
        {
            public WorldPhysicsEntityProxy2D PhysicsProxy;
            public bool ChunkBlockingProjection;
            public WorldPhysicsProxyMode2D? RetainedPhysicsModeUntilNextSpawn;
            public Vector3 PhysicsScale;
            public bool HasGeneratedNaturalSource;
            public bool HasIndependentPhysicsSource;
            public string PhysicsSourceWorldKey;
            public int PhysicsSourceChunkX, PhysicsSourceChunkY;

            public void ReceivePhysicsRegionTrigger(string regionId, string receiverModule,
                WorldPhysicsPurpose2D purpose, Collider2D other, WorldPhysicsTriggerPhase2D phase)
            {
                using var scope = UseHandle(Handle);
                if (!GameNetwork.HasStateAuthority || Busy || !IsValid) return;
                foreach (CompiledResourceCapability extension in InstalledExtensions)
                    if (extension.Module.StableName == receiverModule && extension.Capability.PhysicsTrigger != null)
                    {
                        try
                        {
                            extension.Capability.PhysicsTrigger(ExtensionContext(this, extension),
                                new ResourceEntityPhysicsTriggerEvent(regionId, receiverModule, purpose, other, phase));
                        }
                        catch (Exception exception) { Debug.LogException(exception); }
                        return;
                    }
            }
        }

        public static event Action<NaturalEntityHandle, Vector2, Vector2, WorldPhysicsEntityPose2D> PhysicsPoseCommitted;

        public static bool UsesIndependentPhysicsProxy(RuntimeItemDefinition definition)
            => definition?.Physics?.Mode == WorldPhysicsProxyMode2D.IndependentDynamic;

        public static bool TryGetPhysicsProxy(NaturalEntityHandle handle, out WorldPhysicsEntityProxy2D proxy)
        {
            using var scope = UseHandle(handle);
            proxy = Contains(handle) ? records[handle.Id].PhysicsProxy : null;
            return proxy?.IsValid == true;
        }

        private static void AllocateNaturalEntityPhysicsProxy(Record record)
        {
            ItemPhysicsDefinitionDto configuration = WorldPhysicsService2D.ResolveEntityPhysicsDefinition(record.Profile.Definition.Physics);
            if (record.RetainedPhysicsModeUntilNextSpawn is WorldPhysicsProxyMode2D retained && configuration.Mode != retained)
                configuration = new ItemPhysicsDefinitionDto
                {
                    Mode = retained, MassSource = configuration.MassSource, FreezeRotation = configuration.FreezeRotation,
                    LinearDamping = configuration.LinearDamping, AngularDamping = configuration.AngularDamping,
                    Regions = configuration.Regions
                };
            NaturalEntityBody body = simulation.GetBody(record.Handle.Id);
            var regions = new List<WorldPhysicsEntityRegion2D>(record.Profile.PhysicsRegions);
            record.ChunkBlockingProjection = configuration.Mode == WorldPhysicsProxyMode2D.WorldFixed &&
                !configuration.HasRegions && Mathf.Abs(Mathf.DeltaAngle(0f, body.Rotation)) < .001f &&
                (record.Profile.Definition.Visual?.Collider?.Type is null or nameof(BoxCollider2D));
            if (!record.ChunkBlockingProjection)
                for (int i = 0; i < regions.Count; i++)
                {
                    WorldPhysicsEntityRegion2D region = regions[i];
                    regions[i] = new WorldPhysicsEntityRegion2D(region.Id, region.Purpose, region.Collider,
                        region.ReceiverModule, region.LocalPosition, region.LocalRotation, region.LocalScale,
                        chunkBlockingProjection: false);
                }
            WriteHotState(record, record.Snapshot);
            record.PhysicsProxy = GameplayWorldRegistry.CurrentPhysics.AllocateEntityProxy(record, record.Snapshot,
                configuration, regions, pose => CommitNaturalEntityPhysicsPose(record.Handle, pose), record,
                stateAuthority: GameNetwork.HasStateAuthority);
            record.PhysicsScale = record.Snapshot.transform.scale;
        }

        private static void ReleaseNaturalEntityPhysicsProxy(Record record)
        {
            record.PhysicsProxy?.Dispose();
            record.PhysicsProxy = null;
            record.ChunkBlockingProjection = false;
        }

        private static void CommitNaturalEntityPhysicsPose(NaturalEntityHandle handle, WorldPhysicsEntityPose2D pose,
            bool authoritativeNetworkResult = false)
        {
            using var scope = UseHandle(handle);
            if ((!GameNetwork.HasStateAuthority && !authoritativeNetworkResult) || !Contains(handle)) return;
            Record record = records[handle.Id];
            NaturalEntityBody body = simulation.GetBody(handle.Id);
            Vector2 previous = new(body.Position.x, body.Position.y);
            Vector2 position = WorldLocalPresentation.ToLogical(pose.Position);
            bool moved = !previous.Equals(position) || !body.Rotation.Equals(pose.RotationDegrees);
            record.Snapshot.PhysicsState ??= new ItemPhysicsRuntimeState();
            record.Snapshot.PhysicsState.Velocity = pose.Velocity;
            record.Snapshot.PhysicsState.AngularVelocity = pose.AngularVelocity;
            if (!moved)
            {
                PhysicsPoseCommitted?.Invoke(handle, previous, position, pose);
                return;
            }
            body.Position = new float2(position.x, position.y);
            body.Rotation = pose.RotationDegrees;
            unchecked { body.VisualVersion++; }
            simulation.SetBody(handle.Id, body);
            record.Snapshot.transform.position = new Vector3(position.x, position.y);
            record.Snapshot.transform.rotation = Quaternion.Euler(0f, 0f, body.Rotation);
            UnregisterNavigation(record);
            RefreshSpatial(record, body);
            TryRegisterNavigation(record);
            MarkPresentationDirty(record);
            PhysicsPoseCommitted?.Invoke(handle, previous, position, pose);
        }

        private static bool UsesChunkBlockingProjection(Record record)
            => record.ChunkBlockingProjection;

        internal static ItemData CloneNaturalEntitySnapshotPreservingMotion(ItemData source)
            => source == null ? null : ItemInstanceDataFactory.CloneRuntime(source);

        internal static void SetGeneratedNaturalEntitySourceIdentity(NaturalEntityHandle handle,
            string sourceWorldKey, int sourceX, int sourceY)
        {
            using var scope = UseHandle(handle);
            if (!Contains(handle) || string.IsNullOrWhiteSpace(sourceWorldKey))
                throw new InvalidOperationException("自然资源缺少有效实体或出生世界。");
            Record record = records[handle.Id];
            var key = (sourceWorldKey, sourceX, sourceY, record.Snapshot.Guid);
            if (record.HasGeneratedNaturalSource &&
                (record.PhysicsSourceWorldKey != sourceWorldKey || record.PhysicsSourceChunkX != sourceX ||
                 record.PhysicsSourceChunkY != sourceY))
                throw new InvalidOperationException("自然资源出生身份不能被覆盖。");
            if (State.generatedNaturalSources.TryGetValue(key, out Record existing) && !ReferenceEquals(existing, record))
                throw new InvalidOperationException($"自然资源出生身份重复：{sourceWorldKey}/{sourceX}/{sourceY}/{record.Snapshot.Guid}。");
            record.HasGeneratedNaturalSource = true;
            record.PhysicsSourceWorldKey = sourceWorldKey;
            record.PhysicsSourceChunkX = sourceX; record.PhysicsSourceChunkY = sourceY;
            State.generatedNaturalSources[key] = record;
        }

        internal static void SetIndependentPhysicsSourceIdentity(NaturalEntityHandle handle,
            string sourceWorldKey, int sourceX, int sourceY)
        {
            using var scope = UseHandle(handle);
            SetGeneratedNaturalEntitySourceIdentity(handle, sourceWorldKey, sourceX, sourceY);
            Record record = records[handle.Id];
            var key = (sourceWorldKey, sourceX, sourceY, record.Snapshot.Guid);
            if (State.independentPhysicsSources.TryGetValue(key, out Record existing) && !ReferenceEquals(existing, record))
                throw new InvalidOperationException("独立自然资源出生身份重复。");
            record.HasIndependentPhysicsSource = true;
            State.independentPhysicsSources[key] = record;
        }

        internal static bool HasGeneratedNaturalEntitySourceInCurrentWorld(string sourceWorldKey, int x, int y, int guid)
            => State.generatedNaturalSources.ContainsKey((sourceWorldKey, x, y, guid));

        internal static bool IsGeneratedNaturalEntitySourceReserved(string worldKey, int x, int y, int guid)
            => worlds.TryGetValue(worldKey ?? string.Empty, out WorldState world) &&
                world.generatedNaturalSources.TryGetValue((worldKey, x, y, guid), out Record record) && record.Busy;

        internal static void ReleaseNaturalEntityWorldTransferReservation(NaturalEntityHandle handle)
        {
            using var scope = UseHandle(handle);
            if (Contains(handle)) records[handle.Id].Busy = false;
        }

        /// <summary>目标世界准备完成后迁移原资源身份；失败时保留源实体和物理租约。</summary>
        public static bool TryTransferNaturalEntityToWorld(NaturalEntityHandle handle, string targetWorldKey,
            Vector2 targetPosition, out NaturalEntityHandle targetHandle, out string failure)
        {
            targetHandle = default;
            failure = null;
            using var scope = UseHandle(handle);
            if (!GameNetwork.HasStateAuthority || !Contains(handle))
            { failure = "只有有效的权威自然实体可以跨世界迁移。"; return false; }
            Record record = records[handle.Id];
            if (!record.HasGeneratedNaturalSource || record.Busy || !record.IsValid)
            { failure = "实体没有可迁移的天然来源，或正在执行其他事务。"; return false; }
            if (string.IsNullOrWhiteSpace(targetWorldKey) || record.WorldKey == targetWorldKey ||
                !float.IsFinite(targetPosition.x) || !float.IsFinite(targetPosition.y))
            { failure = "目标世界或位置无效。"; return false; }
            if (!GameplayWorldRegistry.TryGet(record.WorldKey, out var sourceWorld) || sourceWorld.Chunks == null ||
                !GameplayWorldRegistry.TryGet(targetWorldKey, out var targetWorld) || targetWorld.Chunks == null ||
                sourceWorld.Chunks.WorldRuntime == null || targetWorld.Chunks.WorldRuntime == null ||
                sourceWorld.Chunks.IsWorldRuntimeShuttingDown || targetWorld.Chunks.IsWorldRuntimeShuttingDown ||
                !GameplayWorldRegistry.TryGetPhysics(record.WorldKey, out var sourcePhysics) ||
                !GameplayWorldRegistry.TryGetPhysics(targetWorldKey, out var targetPhysics) ||
                sourcePhysics.PrimarySpace.PhysicsScene == targetPhysics.PrimarySpace.PhysicsScene)
            { failure = "源世界或目标世界尚未准备好独立物理空间与区块运行时。"; return false; }
            if (GameplayWorldRegistry.IsPhysicsDispatchInProgress || sourcePhysics.IsSimulating || targetPhysics.IsSimulating)
            { failure = "物理世界正在模拟，请在本轮分发结束后迁移。"; return false; }
            if (record.PhysicsProxy?.IsParticipatingInWorldPhysics != true || record.PhysicsProxy.Owner != sourcePhysics)
            { failure = "源物理代理已失效或不属于源世界。"; return false; }
            IndependentNaturalEntityRegistry source, target;
            try
            {
                source = sourceWorld.Chunks.IndependentNaturalEntities;
                using (GameplayWorldRegistry.UseWorld(targetWorldKey)) target = targetWorld.Chunks.IndependentNaturalEntities;
            }
            catch (Exception exception) { failure = exception.Message; return false; }
            record.Busy = true;
            try
            {
                return source.TryTransferNaturalEntityToWorld(handle, target, targetPosition, record.PhysicsSourceWorldKey,
                    record.PhysicsSourceChunkX, record.PhysicsSourceChunkY, record.Snapshot.Guid,
                    record.HasIndependentPhysicsSource, out targetHandle, out failure);
            }
            finally { record.Busy = false; }
        }

        private static void ForgetIndependentPhysicsSourceIdentity(Record record)
        {
            var key = (record.PhysicsSourceWorldKey, record.PhysicsSourceChunkX, record.PhysicsSourceChunkY, record.Snapshot.Guid);
            if (record.HasIndependentPhysicsSource && State.independentPhysicsSources.TryGetValue(key, out Record current) &&
                ReferenceEquals(current, record)) State.independentPhysicsSources.Remove(key);
            if (record.HasGeneratedNaturalSource && State.generatedNaturalSources.TryGetValue(key, out current) &&
                ReferenceEquals(current, record)) State.generatedNaturalSources.Remove(key);
        }

        public static NaturalEntityPhysicsNetworkPose[] CaptureIndependentPhysicsNetworkPoses(string worldKey,
            Vector2 observerPosition, float radius)
        {
            if (!GameNetwork.HasStateAuthority || !GameplayWorldRegistry.TryGet(worldKey, out _))
                return Array.Empty<NaturalEntityPhysicsNetworkPose>();
            using var scope = GameplayWorldRegistry.UseWorld(worldKey);
            using var bufferScope = ListPool<NaturalEntityPhysicsNetworkPose>.Get(out var result);
            float squaredRadius = radius * radius;
            foreach (Record record in State.independentPhysicsSources.Values)
            {
                if (!record.HasIndependentPhysicsSource || !record.IsValid ||
                    WorldTopologyRuntime.SqrDistance(observerPosition, record.Snapshot.transform.position) > squaredRadius) continue;
                ItemPhysicsRuntimeState motion = record.Snapshot.PhysicsState;
                result.Add(new NaturalEntityPhysicsNetworkPose
                {
                    SourceWorldKey = record.PhysicsSourceWorldKey,
                    SourceChunkX = record.PhysicsSourceChunkX, SourceChunkY = record.PhysicsSourceChunkY,
                    Guid = record.Snapshot.Guid, Position = record.Snapshot.transform.position,
                    RotationDegrees = record.Snapshot.transform.rotation.eulerAngles.z,
                    Velocity = motion?.Velocity ?? Vector2.zero, AngularVelocity = motion?.AngularVelocity ?? 0f,
                    IsDynamic = record.PhysicsProxy?.Mode == WorldPhysicsProxyMode2D.IndependentDynamic
                });
            }
            return result.ToArray();
        }

        public static bool TryCaptureIndependentPhysicsNetworkState(string worldKey, string sourceWorldKey, int sourceX, int sourceY,
            int guid, out ItemData snapshot)
        {
            snapshot = null;
            if (!GameNetwork.HasStateAuthority || !GameplayWorldRegistry.TryGet(worldKey, out _)) return false;
            using var scope = GameplayWorldRegistry.UseWorld(worldKey);
            return State.independentPhysicsSources.TryGetValue((sourceWorldKey, sourceX, sourceY, guid), out Record record) &&
                Contains(record.Handle) && TryCapture(record.Handle, out snapshot);
        }

        public static void ApplyIndependentPhysicsNetworkPoses(string worldKey, NaturalEntityPhysicsNetworkPose[] poses)
        {
            if (GameNetwork.HasStateAuthority || poses == null || poses.Length > 4096 || !GameplayWorldRegistry.TryGet(worldKey, out _)) return;
            using var scope = GameplayWorldRegistry.UseWorld(worldKey);
            foreach (NaturalEntityPhysicsNetworkPose pose in poses)
            {
                if (string.IsNullOrWhiteSpace(pose.SourceWorldKey) || pose.Guid == 0 ||
                    !math.all(math.isfinite(new float4(pose.Position.x, pose.Position.y, pose.Velocity.x, pose.Velocity.y))) ||
                    !float.IsFinite(pose.RotationDegrees) || !float.IsFinite(pose.AngularVelocity)) continue;
                ChunkMgr.ExistingInstance?.IndependentNaturalEntities.ApplyNetworkPose(pose);
            }
        }

        internal static bool ApplyIndependentPhysicsNetworkPoseToEntity(NaturalEntityHandle handle, NaturalEntityPhysicsNetworkPose pose)
        {
            if (!Contains(handle) || !TryGetPhysicsProxy(handle, out var proxy)) return false;
            Vector2 projected = WorldLocalPresentation.ProjectPosition(pose.Position);
            var nativePose = new WorldPhysicsEntityPose2D(projected, pose.RotationDegrees, pose.Velocity, pose.AngularVelocity);
            proxy.SetPose(nativePose);
            CommitNaturalEntityPhysicsPose(handle, nativePose, authoritativeNetworkResult: true);
            return true;
        }

        public static void ApplyIndependentPhysicsNetworkState(string worldKey, string sourceWorldKey, int sourceX, int sourceY,
            int guid, ItemData snapshot)
        {
            if (GameNetwork.HasStateAuthority || string.IsNullOrWhiteSpace(sourceWorldKey) || guid == 0 ||
                !GameplayWorldRegistry.TryGet(worldKey, out _)) return;
            using var scope = GameplayWorldRegistry.UseWorld(worldKey);
            ChunkMgr.ExistingInstance?.IndependentNaturalEntities.ApplyNetworkState(sourceWorldKey, sourceX, sourceY, guid, snapshot);
        }

        private static void ResolveConfiguredPhysicsBounds(Record record)
        {
            if (record.PhysicsProxy?.IsValid != true) return;
            NaturalEntityBody body = simulation.GetBody(record.Handle.Id);
            Vector3 scale = new(body.Scale.x, body.Scale.y, 1f);
            if (!record.PhysicsScale.Equals(scale))
            {
                record.PhysicsProxy.SetEntityScale(scale);
                record.PhysicsScale = scale;
            }
            bool hasBody = false, hasDamage = false;
            record.HasConfiguredInteractionBounds = false;
            foreach (Collider2D collider in record.PhysicsProxy.Colliders)
            {
                if (collider == null || !collider.enabled || !WorldPhysicsService2D.TryResolveCollider(collider, out var binding)) continue;
                Bounds bounds = ResolvePhysicsColliderWorldBounds(collider);
                bounds.center = WorldTopologyRuntime.NormalizePosition(WorldLocalPresentation.ToLogical(bounds.center));
                if ((binding.Purpose & WorldPhysicsPurpose2D.Blocking) != 0)
                { if (hasBody) record.BodyBounds.Encapsulate(bounds); else record.BodyBounds = bounds; hasBody = true; }
                if ((binding.Purpose & WorldPhysicsPurpose2D.Damage) != 0)
                { if (hasDamage) record.HitBounds.Encapsulate(bounds); else record.HitBounds = bounds; hasDamage = true; }
                if ((binding.Purpose & WorldPhysicsPurpose2D.Interaction) != 0)
                {
                    if (record.HasConfiguredInteractionBounds) record.InteractionBounds.Encapsulate(bounds);
                    else record.InteractionBounds = bounds;
                    record.HasConfiguredInteractionBounds = true;
                }
            }
            record.BlocksMovement = hasBody || record.ChunkBlockingProjection && record.Profile.SpatialGeometry.BlocksMovement;
            record.HitColliderEnabled = hasDamage;
        }

        // 默认点选区域跟随当前显示阶段，显式配置区域始终由定义决定。
        private static void RefreshDefaultNaturalInteractionRegionGeometry(Record record, Sprite sprite, Vector3 rendererScale)
        {
            if (record.Profile.Definition.Physics?.HasRegions == true || record.PhysicsProxy?.IsValid != true) return;
            var visual = record.Profile.Definition.Visual;
            foreach (Collider2D collider in record.PhysicsProxy.Colliders)
            {
                if (collider is not BoxCollider2D box || !WorldPhysicsService2D.TryResolveCollider(collider, out var binding) ||
                    binding.RegionId != "interaction" || binding.Purpose != WorldPhysicsPurpose2D.Interaction) continue;
                Bounds bounds = sprite != null ? sprite.bounds : new Bounds(Vector3.zero, Vector3.one);
                Vector3 position = visual?.RendererLocalPosition ?? Vector3.zero;
                Quaternion rotation = Quaternion.Euler((visual?.RendererLocalEulerAngles ?? Vector3.zero) +
                    Vector3.forward * ManualHarvestWork.ShakeAngle(Time.time - record.HarvestShakeStartedAt));
                if (box.offset != (Vector2)bounds.center) box.offset = bounds.center;
                if (box.size != (Vector2)bounds.size) box.size = bounds.size;
                if (box.transform.localPosition != position || box.transform.localRotation != rotation)
                    box.transform.SetLocalPositionAndRotation(position, rotation);
                if (box.transform.localScale != rendererScale) box.transform.localScale = rendererScale;
                return;
            }
        }

        private static Bounds ResolvePhysicsColliderWorldBounds(Collider2D collider)
        {
            Vector2 center = collider.offset, size;
            switch (collider)
            {
                case BoxCollider2D box: size = box.size; break;
                case CircleCollider2D circle: size = Vector2.one * (circle.radius * 2f); break;
                case CapsuleCollider2D capsule: size = capsule.size; break;
                case PolygonCollider2D polygon:
                    Bounds local = new(collider.offset, Vector3.zero);
                    bool first = true;
                    using (var pointsScope = ListPool<Vector2>.Get(out var points))
                    {
                        polygon.GetPath(0, points);
                        foreach (Vector2 point in points)
                        {
                            Vector2 translated = point + collider.offset;
                            if (first) { local = new Bounds(translated, Vector3.zero); first = false; }
                            else local.Encapsulate(translated);
                        }
                    }
                    center = local.center; size = local.size;
                    break;
                default: return collider.bounds;
            }
            return TransformBounds(collider.transform.localToWorldMatrix, center, size);
        }

        private static bool ContainsConfiguredPhysicsPoint(Record record, Vector2 point, WorldPhysicsPurpose2D purpose)
        {
            point = WorldLocalPresentation.ProjectPosition(WorldLocalPresentation.ToLogical(point));
            foreach (Collider2D collider in record.PhysicsProxy.Colliders)
                if (collider != null && collider.enabled && WorldPhysicsService2D.TryResolveCollider(collider, out var binding) &&
                    (binding.Purpose & purpose) != 0 && collider.OverlapPoint(point)) return true;
            return false;
        }

        // 原生形状负责最终命中判定，联合 Bounds 只承担空间桶宽相。
        private static void AppendConfiguredPhysicsWeaponCandidates(AttackShape2D shape,
            List<(Record Record, float Fraction, float Distance, Vector2 Point)> candidates)
        {
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(1 << CombatPhysicsChannels.DamageReceiverLayer);
            Vector2 size = new(Mathf.Max(.0001f, shape.HalfExtents.x * 2f), Mathf.Max(.0001f, shape.HalfExtents.y * 2f));
            Vector2 travel = (Vector2)shape.SweepDelta;
            float distance = travel.magnitude;
            if (distance <= .0001f)
            {
                using var scope = ListPool<Collider2D>.Get(out var hits);
                GameplayPhysics2D.OverlapBox((Vector2)shape.Center, size, shape.Rotation * Mathf.Rad2Deg, filter, hits);
                foreach (Collider2D collider in hits)
                    if (TryGetConfiguredPhysicsDamageRecord(collider, out Record record))
                    {
                        Vector2 point = collider.ClosestPoint((Vector2)shape.Center);
                        candidates.Add((record, 0f, ((Vector2)shape.Center - point).sqrMagnitude, point));
                    }
                return;
            }
            using var castScope = ListPool<RaycastHit2D>.Get(out var casts);
            GameplayPhysics2D.BoxCast((Vector2)(shape.Center - shape.SweepDelta), size, shape.Rotation * Mathf.Rad2Deg,
                travel / distance, filter, casts, distance);
            foreach (RaycastHit2D hit in casts)
                if (TryGetConfiguredPhysicsDamageRecord(hit.collider, out Record record))
                    candidates.Add((record, Mathf.Clamp01(hit.distance / distance),
                        WorldTopologyRuntime.SqrDistance((Vector2)shape.BoundsCenter, hit.point), hit.point));
        }

        private static bool TryGetConfiguredPhysicsDamageRecord(Collider2D collider, out Record record)
        {
            record = null;
            if (!WorldPhysicsService2D.TryResolveCollider(collider, out var binding) ||
                (binding.Purpose & WorldPhysicsPurpose2D.Damage) == 0 || binding.Entity is not Record candidate ||
                candidate.WorldKey != State.WorldKey || !candidate.IsValid || candidate.PhysicsProxy?.IsValid != true) return false;
            record = candidate;
            return true;
        }

        internal static List<WorldPhysicsEntityRegion2D> CompileNaturalEntityPhysicsRegions(NaturalEntityEcsProfile profile)
        {
            var result = new List<WorldPhysicsEntityRegion2D>();
            RuntimeItemDefinition definition = profile.Definition;
            if (definition.Physics?.HasRegions == true)
            {
                foreach (ItemPhysicsRegionDefinitionDto region in definition.Physics.Regions)
                {
                    if (!string.IsNullOrEmpty(region.Path))
                        throw new InvalidOperationException($"数据资源 {definition.Id} 的区域 {region.Id} 不能使用 GameObject path，请用模块稳定槽声明接收者。");
                    if ((region.Purposes & WorldPhysicsPurpose2D.Damage) != 0 &&
                        (string.IsNullOrEmpty(region.Module) || region.Module != profile.HealthModuleName))
                        throw new InvalidOperationException($"数据资源 {definition.Id} 的区域 {region.Id} 必须指向本实体唯一生命模块 {profile.HealthModuleName}。");
                    if ((region.Purposes & WorldPhysicsPurpose2D.Trigger) != 0 &&
                        !profile.Extensions.Exists(extension => extension.Module.StableName == region.Module &&
                            extension.Capability.PhysicsTrigger != null))
                        throw new InvalidOperationException($"数据资源 {definition.Id} 的区域 {region.Id} 必须指向已声明 PhysicsTrigger 回调的资源能力模块。");
                    if ((region.Purposes & WorldPhysicsPurpose2D.Interaction) != 0 &&
                        (string.IsNullOrEmpty(region.Module) || region.Module != profile.GrowthModuleName &&
                         region.Module != profile.CropModuleName && region.Module != profile.StockModuleName))
                        throw new InvalidOperationException($"数据资源 {definition.Id} 的区域 {region.Id} 必须指向本实体真实的生长、作物或采集模块。");
                    JObject parameters = null;
                    if (!string.IsNullOrEmpty(region.Module))
                        foreach (RuntimeItemModuleDefinition module in definition.ModuleDefinitions)
                            if (module.StableName == region.Module)
                            { parameters = string.IsNullOrEmpty(module.ParametersJson) ? null : JObject.Parse(module.ParametersJson); break; }
                    AddRegion(result, region.Id, region.Purposes, region.Collider, region.Module, parameters,
                        chunkBlockingProjection: false);
                }
                return result;
            }
            ItemColliderDefinitionDto root = definition.Visual?.Collider;
            if (root?.Enabled != false && root?.IsTrigger == false)
                AddRegion(result, "body", WorldPhysicsPurpose2D.Blocking, root, null, null, true);
            if (profile.HealthModuleName != null)
            {
                ItemColliderDefinitionDto hit = profile.HealthParameters?["$collider2D"]?.ToObject<ItemColliderDefinitionDto>() ?? root;
                if (hit?.Enabled != false)
                    AddRegion(result, "damage", WorldPhysicsPurpose2D.Damage, hit ?? new ItemColliderDefinitionDto
                        { Type = nameof(BoxCollider2D), Size = Vector2.one }, profile.HealthModuleName, profile.HealthParameters, false);
            }
            // 点选区域复用现有可见范围，不给物种增加额外配置真源。
            Bounds visual = definition.Sprite != null ? definition.Sprite.bounds : new Bounds(Vector3.zero, Vector3.one);
            var interaction = new ItemColliderDefinitionDto
            { Type = nameof(BoxCollider2D), Offset = visual.center, Size = visual.size, IsTrigger = true };
            // Unity 向量直接作为姿态使用，避免 JSON 序列化访问递归的 normalized 属性。
            result.Add(new WorldPhysicsEntityRegion2D("interaction", WorldPhysicsPurpose2D.Interaction, interaction,
                localPosition: definition.Visual?.RendererLocalPosition ?? Vector3.zero,
                localRotation: Quaternion.Euler(definition.Visual?.RendererLocalEulerAngles ?? Vector3.zero),
                localScale: definition.Visual?.RendererLocalScale ?? Vector3.one, chunkBlockingProjection: false));
            return result;
        }

        private static void AddRegion(List<WorldPhysicsEntityRegion2D> result, string id,
            WorldPhysicsPurpose2D purposes, ItemColliderDefinitionDto collider, string module, JObject parameters,
            bool chunkBlockingProjection)
        {
            if (collider == null || collider.Enabled == false) return;
            ItemColliderDefinitionDto shape = FastCloner.FastCloner.DeepClone(collider);
            shape.Type ??= nameof(BoxCollider2D);
            shape.IsTrigger = (purposes & WorldPhysicsPurpose2D.Blocking) == 0;
            JObject transform = parameters?["$transform"] as JObject;
            result.Add(new WorldPhysicsEntityRegion2D(id, purposes, shape, module,
                transform?["localPosition"]?.ToObject<Vector3>() ?? Vector3.zero,
                Quaternion.Euler(transform?["localEulerAngles"]?.ToObject<Vector3>() ?? Vector3.zero),
                transform?["localScale"]?.ToObject<Vector3>() ?? Vector3.one, chunkBlockingProjection));
        }

        #endregion
    }
}
