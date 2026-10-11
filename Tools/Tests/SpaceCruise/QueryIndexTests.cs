using System.Collections;
using FlatWorld.Spaceflight;
using Newtonsoft.Json;

internal static partial class Program
{
    #region 身份查询缓存回归
    private static void QueryIndexMutations()
    {
        var session = new SpaceSession();
        var first = new ShipState { ShipId = "first" };
        session.State.Ships.Add(first);
        Check(ReferenceEquals(first, session.FindShip("first")), "initial member must be indexed");
        var duplicate = new ShipState { ShipId = "first" };
        session.State.Ships.Add(duplicate);
        Check(ReferenceEquals(first, session.FindShip("first")), "duplicate identity keeps first member semantics");
        session.State.Ships.Remove(first);
        Check(ReferenceEquals(duplicate, session.FindShip("first")), "removing first duplicate exposes next member");
        ulong revision = session.State.Ships.Revision;
        var replacement = new ShipState { ShipId = "replacement" };
        ((IList)session.State.Ships)[0] = replacement;
        Check(session.State.Ships.Revision > revision && session.FindShip("first") == null &&
            ReferenceEquals(replacement, session.FindShip("replacement")), "same-count replacement through IList invalidates index");
        var other = new SpaceStateCollection<ShipState> { first };
        session.State.Ships = other;
        Check(ReferenceEquals(first, session.FindShip("first")) && session.FindShip("replacement") == null,
            "same-count collection replacement invalidates index");
        other.Clear();
        Check(session.FindShip("first") == null && session.FindShip(null) == null, "clear and null identity cannot return stale state");

        var flight = new ShipFlightState { ShipId = "ship" };
        var device = new ShipDeviceState { PieceId = "device" };
        session.State.Flights.Add(flight); session.State.Devices.Add(device);
        Check(ReferenceEquals(flight, session.FindFlight("ship")) && ReferenceEquals(device, session.FindDevice("device")),
            "flight and device use independent identity collections");
        session.State.Devices.RemoveAll(value => value.PieceId == "device");
        Check(session.FindDevice("device") == null, "RemoveAll invalidates device index");
        session.State.Ships.Add(first);
        string json = JsonConvert.SerializeObject(session.State);
        Check(Newtonsoft.Json.Linq.JObject.Parse(json)["Ships"] is Newtonsoft.Json.Linq.JArray,
            "versioned collection keeps JSON array format");
        var restored = JsonConvert.DeserializeObject<SpaceSessionState>(json);
        Check(restored.Ships.Count == 1 && restored.Ships[0].ShipId == "first", "array restore binds collection correctly");
        session.State = restored;
        Check(ReferenceEquals(restored.Ships[0], session.FindShip("first")), "restore cannot retain previous session object");
        session.ResetQueryIndex();
        Check(ReferenceEquals(restored.Ships[0], session.FindShip("first")), "explicit session release rebuilds on demand");
    }

    private static void QueryIndexAllocations()
    {
        var session = new SpaceSession();
        for (int i = 0; i < 1000; i++) session.State.Ships.Add(new ShipState { ShipId = i.ToString() });
        string id = "999";
        ShipState expected = session.State.Ships[999];
        for (int i = 0; i < 1000; i++) session.FindShip(id);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++)
            if (!ReferenceEquals(expected, session.FindShip(id))) throw new InvalidOperationException("warm identity query changed");
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"stable identity queries allocated {bytes} bytes");
        Console.WriteLine($"Identity query: 1000 ships, 100000 warm queries, {bytes} B");
        int visited = 0;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int pass = 0; pass < 100; pass++)
            foreach (ShipState ship in session.State.Ships) if (ship != null) visited++;
        bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(visited == 100000 && bytes == 0, $"direct state collection enumeration allocated {bytes} bytes");
    }
    #endregion
}

namespace FlatWorld.Spaceflight
{
    // 只替代会话生命周期，身份查询直接编译生产 partial。
    public sealed partial class SpaceSession
    {
        public SpaceSessionState State = new();
        public ShipState FindShip(string id) => shipQueryIndex.Find(State.Ships, id);
        public ShipFlightState FindFlight(string id) => flightQueryIndex.Find(State.Flights, id);
        public ShipDeviceState FindDevice(string id) => deviceQueryIndex.Find(State.Devices, id);
        public void ResetQueryIndex() => ClearQueryIndexes();
    }
}
