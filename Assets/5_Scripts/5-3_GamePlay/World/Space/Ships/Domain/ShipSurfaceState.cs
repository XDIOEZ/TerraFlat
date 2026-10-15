using FlatWorld.Combat;

namespace FlatWorld.Spaceflight
{
    public static class ShipSurfaceState
    {
        #region 地表升高与空地受击规则
        public const double AirborneVisualRise = 1d;

        // 停泊保留逐块阻挡，开始起飞后直到落地都由整船承载运动。
        public static bool UsesWholeShipBody(ShipState ship, ShipFlightState flight) => ship != null &&
            (ship.PlatformKind == ShipPlatformKind.Space || flight != null &&
                flight.Phase is ShipFlightPhase.Lifting or ShipFlightPhase.Cruising or ShipFlightPhase.SurfaceDescending);

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
