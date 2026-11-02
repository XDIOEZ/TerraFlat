using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 卡顿优化的快照与批量查询回归
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterHitchRegressionRules()
    {
        Register("save", "frozen_space_encoding", FrozenSpaceEncoding);
        Register("rendering", "snow_batch_query", SnowBatchQuery);
    }

    private static JObject FrozenSpaceEncoding(JObject args)
    {
        var source = JObject.Parse(@"{'Version':3,'SimulationSeconds':12.5,'Sectors':[],
            'Ships':[{'ShipId':'ship-a','SurfaceWorldKey':'earth','Extra':{'quality':7}}],
            'Passengers':[{'ProfileId':'player-a','WorldKey':'cave'}],
            'UnknownExtension':{'enabled':true}}");
        string frozen = source.ToString(Formatting.None);
        source["SimulationSeconds"] = 99;
        source["Ships"][0]["Extra"]["quality"] = 999;
        // 真正从工作线程调用生产编码器，只输入冻结字符串，不读取运行态。
        var records = Task.Run(() => SaveDataMgr.EncodeFrozenSqliteSpaceRecords(frozen)).GetAwaiter().GetResult();
        JObject header = JObject.Parse(Encoding.UTF8.GetString(records.Single(value => value.Key == "space").Payload));
        var ship = records.Single(value => value.Key == "space:Ships:ship-a");
        JObject savedShip = JObject.Parse(Encoding.UTF8.GetString(ship.Payload));
        bool invalidRejected = false;
        try { SaveDataMgr.EncodeFrozenSqliteSpaceRecords("{\"Ships\":[{}]}"); }
        catch (System.IO.InvalidDataException) { invalidRejected = true; }
        return new JObject
        {
            ["count"] = records.Count, ["clock"] = header.Value<double>("SimulationSeconds"),
            ["shipQuality"] = savedShip["Extra"]["quality"],
            ["shipWorld"] = ship.WorldKey,
            ["passengerWorld"] = records.Single(value => value.Key == "space:Passengers:player-a").WorldKey,
            ["headerHasNoEntities"] = !((JArray)header["Ships"]).Any() && !((JArray)header["Passengers"]).Any(),
            ["extensionPreserved"] = header["UnknownExtension"].Value<bool>("enabled"),
            ["schemaPreserved"] = records.All(value => value.SchemaId == "flatworld.space"),
            ["emptyCount"] = SaveDataMgr.EncodeFrozenSqliteSpaceRecords(null).Count,
            ["invalidRejected"] = invalidRejected
        };
    }

    private static JObject SnowBatchQuery(JObject args)
    {
        const string dimension = "probe:snow-batch";
        using var world = new WorldRuntime(dimension, 1);
        var topology = new WorldTopologyDomain(new int2(-4, -4), new int2(8, 8), true);
        var profile = new ChunkGenerationProfileSnapshot(dimension, 1, 2, 2);
        var address = new FlatWorld.WorldModel.WorldAddress(dimension, new Int2(-4, -2));
        ChunkTerrainData Commit(float depth)
        {
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
            using var buffer = new ChunkTerrainBuffer(2, 2);
            buffer.SetCell(0, 0, new TerrainCell(1, 0, 0, 0, 1, TerrainCellFlags.Walkable));
            using var generated = new ChunkGenerationResult(request, buffer);
            if (!world.TryCommit(generated, out string reason)) throw new InvalidOperationException(reason);
            world.TryGetChunk(address, out ChunkRuntime chunk);
            chunk.Terrain.SetEnvironmentValue(SnowDepthLayer.LayerId, 0, 0, depth);
            return chunk.Terrain;
        }
        ChunkTerrainData terrain = Commit(.3f);
        var query = new ChunkMgr.RuntimeTerrainPresenceQuery(world, topology, dimension, 2, 2);
        Vector3 position = new(-3.75f, -1.75f, 0f);
        float Sample() => SeasonalSnowRenderEffect.SampleCoverage(ref query, position, null, 0f);
        var result = new JObject { ["natural"] = Sample() };
        terrain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, 0, 0, .6f);
        result["weather"] = Sample();
        terrain.SetLiquid(0, 0, terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 6);
        result["water"] = Sample();
        TerrainSupportLayer.Set(terrain, 0, 0, 2, 1);
        result["platform"] = Sample();
        terrain.SetEnvironmentValue(WorldSnowInteraction.EditedLayer, 0, 0, 1f);
        terrain.SetEnvironmentValue(WorldSnowInteraction.DepthLayer, 0, 0, .2f);
        terrain.SetEnvironmentValue(WorldSnowInteraction.SeasonLayer, 0, 0, .6f);
        result["edited"] = Sample();
        terrain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, 0, 0, .8f);
        result["weatherAfterEdit"] = Sample();
        result["wrapped"] = SeasonalSnowRenderEffect.SampleCoverage(ref query, position + Vector3.right * 8f, null, 0f);
        result["missing"] = SeasonalSnowRenderEffect.SampleCoverage(ref query, Vector3.zero, null, 0f);
        Sample();
        world.EvictChunk(address);
        result["evicted"] = Sample();
        terrain = Commit(.7f);
        result["replaced"] = Sample();
        using var foreign = new WorldRuntime("probe:snow-other-world", 1);
        result["foreignRejected"] = !query.IsForWorld(foreign);
        world.BeginNewEpoch(2);
        result["oldEpoch"] = Sample();
        return result;
    }
    #endregion
}
