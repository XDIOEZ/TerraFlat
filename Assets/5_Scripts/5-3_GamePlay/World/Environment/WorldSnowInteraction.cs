using System;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>积雪工具共用选格与权威事务，一层雪对应一个雪球。</summary>
public static class WorldSnowInteraction
{
    #region 雪厚与编辑快照
    public const string SnowballId = "Snowball";
    public const string EditedLayer = "snow.player.edited";
    public const string DepthLayer = "snow.player.depth";
    public const string SeasonLayer = "snow.player.season";

    public static int GetLayerCount(RuntimeTerrainTileSample sample)
    {
        GetContext(out SnowCoverState snow, out float offset);
        return Mathf.RoundToInt(WorldSnowSystem.GetSurfaceDepth(sample.Terrain,
            sample.LocalCell.x, sample.LocalCell.y, snow, offset) * SnowDepthLayer.LayerCount);
    }

    private static void GetContext(out SnowCoverState snow, out float offset)
    {
        snow = null;
        offset = 0f;
        WeatherMgr.ExistingInstance?.TryGetSnowCoverageContext(out snow, out offset);
    }

    public static SnowCellSaveData Capture(RuntimeTerrainTileSample sample)
        => Capture(sample.Terrain, sample.LocalCell);

    public static SnowCellSaveData Capture(ChunkTerrainData terrain, Vector2Int local)
    {
        terrain.TryGetEnvironmentValue(EditedLayer, local.x, local.y, out float edited);
        terrain.TryGetEnvironmentValue(DepthLayer, local.x, local.y, out float depth);
        terrain.TryGetEnvironmentValue(SeasonLayer, local.x, local.y, out float season);
        return new SnowCellSaveData { LocalPosition = local, Edited = edited > 0f,
            Depth = depth, SeasonalDepth = season,
            WeatherDepth = WorldSnowSystem.GetWeatherDepth(terrain, local.x, local.y) };
    }

    public static void Restore(ChunkTerrainData terrain, SnowCellSaveData state)
    {
        var local = state.LocalPosition;
        if ((uint)local.x >= (uint)terrain.Width || (uint)local.y >= (uint)terrain.Height ||
            !IsFinite(state.Depth) || state.Depth < 0f || !IsFinite(state.SeasonalDepth) ||
            state.SeasonalDepth < 0f || state.SeasonalDepth > 1f ||
            !IsFinite(state.WeatherDepth) || state.WeatherDepth < 0f || state.WeatherDepth > 1f)
            throw new InvalidOperationException("积雪差量的坐标或厚度无效。");
        terrain.SetEnvironmentValue(DepthLayer, local.x, local.y, state.Depth);
        terrain.SetEnvironmentValue(SeasonLayer, local.x, local.y, state.SeasonalDepth);
        terrain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, local.x, local.y, state.WeatherDepth);
        terrain.SetEnvironmentValue(EditedLayer, local.x, local.y, state.Edited ? 1f : 0f);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void WriteLayers(RuntimeTerrainTileSample sample, int layers)
    {
        GetContext(out SnowCoverState snow, out float offset);
        float seasonalDepth = 0f;
        if (snow != null && sample.Terrain.TryGetEnvironmentValue("temperature.celsius",
                sample.LocalCell.x, sample.LocalCell.y, out float temperature))
            seasonalDepth = snow.Sample(temperature + offset);
        // 编辑后的剩余雪统一归玩家层，清空天气层让新降雪继续积累。
        Restore(sample.Terrain, new SnowCellSaveData { LocalPosition = sample.LocalCell,
            Edited = true, Depth = layers * SnowDepthLayer.LayerStep, SeasonalDepth = seasonalDepth,
            WeatherDepth = 0f });
        SaveDataMgr.Instance.RecordSnowCell(sample);
    }

    private static void Rollback(RuntimeTerrainTileSample sample, SnowCellSaveData state)
    {
        Restore(sample.Terrain, state);
        SaveDataMgr.Instance.RecordSnowCell(sample);
    }
    #endregion

