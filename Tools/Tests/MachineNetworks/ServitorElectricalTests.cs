using UnityEngine;

internal static class ServitorElectricalTests
{
    public static void Run(Action<bool, string> check)
    {
        var state = new ServitorBatteryState { StoredJoules = 2001f };
        state.Consume(2f, 10000f, .2f);
        check(state.RechargeRequested, "低电量没有锁存回充意图");
        state.ReceiveEnergy(1000f, 10000f);
        check(state.RechargeRequested, "刚超过20%就停止充电导致模式抖动");
        state.StoredJoules = 9999f;
        check(state.RequestChargingWatts(1f, 10000f) == 1f, "满电前仍请求多余能量");
        state.ReceiveEnergy(1f, 10000f);
        check(!state.RechargeRequested && state.StoredJoules == 10000f, "满电没有退出回充模式");

        MachineEntity Node(int id, string role, float watts = 0f) => new()
        {
            Id = id, Cell = Vector2Int.zero, State = new(), Snapshot = new(),
            Definition = new() { Layer = role == "wire" ? 1 : 0, HasMechanicalPorts = false,
                Electrical = new() { Role = role, PowerWatts = watts } }
        };
        MachineEntity wire = Node(17000, "wire"), generator = Node(17001, "generator", 100f);
        MachineEntity[] nodes = { wire, generator };
        var mechanical = new MechanicalNetworkGraph(new(16, 16), Vector2Int.zero, default(WorldTopologyDomain));
        var electrical = new ElectricalNetworkGraph(default);
        mechanical.Rebuild(nodes); electrical.Rebuild(nodes);
        MachineWorld.SetWorld(mechanical, electrical, nodes);
        MachineWorld.RebuildTopologyForTest(nodes);
        state.StoredJoules = 2000f; state.RechargeRequested = true;
        int receipts = 0;
        using var first = MachineWorld.ConnectExternalElectricalConsumer(wire,
            dt => state.RequestChargingWatts(dt, 10000f), joules => { receipts++; state.ReceiveEnergy(joules, 10000f); });
        MachineWorld.SettleCoupled(1f);
        check(state.StoredJoules == 2100f && receipts == 1, "真实耦合结算没有恰好每秒交付1%电量");
        check(wire.ElectricalNetwork.DemandWatts == 100f && wire.ElectricalNetwork.DeliveredWatts == 100f,
            "移动负载绕过电网需求与交付统计");
        for (int i = 0; i < 8; i++)
            ElectricalNetworkGraph.Solve(wire.ElectricalNetwork, 1f, _ => 1f, _ => 1f, null, false);
        check(state.StoredJoules == 2100f && receipts == 1, "电网探算重复给机仆充电");

        var secondState = new ServitorBatteryState { StoredJoules = 0f, RechargeRequested = true };
        using var second = MachineWorld.ConnectExternalElectricalConsumer(wire,
            dt => secondState.RequestChargingWatts(dt, 10000f), joules => secondState.ReceiveEnergy(joules, 10000f));
        MachineWorld.SettleCoupled(1f);
        check(state.StoredJoules == 2150f && secondState.StoredJoules == 50f,
            "两个机仆重复领取同一份发电功率");
        first.Dispose(); MachineWorld.SettleCoupled(1f);
        check(state.StoredJoules == 2150f && secondState.StoredJoules == 150f, "解除用电租约后仍充电或占用功率");
        generator.Definition.Electrical.PowerWatts = 0f;
        MachineWorld.SettleCoupled(1f);
        check(secondState.StoredJoules == 150f && second.LastSuppliedWatts == 0f, "无发电来源的电线仍然充电");
        generator.Definition.Electrical.PowerWatts = 100f;
        electrical.Rebuild(new[] { generator });
        MachineWorld.SettleCoupled(1f);
        check(!second.IsConnected && secondState.StoredJoules == 150f, "拆除电线后旧租约仍交付能量");
    }
}
