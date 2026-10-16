using FlatWorld.Combat;

namespace FlatWorld.Spaceflight
{
    public static class ShipSurfaceState
    {
        #region 地表升高与空地受击规则
        public const double AirborneVisualRise = 1d;

        // 普通地面建筑没有船体记录，尚未完成落地移交的船仍须保留整体阻挡。
        public static bool UsesWholeShipBody(ShipState ship, ShipFlightState flight) => ship != null;

        // 起降高度统一换算成贴图在世界 Y 轴上的升高距离。
        public static double VisualRise(ShipFlightState flight) =>
            flight != null && flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.SurfaceDescending
                ? ShipLandingGuidance.SurfaceVisualRise(flight.HeightMeters) : 0d;

        public static bool IsAirborne(ShipFlightState flight) => flight != null &&
            (flight.Phase == ShipFlightPhase.Cruising || VisualRise(flight) >= AirborneVisualRise);

        // 点火立即抬高排序，熄火后的起降过程继续保持船体高于地面。
        public static bool IsElevated(ShipFlightState flight) => flight != null &&
            (flight.Ignited || flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.Cruising or ShipFlightPhase.SurfaceDescending);

        // 空中船体只接收拥有空中命中能力的投射物，飞行近身攻击仍不能伤害船体。
        public static bool AllowsDamage(ShipFlightState flight, CombatDeliveryCapabilities capabilities)
        {
            bool airborne = IsAirborne(flight);
            const CombatDeliveryCapabilities airborneProjectile = CombatDeliveryCapabilities.Projectile | CombatDeliveryCapabilities.AirborneTargets;
            return (!airborne || (capabilities & airborneProjectile) == airborneProjectile) &&
                (airborne || (capabilities & CombatDeliveryCapabilities.AirborneOnly) == 0);
        }
        #endregion
    }
}
