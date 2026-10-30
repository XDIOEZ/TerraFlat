using System;

/// <summary>目录编译一次可燃能力，火场热路径只读取纯数值。</summary>
public readonly struct WorldFireFuelProfile
{
    #region 可燃能力编译
    public readonly float Flammability;
    public readonly float BurnDurationSeconds;
    public bool IsCombustible => Flammability > 0f;
    public WorldFireFuelProfile(float flammability, float seconds = 60f)
    { Flammability = flammability; BurnDurationSeconds = seconds; }

    public static WorldFireFuelProfile Compile(RuntimeItemDefinition definition)
    {
        if (definition.IsActor) return default;
        if (definition.CanBePickedUp)
            return definition.Matter?.IgnitionTemperature.HasValue == true ? new WorldFireFuelProfile(1f) : default;
        bool combustible = definition.Matter?.IgnitionTemperature.HasValue == true ||
            definition.HasTag("Wood") || definition.HasTag("Tree") || definition.HasTag("Plant");
        foreach (RuntimeItemModuleDefinition module in definition.ModuleDefinitions)
            if (module.Enabled && (module.PrefabId == "Module_PlantClimate" || module.PrefabId == "Module_Crop"))
                combustible = true;
        return combustible ? new WorldFireFuelProfile(1f) : default;
    }
    #endregion
}
