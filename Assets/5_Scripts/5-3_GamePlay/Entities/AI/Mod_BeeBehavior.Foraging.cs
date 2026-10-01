using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>按九宫格区块选择可采蜜植物，花和作物的恢复速度由可替换配置决定。</summary>
public sealed partial class Mod_BeeBehavior
{
    #region 采蜜目标
    private readonly List<Item> forageCandidates = new(); // ItemMgr 空间查询缓冲。
    private readonly HashSet<Item> forageDedupe = new(); // 循环世界镜像去重。
    private readonly HashSet<Vector2Int> forageChunks = new(); // 当前九宫格区块。
    private Item forageCrop; // 有 BeeForage.Crop 标签的植株。
    private int forageFlowerGuid; // 无 Item 地表花朵的生成点身份。
    private int forageEntityGuid; // ECS 作物的稳定身份，不为采蜜创建资源 GameObject。
    private Vector2 foragePosition;
    private float forageRate;
    private float forageScanRemaining;
    private bool HasActiveForageTarget => forageCrop != null || forageFlowerGuid != 0 || forageEntityGuid != 0;

    /// <summary>清空短期采蜜、移动和攻击目标，存档只保存行为数值。</summary>
    private void ResetBeeTargets()
    {
        ClearForageTarget();
        ResetPatrol();
        forageScanRemaining = 0f;
        ResetBeeCombat();
    }

    /// <summary>低于八百才主动寻蜜；每次采蜜动作结束后单独判定是否返巢。</summary>
    private void TickForaging(float flightSeconds, float gameSeconds)
    {
        if (HasActiveForageTarget && !IsForageTargetValid())
            CompleteForageVisit();
        if (state.ReturningHome)
        {
            TickReturnHome(flightSeconds);
            return;
        }
        if (!HasActiveForageTarget)
        {
            if (state.Satiety >= ForageBelow)
            {
                TickTerritoryPatrol(flightSeconds, PatrolFlightSpeedMultiplier);
                return;
            }
            forageScanRemaining -= gameSeconds;
            if (forageScanRemaining > 0f)
            {
                TickTerritoryPatrol(flightSeconds, PatrolFlightSpeedMultiplier);
                return;
            }
            forageScanRemaining = ForageScanInterval;
            if (!TrySelectForageTarget())
            {
                TickHoneyMeal(flightSeconds);
                return;
            }
        }

        if (WorldTopologyRuntime.SqrDistance(item.transform.position, foragePosition) >
            ForageLandingDistance * ForageLandingDistance)
        {
            bird.FlyTo(foragePosition, flightSeconds);
            return;
        }
        if (!Mod_AI_Bird.CanLand(item.transform.position))
        {
            CompleteForageVisit();
            return;
        }
        bird.SetPilotGrounded(this, true);
        state.Satiety = Mathf.Min(SatietyMaximum, state.Satiety + forageRate * gameSeconds);
        if (state.Satiety >= SatietyMaximum)
            CompleteForageVisit();
    }

    /// <summary>采蜜结束后按当前蜜蜂的饱食度严格大于一千才返巢。</summary>
    private void CompleteForageVisit()
    {
        ClearForageTarget();
        if (state.Satiety > ReturnAbove)
            state.ReturningHome = true;
    }

    /// <summary>返巢后扣除五百饱食度并向蜂巢交付一单位蜂蜜。</summary>
    private void TickReturnHome(float flightSeconds)
    {
        Vector2 home = colony.HomePosition;
        if (WorldTopologyRuntime.SqrDistance(item.transform.position, home) >
            HomeArrivalDistance * HomeArrivalDistance)
        {
            bird.FlyTo(home, flightSeconds);
            return;
        }
        state.Satiety = Mathf.Max(0f, state.Satiety - HoneyContributionCost);
        colony.ReceiveHoney();
        state.ReturningHome = false;
    }

    /// <summary>附近没有采蜜目标时回巢领取库存，每点蜂蜜补五百饱食度。</summary>
    private void TickHoneyMeal(float flightSeconds)
    {
        Vector2 home = colony.HomePosition;
        if (WorldTopologyRuntime.SqrDistance(item.transform.position, home) >
            HomeArrivalDistance * HomeArrivalDistance)
        {
            bird.FlyTo(home, flightSeconds);
            return;
        }
        if (colony.TryConsumeHoney())
            state.Satiety = Mathf.Min(SatietyMaximum, state.Satiety + HoneyMealGain);
        else
            TickTerritoryPatrol(flightSeconds, PatrolFlightSpeedMultiplier);
    }

