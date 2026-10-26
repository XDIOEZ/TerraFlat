using System;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestRuntimeProbe
{
    #region 容器整份落地现场
    // 真实冷库存容器调用正式倒液与装液入口，测试结束归还原地表液体。
    private static JObject CaptureVesselWholePour(Player player, JObject args)
    {
        RuntimeTerrainTileSample sample = default;
        int offset = 0;
        bool found = false;
        foreach (int candidate in new[] { 0, -1, 1 })
        {
            Vector2 position = (Vector2)player.transform.position + Vector2.right * candidate;
            if (!ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(position, out var current) ||
                current.Cell.BlockingTileId != 0 || FarmlandSystem.IsFarmland(current.Cell) ||
                TerrainSupportLayer.GetTileId(current.Terrain, current.LocalCell.x, current.LocalCell.y) != 0) continue;
            sample = current; offset = candidate; found = true; break;
        }
        if (!found) throw new InvalidOperationException("整份倒液测试缺少玩家脚下或左右的普通地表格。");
        int beforeLevel = sample.Terrain.GetLiquidLevel(sample.LocalCell.x, sample.LocalCell.y);
        string beforeId = sample.Terrain.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y);
        string water = FluidCatalog.Default.Find(FluidIds.PureWater).LiquidId;
        string salt = FluidCatalog.Default.Find("core:salt_water").LiquidId;
        ItemData item = CreateDragItem("WoodenBarrel");
        Inventory inventory = CreateDragInventory("lua.vessel.whole-pour", item);
        var vessel = new ColdInventoryLiquidVessel(item, inventory, player);
        var result = new JObject();
        try
        {
            MixedLiquidContents.Clear(vessel.Data);
            MixedLiquidContents.Add(vessel.Data, water, 100.5f, 20f);
            MixedLiquidContents.Add(vessel.Data, salt, .5f, 20f);
            vessel.CommitVessel();
            SetVesselPourCell(sample, water, 100);
            result["blockedRemoved"] = LiquidVesselOperations.PourToGround(vessel, player, 40f, offset);
            result["blocked"] = VesselPourSample(vessel, sample, water, salt);
            SetVesselPourCell(sample, water, 0);
            result["fractionRemoved"] = LiquidVesselOperations.PourToGround(vessel, player, 20f, offset);
            result["fraction"] = VesselPourSample(vessel, sample, water, salt);
            result["firstRemoved"] = LiquidVesselOperations.PourToGround(vessel, player, 40f, offset);
            result["first"] = VesselPourSample(vessel, sample, water, salt);
            result["secondRemoved"] = LiquidVesselOperations.PourToGround(vessel, player, 108f, offset);
            result["second"] = VesselPourSample(vessel, sample, water, salt);
            result["tailRemoved"] = LiquidVesselOperations.PourToGround(vessel, player, 40f, offset);
            result["tail"] = VesselPourSample(vessel, sample, water, salt);

            MixedLiquidContents.Clear(vessel.Data);
            MixedLiquidContents.Add(vessel.Data, water, vessel.Capacity - .5f, 20f);
            vessel.CommitVessel();
            Vector2 sourcePosition = (Vector2)player.transform.position + Vector2.right * offset;
            if (!WorldLiquidSourceResolver.TryResolve(sourcePosition, out var source))
                throw new InvalidOperationException("整份装液测试无法定位已倒出的真实地表水。");
            result["halfSpaceFilled"] = LiquidVesselOperations.FillFromWorld(vessel, player, source);
            result["halfSpace"] = VesselPourSample(vessel, sample, water, salt);
            MixedLiquidContents.Clear(vessel.Data);
            MixedLiquidContents.Add(vessel.Data, water, vessel.Capacity - 60f, 20f);
            vessel.CommitVessel();
            result["wholeSpaceFilled"] = LiquidVesselOperations.FillFromWorld(vessel, player, source);
            result["wholeSpace"] = VesselPourSample(vessel, sample, water, salt);
            result["capacity"] = vessel.Capacity;
        }
        finally
        {
            SetVesselPourCell(sample, beforeId, beforeLevel);
            result["restoredLevel"] = sample.Terrain.GetLiquidLevel(sample.LocalCell.x, sample.LocalCell.y);
            result["restoredId"] = sample.Terrain.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y);
            result["originalLevel"] = beforeLevel; result["originalId"] = beforeId;
        }
        return result;
    }

    private static void SetVesselPourCell(RuntimeTerrainTileSample sample, string id, int level)
    {
        WorldLiquidSystem.TrySet(sample, id, level);
        if (sample.Terrain.GetLiquidLevel(sample.LocalCell.x, sample.LocalCell.y) != level ||
            level > 0 && sample.Terrain.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y) != id)
            throw new InvalidOperationException("整份倒液测试无法设置或归还地表格。");
    }

    private static JObject VesselPourSample(ILiquidVessel vessel, RuntimeTerrainTileSample sample, string water, string salt)
        => new()
        {
            ["amount"] = vessel.Data.Amount, ["water"] = MixedLiquidContents.GetAmount(vessel.Data, water),
            ["salt"] = MixedLiquidContents.GetAmount(vessel.Data, salt),
            ["level"] = sample.Terrain.GetLiquidLevel(sample.LocalCell.x, sample.LocalCell.y)
        };
    #endregion
}
