using FlatWorld.Networking;
using FlatWorld.WorldModel;
using Unity.Mathematics;
using UnityEngine;

public static partial class LooseItemMotion
{
    #region 世界采样与统一阻挡
    internal struct WorldField : IField
    {
        internal string World;
        internal ItemData Item;
        internal Vector2 WaterVelocity, WindVelocity;
        internal bool Conveyor, InWater, ReceiveExternal;
        internal ChunkMgr Chunks;
        internal bool LocalPhysics;
        internal bool SamplingInitialized, HasTerrainQuery, SurfaceWorld;
        internal bool WaitingForSurface;
        internal ChunkMgr.RuntimeTerrainPresenceQuery TerrainQuery;
        internal WorldTopologyDomain Topology;
        public bool WaterOnly => InWater;
        public Vector2 CarrierVelocity(Vector2 position, float seconds) => Conveyor
            ? MachineWorld.SampleLooseConveyorVelocity(position, seconds) : WaterVelocity;
        public Vector2 ExternalVelocity(Vector2 position) => ReceiveExternal
            ? WindVelocity + SampleAdditionalVelocity(new LooseItemMotionContext(World, Item, position, InWater))
            : Vector2.zero;
        public Vector2 Delta(Vector2 from, Vector2 to)
        {
            EnsureSampling();
            float2 delta = Topology.ShortestDelta(new float2(from.x, from.y), new float2(to.x, to.y));
            return new Vector2(delta.x, delta.y);
        }
        public bool IsWater(Vector2 position)
        {
            EnsureSampling();
            position = NormalizeMotionPosition(Topology, position);
            if (HasTerrainQuery)
            {
                if (!TerrainQuery.TrySample(position, out RuntimeTerrainTileSample current))
                { WaitingForSurface = SurfaceWorld; return false; }
                return current.LiquidDepth > 0f;
            }
            if (Chunks == null && FlatWorld.Spaceflight.SpaceSurfaceQuery.QueryLandingSurface(World, position, out _) ==
                FlatWorld.Spaceflight.SurfaceQueryStatus.Pending)
            { WaitingForSurface = true; return false; }
            if (Chunks != null)
            {
                if (!Chunks.TryGetRuntimeTerrainTile(position, out RuntimeTerrainTileSample sample))
                { WaitingForSurface = SurfaceWorld; return false; }
                return sample.LiquidDepth > 0f;
            }
            return FlatWorld.Spaceflight.SpaceSurfaceQuery.TrySampleLiquid(World, position, out _, out _);
        }
        public Vector2 Move(Vector2 position, Vector2 displacement)
        {
            EnsureSampling();
            return ResolveDisplacement(World, position, displacement, Chunks, LocalPhysics,
                SurfaceWorld, Topology, HasTerrainQuery, ref TerrainQuery, ref WaitingForSurface);
        }

        private void EnsureSampling()
        {
            if (SamplingInitialized) return;
            // 确认有位移之后才建立单次寻址，静水、无风和零外力不新增准备成本。
            SamplingInitialized = true;
            HasTerrainQuery = Chunks != null && Chunks.TryCreateTerrainPresenceQuery(out TerrainQuery);
            Topology = PressureExplosionQueue.TryGetTopology(World, out WorldTopologyBounds bounds)
                ? bounds.ToDomain() : default;
            SurfaceWorld = WorldAddress.FromWorldKey(World).IsSurface;
        }
    }

    private static readonly RaycastHit2D[] hits = new RaycastHit2D[1];
    private static int collisionMask;

    private static Vector2 NormalizeMotionPosition(WorldTopologyDomain topology, Vector2 position)
    {
        float2 normalized = topology.Normalize(new float2(position.x, position.y));
        return new Vector2(normalized.x, normalized.y);
    }