    /// <summary>搜索自身区块及八个相邻区块，不按植物当前果实库存过滤。</summary>
    private bool TrySelectForageTarget()
    {
        forageChunks.Clear();
        Vector2 size = ChunkMgr.GetChunkSize();
        Vector2Int origin = Chunk.GetChunkPosition(item.transform.position);
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        float nearest = float.PositiveInfinity;
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                Vector2 center = WorldTopologyRuntime.NormalizePosition(new Vector2(
                    origin.x + (x + 0.5f) * size.x, origin.y + (y + 0.5f) * size.y));
                forageChunks.Add(Chunk.GetChunkPosition(center));
                if (chunkManager.TryGetRuntimeTerrainTile(center, out RuntimeTerrainTileSample sample) &&
                    chunkManager.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk))
                    ConsiderGroundFlowers(chunk, chunkManager, ref nearest);
            }
        }

        float radius = Mathf.Sqrt(4f * size.x * size.x + 4f * size.y * size.y);
        if (NaturalEntityEcsService.TryFindForage(item.transform.position, radius, CropNectarTag,
            CanReachEntityForage, out Vector2 entityPosition, out int entityGuid))
        {
            float distance = WorldTopologyRuntime.SqrDistance(item.transform.position, entityPosition);
            if (distance < nearest)
            {
                nearest = distance;
                forageCrop = null;
                forageFlowerGuid = 0;
                forageEntityGuid = entityGuid;
                foragePosition = entityPosition;
                forageRate = CropGainPerSecond;
            }
        }
        ItemMgr.Instance.QueryItemsInCircleNonAlloc(item.transform.position, radius, ~0, item,
            forageCandidates, forageDedupe);
        foreach (Item candidate in forageCandidates)
        {
            if (candidate == null || candidate.DestructionHandled || !candidate.gameObject.activeInHierarchy ||
                !forageChunks.Contains(Chunk.GetChunkPosition(candidate.transform.position)) ||
                candidate.itemData?.Tags == null || !candidate.itemData.Tags.Contains(CropNectarTag) ||
                candidate.itemMods?.GetMod_ByID<Mod_Crop>(ModText.Crop) == null ||
                !Mod_AI_Bird.CanLand(candidate.transform.position))
                continue;
            float distance = WorldTopologyRuntime.SqrDistance(item.transform.position, candidate.transform.position);
            if (distance >= nearest)
                continue;
            nearest = distance;
            forageCrop = candidate;
            forageFlowerGuid = 0;
            forageEntityGuid = 0;
            foragePosition = candidate.transform.position;
            forageRate = CropGainPerSecond;
        }

        if (!HasActiveForageTarget)
            return false;
        return true;
    }

    private bool CanReachEntityForage(Vector2 position) =>
        forageChunks.Contains(Chunk.GetChunkPosition(position)) && Mod_AI_Bird.CanLand(position);

    /// <summary>从无 Item 的地表花层读取仍可见、未被采走的花朵生成点。</summary>
    private void ConsiderGroundFlowers(ChunkRuntime chunk, ChunkMgr manager, ref float nearest)
    {
        IReadOnlyList<NaturalItemPlacement> placements = chunk.Ecology?.Placements;
        if (placements == null)
            return;
        for (int i = 0; i < placements.Count; i++)
        {
            NaturalItemPlacement placement = placements[i];
            if (!GameRes.Instance.TryGetItemDefinition(placement.ItemId, out RuntimeItemDefinition definition) ||
                !definition.IsGroundCover || !definition.HasTag(FlowerNectarTag) ||
                manager.IsNaturalItemRemoved(chunk.Address, placement.Guid) ||
                !GroundCoverSystem.TryFindAt(chunk, placement.LocalX, placement.LocalY, manager,
                    out GroundCoverTarget visible) || visible.Placement.Guid != placement.Guid)
                continue;
            Vector2 position = new GroundCoverTarget(chunk, placement, definition).WorldPosition;
            if (!Mod_AI_Bird.CanLand(position))
                continue;
            float distance = WorldTopologyRuntime.SqrDistance(item.transform.position, position);
            if (distance >= nearest)
                continue;
            nearest = distance;
            forageCrop = null;
            forageFlowerGuid = placement.Guid;
            forageEntityGuid = 0;
            foragePosition = WorldTopologyRuntime.NormalizePosition(position);
            forageRate = FlowerGainPerSecond;
        }
    }

    /// <summary>采蜜期间持续核实源头仍属于原植株或地表花生成点。</summary>
    private bool IsForageTargetValid()
    {
        if (forageEntityGuid != 0)
            return NaturalEntityEcsService.IsForageAvailable(foragePosition, forageEntityGuid, CropNectarTag);
        if (forageCrop != null)
            return !forageCrop.DestructionHandled && forageCrop.gameObject.activeInHierarchy &&
                forageCrop.itemData?.Tags?.Contains(CropNectarTag) == true;
        return GroundCoverSystem.TryResolve(foragePosition, out GroundCoverTarget flower) &&
            flower.Placement.Guid == forageFlowerGuid && flower.Definition.HasTag(FlowerNectarTag);
    }

    /// <summary>切换状态时仅抛弃临时采蜜目标，不改变本蜂饱食度。</summary>
    private void ClearForageTarget()
    {
        forageCrop = null;
        forageFlowerGuid = 0;
        forageEntityGuid = 0;
        foragePosition = default;
        forageRate = 0f;
    }
    #endregion
}
