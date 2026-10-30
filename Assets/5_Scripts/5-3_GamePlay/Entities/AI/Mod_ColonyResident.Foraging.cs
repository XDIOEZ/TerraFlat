using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>按九宫格区块选择可采蜜植物，花和作物的恢复速度由可替换配置决定。</summary>
public sealed partial class Mod_ColonyResident
{
    #region 采蜜目标
    private static readonly Dictionary<(FlatWorld.WorldModel.WorldAddress Address, int Guid), Mod_ColonyResident>
        flowerReservations = new(); // 地表花采蜜占位，同一朵花只允许一只蜜蜂锁定。
    private readonly List<Item> forageCandidates = new(); // ItemMgr 空间查询缓冲。
    private readonly HashSet<Item> forageDedupe = new(); // 循环世界镜像去重。
    private readonly HashSet<Vector2Int> forageChunks = new(); // 当前九宫格区块。
    private Item forageCrop; // 有 BeeForage.Crop 标签的植株。
    private int forageFlowerGuid; // 无 Item 地表花朵的生成点身份。
    private FlatWorld.WorldModel.WorldAddress forageFlowerAddress; // 地表花所属区块地址。
    private int forageEntityGuid; // ECS 作物的稳定身份，不为采蜜创建资源 GameObject。
    private Vector2 foragePosition;
    private float forageRate;
    private float forageScanRemaining;
    private bool noForageAvailable;
    private bool HasActiveForageTarget => forageCrop != null || forageFlowerGuid != 0 || forageEntityGuid != 0;

    /// <summary>清空短期采蜜、移动和攻击目标，存档只保存行为数值。</summary>
    private void ResetBeeTargets()
    {
        ClearForageTarget();
        ResetPatrol();
        forageScanRemaining = 0f;
        noForageAvailable = false;
        ResetBeeCombat();
    }

    /// <summary>搜索并采集一个来源；找不到时交给树选择下一行为。</summary>
    public AnimalBehaviorTreeStatus TickColonyForageAction(float deltaTime)
    {
        if (HasActiveForageTarget && !IsForageTargetValid()) CompleteForageVisit();
        if (state.ReturningHome) return AnimalBehaviorTreeStatus.Success;
        if (!HasActiveForageTarget)
        {
            if (state.Satiety >= ForageBelow || forageScanRemaining > 0f)
                return AnimalBehaviorTreeStatus.Failure;
            forageScanRemaining = ForageScanInterval;
            if (!TrySelectForageTarget())
            {
                noForageAvailable = true;
                return AnimalBehaviorTreeStatus.Failure;
            }
            noForageAvailable = false;
        }
        if (WorldTopologyRuntime.SqrDistance(item.transform.position, foragePosition) >
            ForageLandingDistance * ForageLandingDistance)
        {
            bird.FlyTo(foragePosition, deltaTime);
            return AnimalBehaviorTreeStatus.Running;
        }
        if (!Mod_AnimalFlightMovement.CanLand(item.transform.position))
        {
            CompleteForageVisit();
            return AnimalBehaviorTreeStatus.Success;
        }
        bird.SetPermanentFlightGrounded(true);
        state.Satiety = Mathf.Min(SatietyMaximum, state.Satiety + forageRate * deltaTime);
        if (state.Satiety < SatietyMaximum) return AnimalBehaviorTreeStatus.Running;
        CompleteForageVisit();
        return AnimalBehaviorTreeStatus.Success;
    }

    private void CompleteForageVisit()
    {
        ClearForageTarget();
        if (state.Satiety > ReturnAbove) state.ReturningHome = true;
    }

    public AnimalBehaviorTreeStatus TickColonyReturnHoney(float deltaTime)
    {
        Vector2 home = colony.HomePosition;
        if (WorldTopologyRuntime.SqrDistance(item.transform.position, home) > HomeArrivalDistance * HomeArrivalDistance)
        {
            bird.FlyTo(home, deltaTime);
            return AnimalBehaviorTreeStatus.Running;
        }
        state.Satiety = Mathf.Max(0f, state.Satiety - HoneyContributionCost);
        colony.ReceiveHoney();
        state.ReturningHome = false;
        return AnimalBehaviorTreeStatus.Success;
    }

