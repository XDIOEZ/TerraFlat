using System;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public partial class SaveDataMgr
{
    #region 独立农业差量键采样

    // 停用外壳只写自己的内存差量，实走正式查询与农业记录，不切换玩家世界或写盘。
    internal JObject CaptureTestSoilPersistenceKeys()
    {
        if (ReferenceEquals(this, instance) || gameObject.activeInHierarchy || !string.IsNullOrEmpty(UserSavePath))
            throw new InvalidOperationException("农业差量采样必须使用未激活的独立存档外壳。");
        SaveData = new GameSaveData { saveName = "probe:soil-persistence" };
        var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(-32, 64));
        string planet = GameplayWorldRegistry.ExecutionWorldKey;
        if (string.IsNullOrWhiteSpace(planet)) planet = ResolveRuntimePlanetName();
        bool worldKeyEquivalent = planet == ResolveRuntimePlanetName();
        bool missingDoesNotCreate = GetRuntimeChunkRecord(address, false) == null && chunkDeltas.Count == 0;
        ChunkSaveRecord first = GetRuntimeChunkRecord(address, true);
        bool reused = ReferenceEquals(first, GetRuntimeChunkRecord(address, true)) &&
            first.PlanetName == planet && first.ChunkName == ToChunkName(address) && chunkDeltas.Count == 1;

        var worlds = new[] { "probe:surface", "probe:surface__dimension__cave", "SpaceSector__2__-3", "ship:probe", "world" };
        bool isolated = true;
        int expectedRecordCount = 1;
        for (int i = 0; i < worlds.Length; i++)
        {
            bool currentWorld = worlds[i] == planet;
            isolated &= currentWorld ? ReferenceEquals(first, GetRuntimeChunkRecord(worlds[i], address, false)) :
                GetRuntimeChunkRecord(worlds[i], address, false) == null;
            ChunkSaveRecord record = GetRuntimeChunkRecord(worlds[i], address, true);
            record.GetOrAddAgricultureCell(Vector2Int.zero).Water = i;
            isolated &= record.PlanetName == worlds[i] && (ReferenceEquals(first, record) == currentWorld);
            if (!currentWorld) expectedRecordCount++;
        }
        for (int i = 0; i < worlds.Length; i++)
            isolated &= GetRuntimeChunkRecord(worlds[i], address, false).FindAgricultureCell(Vector2Int.zero).Water == i;

        // 数字地块仅用于独立存储读写，实际耕地资格由农业业务入口校验。
        using var buffer = new ChunkTerrainBuffer(8, 8);
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            buffer.SetCell(x, y, new TerrainCell(7, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        using ChunkTerrainData terrain = buffer.Seal();
        var samples = new RuntimeTerrainTileSample[64];
        for (int i = 0; i < samples.Length; i++)
        {
            var local = new Vector2Int(i % 8, i / 8);
            samples[i] = new RuntimeTerrainTileSample(address, terrain,
                new Vector2Int(address.ChunkOrigin.X + local.x, address.ChunkOrigin.Y + local.y),
                local, terrain.GetCell(local.x, local.y), 7);
            terrain.SetEnvironmentValue(FarmlandSystem.SourceLayer, local.x, local.y, 7f);
            terrain.SetEnvironmentValue(FarmlandSystem.WaterLayer, local.x, local.y, 8f);
            terrain.SetEnvironmentValue(FarmlandSystem.WaterSupplyRemainderLayer, local.x, local.y, .375f);
            terrain.SetEnvironmentValue(FarmlandSystem.WaterConsumptionRemainderLayer, local.x, local.y, .125f);
            terrain.SetEnvironmentValue(FarmlandSystem.FertilityLayer, local.x, local.y, .875f);
            RecordAgricultureCell(samples[i]);
        }
        AgricultureCellSaveData saved = first.FindAgricultureCell(Vector2Int.zero);
        var crop = new Data_GeneralItem { IDName = "probe:crop", Guid = 177 };
        saved.Crop = crop;
        for (int i = 0; i < samples.Length; i++) RecordAgricultureCell(samples[i]);
        long before = GC.GetAllocatedBytesForCurrentThread();
        const int iterations = 8192;
        for (int i = 0; i < iterations; i++) RecordAgricultureCell(samples[i & 63]);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        bool valuesPreserved = first.AgricultureCells.Count == 64;
        for (int i = 0; i < samples.Length; i++)
        {
            AgricultureCellSaveData value = first.FindAgricultureCell(samples[i].LocalCell);
            valuesPreserved &= value != null && value.SourceTileId == 7 && value.Water == 8 &&
                value.WaterSupplyRemainder == .375f && value.WaterConsumptionRemainder == .125f && value.Fertility == .875f;
        }
        terrain.SetEnvironmentValue(FarmlandSystem.WaterLayer, 0, 0, 4f);
        RecordAgricultureCell(samples[0]);
        bool currentValues = ReferenceEquals(saved, first.FindAgricultureCell(Vector2Int.zero)) &&
            ReferenceEquals(saved.Crop, crop) && saved.Water == 4;

        for (int i = 0; i < 300; i++)
            _ = GetRuntimeChunkRecord("probe:bounded", new FlatWorld.WorldModel.WorldAddress("surface", new Int2(i * 32, 0)), false);
        bool bounded = chunkDeltaLookupKeys.Count <= 256 && chunkDeltas.Count == expectedRecordCount &&
            ReferenceEquals(first, GetRuntimeChunkRecord(address, false));
        var naturalAddress = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(96, 96));
        var naturalSample = new RuntimeTerrainTileSample(naturalAddress, terrain, new Vector2Int(96, 96),
            Vector2Int.zero, terrain.GetCell(0, 0), 7);
        RecordCultivatedCropData(naturalAddress, Vector2Int.zero, crop);
        AgricultureCellSaveData cropOnly = GetRuntimeChunkRecord(naturalAddress, false).FindAgricultureCell(Vector2Int.zero);
        bool naturalCropOnly = HasNaturalSubstrateCrop(naturalSample) && cropOnly.SourceTileId == 0 &&
            cropOnly.Water == 0 && cropOnly.Fertility == 0f;
        RecordCultivatedCropData(naturalAddress, Vector2Int.zero, null);
        bool naturalCropRemoved = !HasNaturalSubstrateCrop(naturalSample) &&
            GetRuntimeChunkRecord(naturalAddress, false).AgricultureCells.Count == 0;
        SaveData = new GameSaveData { saveName = "probe:soil-replacement" };
        ResetChunkDifferenceState();
        bool cleared = GetRuntimeChunkRecord(address, false) == null && chunkDeltas.Count == 0;
        ChunkSaveRecord replacement = GetRuntimeChunkRecord(address, true);
        bool replaced = !ReferenceEquals(first, replacement) && replacement.AgricultureCells.Count == 0;
        return new JObject
        {
            ["worldKeyEquivalent"] = worldKeyEquivalent, ["missingDoesNotCreate"] = missingDoesNotCreate,
            ["reused"] = reused, ["isolated"] = isolated, ["valuesPreserved"] = valuesPreserved,
            ["currentValues"] = currentValues, ["bounded"] = bounded,
            ["cleared"] = cleared, ["replaced"] = replaced,
            ["iterations"] = iterations, ["allocatedBytes"] = allocatedBytes,
            ["naturalCropOnly"] = naturalCropOnly, ["naturalCropRemoved"] = naturalCropRemoved
        };
    }

    #endregion
}
