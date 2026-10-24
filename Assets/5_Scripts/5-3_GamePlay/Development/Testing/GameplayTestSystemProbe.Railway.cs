using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 铁路与通用动力生产规则采样
    private static void RegisterRailwayOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("mechanical/railway_paths", RailwayPaths);
        operations.Add("mechanical/engine_power", RailwayEnginePower);
        RegisterMinecartOperations(operations);
    }
    private static JObject RailwayPaths(JObject args)
    {
        var paths = new JArray();
        for (int input = 0; input < 4; input++) for (int output = 0; output < 4; output++)
        {
            if (input == output) continue;
            var route = new ConveyorPath(input, output);
            Vector2 start = RailPathGeometry.Evaluate(route, 0f), end = RailPathGeometry.Evaluate(route, route.Length);
            float maxProjectionError = 0f, minTangentLength = 1f;
            for (int i = 0; i <= 20; i++)
            {
                float progress = route.Length * i / 20f;
                Vector2 point = RailPathGeometry.Evaluate(route, progress);
                maxProjectionError = Mathf.Max(maxProjectionError, Mathf.Abs(RailPathGeometry.Project(route, point) - progress));
                minTangentLength = Mathf.Min(minTangentLength, RailPathGeometry.Tangent(route, progress).magnitude);
            }
            paths.Add(new JObject
            {
                ["input"] = input, ["output"] = output, ["curved"] = route.Curved, ["length"] = route.Length,
                ["startError"] = (start - RailPathGeometry.Direction(input) * .5f).magnitude,
                ["endError"] = (end - RailPathGeometry.Direction(output) * .5f).magnitude,
                ["projectionError"] = maxProjectionError, ["minTangentLength"] = minTangentLength,
                ["startDirection"] = Vector2.Dot(RailPathGeometry.Tangent(route, 0f), -RailPathGeometry.Direction(input)),
                ["endDirection"] = Vector2.Dot(RailPathGeometry.Tangent(route, route.Length), RailPathGeometry.Direction(output))
            });
        }
        ConveyorPath tee = ConveyorPath.Select(7, 0), cross = ConveyorPath.Select(15, 1);
        return new JObject { ["paths"] = paths, ["teeMask"] = tee.Mask, ["crossMask"] = cross.Mask, ["teeCurved"] = tee.Curved, ["crossCurved"] = cross.Curved };
    }
    private sealed class RailwayRulePowerProvider : IEnginePowerProvider
    {
        public string ModuleId => "rules_engine_power";
        public float AvailablePower(ItemData engine, ModuleData module, Inventory_Data resources) => 2f;
        public bool TrySupply(ItemData engine, ModuleData module, Inventory_Data resources, float seconds, float requestedPower, out float suppliedPower)
        { suppliedPower = Mathf.Min(2f, requestedPower); return true; }
    }
    private static JObject RailwayEnginePower(JObject args)
    {
        EnginePowerService.Register(new RailwayRulePowerProvider());
        SteamEnginePowerProvider.Register();
        var data = new Data_GeneralItem { IDName = "rules_motor", Stack = new ItemStack() };
        data.ModuleDataDic["motor"] = new Ex_ModData { ID = "rules_engine_power", Name = "motor", Enabled = true };
        var engines = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = data } }, "engine");
        var cargo = new Inventory_Data(new List<ItemSlot>(), "cargo");
        bool supplied = EnginePowerService.Supply(engines, cargo, .02f, .75f, out float power);
        var steam = new Data_GeneralItem { IDName = "rules_steam", Stack = new ItemStack() };
        var steamStorage = new Ex_ModData { ID = Mod_SteamEngine.ModuleId, Name = "steam", Enabled = true };
        steamStorage.WriteData(new SteamEngineState { StoredFuelSeconds = 30f });
        steam.ModuleDataDic["steam"] = steamStorage;
        var steamSlots = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = steam } }, "steam");
        string before = steamStorage.BitData;
        bool drySupply = EnginePowerService.Supply(steamSlots, cargo, .02f, 1f, out float dryPower);
        return new JObject
        {
            ["genericMotorRecognized"] = EnginePowerService.IsEngine(data),
            ["genericAvailable"] = EnginePowerService.AvailablePower(engines, cargo), ["genericSupplied"] = supplied, ["genericPower"] = power,
            ["lowPower"] = SteamEngineRules.AvailablePower(3f, 1, true, 1f, 1f),
            ["highPower"] = SteamEngineRules.AvailablePower(3f, 3, true, 1f, 1f),
            ["offPower"] = SteamEngineRules.AvailablePower(3f, 3, false, 1f, 1f),
            ["dryPower"] = SteamEngineRules.AvailablePower(3f, 3, true, 0f, 1f),
            ["emptyPower"] = SteamEngineRules.AvailablePower(3f, 3, true, 1f, 0f),
            ["shortWater"] = SteamEngineRules.CanSupply(.02f, 1f, .01f, 1f),
            ["shortFuel"] = SteamEngineRules.CanSupply(.02f, 1f, .02f, .5f),
            ["exactResources"] = SteamEngineRules.CanSupply(.02f, 1f, .02f, 1f),
            ["invalidResources"] = SteamEngineRules.CanSupply(float.NaN, 1f, 1f, 1f),
            ["dryActualSupply"] = drySupply, ["dryActualPower"] = dryPower, ["dryFuelUnchanged"] = before == steamStorage.BitData
        };
    }
    #endregion
}