    // 数据阻挡和本地实体碰撞归同一入口，后台世界绝不借用镜头所在世界的物理场。
    private static Vector2 ResolveDisplacement(string world, Vector2 position, Vector2 displacement,
        ChunkMgr chunks, bool localPhysics, bool surfaceWorld,
        WorldTopologyDomain topology, bool hasTerrainQuery, ref ChunkMgr.RuntimeTerrainPresenceQuery terrainQuery,
        ref bool waitingForSurface)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(displacement.magnitude / .1f), 1, 128);
        Vector2 move = displacement / steps;
        Vector2 initial = position;
        if (surfaceWorld && !hasTerrainQuery && chunks == null && FlatWorld.Spaceflight.SpaceSurfaceQuery.EnsureSurfaceAreaReady(world,
                position + displacement * .5f, displacement.magnitude * .5f + .1f) == FlatWorld.Spaceflight.SurfaceQueryStatus.Pending)
        { waitingForSurface = true; return position; }
        var filter = new ContactFilter2D { useTriggers = false };
        if (localPhysics)
        {
            if (collisionMask == 0) collisionMask = LayerMask.GetMask("Collider");
            filter.SetLayerMask(collisionMask);
            if (chunks == null && !hasTerrainQuery) return position; // 本地地形窗口未准备好，不靠查询临时加载地形。
        }
        for (int index = 0; index < steps; index++)
        {
            Vector2 destination = NormalizeMotionPosition(topology, position + move);
            if (hasTerrainQuery || chunks != null)
            {
                bool found = hasTerrainQuery
                    ? terrainQuery.TrySample(destination, out RuntimeTerrainTileSample sample)
                    : chunks.TryGetRuntimeTerrainTile(destination, out sample);
                if (!found) { waitingForSurface = surfaceWorld; return initial; }
                if (!BuildingOccupancyRegistry.GetEffectiveWalkable(sample.WorldCell,
                        sample.Cell.IsWalkable || sample.LiquidDepth > 0f)) break;
            }
            else if (surfaceWorld)
            {
                if (!FlatWorld.Spaceflight.SpaceSurfaceQuery.TryGetLandingSurface(world, destination, out var surface) ||
                    surface.BlockingTileId != 0) break;
            }
            float distance = move.magnitude;
            if (localPhysics && distance > .00001f && GameplayPhysics2D.CircleCast(position, .06f,
                move / distance, filter, hits, distance) > 0)
            {
                float allowed = Mathf.Clamp(hits[0].distance - .01f, 0f, distance);
                Vector2 stopped = position + move / distance * allowed;
                return NormalizeMotionPosition(topology, stopped);
            }
            position = destination;
        }
        return position;
    }
    #endregion

    #region 来源适配与统一提交
    private static WorldField CreateField(string world, ItemData item, Vector2 position, bool inWater,
        bool receiveExternal, Vector2? windVelocity)
    {
        bool ownsContext = world == GameplayWorldRegistry.ExecutionWorldKey;
        ChunkMgr chunks = ownsContext ? ChunkMgr.ExistingInstance : null;
        return new WorldField
        {
            World = world, Item = item, InWater = inWater, ReceiveExternal = receiveExternal,
            Chunks = chunks,
            LocalPhysics = world == GameplayWorldRegistry.LocalPresentationWorldKey,
            WindVelocity = receiveExternal ? windVelocity ?? (ownsContext && item?.Stack != null
                ? WorldMotionSystem.SampleWindVelocity(position, item.Stack.CurrentWeight) : Vector2.zero) : Vector2.zero
        };
    }

    public static Vector2 AdvanceConveyor(string world, ItemData item, Vector2 position, float seconds)
    {
        if (!GameNetwork.HasStateAuthority || !Finite(position) || !Finite(seconds) || seconds <= 0f) return position;
        var field = CreateField(world, item, position, false, true, null);
        field.Conveyor = true;
        return Integrate(position, Mathf.Min(seconds, .5f), ref field);
    }

    public static Vector2 AdvanceWater(string world, ItemData item, Vector2 position, Vector2 currentVelocity,
        float seconds, bool receiveExternal = true)
    {
        TryAdvanceWater(world, item, position, currentVelocity, seconds, out Vector2 result, receiveExternal);
        return result;
    }

    public static bool TryAdvanceWater(string world, ItemData item, Vector2 position, Vector2 currentVelocity,
        float seconds, out Vector2 result, bool receiveExternal = true)
    {
        result = position;
        if (!GameNetwork.HasStateAuthority || !Finite(position) || !Finite(seconds) || seconds <= 0f) return false;
        var field = CreateField(world, item, position, true, receiveExternal, null);
        field.WaterVelocity = currentVelocity;
        Vector2 advanced = Integrate(position, seconds, ref field);
        if (field.WaitingForSurface) return false;
        result = advanced;
        return true;
    }

    public static Vector2 AdvanceExternal(string world, ItemData item, Vector2 position, float seconds,
        Vector2? windVelocity = null)
    {
        TryAdvanceExternal(world, item, position, seconds, out Vector2 result, windVelocity);
        return result;
    }

    public static bool TryAdvanceExternal(string world, ItemData item, Vector2 position, float seconds,
        out Vector2 result, Vector2? windVelocity = null)
    {
        result = position;
        if (!GameNetwork.HasStateAuthority || !Finite(position) || !Finite(seconds) || seconds <= 0f) return false;
        var field = CreateField(world, item, position, false, true, windVelocity);
        Vector2 advanced = Integrate(position, seconds, ref field);
        if (field.WaitingForSurface) return false;
        result = advanced;
        return true;
    }

    internal static bool IsDrivenByConveyor(Vector2 position)
    {
        MachineEntity belt = MachineWorld.GetTransportAt(position);
        return belt != null && belt.Active && belt.SpeedRpm > 0f;
    }

    /// <summary>完整 Item 也统一同步刚体、存档坐标、空间索引和联机表现。</summary>
    internal static bool CommitItemPosition(Item item, Vector2 position)
    {
        if (item == null || item.DestructionHandled || !Finite(position)) return false;
        if (WorldPhysicsService2D.GetConfiguredItemPhysics(item) != null)
            return WorldPhysicsService2D.RequestItemMovementTowardsLogicalPosition(item, item.GetComponent<Rigidbody2D>(), position);
        string world = item.gameObject.scene.name;
        Vector2 before = item.transform.position;
        Vector2 movement = PressureExplosionQueue.DeltaInWorld(world, before, position);
        if (movement.sqrMagnitude <= .00000001f) return false;
        Vector3 display = new(before.x + movement.x, before.y + movement.y, item.transform.position.z);
        if (item.TryGetComponent(out Rigidbody2D body)) { body.position = display; body.velocity = Vector2.zero; }
        item.transform.position = display;
        item.itemData.transform.position = new Vector3(position.x, position.y, display.z);
        if (world == GameplayWorldRegistry.ExecutionWorldKey)
            ItemWorldPlacement.TryAttachWorldModelTransientItem(item, position);
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        WorldMotionSystem.MarkLooseItemMoved(item);
        return true;
    }
    #endregion
}
