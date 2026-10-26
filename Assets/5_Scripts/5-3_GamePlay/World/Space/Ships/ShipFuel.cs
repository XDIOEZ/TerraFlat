using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public static class ShipFuel
    {
        #region 化学引擎两相燃料原子消耗
        public const string LiquidFuelId = "core:fuel_oil";

        public static bool IsEligible(FluidInventory inventory, ShipPartConfiguration engine)
            => AvailableReactionMoles(inventory, engine) > 0m;

        public static decimal AvailableReactionMoles(FluidInventory inventory, ShipPartConfiguration engine)
        {
            if (inventory == null || engine == null) return 0m;
            decimal fuel = GetAvailableHydrogenReactionMoles(inventory);
            decimal oxygenPerFuel = GetLiquidFuelOxygenRatio(engine);
            if (oxygenPerFuel > 0m)
                fuel += inventory.GetAvailableMoles(LiquidFuelId, FluidPhase.Liquid) * oxygenPerFuel;
            return Math.Min(inventory.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas), fuel);
        }

        private static decimal GetLiquidFuelOxygenRatio(ShipPartConfiguration engine)
            => float.IsFinite(engine.LiquidFuelOxygenMolesPerMole) && engine.LiquidFuelOxygenMolesPerMole > 0f
                ? (decimal)engine.LiquidFuelOxygenMolesPerMole : 0m;

        private static decimal GetAvailableHydrogenReactionMoles(FluidInventory inventory)
        {
            decimal hydrogen = inventory.GetAvailableMoles(FluidIds.Hydrogen, FluidPhase.Gas);
            decimal reaction = hydrogen / 2m;
            decimal roundedExcess = reaction * 2m - hydrogen;
            // 最末位除二若向上舍入，回到可精确扣取的下界，不能多烧一份尾料。
            return roundedExcess > 0m ? (hydrogen - roundedExcess) / 2m : reaction;
        }

        public static bool TryBurn(FluidInventory inventory, ShipPartConfiguration engine, decimal oxygenMoles, out double energyJoules)
        {
            energyJoules = 0d;
            if (oxygenMoles <= 0m || AvailableReactionMoles(inventory, engine) < oxygenMoles) return false;
            decimal hydrogenReaction = Math.Min(oxygenMoles, GetAvailableHydrogenReactionMoles(inventory));
            decimal liquidReaction = oxygenMoles - hydrogenReaction;
            decimal liquidMoles = liquidReaction > 0m ? liquidReaction / GetLiquidFuelOxygenRatio(engine) : 0m;
            var requirements = new List<FluidRequirement>(3)
            { new(FluidIds.Oxygen, FluidPhase.Gas, oxygenMoles) };
            if (hydrogenReaction > 0m) requirements.Add(new(FluidIds.Hydrogen, FluidPhase.Gas, hydrogenReaction * 2m));
            if (liquidMoles > 0m) requirements.Add(new(LiquidFuelId, FluidPhase.Liquid, liquidMoles));
            // 先烧氢氧再补燃油，所有反应物一次扣除，余气和无关组分留在原库存。
            if (!inventory.TryTakeRecipe(requirements, out _)) return false;
            energyJoules = (double)hydrogenReaction * 483600d + (double)liquidMoles * 8600000d;
            return true;
        }

        public static double ConsumeStoredChemicalPropellants(FluidInventory inventory, ShipPartConfiguration engine,
            double throttle, double seconds)
        {
            if (engine == null || !float.IsFinite(engine.FuelMolesPerSecond) || engine.FuelMolesPerSecond <= 0f ||
                !double.IsFinite(throttle) || !double.IsFinite(seconds) || throttle <= 0d || seconds <= 0d) return 0d;
            throttle = Math.Min(1d, throttle);
            decimal requested = (decimal)(engine.FuelMolesPerSecond * throttle * seconds);
            decimal available = Math.Min(requested, AvailableReactionMoles(inventory, engine));
            return available > 0m && TryBurn(inventory, engine, available, out _)
                ? throttle * (double)(available / requested) : 0d;
        }
        #endregion
    }

}
