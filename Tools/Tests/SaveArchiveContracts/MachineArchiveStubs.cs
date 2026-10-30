using System.Collections.Generic;
using UnityEngine;

public sealed class MachineArchive
{
    public int Version = 2;
    public Dictionary<string, HashSet<int>> RemovedGenerated = new();
    public ulong NextPressureExplosionId;
    public List<PressureExplosionEvent> PressureExplosions = new();
    public List<string> RecentPressureExplosionSources = new();
    public List<PressureLiquidSpillState> PendingPressureLiquidSpills = new();
}

public sealed class PressureExplosionEvent
{
    public ulong Id;
    public string WorldKey, SourceId, SpatialContextJson, PresentationWorldKey;
    public Vector2 Center;
    public double Intensity, PressureEnergyJoules, ChemicalEnergyJoules, ProductHeatingJoules;
    public List<PressureExplosionMaterial> Materials = new();
}

public sealed class PressureExplosionMaterial
{
    public string FluidId;
    public decimal GasMoles, LiquidMoles;
    public double InternalEnergyJoules;
}

public sealed class PressureLiquidSpillState
{
    public string WorldKey, FluidId, DetachedWorldKey;
    public Vector2 Center, DetachedCenter;
    public decimal LiquidMoles;
    public double InternalEnergyJoules;
    public int NextCellIndex;
}

public static class MachineDefinition { public static bool Positive(float value) => value > 0; }
public static class TemperatureMgr { public const float DefaultAmbientTemperature = 20; }
public static class FluidUnits { public static decimal MolToStandardLiters(decimal value) => value * 24.3738471641152m; }
public static class MachineInventory
{
    public static Inventory_Data NewData(string name, int count)
    {
        var slots = new List<ItemSlot>(count);
        for (int i = 0; i < count; i++) slots.Add(new ItemSlot(i));
        return new Inventory_Data(slots, name);
    }
}
