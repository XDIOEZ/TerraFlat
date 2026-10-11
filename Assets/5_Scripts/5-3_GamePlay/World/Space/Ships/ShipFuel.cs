using System;

namespace FlatWorld.Spaceflight
{
    public static class ShipFuel
    {
        #region 真实混合航空燃料
        public static bool IsEligible(FluidInventory inventory, ShipPartConfiguration engine)
        {
            if (inventory == null || inventory.GasMoles <= 0m) return false;
            decimal h = inventory.GetAvailableMoles(FluidIds.Hydrogen, FluidPhase.Gas);
            decimal o = inventory.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas);
            if (h <= 0m || o <= 0m) return false;
            double ratio = (double)(h / o);
            return Math.Abs(ratio / 2d - 1d) <= engine.FuelRatioTolerance &&
                (double)((inventory.GasMoles - h - o) / inventory.GasMoles) <= engine.MaximumImpurityFraction;
        }
        public static decimal AvailableReactionMoles(FluidInventory inventory, ShipPartConfiguration engine)
            => IsEligible(inventory, engine) ? Math.Min(inventory.GetAvailableMoles(FluidIds.Hydrogen, FluidPhase.Gas) / 2m,
                inventory.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas)) : 0m;

        public static bool TryBurn(FluidInventory inventory, decimal oxygenMoles, out double energyJoules)
        {
            energyJoules = 0d;
            if (oxygenMoles <= 0m || !inventory.TryTakeRecipe(new[]
            { new FluidRequirement(FluidIds.Hydrogen, FluidPhase.Gas, oxygenMoles * 2m),
              new FluidRequirement(FluidIds.Oxygen, FluidPhase.Gas, oxygenMoles) }, out _)) return false;
            energyJoules = (double)oxygenMoles * 483600d; return true;
        }
        #endregion
    }

}
