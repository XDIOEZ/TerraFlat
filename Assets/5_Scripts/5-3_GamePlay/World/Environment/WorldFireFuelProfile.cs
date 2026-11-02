using System;
using Newtonsoft.Json;

/// <summary>目录编译一次可燃能力，火场热路径只读取纯数值。</summary>
public readonly struct WorldFireFuelProfile
{
    #region 可燃能力编译
    public readonly float Flammability;
    public readonly float BurnDurationSeconds;
    public bool IsCombustible => BurnDurationSeconds > 0f;
    public WorldFireFuelProfile(float flammability, float seconds = 60f)
    { Flammability = flammability; BurnDurationSeconds = seconds; }

    public static WorldFireFuelProfile Compile(RuntimeItemDefinition definition)
    {
        if (definition.IsActor) return default;
        if (definition.CanBePickedUp)
            return definition.Matter?.IgnitionTemperature.HasValue == true ? new WorldFireFuelProfile(.1f) : default;
        bool combustible = definition.Matter?.IgnitionTemperature.HasValue == true ||
            definition.HasTag("Wood") || definition.HasTag("Tree") || definition.HasTag("Plant");
        foreach (RuntimeItemModuleDefinition module in definition.ModuleDefinitions)
            if (module.Enabled && (module.PrefabId == "Module_PlantClimate" || module.PrefabId == "Module_Crop"))
                combustible = true;
        return combustible ? new WorldFireFuelProfile(.1f) : default;
    }
    #endregion
}

/// <summary>物品、地块和液体共用的两参数可燃性配置。</summary>
[Serializable]
[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
public sealed partial class WorldCombustibilityDefinitionDto
{
    #region 可燃性配置
    [MemoryPack.MemoryPackOrder(0), JsonProperty("ignitionProbability")] public float IgnitionProbability;
    [MemoryPack.MemoryPackOrder(1), JsonProperty("fuelSeconds")] public float FuelSeconds;
    public WorldFireFuelProfile Compile() => new(Math.Clamp(IgnitionProbability, 0f, 1f), Math.Max(0f, FuelSeconds));
    #endregion
}
