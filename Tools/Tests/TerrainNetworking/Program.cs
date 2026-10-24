using FlatWorld.Networking.Gameplay;

// 和 Lua 共用正式生产类采样，独立 CLR 不执行 Mirror Weaver、连接或 Unity 现场。
var data = GameplayTestNetworkTerrainRules.Capture();
var expected = new Dictionary<string, object>
{
    ["partialAccepted"] = true, ["partialFull"] = false, ["support"] = 91,
    ["baseGround"] = 17, ["baseCost"] = 25, ["liquid"] = 8, ["effectiveGround"] = 91,
    ["walkable"] = true, ["duplicateAccepted"] = true, ["duplicateWrites"] = 0,
    ["removeAccepted"] = true, ["oldSnapshotSupport"] = 0, ["fullAfterReorder"] = true,
    ["removedWalkable"] = false, ["removedLiquid"] = 8, ["weather"] = 45,
    ["wallAccepted"] = true, ["wall"] = 74, ["damage"] = 9, ["oldDeltaWall"] = 74,
    ["invalidAccepted"] = false, ["atomicSupport"] = 0, ["nanAccepted"] = false,
    ["wrongSizeAccepted"] = false, ["wrongTokenAccepted"] = false, ["zeroRevisionAccepted"] = false,
    ["oversizeAccepted"] = false, ["invalidatedFull"] = false, ["duplicateCompleted"] = false,
    ["refreshedFull"] = true, ["reloadedFull"] = false, ["oldChunkTokenAccepted"] = false,
    ["reloadedSupport"] = 0, ["reloadedWall"] = 0, ["reloadedComplete"] = true
};
foreach (var entry in expected)
{
    if (!data.TryGetValue(entry.Key, out object actual) ||
        (entry.Value is bool ? !Equals(actual, entry.Value) : Convert.ToDouble(actual) != Convert.ToDouble(entry.Value)))
        throw new InvalidOperationException($"FAIL {entry.Key}: expected={entry.Value}, actual={actual}");
    Console.WriteLine($"PASS {entry.Key}: {actual}");
}
Console.WriteLine($"PASS {expected.Count} terrain networking assertions (production state/cursor, no Unity session)");
