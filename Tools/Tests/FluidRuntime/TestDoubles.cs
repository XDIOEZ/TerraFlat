using System;
using System.Collections.Generic;

#region 非流体目标的坐标依赖
namespace UnityEngine
{
    public static class Mathf
    {
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
        public static float MoveTowards(float current, float target, float delta)
            => MathF.Abs(target - current) <= delta ? target : current + MathF.Sign(target - current) * delta;
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
}
public readonly struct WorldAddress
{
    public const string SurfaceDimensionId = "surface";
    public WorldAddress(string planetId, string dimensionId) { }
}
#endregion

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

public static class TemperatureMgr
{
    public const float DefaultAmbientTemperature = 20f;
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
            Action<ShipState, ShipPieceState, string> onDestroyed = null,
            Func<ShipState, ShipPieceState, double, string, double?> applyOwnedDamage = null) => throw new NotSupportedException();
    }
    #endregion
}
