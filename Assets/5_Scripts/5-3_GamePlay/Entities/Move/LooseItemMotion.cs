using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class LooseItemMotion
{
    #region 世界采样与统一阻挡
    private struct WorldField : IField
    {
        internal string World;
        internal ItemData Item;
        internal Vector2 WaterVelocity, WindVelocity;
        internal bool Conveyor, InWater, ReceiveExternal;
        internal ChunkMgr Chunks;
        internal bool LocalPhysics;
        public bool WaterOnly => InWater;
        public Vector2 CarrierVelocity(Vector2 position, float seconds) => Conveyor
            ? MachineWorld.SampleLooseConveyorVelocity(position, seconds) : WaterVelocity;
        public Vector2 ExternalVelocity(Vector2 position) => ReceiveExternal
            ? WindVelocity + SampleAdditionalVelocity(new LooseItemMotionContext(World, Item, position, InWater))
            : Vector2.zero;
        public Vector2 Delta(Vector2 from, Vector2 to) => PressureExplosionQueue.DeltaInWorld(World, from, to);
        public bool IsWater(Vector2 position)
        {
            position = PressureExplosionQueue.NormalizeInWorld(World, position);
            return Chunks != null
                ? Chunks.TryGetRuntimeTerrainTile(position, out RuntimeTerrainTileSample sample) && sample.LiquidDepth > 0f
                : FlatWorld.Spaceflight.SpaceSurfaceQuery.TrySampleLiquid(World, position, out _, out _);
        }
        public Vector2 Move(Vector2 position, Vector2 displacement)
            => ResolveDisplacement(World, position, displacement, Chunks, LocalPhysics);
    }

    private static readonly RaycastHit2D[] hits = new RaycastHit2D[1];
    private static int collisionMask;

    // 数据阻挡和本地实体碰撞归同一入口，后台世界绝不借用镜头所在世界的物理场。
    private static Vector2 ResolveDisplacement(string world, Vector2 position, Vector2 displacement,
        ChunkMgr chunks, bool localPhysics)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(displacement.magnitude / .1f), 1, 128);
        Vector2 move = displacement / steps;
        var filter = new ContactFilter2D { useTriggers = false };
        if (localPhysics)
        {
            if (collisionMask == 0) collisionMask = LayerMask.GetMask("Collider");
            filter.SetLayerMask(collisionMask);
            if (chunks == null) return position; // 本地地形窗口未准备好，不靠查询临时加载地形。
        }
        for (int index = 0; index < steps; index++)
        {
            Vector2 destination = PressureExplosionQueue.NormalizeInWorld(world, position + move);
            if (chunks != null)
            {
                if (!chunks.TryGetRuntimeTerrainTile(destination, out RuntimeTerrainTileSample sample) ||
                    !BuildingOccupancyRegistry.GetEffectiveWalkable(sample.WorldCell,
                        sample.Cell.IsWalkable || sample.LiquidDepth > 0f)) break;
            }
            else if (WorldAddress.FromWorldKey(world).IsSurface)
            {
                if (!FlatWorld.Spaceflight.SpaceSurfaceQuery.TryGetLandingSurface(world, destination, out var surface) ||
                    surface.BlockingTileId != 0) break;
            }
            float distance = move.magnitude;
            if (localPhysics && distance > .00001f && GameplayPhysics2D.CircleCast(position, .06f,
                move / distance, filter, hits, distance) > 0)
            {
                float allowed = Mathf.Clamp(hits[0].distance - .01f, 0f, distance);
                return PressureExplosionQueue.NormalizeInWorld(world, position + move / distance * allowed);
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
        return new WorldField
        {
            World = world, Item = item, InWater = inWater, ReceiveExternal = receiveExternal,
            Chunks = ownsContext ? ChunkMgr.ExistingInstance : null,
            LocalPhysics = world == SceneManager.GetActiveScene().name,
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
        if (!GameNetwork.HasStateAuthority || !Finite(position) || !Finite(seconds) || seconds <= 0f) return position;
        var field = CreateField(world, item, position, true, receiveExternal, null);
        field.WaterVelocity = currentVelocity;
        return Integrate(position, seconds, ref field);
    }

    public static Vector2 AdvanceExternal(string world, ItemData item, Vector2 position, float seconds,
        Vector2? windVelocity = null)
    {
        if (!GameNetwork.HasStateAuthority || !Finite(position) || !Finite(seconds) || seconds <= 0f) return position;
        var field = CreateField(world, item, position, false, true, windVelocity);
        return Integrate(position, seconds, ref field);
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
        string world = item.gameObject.scene.name;
        Vector2 before = item.transform.position;
        Vector2 movement = PressureExplosionQueue.DeltaInWorld(world, before, position);
        if (movement.sqrMagnitude <= .00000001f) return false;
        Vector3 display = new(before.x + movement.x, before.y + movement.y, item.transform.position.z);
        Rigidbody2D body = item.GetComponent<Rigidbody2D>();
        if (body != null) { body.position = display; body.velocity = Vector2.zero; }
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
