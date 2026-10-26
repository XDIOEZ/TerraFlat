using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 机械气泵四向放置采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterMechanicalGasPumpPlacementRotationRules()
        => Register("mechanical", "gas_pump_placement_rotation", ProbeMechanicalGasPumpPlacementRotation);

    private static JObject ProbeMechanicalGasPumpPlacementRotation(JObject arguments)
    {
        // 只读正式目录并调用放置轮换和端口规则，不创建或改动当前世界的设备。
        TextAsset catalogAsset = Resources.Load<TextAsset>("Config/Mechanical/mechanical-catalog")
            ?? throw new InvalidOperationException("机械气泵旋转测试缺少正式机械目录。");
        MachineCatalogDocument catalog = JsonConvert.DeserializeObject<MachineCatalogDocument>(catalogAsset.text);
        MachineDefinition definition = catalog.Nodes.Find(node => node?.Id == "GasPump_Mechanical")
            ?? throw new InvalidOperationException("机械目录缺少机械气泵。");
        definition.Validate();
        var shaftDefinition = new MachineDefinition { Kind = "shaft", Ports = "axis" };
        var pump = new MachineEntity { Definition = definition };
        var samples = new JArray();
        int quarterTurns = 0;
        for (int press = 0; press <= 4; press++)
        {
            pump.RotationQuarterTurns = quarterTurns;
            var shaftConnections = new JArray();
            for (int direction = 0; direction < 4; direction++)
                shaftConnections.Add(pump.CanConnectTo(shaftDefinition, direction));
            samples.Add(new JObject
            {
                ["quarterTurns"] = quarterTurns,
                ["shaftConnections"] = shaftConnections
            });
            quarterTurns = definition.NextPlacementQuarterTurns(quarterTurns);
        }
        FluidMachinePortDefinition outlet = definition.Fluid.Ports[0];
        return new JObject
        {
            ["rotatable"] = definition.Rotatable,
            ["steps"] = definition.PlacementRotationSteps,
            ["samples"] = samples,
            ["outletDirection"] = outlet.Direction,
            ["outletMode"] = outlet.Mode,
            ["outletPhase"] = outlet.Phase
        };
    }
    #endregion
}
