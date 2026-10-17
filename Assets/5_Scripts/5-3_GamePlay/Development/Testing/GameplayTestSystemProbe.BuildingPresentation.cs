using System;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 船件建筑状态的正式能力采样

    // 禁用独立物品只装配必要模块，分层与通行判断全部交给正式船件接口。
    private static JObject ServiceShipPartPlacementState(JObject args) => WithEntityProbeItem(item =>
    {
        var stored = new Ex_ModData { ID = ModText.Building, StableName = "building" };
        stored.WriteData(new Mod_Building.Building_Data { BuildingPrefabId = "Shaft_Wood" });
        item.BindData(new Data_GeneralItem { IDName = "probe:ship-part-placement" });
        item.itemData.ModuleDataDic[stored.StableName] = stored;
        T Create<T>(string name) where T : Module
        {
            var child = new GameObject(name);
            child.transform.SetParent(item.transform, false);
            T module = child.AddComponent<T>();
            module.item = item;
            return module;
        }
        Mod_Building building = Create<Mod_Building>("building");
        building.BuildingData = stored;
        building.Data.BuildingPrefabId = "probe:not-mechanical";
        Mod_MechanicalNode mechanical = Create<Mod_MechanicalNode>("mechanical");
        mechanical.Data.StableName = "mechanical";
        mechanical.DefinitionId = "Shaft_Wood";
        Mod_ShipPart part = Create<Mod_ShipPart>("ship-part");
        part.ModData.StableName = "ship-part";
        part.Configuration.Kind = ShipPieceKind.Wall;
        part.Configuration.Layer = 9;
        part.Configuration.BlocksMovement = true;
        item.itemMods.AddMod(building);
        item.itemMods.AddMod(mechanical);
        item.itemMods.AddMod(part);
        JObject Capture() => new()
        {
            ["layer"] = part.OccupancyLayer, ["blocks"] = part.BlocksMovement,
            ["speed"] = part.PlayerMoveSpeedMultiplier
        };
        var result = new JObject { ["liveOrdinary"] = Capture() };
        building.Data.BuildingPrefabId = "Shaft_Wood";
        result["liveMechanical"] = Capture();
        long before = GC.GetAllocatedBytesForCurrentThread();
        int layerSum = 0;
        for (int i = 0; i < 256; i++)
        {
            layerSum += part.OccupancyLayer;
            _ = part.BlocksMovement;
            _ = part.PlayerMoveSpeedMultiplier;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        result["allocated"] = allocated;
        result["layerSum"] = layerSum;
        building.Data.BuildingPrefabId = "probe:not-mechanical";
        result["liveChanged"] = Capture();
        building.Data.BuildingPrefabId = null;
        result["beforeBuildingLoad"] = Capture();
        item.itemMods.RemoveMod(building);
        result["storedFallback"] = Capture();
        item.itemMods.RemoveMod(mechanical);
        result["mechanicalRemoved"] = Capture();
        stored.BitData = "not-json";
        part.Configuration.Kind = ShipPieceKind.Floor;
        result["floor"] = Capture();
        return result;
    });

    #endregion
}
