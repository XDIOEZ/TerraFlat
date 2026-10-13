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
    /// <summary>角色和载具共用有效表面流场；河流按权威流量区分快慢，平台与静水返回零速度。</summary>
    public static Vector2 SampleWaterVelocity(Vector2 position, float speed)
    {
        if (speed <= 0f) return Vector2.zero;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeWaterCurrent(position, out RuntimeWaterCurrentSample current))
            return Vector2.zero;

        float strength = current.Kind == RuntimeWaterCurrentKind.River
            ? WaterEnvironmentRules.ResolveRiverStrength(current.Flow)
            : Mathf.Clamp01(current.Flow);
        return current.Direction * (speed * strength);
    }

    /// <summary>按当地风级、物体总质量与星球重力读取分档推动速度。</summary>
    public static Vector2 SampleWindVelocity(Vector2 position, float mass)
    {
        if (!WindEnvironmentRules.TryGetMassRange(out float minimumMass, out float maximumMass))
            return Vector2.zero;
        WeatherMgr weather = WeatherMgr.ExistingInstance;
        if (weather == null || !weather.TryGetWindSurfaceGravity(out double surfaceGravity))
            return Vector2.zero;
        int requiredWindLevel = WindEnvironmentRules.ResolveRequiredWindLevel(mass, surfaceGravity, minimumMass, maximumMass);
        if (requiredWindLevel > WindEnvironmentRules.MaximumWindLevel ||
            !weather.TryGetWindBlendAt(WorldLocalPresentation.ToLogical(position), out var sample))
            return Vector2.zero;

        // 每区先按自身风级裁定能否推动，再沿空间边缘混合速度向量。
        Vector2 ResolveSourceVelocity(Unity.Mathematics.float3 wind)
        {
            Vector2 vector = new(wind.x, wind.y);
            float speed = WindEnvironmentRules.ResolvePushSpeed(wind.z, requiredWindLevel);
            return vector.normalized * speed;
        }

        return Vector2.Lerp(
            Vector2.Lerp(ResolveSourceVelocity(sample.Origin), ResolveSourceVelocity(sample.Horizontal), sample.HorizontalWeight),
            Vector2.Lerp(ResolveSourceVelocity(sample.Vertical), ResolveSourceVelocity(sample.Diagonal), sample.HorizontalWeight),
            sample.VerticalWeight);
    }
    #endregion

    #region 散落物风推动

    private static readonly HashSet<Item> looseWindItems = new();
    private static readonly List<Item> looseWindScratch = new();

    /// <summary>完整散落物登记一次，移动模块和固定建筑不重复接管。</summary>
    public static void RegisterLooseWindItem(Item item)
    {
        if (item != null && item.PersistenceOwner == null &&
            item.GetComponentInChildren<Mod_Mover>(true) == null &&
            item.GetComponentInChildren<Mod_Carrier>(true) == null &&
            item.GetComponentInChildren<Mod_Building>(true)?.IsInstalled() != true)
            looseWindItems.Add(item);
    }

    public static void UnregisterLooseWindItem(Item item) => looseWindItems.Remove(item);

    /// <summary>沿风移动散落物并更新区块归属，完全沉没或仍在抛掷轨迹中时不受风。</summary>
    public static void TickLooseItemWind(float seconds)
    {
        if (!GameNetwork.HasStateAuthority || seconds <= 0f || looseWindItems.Count == 0 || !HasWindPush)
            return;
        looseWindScratch.Clear();
        looseWindScratch.AddRange(looseWindItems);
        foreach (Item item in looseWindScratch)
        {
            if (item == null || item.DestructionHandled || item.itemData?.Stack == null || item.itemData.inHand ||
                !item.itemData.Stack.CanBePickedUp || item.GetComponentInChildren<Mod_Building>(true)?.IsInstalled() == true)
            {
                looseWindItems.Remove(item);
                continue;
            }
            if (!item.isActiveAndEnabled || Mod_Droping.IsDropInProgress(item) ||
                item.GetComponent<WorldItemWaterRuntime>()?.IsSubmerged == true)
                continue;
            Vector2 position = item.transform.position;
            Vector2 velocity = SampleWindVelocity(position, item.itemData.Stack.CurrentWeight);
            if (velocity.sqrMagnitude <= 0f) continue;
            Vector2 destination = ResolveLooseWindPosition(position, velocity * seconds);
            if (WorldTopologyRuntime.ShortestDelta(position, destination).sqrMagnitude <= 0.00000001f) continue;
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
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(displacement.magnitude / 0.2f));
        Vector2 step = displacement / steps;
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (chunks == null) return position;
        for (int index = 0; index < steps; index++)
        {
            Vector2 next = WorldTopologyRuntime.NormalizePosition(position + step);
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
