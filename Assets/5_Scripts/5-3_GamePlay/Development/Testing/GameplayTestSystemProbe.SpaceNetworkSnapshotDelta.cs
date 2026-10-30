#if UNITY_EDITOR || DEVELOPMENT_BUILD || SPACE_SNAPSHOT_OFFLINE
using System;
using System.IO;
using System.Linq;
using System.Text;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;
#if !SPACE_SNAPSHOT_OFFLINE
using UnityEngine;
#endif

/// <summary>采样正式太空差量编解码，不启动网络或接管游戏会话。</summary>
public static class GameplayTestSpaceNetworkSnapshotDeltaProbe
{
    #region 规则登记
#if !SPACE_SNAPSHOT_OFFLINE
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register() => GameplayTestSystemProbe.Register("networking", "space_snapshot_delta", Capture);
#endif
    #endregion

    #region 太空差量生命周期采样
    public static JObject Capture(JObject arguments)
    {
        var codec = new SpaceNetworkSnapshotDeltaCodec();
        var firstCursor = new SpaceNetworkSnapshotDeltaCursor();
        var secondCursor = new SpaceNetworkSnapshotDeltaCursor();
        var firstReceiver = new SpaceNetworkSnapshotDeltaCodec.Receiver();
        var secondReceiver = new SpaceNetworkSnapshotDeltaCodec.Receiver();
        string originalInventory = new string('A', 4096);
        string changedInventory = new string('B', 4096);
        byte[] worldPayload = { 1, 2, 3, 255 };
        SpaceSessionState source = CreateState(originalInventory, worldPayload);
        ShipState ship = source.Ships.Find(value => value.ShipId == "ship-a");
        ShipState secondShip = source.Ships.Find(value => value.ShipId == "ship-b");

        codec.BeginCaptureBatch();
        SpaceNetworkSnapshotDeltaPacket first = codec.Encode(source, firstCursor, 100);
        int capturedAfterFirst = codec.CapturedEntityCount;
        int encodedAfterFirst = codec.EncodedFieldCount;
        int compressedAfterFirst = codec.CompressedFieldCount;
        SpaceNetworkSnapshotDeltaPacket second = codec.Encode(source, secondCursor, 300);
        var firstFull = firstReceiver.Decode(first.Payload, 100);
        var secondFull = secondReceiver.Decode(second.Payload, 300);
        first.ConfirmSent(firstCursor); second.ConfirmSent(secondCursor);
        firstReceiver.ConfirmApplied(firstFull); secondReceiver.ConfirmApplied(secondFull);
        JObject shared = new()
        {
            ["firstFull"] = firstFull.IsFullSnapshot, ["secondFull"] = secondFull.IsFullSnapshot,
            ["capturedFirst"] = capturedAfterFirst, ["capturedSecond"] = codec.CapturedEntityCount,
            ["encodedFirst"] = encodedAfterFirst, ["encodedSecond"] = codec.EncodedFieldCount,
            ["compressedFirst"] = compressedAfterFirst, ["firstBytes"] = first.Payload.Length,
            ["secondBytes"] = second.Payload.Length,
            ["firstWorlds"] = firstFull.State.LocalWorldStates.Count,
            ["secondWorlds"] = secondFull.State.LocalWorldStates.Count,
            ["firstWorldPayload"] = firstFull.State.LocalWorldStates[0].Payload.SequenceEqual(worldPayload),
            ["secondWorldPayload"] = secondFull.State.LocalWorldStates[0].Payload.SequenceEqual(worldPayload)
        };
        var narrowSource = new SpaceSessionState { SimulationSeconds = source.SimulationSeconds };
        narrowSource.Ships.Add(secondShip);
        narrowSource.Flights.Add(source.Flights.Find(value => value.ShipId == "ship-b"));
        narrowSource.Sectors.Add(source.Sectors[0]);
        var narrowCursor = new SpaceNetworkSnapshotDeltaCursor();
        var narrowReceiver = new SpaceNetworkSnapshotDeltaCodec.Receiver();
        SpaceNetworkSnapshotDeltaPacket narrowPacket = codec.Encode(narrowSource, narrowCursor, 400);
        var narrow = narrowReceiver.Decode(narrowPacket.Payload, 400);
        JObject differentViews = new()
        {
            ["wideShipA"] = Ship(firstFull.State, "ship-a") != null,
            ["wideShipB"] = Ship(firstFull.State, "ship-b") != null,
            ["narrowShipA"] = Ship(narrow.State, "ship-a") != null,
            ["narrowShipB"] = Ship(narrow.State, "ship-b") != null,
            ["narrowFlights"] = narrow.State.Flights.Count,
            ["narrowFlightShip"] = narrow.State.Flights[0].ShipId,
            ["narrowPassengers"] = narrow.State.Passengers.Count,
            ["narrowLooseItems"] = narrow.State.LooseItems.Count,
            ["narrowWorlds"] = narrow.State.LocalWorldStates.Count
        };

        // 客户端可自由修改运行态对象，接收基线仍必须保持冻结。
        Ship(firstFull.State, "ship-a").PositionX = -999d;
        Piece(Ship(firstFull.State, "ship-a"), "101").Health = 1d;
        firstFull.State.Sectors[0].Bodies[0].PlanetId = "client-mutation";
        source.SimulationSeconds = 10.2d;
        ship.PositionX = 120.5d;
        source.Passengers[0].LocalX = 4.5d;
        source.LocalWorldStates.Clear();
        codec.BeginCaptureBatch();
        SpaceNetworkSnapshotDeltaPacket motionPacket = codec.Encode(source, firstCursor, 101);
        int motionEncoded = codec.EncodedFieldCount;
        int motionCompressed = codec.CompressedFieldCount;
        var discarded = firstReceiver.Decode(motionPacket.Payload, 101);
        Ship(discarded.State, "ship-a").PositionX = -123d;
        var motion = firstReceiver.Decode(motionPacket.Payload, 101);
        SpaceNetworkSnapshotDeltaPacket secondMotionPacket = codec.Encode(source, secondCursor, 301);
        var secondMotion = secondReceiver.Decode(secondMotionPacket.Payload, 301);
        motionPacket.ConfirmSent(firstCursor); secondMotionPacket.ConfirmSent(secondCursor);
        firstReceiver.ConfirmApplied(motion); secondReceiver.ConfirmApplied(secondMotion);
        JObject movement = new()
        {
            ["deltaBytes"] = motionPacket.Payload.Length, ["encodedFields"] = motionEncoded,
            ["compressedFields"] = motionCompressed, ["shipX"] = Ship(motion.State, "ship-a").PositionX,
            ["secondShipX"] = Ship(secondMotion.State, "ship-a").PositionX,
            ["passengerX"] = motion.State.Passengers[0].LocalX,
            ["pieceHealth"] = Piece(Ship(motion.State, "ship-a"), "101").Health,
            ["inventoryIntact"] = Piece(Ship(motion.State, "ship-a"), "101").SnapshotJson == originalInventory,
            ["sectorIntact"] = motion.State.Sectors[0].Bodies[0].PlanetId == "planet-a",
            ["worlds"] = motion.State.LocalWorldStates.Count,
            ["secondWorlds"] = secondMotion.State.LocalWorldStates.Count
        };

        ShipPieceState originalPiece = Piece(ship, "101");
        originalPiece.DoorOpen = true;
        originalPiece.Health = 73d;
        originalPiece.SnapshotJson = changedInventory;
        ship.Pieces.Remove(Piece(ship, "102"));
        ShipPieceState movedPiece = Piece(ship, "104");
        ship.Pieces.Remove(movedPiece);
        secondShip.Pieces.Add(movedPiece);
        ship.Pieces.Add(new ShipPieceState { PieceId = "103", ItemId = "added", CellX = 3 });
        SetGas(ship.Compartments[0].Gas, 81d);
        SetConfiguration(source.Devices[0], 42f);
        source.Flights[0].HeightMeters = 35d;
        source.LooseItems[0].Snapshot = "changed-loose";
        source.LooseItems[0].Motion.PositionMeters = new SpaceVector2(33d, -5d);
        source.SimulationSeconds = 10.4d;
        codec.BeginCaptureBatch();
        SpaceNetworkSnapshotDeltaPacket changedPacket = codec.Encode(source, firstCursor, 102);
        var truncatedPayload = new byte[changedPacket.Payload.Length - 1];
        Buffer.BlockCopy(changedPacket.Payload, 0, truncatedPayload, 0, truncatedPayload.Length);
        bool truncatedRejected = RejectCorruptPayload(firstReceiver, truncatedPayload, 102);
        bool invalidLengthRejected = RejectCorruptPayload(firstReceiver, CorruptInventoryFieldLength(changedPacket.Payload), 102);
        var changed = firstReceiver.Decode(changedPacket.Payload, 102);
        changedPacket.ConfirmSent(firstCursor); firstReceiver.ConfirmApplied(changed);
        ShipState changedShip = Ship(changed.State, "ship-a");
        JObject changes = new()
        {
            ["doorOpen"] = Piece(changedShip, "101").DoorOpen,
            ["health"] = Piece(changedShip, "101").Health,
            ["inventoryChanged"] = Piece(changedShip, "101").SnapshotJson == changedInventory,
            ["gas"] = GetGas(changedShip.Compartments[0].Gas),
            ["configuration"] = GetConfiguration(changed.State.Devices[0]),
            ["configurationNative"] = HasNativeConfiguration(),
            ["height"] = changed.State.Flights[0].HeightMeters,
            ["looseSnapshot"] = changed.State.LooseItems[0].Snapshot,
            ["looseX"] = changed.State.LooseItems[0].Motion.PositionMeters.X,
            ["added"] = Piece(changedShip, "103") != null,
            ["removed"] = Piece(changedShip, "102") == null,
            ["transferred"] = Piece(changedShip, "104") == null && Piece(Ship(changed.State, "ship-b"), "104") != null,
            ["oldDoorOpen"] = Piece(Ship(motion.State, "ship-a"), "101").DoorOpen,
            ["oldHealth"] = Piece(Ship(motion.State, "ship-a"), "101").Health
        };
        JObject corrupt = new()
        {
            ["truncatedRejected"] = truncatedRejected,
            ["invalidLengthRejected"] = invalidLengthRejected,
            ["validAfterRejection"] = Piece(changedShip, "101").Health == 73d &&
                Piece(changedShip, "101").SnapshotJson == changedInventory
        };

        source.Ships.Remove(ship);
        source.Flights.Remove(source.Flights.Find(value => value.ShipId == "ship-a"));
        source.Devices.Clear();
        source.SimulationSeconds = 10.6d;
        codec.BeginCaptureBatch();
        SpaceNetworkSnapshotDeltaPacket leavePacket = codec.Encode(source, firstCursor, 103);
        var left = firstReceiver.Decode(leavePacket.Payload, 103);
        leavePacket.ConfirmSent(firstCursor); firstReceiver.ConfirmApplied(left);
        source.Ships.Add(ship);
        source.Flights.Add(new ShipFlightState { ShipId = "ship-a", HeightMeters = 35d });
        source.Devices.Add(new ShipDeviceState { PieceId = "101" });
        SetConfiguration(source.Devices[0], 42f);
        source.LocalWorldStates.Add(new SpaceLocalWorldNetworkState { WorldKey = "ship:ship-a", Revision = 1, Payload = worldPayload });
        source.SimulationSeconds = 10.8d;
        codec.BeginCaptureBatch();
        SpaceNetworkSnapshotDeltaPacket returnPacket = codec.Encode(source, firstCursor, 104);
        var returned = firstReceiver.Decode(returnPacket.Payload, 104);
        returnPacket.ConfirmSent(firstCursor); firstReceiver.ConfirmApplied(returned);
        JObject visibility = new()
        {
            ["left"] = Ship(left.State, "ship-a") == null,
            ["returned"] = Ship(returned.State, "ship-a") != null,
            ["returnedHealth"] = Piece(Ship(returned.State, "ship-a"), "101").Health,
            ["returnedInventory"] = Piece(Ship(returned.State, "ship-a"), "101").SnapshotJson == changedInventory,
            ["returnedWorlds"] = returned.State.LocalWorldStates.Count,
            ["returnedWorldPayload"] = returned.State.LocalWorldStates[0].Payload.SequenceEqual(worldPayload)
        };

        source.LocalWorldStates.Clear();
        ship.PositionX = 201d;
        source.SimulationSeconds = 11d;
        codec.BeginCaptureBatch();
        SpaceNetworkSnapshotDeltaPacket lost = codec.Encode(source, firstCursor, 105);
        lost.ConfirmSent(firstCursor);
        ship.PositionX = 202d;
        source.SimulationSeconds = 11.2d;
        codec.BeginCaptureBatch();
        SpaceNetworkSnapshotDeltaPacket missingBase = codec.Encode(source, firstCursor, 106);
        bool missingRejected = false;
        try { firstReceiver.Decode(missingBase.Payload, 106); }
        catch (InvalidDataException) { missingRejected = true; }
        firstCursor.Reset(); firstReceiver.Reset();
        source.LocalWorldStates.Add(new SpaceLocalWorldNetworkState { WorldKey = "ship:ship-a", Revision = 1, Payload = worldPayload });
        codec.BeginCaptureBatch();
        SpaceNetworkSnapshotDeltaPacket recoveredPacket = codec.Encode(source, firstCursor, 107);
        var recovered = firstReceiver.Decode(recoveredPacket.Payload, 107);
        recoveredPacket.ConfirmSent(firstCursor); firstReceiver.ConfirmApplied(recovered);
        source.LocalWorldStates.Clear();
        ship.PositionX = 203d;
        codec.BeginCaptureBatch();
        SpaceNetworkSnapshotDeltaPacket immediatePacket = codec.Encode(source, firstCursor, 108);
        var immediate = firstReceiver.Decode(immediatePacket.Payload, 108);
        JObject recovery = new()
        {
            ["missingRejected"] = missingRejected, ["fullAgain"] = recovered.IsFullSnapshot,
            ["recoveredX"] = Ship(recovered.State, "ship-a").PositionX,
            ["recoveredHealth"] = Piece(Ship(recovered.State, "ship-a"), "101").Health,
            ["recoveredWorlds"] = recovered.State.LocalWorldStates.Count,
            ["immediateX"] = Ship(immediate.State, "ship-a").PositionX,
            ["worlds"] = immediate.State.LocalWorldStates.Count
        };
        immediatePacket.ConfirmSent(firstCursor); firstReceiver.ConfirmApplied(immediate);
        SetGas(ship.Compartments[0].Gas, 82d);
        source.SimulationSeconds = 11.4d;
        codec.BeginCaptureBatch();
        SpaceNetworkSnapshotDeltaPacket gasPacket = codec.Encode(source, firstCursor, 109);
        var gasSnapshot = firstReceiver.Decode(gasPacket.Payload, 109);
        ShipCompartmentState room = Ship(gasSnapshot.State, "ship-a").Compartments[0];
        JObject gasOnly = new()
        {
            ["encodedFields"] = codec.EncodedFieldCount,
            ["geometryIntact"] = room.CompartmentId == "room-a" && room.VolumeLiters == 1000d &&
                room.Cells.Count == 2 && room.Cells[0].X == -2 && room.Cells[0].Y == 3 &&
                room.Cells[1].X == -1 && room.Cells[1].Y == 3,
            ["gas"] = GetGas(room.Gas), ["bytes"] = gasPacket.Payload.Length
        };
        return new JObject { ["shared"] = shared, ["differentViews"] = differentViews, ["motion"] = movement,
            ["changes"] = changes, ["corrupt"] = corrupt, ["visibility"] = visibility, ["recovery"] = recovery,
            ["gasOnly"] = gasOnly };
    }

