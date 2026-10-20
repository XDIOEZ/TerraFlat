using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>实体模块声明是否实际接触水面；飞行等离地阶段不接受表层水流。</summary>
public interface IWaterCurrentExposure
{
    bool ReceivesWaterCurrent { get; }
}

/// <summary>自行推进飞行等运动的模块声明风位移所有权，避免移动模块重复叠加。</summary>
public interface IWindMotionOwner
{
    bool OwnsWindMotion { get; }
}

/// <summary>
/// 游戏意图与环境速度的公共入口；接触、推动和阻挡交给 Physics2D。
/// </summary>
public static class WorldMotionSystem
{
    #region 环境速度
    public static bool HasWindPush => WeatherMgr.ExistingInstance?.HasWindPush == true;

    /// <summary>同一批物品共用质量目录与星球重力，风区和重量仍按每个物品单独结算。</summary>
    public readonly struct WindSamplingContext
    {
        private readonly WeatherMgr weather;
        private readonly double surfaceGravity;
        private readonly float minimumMass;
        private readonly float maximumMass;
        private readonly WorldTopologyDomain topology;

        internal WindSamplingContext(WeatherMgr weather, double surfaceGravity, float minimumMass, float maximumMass)
        {
            this.weather = weather;
            this.surfaceGravity = surfaceGravity;
            this.minimumMass = minimumMass;
            this.maximumMass = maximumMass;
            topology = WorldTopologyRuntime.GetActiveDomain();
        }

        public Vector2 Sample(Vector2 position, float mass)
        {
            int required = WindEnvironmentRules.ResolveRequiredWindLevel(
                mass, surfaceGravity, minimumMass, maximumMass);
            // 沉重物品不用再查所在天气区域，也不进行不必要的坐标投影。
            if (required > WindEnvironmentRules.MaximumWindLevel)
                return Vector2.zero;
            return weather.SampleWindPushVelocityAt(topology.Normalize(position), required);
        }
    }

    public static bool TryCreateWindSamplingContext(out WindSamplingContext context)
    {
        context = default;
        WeatherMgr weather = WeatherMgr.ExistingInstance;
        if (weather == null || !WindEnvironmentRules.TryGetMassRange(out float minimumMass, out float maximumMass) ||
            !weather.TryGetWindSurfaceGravity(out double surfaceGravity))
            return false;
        context = new WindSamplingContext(weather, surfaceGravity, minimumMass, maximumMass);
        return true;
    }

    /// <summary>角色和载具共用有效表面流场；河流按权威流量区分快慢，平台与静水返回零速度。</summary>
    public static Vector2 SampleWaterVelocity(Vector2 position, float speed)
    {
        if (speed <= 0f) return Vector2.zero;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeWaterCurrent(position, out RuntimeWaterCurrentSample current))
            return Vector2.zero;

