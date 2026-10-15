using System;
using FlatWorld.Spaceflight;
using Newtonsoft.Json;
using UnityEngine;

public sealed class Mod_ShipPart : Module, IBuildingPlacementExtension, IBuildingTraversalPolicy
{
    #region 显式船体能力
    public const string ModuleId = "船体部件模块";
    public const int GroundFloorLayer = 6;
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public Ex_ModData_MemoryPackable ModData = new() { ID = ModuleId };
    public FlatWorld.Spaceflight.ShipPartConfiguration Configuration = new();
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ?? throw new ArgumentException("船体部件数据类型错误。");
    }
    protected override void OnLoad() { }
    protected override void OnSave() { }
    protected override void OnUnload() { }

    // 地面地板使用独立占地层，普通设备和管电线路仍归各自的正式建筑层。
    public int OccupancyLayer => Configuration.Kind == ShipPieceKind.Floor ? GroundFloorLayer :
        MechanicalExtension?.OccupancyLayer ?? Configuration.Layer;
    public bool BlocksMovement => Configuration.Kind != ShipPieceKind.Floor &&
        (MechanicalExtension?.BlocksMovement ?? Configuration.BlocksMovement ?? true);
    public float PlayerMoveSpeedMultiplier => MechanicalExtension?.PlayerMoveSpeedMultiplier ?? 1f;
    private Mod_MechanicalNode MechanicalExtension
    {
        get
        {
            if (Configuration.Kind == ShipPieceKind.Floor || item?.itemMods == null) return null;
            Mod_Building.TryReadBuildingData(item.itemData, out _, out Mod_Building.Building_Data building);
            string placedId = string.IsNullOrWhiteSpace(building?.BuildingPrefabId) ? item.itemData.IDName : building.BuildingPrefabId;
            return MachineCatalog.Get(placedId) != null
                ? item.itemMods.GetMod_ByID<Mod_MechanicalNode>(Mod_MechanicalNode.ModuleId) : null;
        }
    }

    public bool ValidatePlacement(Vector2Int cell, out string reason)
    {
        Mod_MechanicalNode mechanical = MechanicalExtension;
        if (mechanical != null) return mechanical.ValidatePlacement(cell, out reason);
        bool clear = !BuildingOccupancyRegistry.IsOccupied(cell, layer: OccupancyLayer);
        reason = clear ? null : "当前建筑层已占用";
        return clear;
    }

    public void PreparePlacedData(ItemData data) => MechanicalExtension?.PreparePlacedData(data);
    public void PrepareRepackedSnapshot(ItemData snapshot) => MechanicalExtension?.PrepareRepackedSnapshot(snapshot);
    public void ApplyPreview(BuildingShadow shadow) => MechanicalExtension?.ApplyPreview(shadow);

    public static FlatWorld.Spaceflight.ShipPartConfiguration Read(ItemData data)
    {
        if (data == null || GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition)) return null;
        foreach (RuntimeItemModuleDefinition entry in definition.ModuleDefinitions)
        {
            if (entry.ModuleId != ModuleId || !entry.Enabled) continue;
            GameObject prefab = GameRes.ExistingInstance.GetPrefab(entry.PrefabId, false);
            Mod_ShipPart authoring = prefab != null ? prefab.GetComponentInChildren<Mod_ShipPart>(true) : null;
            if (authoring == null) throw new InvalidOperationException("船体部件模板缺失：" + entry.PrefabId);
            var configuration = JsonConvert.DeserializeObject<FlatWorld.Spaceflight.ShipPartConfiguration>(JsonConvert.SerializeObject(authoring.Configuration));
            if (!string.IsNullOrWhiteSpace(entry.ParametersJson))
            {
                var parameters = Newtonsoft.Json.Linq.JObject.Parse(entry.ParametersJson);
                if (parameters["Configuration"] is Newtonsoft.Json.Linq.JObject values)
                    JsonConvert.PopulateObject(values.ToString(), configuration);
            }
            return configuration;
        }
        return null;
    }
    #endregion
}