    private static SpaceSessionState CreateState(string inventory, byte[] worldPayload)
    {
        var state = new SpaceSessionState { SimulationSeconds = 10d, NavigationSequence = 3 };
        var ship = new ShipState { ShipId = "ship-a", PositionX = 100d };
        ship.Pieces.Add(new ShipPieceState { PieceId = "101", ItemId = "door", Kind = ShipPieceKind.Door, SnapshotJson = inventory });
        ship.Pieces.Add(new ShipPieceState { PieceId = "102", ItemId = "removed" });
        ship.Pieces.Add(new ShipPieceState { PieceId = "104", ItemId = "moved" });
        ship.Compartments.Add(new ShipCompartmentState { CompartmentId = "room-a", VolumeLiters = 1000d,
            Cells = { new ShipCell(-2, 3), new ShipCell(-1, 3) } });
        SetGas(ship.Compartments[0].Gas, 20d);
        state.Ships.Add(ship);
        state.Ships.Add(new ShipState { ShipId = "ship-b" });
        state.Flights.Add(new ShipFlightState { ShipId = "ship-a", Phase = ShipFlightPhase.Cruising });
        state.Flights.Add(new ShipFlightState { ShipId = "ship-b", Phase = ShipFlightPhase.Cruising });
        var device = new ShipDeviceState { PieceId = "101" };
        SetConfiguration(device, 5f);
        state.Devices.Add(device);
        state.Passengers.Add(new SpacePassengerState { ProfileId = "player-a", ShipId = "ship-a", Supported = true, LocalX = 2d });
        state.LooseItems.Add(new SpaceLooseItemState { Id = "loose-a", Snapshot = "original-loose", Motion = new SpaceMotionState() });
        state.Sectors.Add(new UniverseState { Sector = new SpaceSectorId(8, -9),
            Bodies = { new BodyState { BodyId = "body-a", PlanetId = "planet-a", RadiusMeters = 100d } } });
        state.LocalWorldStates.Add(new SpaceLocalWorldNetworkState { WorldKey = "ship:ship-a", Revision = 1, Payload = worldPayload });
        return state;
    }

