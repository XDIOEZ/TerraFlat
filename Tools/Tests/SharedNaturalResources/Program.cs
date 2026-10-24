using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlatWorld.WorldModel;

#region 规则夹具
var root = Path.GetFullPath(args[0]);
int checks = 0, sharedCopies = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++;
}
EcologySpawnRuleSnapshot Rule(int radius = 8, int height = 5, int spacing = 64) => new(
    "probe.shared", "probe.mine", 1, 1d, 1d, 0, 0, 1, 0, 1, 0, 1,
    patchSpacing: spacing, sharedRadius: radius, footprintWidth: 13, footprintHeight: height);
var rule = Rule();
var numbers = new Dictionary<string, double> { ["river.enabled"] = 0, ["cave.portal.enabled"] = 0,
    ["climate.temperature.blendRadius"] = 0, ["structures.enabled"] = 0 };
var profile = new ChunkGenerationProfileSnapshot("shared-regression", 79, 32, 32, numbers, ecologyRules: new[] { rule });
ChunkGenerationRequest Request(Int2 chunk, int seed = 1, ChunkGenerationTopologySnapshot topology = default,
    ChunkGenerationProfileSnapshot configured = null) => new(1, new WorldAddress("surface", chunk), seed, 1, configured ?? profile, topology);
ChunkEcologyData Plan(ChunkGenerationRequest request) => ChunkEcologyGenerator.GenerateShared(request, null, CancellationToken.None);
var topology = new ChunkGenerationTopologySnapshot(new Int2(-64, -64), new Int2(128, 128));
bool seam = false, fourWay = false;
for (int seed = 1; seed <= 64; seed++)
{
    var ids = new Dictionary<Int2, int>();
    var owners = new Dictionary<Int2, HashSet<Int2>>();
    for (int cy = -64; cy < 64; cy += 32)
    for (int cx = -64; cx < 64; cx += 32)
    {
        Int2 origin = new(cx, cy);
        foreach (var p in Plan(Request(origin, seed, topology)).Placements)
        {
            var anchor = p.SharedAnchor!.Value;
            Check(p.Guid < 0 && p.IsShared && p.HostGuid == 0, "共享身份域/宿主错误");
            Check(topology.NormalizeX(cx + p.LocalX) == anchor.X &&
                topology.NormalizeY(cy + p.LocalY) == anchor.Y, "规范锚点与局部镜像不一致");
            if (ids.TryGetValue(anchor, out int previous))
            { Check(previous == p.Guid, "同一实体在不同区块改变了身份"); sharedCopies++; }
            else { ids.Add(anchor, p.Guid); owners.Add(anchor, new HashSet<Int2>()); }
            owners[anchor].Add(origin);
        }
    }
    foreach (var set in owners.Values)
    {
        seam |= set.Any(c => c.X == -64) && set.Any(c => c.X == 32) ||
                set.Any(c => c.Y == -64) && set.Any(c => c.Y == 32);
        fourWay |= set.Count >= 4;
    }
    for (int cy = 32; cy >= -64; cy -= 32)
    for (int cx = 32; cx >= -64; cx -= 32)
        foreach (var p in Plan(Request(new Int2(cx, cy), seed, topology)).Placements)
            Check(ids[p.SharedAnchor!.Value] == p.Guid, "反向加载依赖之前的区块查询");
}
Check(seam && fourWay && sharedCopies > 0, "未覆盖世界接缝或四区块交界");
Check(ChunkEcologyGenerator.SharedOwnerOrigin(new Int2(-1, -33), 32, 32).Equals(new Int2(-32, -64)), "负坐标错误向零取整");
Check(ChunkEcologyGenerator.SharedFootprintContains(topology, rule, new Int2(63, 0), new Int2(-64, 0)), "跨世界边界占格丢失");
Check(Plan(Request(new Int2(0, 0), 1, new ChunkGenerationTopologySnapshot(new Int2(0, 0), new Int2(32, 32)))).Count == 0,
    "过小世界让矿山镜像与自身重叠");
