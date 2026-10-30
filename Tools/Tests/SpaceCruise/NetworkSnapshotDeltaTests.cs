using Newtonsoft.Json.Linq;

internal static partial class Program
{
    #region 太空网络差量离线断言
    private static void NetworkSnapshotDelta()
    {
        JObject result = GameplayTestSpaceNetworkSnapshotDeltaProbe.Capture(new JObject());
        JObject shared = (JObject)result["shared"];
        Check(shared.Value<bool>("firstFull") && shared.Value<bool>("secondFull"), "both observers need a full first snapshot");
        Check(shared.Value<int>("capturedFirst") > 0 && shared.Value<int>("capturedFirst") == shared.Value<int>("capturedSecond"),
            "a second observer must reuse entity capture in the same batch");
        Check(shared.Value<int>("encodedFirst") > 0 && shared.Value<int>("encodedFirst") == shared.Value<int>("encodedSecond"),
            "a second observer must reuse encoded fields in the same batch");
        Check(shared.Value<int>("compressedFirst") > 0, "large initial fields must be compressed");
        Check(shared.Value<int>("firstWorlds") == 1 && shared.Value<int>("secondWorlds") == 1 &&
            shared.Value<bool>("firstWorldPayload") && shared.Value<bool>("secondWorldPayload"), "each first packet supplies its local world");

        JObject views = (JObject)result["differentViews"];
        Check(views.Value<bool>("wideShipA") && views.Value<bool>("wideShipB") &&
            !views.Value<bool>("narrowShipA") && views.Value<bool>("narrowShipB"), "different observers must not inherit each other's ship visibility");
        Check(views.Value<int>("narrowFlights") == 1 && views.Value<string>("narrowFlightShip") == "ship-b" &&
            views.Value<int>("narrowPassengers") == 0 && views.Value<int>("narrowLooseItems") == 0 && views.Value<int>("narrowWorlds") == 0,
            "different observers must not inherit flight, passenger, loose item or local world records");

        JObject motion = (JObject)result["motion"];
        Check(motion.Value<int>("deltaBytes") < shared.Value<int>("firstBytes"), "motion update should be smaller than the full snapshot");
        Check(motion.Value<int>("encodedFields") > 0 && motion.Value<int>("compressedFields") == 0,
            "a small movement must not recompress unchanged large fields");
        Check(motion.Value<double>("shipX") == 120.5d && motion.Value<double>("secondShipX") == 120.5d &&
            motion.Value<double>("passengerX") == 4.5d, "both independent observers receive the new motion");
        Check(motion.Value<double>("pieceHealth") == 100d && motion.Value<bool>("inventoryIntact") && motion.Value<bool>("sectorIntact"),
            "client edits and discarded pending decode must not mutate the receiver baseline");
        Check(motion.Value<int>("worlds") == 0 && motion.Value<int>("secondWorlds") == 0,
            "local world payloads must not replay in subsequent packets");

        JObject changes = (JObject)result["changes"];
        Check(changes.Value<bool>("doorOpen") && changes.Value<double>("health") == 73d && changes.Value<bool>("inventoryChanged"),
            "in-place door, health, and inventory changes must reach the receiver");
        Check(changes.Value<double>("gas") == 81d && changes.Value<double>("configuration") == 42d && changes.Value<double>("height") == 35d,
            "nested gas, configuration and flight state changes must reach the receiver");
        Check(changes.Value<string>("looseSnapshot") == "changed-loose" && changes.Value<double>("looseX") == 33d,
            "loose item state and motion must both update");
        Check(changes.Value<bool>("added") && changes.Value<bool>("removed") && changes.Value<bool>("transferred"),
            "pieces must support addition, removal and moving between ships");
        Check(!changes.Value<bool>("oldDoorOpen") && changes.Value<double>("oldHealth") == 100d,
            "decoding the new snapshot must preserve the previous presentation state");

        JObject corrupt = (JObject)result["corrupt"];
        Check(corrupt.Value<bool>("truncatedRejected") && corrupt.Value<bool>("invalidLengthRejected") &&
            corrupt.Value<bool>("validAfterRejection"), "truncated or malformed lengths must not poison the baseline or prevent a valid retry");

        JObject visibility = (JObject)result["visibility"];
        Check(visibility.Value<bool>("left") && visibility.Value<bool>("returned") && visibility.Value<double>("returnedHealth") == 73d &&
            visibility.Value<bool>("returnedInventory"), "leaving and returning visibility must restore complete current ship state");
        Check(visibility.Value<int>("returnedWorlds") == 1 && visibility.Value<bool>("returnedWorldPayload"),
            "returning visibility must carry the local world again");

        JObject recovery = (JObject)result["recovery"];
        Check(recovery.Value<bool>("missingRejected") && recovery.Value<bool>("fullAgain"), "missing base must be rejected before a fresh full snapshot");
        Check(recovery.Value<double>("recoveredX") == 202d && recovery.Value<double>("recoveredHealth") == 73d &&
            recovery.Value<int>("recoveredWorlds") == 1, "full recovery must restore current state and local world");
        Check(recovery.Value<double>("immediateX") == 203d && recovery.Value<int>("worlds") == 0,
            "the next batch observes an immediate change and does not replay its world");
        JObject gas = (JObject)result["gasOnly"];
        Check(gas.Value<int>("encodedFields") == 1 && gas.Value<bool>("geometryIntact") && gas.Value<double>("gas") == 82d &&
            gas.Value<int>("bytes") < shared.Value<int>("firstBytes"), "gas-only changes must retain compartment geometry and encode only gas");
        Console.WriteLine($"Space delta: full {shared.Value<int>("firstBytes")} B, movement {motion.Value<int>("deltaBytes")} B, gas {gas.Value<int>("bytes")} B; " +
            "offline doubles cover gas data and device target pressure, native gas/configuration require Unity rules.");
    }
    #endregion
}

// 离线 Resources 不加载配置文件，补齐未执行分支所需的解析入口。
internal static class JsonConfigurationCache
{
    public static T Deserialize<T>(string json) where T : class => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
}

namespace FlatWorld.Spaceflight
{
    // 本组不使用矿石和电梯，仅满足会话状态的空集合类型。
    public sealed class SpaceOreAsteroidState { public string AsteroidId; }
    public sealed class SpaceElevatorState { }
}

namespace MemoryPack
{
    // JSON 差量测试不运行 MemoryPack，仅保留编译所需标记。
    public sealed class MemoryPackableAttribute : System.Attribute { }
    public sealed class MemoryPackIgnoreAttribute : System.Attribute { }
    public sealed class MemoryPackOrderAttribute : System.Attribute { public MemoryPackOrderAttribute(int order) { } }
}