    private static ShipState Ship(SpaceSessionState state, string id) => state.Ships.Find(value => value.ShipId == id);
    private static ShipPieceState Piece(ShipState ship, string id) => ship?.Pieces.Find(value => value.PieceId == id);

    private static bool RejectCorruptPayload(SpaceNetworkSnapshotDeltaCodec.Receiver receiver, byte[] payload, ulong sequence)
    {
        try { receiver.Decode(payload, sequence); }
        catch (InvalidDataException) { return true; }
        catch (EndOfStreamException) { return true; }
        return false;
    }

    private static byte[] CorruptInventoryFieldLength(byte[] payload)
    {
        var result = (byte[])payload.Clone();
        byte[] name = Encoding.UTF8.GetBytes(nameof(ShipPieceState.SnapshotJson));
        int index = result.AsSpan().IndexOf(name);
        if (index <= 0 || result[index - 1] != name.Length)
            throw new InvalidOperationException("差量夹具未找到船件库存字段。");
        int encodingIndex = index + name.Length;
        byte encoding = result[encodingIndex];
        if ((encoding & 127) != 6) throw new InvalidOperationException("差量夹具库存字段不是字符串。");
        int lengthIndex = encodingIndex + 1 + ((encoding & 128) != 0 ? 4 : 0);
        Buffer.BlockCopy(BitConverter.GetBytes(int.MaxValue), 0, result, lengthIndex, sizeof(int));
        return result;
    }

