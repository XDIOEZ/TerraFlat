using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using FlatWorld.NaturalEntities;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 大资源代理与几何回归

    private static JObject WorldSharedNaturalResources(JObject args)
    {
        string scenario = Text(args, "scenario", string.Empty);
        if (scenario == "giant_generation") return WorldGiantMineGeneration();
        if (scenario == "giant_clearance") return WorldGiantMineClearance();
        EcologySpawnRuleSnapshot Rule(int radius = 8, int height = 5) => new(
            "probe.shared", "probe.mine", 1, 1d, 1d, 0, 0, 1, 0, 1, 0, 1,
            patchSpacing: 64, sharedRadius: radius, footprintWidth: 13, footprintHeight: height);
        var rule = Rule();
        var profile = new ChunkGenerationProfileSnapshot("shared-probe", 79, 32, 32, ecologyRules: new[] { rule });
        var topology = new ChunkGenerationTopologySnapshot(new Int2(-64, -64), new Int2(128, 128));
        var identity = new Dictionary<Int2, int>();
        var coverage = new Dictionary<Int2, HashSet<Int2>>();
        bool stable = true, sameIdentity = true, seam = false, fourWay = false;
        int sharedCopies = 0;
        for (int seed = 1; seed <= 16; seed++)
        {
            identity.Clear(); coverage.Clear();
            for (int cy = -64; cy < 64; cy += 32)
            for (int cx = -64; cx < 64; cx += 32)
            {
                var origin = new Int2(cx, cy);
                var request = new ChunkGenerationRequest(1, new FlatWorld.WorldModel.WorldAddress("surface", origin), seed, 1, profile, topology);
                var planned = ChunkEcologyGenerator.GenerateShared(request, null, CancellationToken.None);
                var repeated = ChunkEcologyGenerator.GenerateShared(request, null, CancellationToken.None);
                stable &= planned.Count == repeated.Count;
                for (int i = 0; i < planned.Count; i++)
                {
                    var placement = planned.Placements[i];
                    stable &= placement.Guid == repeated.Placements[i].Guid && placement.IsShared && placement.Guid < 0;
                    Int2 anchor = placement.SharedAnchor.Value;
                    sameIdentity &= topology.NormalizeX(cx + placement.LocalX) == anchor.X &&
                        topology.NormalizeY(cy + placement.LocalY) == anchor.Y;
                    if (identity.TryGetValue(anchor, out int guid))
                    { sameIdentity &= guid == placement.Guid; sharedCopies++; }
                    else { identity.Add(anchor, placement.Guid); coverage.Add(anchor, new HashSet<Int2>()); }
                    coverage[anchor].Add(origin);
                }
            }
            foreach (var pair in coverage)
            {
                bool left = false, right = false, bottom = false, top = false;
                foreach (var chunk in pair.Value)
                { left |= chunk.X == -64; right |= chunk.X == 32; bottom |= chunk.Y == -64; top |= chunk.Y == 32; }
                seam |= left && right || bottom && top;
                fourWay |= pair.Value.Count >= 4;
            }
            // 后加载的区块只从固定种子复算，不消费前一次查询留下的状态。
            for (int cy = 32; cy >= -64; cy -= 32)
            for (int cx = 32; cx >= -64; cx -= 32)
            {
                var request = new ChunkGenerationRequest(1, new FlatWorld.WorldModel.WorldAddress("surface", new Int2(cx, cy)), seed, 2, profile, topology);
                foreach (var placement in ChunkEcologyGenerator.GenerateShared(request, null, CancellationToken.None).Placements)
                    stable &= identity.TryGetValue(placement.SharedAnchor.Value, out int guid) && guid == placement.Guid;
            }
        }
        var baseRequest = new ChunkGenerationRequest(1, new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0)), 1, 1, profile);
        bool cancelled = false;
        try { ChunkEcologyGenerator.GenerateShared(baseRequest, null, new CancellationToken(true)); }
        catch (OperationCanceledException) { cancelled = true; }
        var tiny = new ChunkGenerationRequest(1, baseRequest.Address, 1, 1, profile,
            new ChunkGenerationTopologySnapshot(new Int2(0, 0), new Int2(32, 32)));
        var owners = new SharedEntityLeaseSet<object>();
        object a = new(), b = new();
        owners.Add(b); owners.Add(a);
        bool unique = !owners.Add(b) && owners.Count == 2 && owners.Primary == b;
        owners.Remove(b);
        bool handoff = owners.Count == 1 && owners.Primary == a && !owners.Remove(b);
        owners.Remove(a);
        bool finalRelease = owners.Count == 0 && owners.Primary == null;
        var saved = EcologyRuleSaveData.FromSnapshot(rule).ToSnapshot();
        bool save = saved.SharedRadius == 8 && saved.FootprintWidth == 13 && saved.FootprintHeight == 5;
        var altered = new ChunkGenerationProfileSnapshot("shared-probe", 79, 32, 32, ecologyRules: new[] { Rule(9) });
        var body = new Bounds(Vector3.zero, new Vector3(13f, 5f, .01f));
        Vector2 dropRight = NaturalEntityEcsService.AccessibleResourceDrop(body, new Vector2(9f, 0f));
        Vector2 dropLeft = NaturalEntityEcsService.AccessibleResourceDrop(body, new Vector2(-9f, 0f));
        return new JObject
        {
            ["stable"] = stable, ["sameIdentity"] = sameIdentity, ["sharedCopies"] = sharedCopies,
            ["worldSeam"] = seam, ["fourWay"] = fourWay,
            ["negativeOwner"] = ChunkEcologyGenerator.SharedOwnerOrigin(new Int2(-1, -33), 32, 32).Equals(new Int2(-32, -64)),
            ["rejected"] = ChunkEcologyGenerator.GenerateShared(baseRequest, (_, _) => false, CancellationToken.None).Count == 0,
            ["tinyWorld"] = ChunkEcologyGenerator.GenerateShared(tiny, null, CancellationToken.None).Count == 0,
            ["cancelled"] = cancelled, ["uniqueLease"] = unique, ["handoff"] = handoff, ["finalRelease"] = finalRelease,
            ["savedGeometry"] = save, ["fingerprint"] = profile.EcologyFingerprint != altered.EcologyFingerprint,
            ["edgeLoot"] = WorldTopologyRuntime.ShortestDelta(body.center, dropRight).x > body.extents.x &&
                WorldTopologyRuntime.ShortestDelta(body.center, dropLeft).x < -body.extents.x
        };
    }

    // 采样正式互斥候选和冻结配置，概率与覆盖断言留在 Lua。
    private static JObject WorldGiantMineGeneration()
    {
        string[] minerals = { "Stone", "Coal", "Copper", "Iron", "Tin", "Niter", "Diamond", "Gold", "Obsidian", "Sulfur", "Tungsten" };
        EcologySpawnRuleSnapshot Rule(string name, double chance, int spacing = 64) => new(
            "probe.shared." + name, "Mine_" + name + "_Large", 1, chance, 1d, 0, 0, 1, 0, 1, 0, 1,
            patchSpacing: spacing, sharedRadius: 8, footprintWidth: 13, footprintHeight: 5);
        var rules = new List<EcologySpawnRuleSnapshot>();
        foreach (string mineral in minerals) rules.Add(Rule(mineral, 1d / minerals.Length));
        ChunkGenerationProfileSnapshot Profile(IEnumerable<EcologySpawnRuleSnapshot> values, double multiplier = 1d)
            => new("shared-choices", 79, 128, 128, ecologyGlobalMultiplier: multiplier, ecologyRules: values);
        var profile = Profile(rules);
        rules.Reverse();
        var reversed = Profile(rules);
        var saturated = Profile(rules, 100d);
        var full = Profile(new[] { Rule("full", 1d) });
        var zero = Profile(new[] { Rule("zero", 0d) });
        var disabled = Profile(rules, 0d);
        var topology = new ChunkGenerationTopologySnapshot(new Int2(-64, -64), new Int2(128, 128));
        ChunkGenerationRequest Request(int seed, ChunkGenerationProfileSnapshot selected) => new(1,
            new FlatWorld.WorldModel.WorldAddress("surface", new Int2(-64, -64)), seed, 1, selected, topology);
        var counts = new JObject();
        foreach (string mineral in minerals) counts["Mine_" + mineral + "_Large"] = 0;
        bool stableOrder = true, exclusive = true, separated = true, fullCoverage = true;
        int fullCount = 0, mixedCount = 0, zeroCount = 0, disabledCount = 0, saturatedCount = 0;
        for (int seed = 1; seed <= 128; seed++)
        {
            var planned = ChunkEcologyGenerator.GenerateShared(Request(seed, profile), null, CancellationToken.None);
            var repeated = ChunkEcologyGenerator.GenerateShared(Request(seed, reversed), null, CancellationToken.None);
            int expected = ChunkEcologyGenerator.GenerateShared(Request(seed, full), null, CancellationToken.None).Count;
            var saturatedPlan = ChunkEcologyGenerator.GenerateShared(Request(seed, saturated), null, CancellationToken.None);
            fullCount += expected; mixedCount += planned.Count; saturatedCount += saturatedPlan.Count;
            zeroCount += ChunkEcologyGenerator.GenerateShared(Request(seed, zero), null, CancellationToken.None).Count;
            disabledCount += ChunkEcologyGenerator.GenerateShared(Request(seed, disabled), null, CancellationToken.None).Count;
            fullCoverage &= planned.Count == expected && saturatedPlan.Count == expected;
            stableOrder &= planned.Count == repeated.Count;
            var anchors = new HashSet<Int2>();
            for (int i = 0; i < planned.Count; i++)
            {
                NaturalItemPlacement item = planned.Placements[i];
                counts[item.ItemId] = (int)counts[item.ItemId] + 1;
                exclusive &= anchors.Add(item.SharedAnchor.Value);
                if (i < repeated.Count)
                    stableOrder &= item.Guid == repeated.Placements[i].Guid &&
                        item.ItemId == repeated.Placements[i].ItemId &&
                        item.SharedAnchor.Value.Equals(repeated.Placements[i].SharedAnchor.Value);
                for (int j = 0; j < i; j++)
                {
                    Int2 other = planned.Placements[j].SharedAnchor.Value;
                    int dx = Math.Abs(item.SharedAnchor.Value.X - other.X);
                    int dy = Math.Abs(item.SharedAnchor.Value.Y - other.Y);
                    dx = Math.Min(dx, 128 - dx); dy = Math.Min(dy, 128 - dy);
                    separated &= dx > 16 || dy > 16;
                }
            }
        }
        bool mixedSpacingRejected = false;
        try { Profile(new[] { Rule("a", .5d), Rule("b", .5d, 128) }); }
        catch (InvalidOperationException) { mixedSpacingRejected = true; }
        var configured = new JArray();
        foreach (EcologySpawnRuleSnapshot rule in NaturalGenerationRuleCatalogLoader.LoadBuiltIn().EcologyRules)
            if (rule.SharedRadius > 0)
                configured.Add(new JObject { ["id"] = rule.RuleId, ["itemId"] = rule.ItemId,
                    ["chance"] = rule.SpawnChance * rule.SpawnChanceMultiplier * rule.PatchChance,
                    ["biomeMask"] = rule.BiomeMask, ["environment"] = rule.RequiredEnvironmentLayer });
        return new JObject { ["counts"] = counts, ["configured"] = configured,
            ["stableOrder"] = stableOrder, ["exclusive"] = exclusive, ["separated"] = separated,
            ["fullCoverage"] = fullCoverage, ["fullCount"] = fullCount, ["mixedCount"] = mixedCount,
            ["saturatedCount"] = saturatedCount, ["zeroCount"] = zeroCount, ["disabledCount"] = disabledCount,
            ["mixedSpacingRejected"] = mixedSpacingRejected,
            ["saturatedChances"] = new JArray(saturated.SharedEcologyCumulativeChances) };
    }

    private static JObject WorldGiantMineClearance()
    {
        var rule = new EcologySpawnRuleSnapshot("probe.shared.shore", "probe.mine", 1, 1d, 1d,
            0, 0, 1, 0, 1, 0, 1, patchSpacing: 64, requiredEnvironmentLayer: "lava.shore",
            minimumEnvironmentValue: .5d, sharedRadius: 8, footprintWidth: 13, footprintHeight: 5);
        var numbers = SmallGenerationParameters();
        numbers["river.enabled"] = 0d; numbers["lake.large.enabled"] = 0d;
        var profile = new ChunkGenerationProfileSnapshot("shared-clearance", 79, 32, 32, numbers,
            ecologyRules: new[] { rule });
        (Int2 anchor, int seed) FindAnchor(ChunkGenerationProfileSnapshot selected, string dimension)
        {
            for (int seed = 1; seed <= 256; seed++)
            {
                var request = new ChunkGenerationRequest(1,
                    new FlatWorld.WorldModel.WorldAddress(dimension, new Int2(0, 0)), seed, 1, selected);
                foreach (var placement in ChunkEcologyGenerator.GenerateShared(request, null, CancellationToken.None).Placements)
                    if (placement.LocalX >= 6 && placement.LocalX <= 25 &&
                        placement.LocalY >= 2 && placement.LocalY <= 29)
                        return (placement.SharedAnchor.Value, seed);
            }
            throw new InvalidOperationException("共享净空夹具没有找到区块内候选。");
        }
        int Count(string scenario, ChunkGenerationProfileSnapshot selected, string dimension, Int2 anchor, int seed)
        {
            var generator = new DeterministicChunkGenerator(terrainDecorator: (request, terrain, _) =>
            {
                for (int y = 0; y < terrain.Height; y++)
                for (int x = 0; x < terrain.Width; x++)
                {
                    int wx = request.Address.ChunkOrigin.X + x, wy = request.Address.ChunkOrigin.Y + y;
                    bool edge = wx == anchor.X + 6 && wy == anchor.Y;
                    TerrainCellFlags flags = TerrainCellFlags.Walkable;
                    if (edge && scenario == "blocking") flags |= TerrainCellFlags.Blocking;
                    if (edge && scenario == "occupied") flags |= TerrainCellFlags.Occupied;
                    if (edge && scenario == "unwalkable") flags = TerrainCellFlags.None;
                    terrain.SetCell(x, y, new TerrainCell(1, 0, 0, 1, 1, flags));
                    terrain.SetLiquid(x, y, edge && scenario == "liquid" ? 1 : 0, edge && scenario == "liquid" ? 1 : 0);
                    terrain.SetEnvironmentValue("structure", x, y, edge && scenario == "structure" ? 1f : 0f);
                    terrain.SetEnvironmentValue("temperature", x, y, .5f);
                    terrain.SetEnvironmentValue("precipitation", x, y, .5f);
                    terrain.SetEnvironmentValue("height", x, y, .5f);
                    terrain.SetEnvironmentValue("riverFloodplain", x, y, 0f);
                    terrain.SetEnvironmentValue("lava.shore", x, y,
                        wx == anchor.X && wy == anchor.Y && scenario != "missing_shore" ? 1f : 0f);
                }
            });
            try
            {
                using var world = new WorldRuntime("shared-clearance", 1);
                var address = new FlatWorld.WorldModel.WorldAddress(dimension, new Int2(0, 0));
                var request = world.BeginChunkGeneration(address, seed, selected);
                using var generated = generator.Generate(request, CancellationToken.None);
                if (!world.TryCommit(generated, out string failure)) throw new InvalidOperationException(failure);
                if (!world.TryGetChunk(address, out var chunk)) throw new InvalidOperationException("共享净空夹具未提交区块。");
                return chunk.Ecology.Count;
            }
            finally { generator.BeginNewWorldHydrologyEpoch(1); }
        }
        var target = FindAnchor(profile, "probe-surface");
        var samples = new JObject();
        foreach (string scenario in new[] { "shore", "missing_shore", "liquid", "blocking", "occupied", "unwalkable", "structure" })
            samples[scenario] = Count(scenario, profile, "probe-surface", target.anchor, target.seed);
        var caveProfile = new ChunkGenerationProfileSnapshot("shared-clearance-cave", 79, 32, 32, numbers,
            new Dictionary<string, string> { ["terrain.mode"] = "cave" }, ecologyRules: new[] { rule });
        var caveTarget = FindAnchor(caveProfile, "probe-cave");
        numbers["cave.spawn.x"] = caveTarget.anchor.X + .5d;
        numbers["cave.spawn.y"] = caveTarget.anchor.Y + .5d;
        var safeProfile = new ChunkGenerationProfileSnapshot("shared-clearance-cave", 79, 32, 32, numbers,
            new Dictionary<string, string> { ["terrain.mode"] = "cave" }, ecologyRules: new[] { rule });
        samples["caveSafe"] = Count("shore", safeProfile, "probe-cave", caveTarget.anchor, caveTarget.seed);
        var body = new Bounds(new Vector3(0f, -5f / 3f, 0f), new Vector3(13f, 5f / 3f, .01f));
        var cells = new HashSet<GridCellOffset>();
        for (int y = -2; y <= -1; y++)
        for (int x = -6; x <= 6; x++) cells.Add(new GridCellOffset(x, y));
        bool contained = NaturalGenerationRuleCatalogValidator.SharedClearanceContains(rule, body, cells);
        cells.Remove(new GridCellOffset(0, -1));
        bool holeRejected = !NaturalGenerationRuleCatalogValidator.SharedClearanceContains(rule, body, cells);
        cells.Add(new GridCellOffset(0, -1)); cells.Add(new GridCellOffset(7, -1));
        bool outsideCellRejected = !NaturalGenerationRuleCatalogValidator.SharedClearanceContains(rule, body, cells);
        cells.Remove(new GridCellOffset(7, -1)); body.center += Vector3.right;
        return new JObject { ["samples"] = samples, ["contained"] = contained, ["holeRejected"] = holeRejected,
            ["outsideCellRejected"] = outsideCellRejected,
            ["outsideBodyRejected"] = !NaturalGenerationRuleCatalogValidator.SharedClearanceContains(rule, body, cells) };
    }

    #endregion
}
