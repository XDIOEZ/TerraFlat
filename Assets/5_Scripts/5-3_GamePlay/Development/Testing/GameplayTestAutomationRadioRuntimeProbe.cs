using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>隔离世界使用正式机器构建无线远程照明，Lua 独立验证实际供电结果。</summary>
public static class GameplayTestAutomationRadioRuntimeProbe
{
    #region 正式现场与收尾
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register() => GameplayTestRuntimeProbe.RegisterAsync("circuit_signals", "wireless_automation", CaptureWorkflow);

    private sealed class Fixture : IDisposable
    {
        public readonly List<int> Owned = new();
        public readonly Vector2Int Anchor;
        public MachineEntity Receiver, Gate, Relay, Lamp, SourcePower;
        public Fixture(Item player)
        {
            Vector2Int origin = MachineWorld.CellOf(player.transform.position) + new Vector2Int(3, 5);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2Int candidate = origin + new Vector2Int(0, attempt * 8);
                bool free = true;
                for (int x = 0; x <= 24 && free; x++)
                for (int y = -3; y <= 1 && free; y++)
                foreach (int layer in new[] { 0, 3, 4 })
                    if (MachineWorld.GetAt(candidate + new Vector2Int(x, y), layer) != null ||
                        BuildingOccupancyRegistry.IsOccupied(candidate + new Vector2Int(x, y), layer: layer)) { free = false; break; }
                if (free) { Anchor = candidate; return; }
            }
            throw new InvalidOperationException("隔离世界没有空闲自动化现场。");
        }
        public MachineEntity Place(string id, int x, int y, MachineRadioConfiguration radio = null)
        {
            if (!GameRes.Instance.TryGetItemDefinition(id, out RuntimeItemDefinition definition))
                throw new InvalidOperationException("自动化正式定义未注册：" + id);
            ItemData snapshot = definition.CreateItemData();
            snapshot.Guid = ItemMgr.Instance.GenerateGuid();
            snapshot.transform ??= new ItemTransform();
            snapshot.transform.position = new Vector3(Anchor.x + x + .5f, Anchor.y + y + .5f);
            snapshot.transform.rotation = Quaternion.identity; snapshot.transform.scale = Vector3.one;
            snapshot.inHand = false; Mod_Building.SetInstalledDataState(snapshot);
            if (radio != null) MachinePersistence.Write(snapshot, "radio-device", radio);
            MachineEntity node = MachineWorld.Place(snapshot); Owned.Add(node.Id); return node;
        }
        public void Dispose()
        {
            for (int i = Owned.Count - 1; i >= 0; i--) MachineWorld.Remove(Owned[i]);
            Owned.Clear();
        }
    }
    private static IEnumerator CaptureWorkflow(Item player, JObject args, Action<JObject> completed)
    {
        var fixture = new Fixture(player);
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        var result = new JObject();
        fixture.SourcePower = fixture.Place("Creative_Power_Source", 0, 0);
        fixture.Place("Wire_Copper", 0, 0); fixture.Place("Wire_Copper", 1, 0); fixture.Place("Wire_Copper", 2, 0);
        fixture.Place("ElectricLightBulb", 1, 0);
        fixture.Place("RadioSignalConverter", 3, 0);
        foreach (Vector2Int cell in new[] { new Vector2Int(1, 1), new(2, 1), new(3, 1), new(4, 1), new(4, 0) })
            fixture.Place("SignalWire", cell.x, cell.y);
        fixture.Place("Creative_Power_Source", 18, 0);
        fixture.Place("Wire_Copper", 18, 0); fixture.Place("Wire_Copper", 19, 0);
        fixture.Receiver = fixture.Place("RadioSignalConverter", 20, 0, new MachineRadioConfiguration { Transmit = false });
        foreach (Vector2Int cell in new[] { new Vector2Int(21, 0), new(21, -1), new(21, -2), new(22, -2), new(24, -2), new(24, -1), new(23, -1) })
            fixture.Place("SignalWire", cell.x, cell.y);
        fixture.Gate = fixture.Place("LogicGate_OR", 23, -2);
        fixture.Place("Creative_Power_Source", 22, 0); fixture.Place("Wire_Copper", 22, 0);
        fixture.Relay = fixture.Place("SignalRelay", 23, 0);
        fixture.Place("Wire_Copper", 24, 0); fixture.Lamp = fixture.Place("ElectricLightBulb", 24, 0);
        yield return WaitFor(() => fixture.Lamp.ElectricalSuppliedWatts > 0f);
        result["initial"] = Sample(fixture);

        ItemData sourceSnapshot = MachineWorld.Remove(fixture.SourcePower.Id);
        yield return WaitFor(() => fixture.Lamp.ElectricalSuppliedWatts == 0f && !fixture.Relay.ElectricalContactsClosed);
        result["off"] = Sample(fixture);
        fixture.SourcePower = MachineWorld.Place(sourceSnapshot);
        yield return WaitFor(() => fixture.Lamp.ElectricalSuppliedWatts > 0f);
        result["restoredPower"] = Sample(fixture);

        var snapshots = new List<ItemData>();
        foreach (int id in fixture.Owned)
        {
            MachineEntity node = MachineWorld.GetById(id);
            if (node != null) snapshots.Add(ItemInstanceDataFactory.CloneRuntime(MachineWorld.CaptureSnapshot(node)));
        }
        int receiverId = fixture.Receiver.Id, gateId = fixture.Gate.Id, relayId = fixture.Relay.Id, lampId = fixture.Lamp.Id;
        for (int i = fixture.Owned.Count - 1; i >= 0; i--) MachineWorld.Remove(fixture.Owned[i]);
        foreach (ItemData snapshot in snapshots) MachineWorld.Place(snapshot);
        fixture.Receiver = MachineWorld.GetById(receiverId); fixture.Gate = MachineWorld.GetById(gateId);
        fixture.Relay = MachineWorld.GetById(relayId); fixture.Lamp = MachineWorld.GetById(lampId);
        result["receiverInitiallyZero"] = !fixture.Receiver.SignalOutput;
        yield return WaitFor(() => fixture.Lamp.ElectricalSuppliedWatts > 0f);
        result["snapshotRebuilt"] = Sample(fixture);
        completed(result);
    }
    private static IEnumerator WaitFor(Func<bool> ready)
    {
        float deadline = Time.realtimeSinceStartup + 8f;
        while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
    }
    private static JObject Sample(Fixture fixture) => new()
    {
        ["validSource"] = fixture.Receiver.RadioHasSource,
        ["receiver"] = fixture.Receiver.SignalOutput,
        ["gate"] = fixture.Gate.SignalOutput,
        ["relay"] = fixture.Relay.ElectricalContactsClosed,
        ["watts"] = fixture.Lamp.ElectricalSuppliedWatts,
        ["channel"] = fixture.Receiver.RadioChannel,
        ["transmitMode"] = fixture.Receiver.RadioTransmitMode
    };
    #endregion
}