    // 离线工程的替身只用于验证序列化链路，正式规则使用真实气体和设备配置字段。
    private static void SetGas(FluidInventoryState gas, double value)
    {
#if SPACE_SNAPSHOT_OFFLINE
        gas.TestMassKg = value;
#else
        if (gas.Components.Count == 0) gas.Components.Add(new FluidComponentState { FluidId = "oxygen" });
        gas.Components[0].GasMoles = (decimal)value;
        gas.TotalInternalEnergyJoules = value;
#endif
    }
    private static double GetGas(FluidInventoryState gas)
    {
#if SPACE_SNAPSHOT_OFFLINE
        return gas.TestMassKg;
#else
        return (double)gas.Components[0].GasMoles;
#endif
    }
    private static void SetConfiguration(ShipDeviceState device, float value)
    {
#if SPACE_SNAPSHOT_OFFLINE
        device.TargetPressureKPa = value;
#else
        device.Configuration.PowerWatts = value;
#endif
    }
    private static double GetConfiguration(ShipDeviceState device)
    {
#if SPACE_SNAPSHOT_OFFLINE
        return device.TargetPressureKPa;
#else
        return device.Configuration.PowerWatts;
#endif
    }
    private static bool HasNativeConfiguration()
    {
#if SPACE_SNAPSHOT_OFFLINE
        return false;
#else
        return true;
#endif
    }
    #endregion
}
#endif
