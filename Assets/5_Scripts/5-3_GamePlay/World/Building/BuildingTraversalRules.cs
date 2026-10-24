/// <summary>建筑与数据机器共用通行声明；占地层不再各自决定物理、导航和视线。</summary>
public static class BuildingTraversalRules
{
    #region 通行声明

    public static bool IsGroundLayer(int layer)
        => layer == (int)BuildingPlacementLayer.Ground || layer == Mod_ShipPart.GroundFloorLayer;

    public static bool BlocksMovement(int layer, bool declaredBlocking)
        => declaredBlocking && !IsGroundLayer(layer);

    // 数据化读取同一建筑模块的配置，不再把所有普通设施默认编成实心的零层机器。
    internal static void ApplyToMachine(MachineDefinition definition, MachineModuleConfiguration configuration)
    {
        if (definition == null || configuration?.Authoring is not Mod_Building authoring)
            return;
        definition.PlacementLayer = (int)configuration.Value(nameof(Mod_Building.PlacementLayer), authoring.PlacementLayer);
        definition.BlocksMovement = configuration.Value(nameof(Mod_Building.BlocksMovementWhenPlaced), authoring.BlocksMovementWhenPlaced);
    }

    #endregion
}