    public AnimalBehaviorTreeStatus TickColonyMeal(float deltaTime)
    {
        Vector2 home = colony.HomePosition;
        if (WorldTopologyRuntime.SqrDistance(item.transform.position, home) > HomeArrivalDistance * HomeArrivalDistance)
        {
            bird.FlyTo(home, deltaTime);
            return AnimalBehaviorTreeStatus.Running;
        }
        noForageAvailable = false;
        if (!colony.TryConsumeHoney()) return AnimalBehaviorTreeStatus.Failure;
        state.Satiety = Mathf.Min(SatietyMaximum, state.Satiety + HoneyMealGain);
        return AnimalBehaviorTreeStatus.Success;
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
        if (NaturalEntityEcsService.TryFindForage(item.transform.position, radius, cropResourceTag,
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
                candidate.itemData?.Tags == null || !candidate.itemData.Tags.Contains(cropResourceTag) ||
                candidate.itemMods?.GetMod_ByID<Mod_Crop>(ModText.Crop) == null ||
                !Mod_AnimalFlightMovement.CanLand(candidate.transform.position))
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
        if (forageFlowerGuid != 0 && !TryReserveFlower())
        {
            ClearForageTarget();
            return false;
        }
        return true;
    }

    private bool CanReachEntityForage(Vector2 position) =>
        forageChunks.Contains(Chunk.GetChunkPosition(position)) && Mod_AnimalFlightMovement.CanLand(position);

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
                !definition.IsGroundCover || !definition.HasTag(groundResourceTag) ||
                manager.IsNaturalItemRemoved(chunk.Address, placement.Guid) ||
                !GroundCoverSystem.TryFindAt(chunk, placement.LocalX, placement.LocalY, manager,
                    out GroundCoverTarget visible) || visible.Placement.Guid != placement.Guid ||
                IsFlowerReservedByOther(chunk.Address, placement.Guid))
                continue;
            Vector2 position = new GroundCoverTarget(chunk, placement, definition).WorldPosition;
            if (!Mod_AnimalFlightMovement.CanLand(position))
                continue;
            float distance = WorldTopologyRuntime.SqrDistance(item.transform.position, position);
            if (distance >= nearest)
                continue;
            nearest = distance;
            forageCrop = null;
            forageFlowerGuid = placement.Guid;
            forageFlowerAddress = chunk.Address;
            forageEntityGuid = 0;
            foragePosition = WorldTopologyRuntime.NormalizePosition(position);
            forageRate = FlowerGainPerSecond;
        }
    }

    /// <summary>采蜜期间持续核实源头仍属于原植株或地表花生成点。</summary>
    private bool IsForageTargetValid()
    {
        if (forageEntityGuid != 0)
            return NaturalEntityEcsService.IsForageAvailable(foragePosition, forageEntityGuid, cropResourceTag);
        if (forageCrop != null)
            return !forageCrop.DestructionHandled && forageCrop.gameObject.activeInHierarchy &&
                forageCrop.itemData?.Tags?.Contains(cropResourceTag) == true;
        return OwnsFlowerReservation() &&
            GroundCoverSystem.TryResolve(foragePosition, out GroundCoverTarget flower) &&
            flower.Placement.Guid == forageFlowerGuid && flower.Definition.HasTag(groundResourceTag);
    }

    /// <summary>查询地表花是否已被其它蜜蜂锁定，避免多只蜜蜂堆在同一朵花上。</summary>
    private bool IsFlowerReservedByOther(FlatWorld.WorldModel.WorldAddress address, int guid)
    {
        return TryGetFlowerReservation(address, guid, out Mod_ColonyResident owner) &&
            !ReferenceEquals(owner, this);
    }

    /// <summary>选中地表花后立即占位，直到离开、失效或卸载时释放。</summary>
    private bool TryReserveFlower()
    {
        var key = (forageFlowerAddress, forageFlowerGuid);
        if (TryGetFlowerReservation(forageFlowerAddress, forageFlowerGuid,
                out Mod_ColonyResident owner) &&
            !ReferenceEquals(owner, this))
            return false;
        flowerReservations[key] = this;
        return true;
    }

    /// <summary>采蜜过程中持续确认当前蜜蜂仍持有这朵花的占位。</summary>
    private bool OwnsFlowerReservation()
    {
        return forageFlowerGuid != 0 &&
            TryGetFlowerReservation(forageFlowerAddress, forageFlowerGuid,
                out Mod_ColonyResident owner) &&
            ReferenceEquals(owner, this);
    }

    /// <summary>自动清理已经卸载或切换目标的旧占位，避免静态表残留。</summary>
    private bool TryGetFlowerReservation(FlatWorld.WorldModel.WorldAddress address, int guid,
        out Mod_ColonyResident owner)
    {
        var key = (address, guid);
        if (!flowerReservations.TryGetValue(key, out owner))
            return false;
        bool stale = owner == null || owner.forageFlowerGuid != guid ||
            !owner.forageFlowerAddress.Equals(address) || owner.item == null ||
            owner.item.DestructionHandled || !owner.item.gameObject.activeInHierarchy;
        if (!stale)
            return true;
        flowerReservations.Remove(key);
        owner = null;
        return false;
    }

    /// <summary>只释放自己持有的花朵占位，避免误删其它蜜蜂的新占位。</summary>
    private void ReleaseFlowerReservation()
    {
        if (forageFlowerGuid == 0)
            return;
        var key = (forageFlowerAddress, forageFlowerGuid);
        if (flowerReservations.TryGetValue(key, out Mod_ColonyResident owner) &&
            ReferenceEquals(owner, this))
            flowerReservations.Remove(key);
    }

    /// <summary>切换状态时仅抛弃临时采蜜目标，不改变本蜂饱食度。</summary>
    private void ClearForageTarget()
    {
        ReleaseFlowerReservation();
        forageCrop = null;
        forageFlowerGuid = 0;
        forageFlowerAddress = default;
        forageEntityGuid = 0;
        foragePosition = default;
        forageRate = 0f;
    }
    #endregion
}