bool cancelled = false;
try { ChunkEcologyGenerator.GenerateShared(Request(new Int2(0, 0)), null, new CancellationToken(true)); }
catch (OperationCanceledException) { cancelled = true; }
Check(cancelled, "已取消请求继续生成");
Check(ChunkEcologyGenerator.GenerateShared(Request(new Int2(0, 0)), (_, _) => false, CancellationToken.None).Count == 0,
    "无效地形未拒绝");
var changed = new ChunkGenerationProfileSnapshot("shared-regression", 79, 32, 32, numbers, ecologyRules: new[] { Rule(9) });
Check(profile.EcologyFingerprint != changed.EcologyFingerprint, "共享覆盖半径未参与生成指纹");
bool invalid = false;
try { Rule(spacing: 16); } catch (ArgumentException) { invalid = true; }
Check(invalid, "候选间距小于资源边界仍被接受");
#endregion

#region 引用顺序与最后释放
var leases = new SharedEntityLeaseSet<object>();
object left = new(), right = new();
Check(leases.Add(right) && leases.Add(left) && !leases.Add(right) && leases.Count == 2, "代理重复引用");
Check(leases.Primary == right, "右侧先加载不能取得唯一宿主");
Check(leases.Remove(right) && leases.Primary == left && leases.Count == 1, "移交时提前释放共享实体");
Check(!leases.Remove(right) && leases.Count == 1, "重复卸载导致引用下溢");
Check(leases.Remove(left) && leases.Primary == null && leases.Count == 0, "最后引用未释放");
leases.Add(left); leases.Add(right); leases.Remove(left);
Check(leases.Primary == right, "左侧先加载的交接不对称");
leases.Clear();
Check(leases.Count == 0, "世界结束残留旧引用");
#endregion

#region 正式共享生成与跨区块地形拒绝
var append = typeof(DeterministicChunkGenerator).GetMethod("AppendSharedResources", BindingFlags.NonPublic | BindingFlags.Instance)!;
ChunkGenerationRequest ghostRequest = default;
NaturalItemPlacement target = default;
for (int seed = 1; seed <= 64 && !target.IsShared; seed++)
for (int cx = -64; cx <= 64 && !target.IsShared; cx += 32)
{
    var request = Request(new Int2(cx, 0), seed);
    foreach (var p in Plan(request).Placements)
        if ((p.LocalX < 0 && p.LocalX >= -6 || p.LocalX >= 32 && p.LocalX <= 37) && p.LocalY >= 0 && p.LocalY < 32)
        { ghostRequest = request; target = p; break; }
}
Check(target.IsShared, "未找到覆盖实际占地的冷宿主测试点");
Int2? blocked = null;
var allocated = new List<ChunkTerrainBuffer>();
void FlatTerrain(ChunkGenerationRequest request, ChunkTerrainBuffer buffer, CancellationToken token)
{
    allocated.Add(buffer);
    for (int y = 0; y < buffer.Height; y++)
    for (int x = 0; x < buffer.Width; x++)
    {
        var cell = new Int2(request.Topology.NormalizeX(request.Address.ChunkOrigin.X + x),
            request.Topology.NormalizeY(request.Address.ChunkOrigin.Y + y));
        bool obstacle = blocked.HasValue && cell.Equals(blocked.Value);
        buffer.SetCell(x, y, new TerrainCell(1, 0, obstacle ? 8 : 0, 4, 1,
            obstacle ? TerrainCellFlags.Blocking : TerrainCellFlags.Walkable));
        buffer.SetLiquid(x, y, 0, 0);
        foreach (string channel in new[] { "height", "temperature", "precipitation" })
            buffer.SetEnvironmentValue(channel, x, y, .5f);
        buffer.SetEnvironmentValue("structure", x, y, 0f);
        buffer.SetEnvironmentValue("riverFloodplain", x, y, 0f);
    }
}
var generator = new DeterministicChunkGenerator(terrainDecorator: FlatTerrain);
ChunkEcologyData Append(ChunkGenerationRequest request, ChunkEcologyData existing)
{
    allocated.Clear();
    using var terrain = new ChunkTerrainBuffer(32, 32);
    FlatTerrain(request, terrain, CancellationToken.None);
    var data = (ChunkEcologyData)append.Invoke(generator, new object[] { request, terrain, existing, CancellationToken.None })!;
    Check(!terrain.IsDisposed, "共享生成错误释放借用的本区块地形");
    Check(allocated.Skip(1).All(t => t.IsDisposed), "邻区验证临时地形未释放");
    return data;
}
var ghost = Append(ghostRequest, ChunkEcologyData.Empty);
Check(ghost.Placements.Any(p => p.Guid == target.Guid), "锚点所属区块未加载时不能生成代理");
Int2 targetAnchor = target.SharedAnchor!.Value;
var ownerRequest = Request(ChunkEcologyGenerator.SharedOwnerOrigin(targetAnchor, 32, 32), ghostRequest.WorldSeed);
Check(Append(ownerRequest, ChunkEcologyData.Empty).Placements.Any(p => p.Guid == target.Guid && p.SharedAnchor!.Value.Equals(targetAnchor)),
    "主区块与先加载的代理身份不同");
