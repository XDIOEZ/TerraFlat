using System;
using System.Collections.Generic;

public sealed class FluidCatalog
{
    #region 测试材料入口
    private readonly Dictionary<string, FluidDefinition> values = new(StringComparer.OrdinalIgnoreCase);
    private static FluidCatalog current = new();
    public static FluidCatalog Default => current;
    public static void Replace(FluidCatalog catalog) => current = catalog ?? throw new ArgumentNullException(nameof(catalog));
    public void Register(FluidDefinition value) => values[value.Id] = value;
    public bool TryGet(string id, out FluidDefinition value) => values.TryGetValue(id, out value);
    public FluidDefinition Find(string id) => values[id];
    #endregion
}

public static class ThermalRuntime
{
    // 未覆盖的环境传热调度主动报错，不能让测试替身冒充生产算法。
    public static float AdvanceTowards(float value, float target, float rate, float seconds) => throw new NotSupportedException();
}

public static class AtmosphereService
{
    public static decimal Quantize(decimal value) => throw new NotSupportedException("本回归不覆盖星球大气抽取和分配。");
}

namespace FlatWorld.Spaceflight
{
    #region 未覆盖的结构损伤入口
    public readonly struct ShipDamageLine { }
    public static class ShipDamagePattern
    {
        public static bool Intersects(ShipState ship, ShipPieceState piece, ShipDamageLine line) => throw new NotSupportedException();
        public static bool TryCreateLine(ShipState ship, double angle, double position, out ShipDamageLine line,
            Predicate<ShipPieceState> eligible = null) => throw new NotSupportedException();
    }
    public static class ShipStructureService
    {
        public static bool ApplyDamage(ShipState ship, string pieceId, double damage, string reason,
            Action<ShipState, ShipPieceState, string> onDestroyed = null) => throw new NotSupportedException();
    }
    #endregion
}
