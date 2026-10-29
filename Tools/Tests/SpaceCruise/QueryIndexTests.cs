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

    private static void BodyIdentityIndexLifecycle()
    {
        SpaceSectorId remoteSector = new(long.MaxValue - 1, long.MinValue + 1);
        string primaryAlias = SpaceSectorCoordinates.BodyId(default, "alias");
        string remoteId = SpaceSectorCoordinates.BodyId(remoteSector, "remote");
        BodyState primary = new() { BodyId = SpaceSectorCoordinates.BodyId(default, "main"),
            TemplateBodyId = remoteId, PlanetId = "primary", RadiusMeters = 1d };
        BodyState aliasTarget = new() { BodyId = primaryAlias, TemplateBodyId = "local", PlanetId = "alias", RadiusMeters = 1d };
        BodyState localAlias = new() { BodyId = SpaceSectorCoordinates.BodyId(default, "third"),
            TemplateBodyId = primaryAlias, PlanetId = "third", RadiusMeters = 1d };
        BodyState remote = new() { BodyId = remoteId, TemplateBodyId = "remote", PlanetId = "remote", RadiusMeters = 1d };
        BodyState remoteOnly = new() { BodyId = SpaceSectorCoordinates.BodyId(remoteSector, "only"),
            TemplateBodyId = "only", PlanetId = "remote-only", RadiusMeters = 1d };
        UniverseSimulation main = new(new UniverseState { Bodies = new() { primary, aliasTarget, localAlias } });
        UniverseSimulation distant = new(new UniverseState { Sector = remoteSector, Bodies = new() { remote, remoteOnly } });
        var index = new SpaceBodyIdentityIndex();
        index.Register(main);
        Check(!index.TryGetBody(remoteOnly.BodyId, main, out _), "unregistered sector cannot resolve");
        index.Register(distant);
        string[] queries = { primary.BodyId, primaryAlias, remoteId, remoteOnly.BodyId, "local", "remote", "only",
            "sector:x:0/body:main", "sector:5:6/body:missing", "", null };
        foreach (string query in queries)
        {
            BodyState expected = null;
            if (!string.IsNullOrWhiteSpace(query))
            {
                if (!SpaceSectorCoordinates.TryParseIdentity(query, out _, out _)) main.TryGetBody(query, out expected);
                else if (!main.TryGetBody(query, out expected)) distant.TryGetBody(query, out expected);
            }
            bool actualResult = index.TryGetBody(query, main, out BodyState actual);
            Check(actualResult == (expected != null) && ReferenceEquals(actual, expected), "indexed result matches sector lookup precedence");
        }
        Check(index.TryGetBody(remoteId, main, out BodyState conflict) && ReferenceEquals(conflict, primary),
            "first sector qualified template retains precedence over later body identity");
        Check(index.TryGetBody(primaryAlias, main, out conflict) && ReferenceEquals(conflict, aliasTarget),
            "within a sector exact body identity precedes a template alias");
        for (int i = 0; i < 1000; i++) index.TryGetBody(remoteOnly.BodyId, main, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++)
            if (!index.TryGetBody(remoteOnly.BodyId, main, out _)) throw new InvalidOperationException("warm body lookup changed");
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"stable body identity lookup allocated {bytes} bytes");
        index.Clear();
        Check(!index.TryGetBody(remoteOnly.BodyId, main, out _), "cleared index cannot return prior sector object");
        BodyState restored = JsonConvert.DeserializeObject<BodyState>(JsonConvert.SerializeObject(remoteOnly));
        UniverseSimulation restoredSector = new(new UniverseState { Sector = remoteSector, Bodies = new() { restored } });
        index.Register(restoredSector);
        Check(index.TryGetBody(restored.BodyId, main, out BodyState found) && ReferenceEquals(restored, found) &&
            !ReferenceEquals(remoteOnly, found), "rebuilt index resolves the restored object");
        Console.WriteLine($"Body identity query: 100000 warm queries, {bytes} B");
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