int localX = Math.Clamp(target.LocalX, 0, 31), localY = Math.Clamp(target.LocalY, 0, 31);
var existing = new ChunkEcologyData(new[] {
    new NaturalItemPlacement(11, "probe.small", localX, localY, 0, 0, "normal"),
    new NaturalItemPlacement(12, "probe.companion", localX, localY, 0, 0, "companion", hostGuid: 11) });
var filtered = Append(ghostRequest, existing);
Check(filtered.Placements.All(p => p.Guid != 11 && p.Guid != 12), "大矿山占地内仍生成普通资源或孤立伴生物");
blocked = targetAnchor;
Check(Append(ghostRequest, ChunkEcologyData.Empty).Placements.All(p => p.Guid != target.Guid),
    "冷宿主区块内的障碍未参与整个矿山的合法性判断");
blocked = null;
#endregion

#region 正式内容与像素单位
using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Assets/StreamingAssets/GameConfig/Items/shells/giant_mines.json")));
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Assets/StreamingAssets/GameConfig/Items/item-manifest.json")));
using var loot = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Assets/StreamingAssets/GameConfig/LootTables/loot-tables.json")));
var definitions = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
foreach (var package in manifest.RootElement.GetProperty("packages").EnumerateArray())
{
    if (!package.GetProperty("enabled").GetBoolean()) continue;
    using var packageDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
        "Assets/StreamingAssets/GameConfig/Items", package.GetProperty("path").GetString()!)));
    foreach (var item in packageDocument.RootElement.GetProperty("items").EnumerateArray())
        definitions.Add(item.GetProperty("id").GetString()!, item.Clone());
}
JsonElement InheritedProperty(JsonElement item, string name) => item.TryGetProperty(name, out var property)
    ? property : InheritedProperty(definitions[item.GetProperty("parent").GetString()!], name);
var lootTables = loot.RootElement.GetProperty("lootTables").EnumerateArray()
    .ToDictionary(table => table.GetProperty("id").GetString()!, table => table);
