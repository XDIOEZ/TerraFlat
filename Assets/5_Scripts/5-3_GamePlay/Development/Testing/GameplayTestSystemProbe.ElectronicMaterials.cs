using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 电子材料目录与磷肥规则采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterElectronicMaterialContentRules()
    {
        Register("crafting", "electronic_material_catalog", CaptureElectronicMaterialCatalogAndPortableState);
        Register("agriculture", "phosphate_fertilizer", CapturePhosphateFertilizerSupplyConfigurationAndSoil);
        Register("electrical", "lithium_iron_phosphate_battery", CaptureLithiumIronPhosphateBatteryElectricalCommit);
    }

    // 正式展开目录、库存与实例封包返回原始结果，材料身份和用途由 Lua 断言。
    private static JObject CaptureElectronicMaterialCatalogAndPortableState(JObject args)
    {
        Dictionary<string, ItemDefinitionDto> definitions = MineralContentDefinitions();
        var items = new JArray();
        foreach (JToken idToken in args["ids"] as JArray ?? new JArray())
        {
            ItemDefinitionDto definition = definitions[idToken.Value<string>()];
            JObject sample = MineralDefinitionSample(definition);
            sample["tags"] = new JArray(definition.Tags);
            sample["processing"] = JObject.FromObject(definition.Processing);
            sample["state"] = MineralPortableState(definition);
            string address = definition.Visual?.SpriteAddress;
            sample["spriteExists"] = !string.IsNullOrWhiteSpace(address) &&
                File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", address)));
            items.Add(sample);
        }
        return new JObject { ["items"] = items };
    }

    private static JObject CapturePhosphateFertilizerSupplyConfigurationAndSoil(JObject args)
    {
        Dictionary<string, ItemDefinitionDto> definitions = MineralContentDefinitions();
        ItemDefinitionDto fertilizer = definitions["Fertilizer_Phosphate"];
        ItemModuleDefinitionDto module = fertilizer.Modules.Values.Single(value => value.Id == "Mod_FarmlandSupply");
        JObject parameters = module.Parameters;
        int waterAmount = parameters.Value<int?>("waterAmount") ?? 3;
        float fertilityAmount = parameters.Value<float?>("fertilityAmount") ?? .35f;
        var soil = new TileData_Farmland
        {
            waterValue = (int)Number(args, "water", 4),
            fertilityValue = new GameValue_float((float)Number(args, "fertility", .2))
        };
        soil.NormalizeValues();
        int previousWater = soil.waterValue;
        float previousFertility = soil.Fertility;
        soil.AddWater(waterAmount);
        soil.AddFertility(fertilityAmount);
        CultivatorConfiguration configuration = definitions["SpaceCultivator"].Modules["cultivator"]
            .Parameters["Configuration"].ToObject<CultivatorConfiguration>();
        configuration.Validate();
        return new JObject
        {
            ["moduleId"] = module.Id, ["enabled"] = module.Enabled ?? true,
            ["waterAmount"] = waterAmount, ["fertilityAmount"] = fertilityAmount,
            ["previousWater"] = previousWater, ["previousFertility"] = previousFertility,
            ["water"] = soil.waterValue, ["fertility"] = soil.Fertility,
            ["changed"] = previousWater != soil.waterValue || !Mathf.Approximately(previousFertility, soil.Fertility),
            ["cultivatorAccepts"] = configuration.FertilizerItemIds.Contains(fertilizer.Id, StringComparer.Ordinal)
        };
    }

    // 新电池读取正式机器参数后交由现有电网结算，不复制充放电计算。
    private static JObject CaptureLithiumIronPhosphateBatteryElectricalCommit(JObject args)
    {
        const string batteryId = "Battery_LithiumIronPhosphate";
        Dictionary<string, ItemDefinitionDto> definitions = MineralContentDefinitions();
        TextAsset catalog = Resources.Load<TextAsset>("Config/Mechanical/mechanical-catalog");
        MachineDefinition definition = JsonConvert.DeserializeObject<MachineCatalogDocument>(catalog.text)
            .Nodes.Single(value => value.Id == batteryId);
        definition.Electrical.Validate(batteryId);
        MachineEntity battery = WorldMachineNode(8801, 0);
        battery.Definition = definition;
        battery.ElectricalStoredJoules = (float)Number(args, "joules", 1000);
        bool charge = Flag(args, "charge", false);
        MachineEntity peer = WorldMachineNode(8802, 1);
        peer.Definition.Ports = "none";
        peer.Definition.Electrical = new ElectricalDefinition
        {
            Role = charge ? "generator" : "consumer", NominalVoltage = 120,
            PowerWatts = (float)Number(args, "watts", 1200)
        };
        var network = new ElectricalNetwork();
        network.Nodes.AddRange(new[] { battery, peer });
        float before = battery.ElectricalStoredJoules;
        for (int index = 0; index < 5; index++)
            ElectricalNetworkGraph.Solve(network, 1, _ => 1, _ => 1, null, false);
        float preview = battery.ElectricalStoredJoules;
        ElectricalNetworkGraph.Solve(network, 1, _ => 1, _ => 1, null);
        JObject bodyPair = JObject.Parse(definitions[batteryId].Modules["建筑模块"].Data.Value<string>("BitData"));
        JObject summonerPair = JObject.Parse(definitions[batteryId + "_Summoner"].Modules["建筑模块"].Data.Value<string>("BitData"));
        return new JObject
        {
            ["role"] = definition.Electrical.Role, ["voltage"] = definition.Electrical.NominalVoltage,
            ["capacity"] = definition.Electrical.CapacityJoules,
            ["maxCharge"] = definition.Electrical.MaxChargeWatts, ["maxDischarge"] = definition.Electrical.MaxDischargeWatts,
            ["before"] = before, ["preview"] = preview, ["after"] = battery.ElectricalStoredJoules,
            ["charging"] = network.ChargingWatts, ["discharging"] = network.DischargingWatts,
            ["delivered"] = network.DeliveredWatts,
            ["bodyDefinitionId"] = definitions[batteryId].Modules["机械扭矩模块"].Parameters.Value<string>("DefinitionId"),
            ["summonerDefinitionId"] = definitions[batteryId + "_Summoner"].Modules["机械扭矩模块"].Parameters.Value<string>("DefinitionId"),
            ["bodyPair"] = bodyPair, ["summonerPair"] = summonerPair
        };
    }
    #endregion
}