        return CalculateWaterCurrentVelocity(current, speed);
    }

    /// <summary>移动角色先确认当地确有水流，再查询物种或重量决定的真实推速。</summary>
    public static Vector2 SampleWaterVelocity(Vector2 position, Item actor)
    {
        if (actor == null) return Vector2.zero;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeWaterCurrent(position, out RuntimeWaterCurrentSample current))
            return Vector2.zero;

        return CalculateWaterCurrentVelocity(current, WaterCurrentPushConfigService.ResolvePushSpeed(actor));
    }

    #region 当前水流速度计算

    // 两种采样入口共用原流量公式，不改变河流、海洋和实验液体的推动强度。
    internal static Vector2 CalculateWaterCurrentVelocity(in RuntimeWaterCurrentSample current, float speed)
    {
        if (speed <= 0f) return Vector2.zero;
        float strength = current.Kind == RuntimeWaterCurrentKind.River
            ? WaterEnvironmentRules.ResolveRiverStrength(current.Flow)
            : Mathf.Clamp01(current.Flow);
        return current.Direction * (speed * strength);
    }

    #endregion

    /// <summary>按当地风级、物体总质量与星球重力读取分档推动速度。</summary>
    public static Vector2 SampleWindVelocity(Vector2 position, float mass)
    {
        // 全域没有八级以上的有效推动或渐弱尾段时，不读取目录质量、重力和拓扑。
        if (!HasWindPush)
            return Vector2.zero;
        return TryCreateWindSamplingContext(out WindSamplingContext context)
            ? context.Sample(position, mass) : Vector2.zero;
    }
    #endregion

    #region 散落物风推动

    private struct LooseWindRegistration
    {
        public Mod_Building Building;
        public WorldItemWaterRuntime WaterRuntime;
    }

    private static readonly Dictionary<Item, LooseWindRegistration> looseWindItems = new();
    private static readonly List<Item> looseWindScratch = new();

    /// <summary>完整散落物登记一次，移动模块和固定建筑不重复接管。</summary>
    public static void RegisterLooseWindItem(Item item)
    {
        if (item == null || looseWindItems.ContainsKey(item) || item.PersistenceOwner != null ||
            item.GetComponentInChildren<Mod_Mover>(true) != null ||
            item.GetComponentInChildren<Mod_Carrier>(true) != null)
            return;

        Mod_Building building = item.GetComponentInChildren<Mod_Building>(true);
        if (building?.IsInstalled() == true) return;
        // 物品组件在注册时绑定，风中每帧不再反复查找通常不存在的 MonoBehaviour。
        looseWindItems.Add(item, new LooseWindRegistration
        {
            Building = building,
            WaterRuntime = item.GetComponent<WorldItemWaterRuntime>()
        });
    }

    public static void UnregisterLooseWindItem(Item item) => looseWindItems.Remove(item);

    /// <summary>入水运行态按需创建时更新引用，避免复用物品持有旧水态。</summary>
    public static void BindLooseWindWaterRuntime(Item item, WorldItemWaterRuntime runtime)
    {
        if (item != null && looseWindItems.TryGetValue(item, out LooseWindRegistration registration))
        {
            // 值类型缓存修改后写回字典，避免每个散落物额外分配对象。
            registration.WaterRuntime = runtime;
            looseWindItems[item] = registration;
        }
    }

    /// <summary>沿风移动散落物并更新区块归属，完全沉没或仍在抛掷轨迹中时不受风。</summary>
    public static void TickLooseItemWind(float seconds)
    {
        if (!GameNetwork.HasStateAuthority || seconds <= 0f || looseWindItems.Count == 0 || !HasWindPush ||
            !TryCreateWindSamplingContext(out WindSamplingContext context))
            return;
        looseWindScratch.Clear();
        looseWindScratch.AddRange(looseWindItems.Keys);
        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        foreach (Item item in looseWindScratch)
        {
            if (!looseWindItems.TryGetValue(item, out LooseWindRegistration registration))
                continue;
            if (item == null || item.DestructionHandled || item.itemData?.Stack == null || item.itemData.inHand ||
                !item.itemData.Stack.CanBePickedUp || registration.Building?.IsInstalled() == true)
            {
                looseWindItems.Remove(item);
                continue;
            }
            if (!item.isActiveAndEnabled || Mod_Droping.IsDropInProgress(item) ||
                registration.WaterRuntime?.IsSubmerged == true)
                continue;
            Vector2 position = item.transform.position;
            Vector2 velocity = context.Sample(position, item.itemData.Stack.CurrentWeight);
            if (velocity.sqrMagnitude <= 0f) continue;
            Vector2 destination = ResolveLooseWindPosition(position, velocity * seconds, topology, chunks);
            if (((Vector2)topology.ShortestDelta(position, destination)).sqrMagnitude <= 0.00000001f) continue;
            Rigidbody2D body = item.GetComponent<Rigidbody2D>();
            if (body != null) body.position = destination;
            else item.transform.position = new Vector3(destination.x, destination.y, item.transform.position.z);
            ItemWorldPlacement.TryAttachWorldModelTransientItem(item, destination);
            ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
            WorldItemWaterSystem.ScheduleSpawnCheck(item);
        }
        looseWindScratch.Clear();
    }

    /// <summary>散落物按小步检查已加载地形与建筑阻挡，不因风推动请求新地形。</summary>
    public static Vector2 ResolveLooseWindPosition(Vector2 position, Vector2 displacement)
        => ResolveLooseWindPosition(position, displacement,
            WorldTopologyRuntime.GetActiveDomain(), ChunkMgr.ExistingInstance);

    internal static Vector2 ResolveLooseWindPosition(Vector2 position, Vector2 displacement,
        WorldTopologyDomain topology, ChunkMgr chunks)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(displacement.magnitude / 0.2f));
        Vector2 step = displacement / steps;
        if (chunks == null) return position;
        for (int index = 0; index < steps; index++)
        {
            // 同一批风推动沿用冻结的拓扑域，逐小步仍检查真实地形与建筑占地。
            Vector2 next = topology.Normalize(position + step);
            if (!chunks.TryGetRuntimeTerrainTile(next, out RuntimeTerrainTileSample sample) ||
                !BuildingOccupancyRegistry.GetEffectiveWalkable(sample.WorldCell, sample.Cell.IsWalkable))
                break;
            position = next;
        }
        return position;
    }

    #endregion

    #region 主动推动意图
    /// <summary>推动取推动者的当前环境移速，和乘船自身的划行限速是两套独立配置。</summary>
    public static Vector2 CalculatePushVelocity(Vector2 currentMoveVelocity, bool sourceInWater, float landMultiplier)
        => currentMoveVelocity * (sourceInWater ? 1f : landMultiplier);

    #endregion
}
