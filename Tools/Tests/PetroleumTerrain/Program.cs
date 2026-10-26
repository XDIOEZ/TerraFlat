using System;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    private static string Text(JObject args, string key, string fallback) => (string)args[key] ?? fallback;
    private static int assertions;
    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
        assertions++;
    }

    public static void Main()
    {
        foreach (string scenario in new[] { "land", "wrapped", "cold", "disabled", "ocean", "lava" })
        {
            JObject data = WorldPetroleumLandBasins(new JObject { ["scenario"] = scenario });
            Check((int)data["cellCount"] == 4096 && (int)data["partitionMismatches"] == 0,
                scenario + ": partition/negative/wrapped mismatch");
            Check((int)data["changedWetCells"] == 0, scenario + ": overwrote existing water/lava");
            int oil = (int)data["oilCells"];
            if (scenario == "land")
            {
                double ratio = oil / (double)data["fullAreaOilCells"];
                Check(ratio >= .15d && ratio <= .25d, "Oil area ratio must approximate 20% of original geometry");
                Console.WriteLine($"Oil area ratio={ratio:F3} ({oil}/{data["fullAreaOilCells"]})");
            }
            Check(scenario is "disabled" or "ocean" ? oil == 0 : oil > 0, scenario + ": unexpected oil count");
            if (scenario is "ocean" or "lava") Check((int)data["baselineWetCells"] > 0, "No baseline liquid");
            foreach (JObject cell in (JArray)data["samples"])
            {
                int level = (int)cell["level"];
                Check(level > 0 && level <= 100 && level % 10 == 0, "Oil must have pumpable native quantity");
                Check((int)cell["previousLevel"] == 0 && (int)cell["ground"] == (int)cell["previousGround"],
                    "Oil must retain its land substrate");
                Check((int)cell["bed"] == (level <= 50 ? 2 : 3), "Incorrect natural oil basin bed");
                if (scenario == "cold") Check((float)cell["celsius"] < 0f, "Cold oil was not below zero");
            }
            Console.WriteLine($"PASS {scenario}: oilCells={oil}, baselineWet={data["baselineWetCells"]}");
        }
        Check(LiquidTypeCatalog.BuiltIn.GetId(LiquidTypeCatalog.BuiltIn.GetIndex(LiquidTypeCatalog.CrudeOilId))
            == "core:crude_oil", "Oil stable identity roundtrip failed");
        Console.WriteLine($"PASS {assertions} petroleum terrain checks using current production generator and Lua probe.");
    }
}