var expectedMines = new HashSet<string> { "Stone", "Coal", "Copper", "Iron", "Tin", "Niter", "Diamond", "Gold", "Obsidian", "Sulfur", "Tungsten" };
var mines = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
Check(mines.Length == expectedMines.Count, "巨型矿山矿种数量不完整");
foreach (var mine in mines)
{
    string id = mine.GetProperty("id").GetString()!;
    string mineral = id[5..^6];
    Check(expectedMines.Remove(mineral) && id == "Mine_" + mineral + "_Large", "巨型矿山矿种缺失或重复");
    string smallId = "Mine_" + mineral;
    Check(mine.GetProperty("parent").GetString() == smallId && definitions.ContainsKey(smallId), "巨矿没有直接继承对应小矿山");
    var small = definitions[smallId];
    var cells = mine.GetProperty("worldGridOccupancy").GetProperty("cells").EnumerateArray()
        .Select(c => (X: c.GetProperty("x").GetInt32(), Y: c.GetProperty("y").GetInt32())).ToHashSet();
    Check(cells.Count == 26 && cells.All(c => c.X >= -6 && c.X <= 6 && (c.Y == -2 || c.Y == -1)), "巨矿没有只保留底部占格");
    var visual = mine.GetProperty("visual");
    var collider = visual.GetProperty("collider");
    double height = collider.GetProperty("size").GetProperty("y").GetDouble();
    double centerY = collider.GetProperty("offset").GetProperty("y").GetDouble();
    Check(collider.GetProperty("size").GetProperty("x").GetDouble() == 13 &&
        Math.Abs(height - 5d / 3d) < .00001 && Math.Abs(centerY - height / 2d + 2.5d) < .00001,
        "巨矿碰撞未固定底边并缩为三分之一");
    var health = mine.GetProperty("modules").GetProperty("生命值系统模块").GetProperty("parameters");
    var data = health.GetProperty("Data");
    Check(data.GetProperty("Hp").GetInt32() == 200000 && data.GetProperty("MaxHp").GetInt32() == 200000,
        "巨矿生命不是二十万");
    Check(data.EnumerateObject().Count() == 2 && !health.TryGetProperty("weakTool", out _) &&
        mine.GetProperty("modules").EnumerateObject().Count() == 1, "巨矿覆盖了原矿山防御、弱点或开采模块");
    var hit = health.GetProperty("$collider2D");
    Check(hit.GetProperty("size").GetProperty("x").GetDouble() == 13.8 &&
        hit.GetProperty("size").GetProperty("y").GetDouble() == 9.6 &&
        hit.GetProperty("offset").GetProperty("y").GetDouble() == 2, "缩碰撞时改变了完整受击范围");
    Check(Math.Abs(visual.GetProperty("rendererLocalScale").GetProperty("x").GetDouble() - 14.02) < .001 &&
        Math.Abs(visual.GetProperty("rendererLocalPosition").GetProperty("y").GetDouble() - 1.72) < .001 &&
        !visual.TryGetProperty("spriteAddress", out _), "巨矿没有保留对应矿物贴图和原视觉尺寸");
    string tableId = InheritedProperty(mine, "lootTableId").GetString()!;
    string smallTableId = InheritedProperty(small, "lootTableId").GetString()!;
    var products = lootTables[tableId].GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("itemId").GetString()).ToHashSet();
    var smallProducts = lootTables[smallTableId].GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("itemId").GetString()).ToHashSet();
    Check(products.SetEquals(smallProducts) && (mineral == "Stone" ? tableId == "Mine_Stone_Large_Default" : tableId == smallTableId),
        "巨矿掉落没有保留对应矿物及副产物");
}
foreach (string meta in Directory.GetFiles(Path.Combine(root, "Assets/6_Art/Generated/Environment/MineralsUnified"), "*.png.meta", SearchOption.AllDirectories))
{
    var text = File.ReadAllText(meta);
    Check(Regex.IsMatch(text, @"spritePixelsToUnits: 1402\r?\n") && Regex.IsMatch(text, @"filterMode: 0\r?\n"),
        "普通矿物 PPU/采样回归: " + meta);
}
#endregion

Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks, sharedCopies, worldSeam = seam, fourWay,
    coverage = "deterministic planning, reversed loading, leases, cold-owner terrain, occupancy, import, content",
    notExecuted = "Unity PlayMode collider/weapon/save integration and visual acceptance" }, new JsonSerializerOptions { WriteIndented = true }));