    #region 选格
    public static bool TryResolveTarget(Item held, float reach, out RuntimeTerrainTileSample sample, out string reason)
    {
        sample = default;
        reason = null;
        if (!GameNetwork.HasStateAuthority || held == null || !held.InHand || held.DestructionHandled ||
            held.Owner is not Player actor || !actor.IsLocalProfile || actor.DestructionHandled ||
            !(actor.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp > 0f))
        {
            reason = "当前无法操作积雪。";
            return false;
        }
        var controller = actor.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        var manager = ChunkMgr.ExistingInstance;
        if (controller == null || controller.IsGameplayInputLocked ||
            (!controller.IsUsingMobile && controller.IsPointerOverUI()))
            return false;
        if (manager == null || SaveDataMgr.Instance?.SaveData == null ||
            !manager.TryGetRuntimeTerrainTile(controller.GetMouseWorldPosition(), out sample))
        {
            reason = "目标地块尚未加载。";
            return false;
        }
        if (!FarmlandSystem.IsWithinReach(actor.transform.position, sample.WorldCell, reach))
        {
            reason = "距离地块太远。";
            return false;
        }
        var surface = TerrainSupportLayer.GetSurfaceCell(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y);
        if (surface.GroundTileId == 0 || surface.BlockingTileId != 0 ||
            (surface.Flags & (TerrainCellFlags.Blocking | TerrainCellFlags.Occupied)) != 0 ||
            BuildingOccupancyRegistry.IsOccupied(sample.WorldCell) ||
            WorldLiquidSystem.GetSurfaceDepth(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) > 0f)
        {
            reason = "只能在没有建筑的干燥地面上操作积雪。";
            return false;
        }
        return true;
    }
    #endregion

    #region 铲雪与铺雪
    public static bool TryShovel(Item held, float reach, out Vector2Int cell, out string reason)
    {
        cell = default;
        if (!TryResolveTarget(held, reach, out var sample, out reason)) return false;
        int layers = GetLayerCount(sample);
        if (layers <= 0) { reason = "这里没有积雪。"; return false; }
        var previous = Capture(sample);
        var drop = DroppedItemService.SpawnLoot(SnowballId, (Vector2)sample.WorldCell + Vector2.one * 0.5f, layers);
        if (!drop.IsValid) { reason = "雪球生成失败。"; return false; }
        try { WriteLayers(sample, 0); }
        catch (Exception exception)
        {
            DroppedItemService.Remove(drop);
            Rollback(sample, previous);
            Debug.LogError($"[积雪] 铲雪保存失败：{exception}");
            reason = "铲雪保存失败。";
            return false;
        }
        cell = sample.WorldCell;
        return true;
    }

    public static bool TryPlace(Item held, float reach, out string reason)
    {
        if (!TryResolveTarget(held, reach, out var sample, out reason)) return false;
        var hotbar = held.Owner.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
        var slot = hotbar?.CurrentSelectItemSlot;
        var data = slot?.itemData;
        if (slot == null || hotbar.Data?.itemSlots == null || !hotbar.Data.itemSlots.Contains(slot) ||
            data?.IDName != SnowballId || held.itemData?.IDName != SnowballId ||
            !(data.Stack?.Amount >= 1f) ||
            !(ReferenceEquals(data, held.itemData) || (data.Guid != 0 && data.Guid == held.itemData.Guid)))
        {
            reason = "当前手持槽没有可用的雪球。";
            return false;
        }
        var previous = Capture(sample);
        float previousAmount = data.Stack.Amount;
        try
        {
            WriteLayers(sample, checked(GetLayerCount(sample) + 1));
            // 地格保存成功后才从真实快捷栏扣除，扣料失败恢复雪厚。
            if (!hotbar.Data.TryConsumeFromSlot(slot, 1, out _))
            {
                Rollback(sample, previous);
                reason = "雪球扣除失败。";
                return false;
            }
        }
        catch (Exception exception)
        {
            hotbar.Data.TrySetSlotItemAmount(slot, data, previousAmount);
            Rollback(sample, previous);
            Debug.LogError($"[积雪] 铺雪提交失败：{exception}");
            reason = "铺雪保存失败。";
            return false;
        }
        hotbar.RefreshUI(hotbar.Data.itemSlots.IndexOf(slot));
        hotbar.NotifyOwnerNetworkStateChanged();
        hotbar.RuntimeInventory.SyncHeldItemImmediately();
        return true;
    }
    #endregion
}
