using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 环境批次正式接口采样
    private static void RegisterEnvironmentBatchOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("environment/environment_batch", EnvironmentBatchValues);
        operations.Add("environment/environment_batch_rejection", EnvironmentBatchRejection);
        operations.Add("environment/environment_batch_lifecycle", EnvironmentBatchLifecycle);
        operations.Add("environment/environment_batch_revision", EnvironmentBatchRevision);
        operations.Add("agriculture/surface_water_rule", AgricultureSurfaceWaterRule);
    }

    private static ChunkTerrainData EnvironmentBatchFixture(int width, int height)
    {
        using var buffer = new ChunkTerrainBuffer(width, height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            buffer.SetCell(x, y, new TerrainCell(1, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        return buffer.Seal();
    }

    private static JObject EnvironmentBatchLayer(ChunkTerrainData terrain, string layerId)
    {
        bool exists = terrain.TryGetEnvironmentLayer(layerId, out ReadOnlyMemory<float> borrowed);
        var values = new JArray();
        // 借用只读层仅在当前同步采样内读取，报告保存独立的数值副本。
        for (int i = 0; i < borrowed.Length; i++) values.Add(borrowed.Span[i]);
        return new JObject { ["exists"] = exists, ["length"] = borrowed.Length, ["values"] = values };
    }

    private static JObject EnvironmentBatchNotice(ChunkTerrainData terrain, ChunkTerrainChanged change)
    {
        var indices = new JArray();
        foreach (int index in change.GetCellIndices(terrain.Width)) indices.Add(index);
        return new JObject
        {
            ["kind"] = change.Kind.ToString(), ["layer"] = change.EnvironmentLayerId,
            ["isBatch"] = change.IsBatch, ["borrowedIndexCount"] = change.CellIndices.Length,
            ["indices"] = indices, ["revision"] = change.Revision,
            ["moisture"] = EnvironmentBatchLayer(terrain, "moisture"),
            ["water"] = EnvironmentBatchLayer(terrain, "agriculture.water")
        };
    }

    private static string EnvironmentBatchException(Action action)
    {
        try { action(); return string.Empty; }
        catch (Exception exception) { return exception.GetType().Name; }
    }
    #endregion

    #region 批量数值与通知
    private static JObject EnvironmentBatchValues(JObject args)
    {
        using ChunkTerrainData terrain = EnvironmentBatchFixture(3, 2);
        terrain.SetEnvironmentBatch("moisture", new[]
        {
            new EnvironmentCellUpdate(0, .2f), new EnvironmentCellUpdate(1, .4f),
            new EnvironmentCellUpdate(2, .8f)
        });
        terrain.SetEnvironmentBatch("agriculture.water", new[]
        {
            new EnvironmentCellUpdate(0, 20f), new EnvironmentCellUpdate(1, 40f),
            new EnvironmentCellUpdate(2, 80f)
        });
        long initialRevision = terrain.Revision;
        var notices = new JArray();
        terrain.Changed += change => notices.Add(EnvironmentBatchNotice(terrain, change));
        var immediate = new[]
        {
            new EnvironmentCellUpdate(0, .3f), new EnvironmentCellUpdate(1, .4f),
            new EnvironmentCellUpdate(4, .9f)
        };
        int immediateChanges = terrain.SetEnvironmentBatch("moisture", immediate);
        JObject afterImmediate = EnvironmentBatchLayer(terrain, "moisture");
        long immediateRevision = terrain.Revision;
        int unchangedChanges = terrain.SetEnvironmentBatch("moisture", immediate);
        int emptyChanges = terrain.SetEnvironmentBatch("probe.empty", Array.Empty<EnvironmentCellUpdate>());
        JObject emptyLayer = EnvironmentBatchLayer(terrain, "probe.empty");
        int zeroChanges = terrain.SetEnvironmentBatch("probe.zero", new[]
        {
            new EnvironmentCellUpdate(0, 0f), new EnvironmentCellUpdate(2, 0f)
        });
        JObject zeroLayer = EnvironmentBatchLayer(terrain, "probe.zero");
        int firstRealChange = terrain.SetEnvironmentBatch("probe.zero", new[] { new EnvironmentCellUpdate(2, .5f) });
        JObject afterFirstRealChange = EnvironmentBatchLayer(terrain, "probe.zero");
        int afterNoopNotices = notices.Count;
        long afterNoopRevision = terrain.Revision;
        int waterChanges = terrain.SetEnvironmentBatch("agriculture.water", new[]
        {
            new EnvironmentCellUpdate(0, 30f), new EnvironmentCellUpdate(4, 90f)
        }, true);
        int moistureChanges = terrain.SetEnvironmentBatch("moisture", new[]
        {
            new EnvironmentCellUpdate(0, .7f), new EnvironmentCellUpdate(2, .9f),
            new EnvironmentCellUpdate(5, .5f)
        }, true);
        int beforePublishNotices = notices.Count;
        JObject beforePublishMoisture = EnvironmentBatchLayer(terrain, "moisture");
        JObject beforePublishWater = EnvironmentBatchLayer(terrain, "agriculture.water");
        terrain.PublishEnvironmentBatches();
        int afterPublishNotices = notices.Count;
        long afterPublishRevision = terrain.Revision;
        terrain.PublishEnvironmentBatches();
        int afterRepublishNotices = notices.Count;
        terrain.SetEnvironmentValue("moisture", 2, 1, .6f);
        return new JObject
        {
            ["initialRevision"] = initialRevision, ["immediateChanges"] = immediateChanges,
            ["immediateRevision"] = immediateRevision, ["afterImmediate"] = afterImmediate,
            ["unchangedChanges"] = unchangedChanges, ["emptyChanges"] = emptyChanges,
            ["emptyLayer"] = emptyLayer, ["zeroChanges"] = zeroChanges, ["zeroLayer"] = zeroLayer,
            ["firstRealChange"] = firstRealChange, ["afterFirstRealChange"] = afterFirstRealChange,
            ["afterNoopNotices"] = afterNoopNotices,
            ["afterNoopRevision"] = afterNoopRevision, ["waterChanges"] = waterChanges,
            ["moistureChanges"] = moistureChanges, ["beforePublishNotices"] = beforePublishNotices,
            ["beforePublishMoisture"] = beforePublishMoisture, ["beforePublishWater"] = beforePublishWater,
            ["afterPublishNotices"] = afterPublishNotices, ["afterPublishRevision"] = afterPublishRevision,
            ["afterRepublishNotices"] = afterRepublishNotices, ["notices"] = notices,
            ["blockingRevision"] = terrain.BlockingRevision, ["surfaceLiquidRevision"] = terrain.SurfaceLiquidRevision
        };
    }
    #endregion

    #region 整批拒绝与失败后复用
    private static JObject EnvironmentBatchRejection(JObject args)
    {
        using ChunkTerrainData terrain = EnvironmentBatchFixture(2, 2);
        terrain.SetEnvironmentBatch("moisture", new[]
        {
            new EnvironmentCellUpdate(0, .2f), new EnvironmentCellUpdate(1, .3f),
            new EnvironmentCellUpdate(2, .4f), new EnvironmentCellUpdate(3, .5f)
        });
        int noticeCount = 0;
        terrain.Changed += _ => noticeCount++;
        var rejected = new JArray();
        void Reject(string name, string layerId, EnvironmentCellUpdate[] updates)
        {
            JObject before = EnvironmentBatchLayer(terrain, "moisture");
            long beforeRevision = terrain.Revision;
            int beforeNotices = noticeCount;
            string exception = EnvironmentBatchException(() => terrain.SetEnvironmentBatch(layerId, updates, true));
            rejected.Add(new JObject
            {
                ["name"] = name, ["exception"] = exception, ["before"] = before,
                ["after"] = EnvironmentBatchLayer(terrain, "moisture"),
                ["beforeRevision"] = beforeRevision, ["afterRevision"] = terrain.Revision,
                ["beforeNotices"] = beforeNotices, ["afterNotices"] = noticeCount
            });
        }
        Reject("空层名", "", new[] { new EnvironmentCellUpdate(0, .9f) });
        Reject("空白层名", " ", new[] { new EnvironmentCellUpdate(0, .9f) });
        Reject("负索引", "moisture", new[] { new EnvironmentCellUpdate(-1, .9f) });
        Reject("越界前有合法写入", "moisture", new[]
            { new EnvironmentCellUpdate(0, .9f), new EnvironmentCellUpdate(4, .8f) });
        Reject("重复索引", "moisture", new[]
            { new EnvironmentCellUpdate(0, .9f), new EnvironmentCellUpdate(0, .8f) });
        Reject("逆序索引", "moisture", new[]
            { new EnvironmentCellUpdate(2, .9f), new EnvironmentCellUpdate(1, .8f) });
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Reject("非有限值" + invalid, "moisture", new[]
                { new EnvironmentCellUpdate(0, .9f), new EnvironmentCellUpdate(1, invalid) });
        Reject("新层越界", "probe.rejected", new[]
            { new EnvironmentCellUpdate(0, .9f), new EnvironmentCellUpdate(4, .8f) });
        JObject rejectedLayer = EnvironmentBatchLayer(terrain, "probe.rejected");
        int recoveredChanges = terrain.SetEnvironmentBatch("moisture", new[]
            { new EnvironmentCellUpdate(0, .6f), new EnvironmentCellUpdate(3, .7f) });
        terrain.PublishEnvironmentBatches();
        return new JObject
        {
            ["rejected"] = rejected, ["rejectedLayer"] = rejectedLayer,
            ["recoveredChanges"] = recoveredChanges, ["recovered"] = EnvironmentBatchLayer(terrain, "moisture"),
            ["noticesAfterRecovery"] = noticeCount, ["surfaceLiquidRevision"] = terrain.SurfaceLiquidRevision
        };
    }
    #endregion

    #region 回调保护与待发队列
    private static JObject EnvironmentBatchLifecycle(JObject args)
    {
        using ChunkTerrainData terrain = EnvironmentBatchFixture(2, 2);
        var notices = new JArray();
        bool failOnce = true;
        terrain.Changed += change =>
        {
            JObject notice = EnvironmentBatchNotice(terrain, change);
            notice["nestedWrite"] = EnvironmentBatchException(() => terrain.SetEnvironmentBatch("probe.nested",
                new[] { new EnvironmentCellUpdate(0, .9f) }));
            notice["nestedSingleWrite"] = EnvironmentBatchException(() => terrain.SetEnvironmentValue("moisture", 1, 0, .9f));
            notice["nestedPublish"] = EnvironmentBatchException(terrain.PublishEnvironmentBatches);
            notice["nestedDispose"] = EnvironmentBatchException(terrain.Dispose);
            notices.Add(notice);
            if (failOnce)
            {
                failOnce = false;
                throw new InvalidOperationException("环境批次观察者夹具故障");
            }
        };
        terrain.SetEnvironmentBatch("moisture", new[] { new EnvironmentCellUpdate(0, .7f) }, true);
        terrain.SetEnvironmentBatch("agriculture.water", new[] { new EnvironmentCellUpdate(1, 70f) }, true);
        long beforePublishRevision = terrain.Revision;
        string firstPublish = EnvironmentBatchException(terrain.PublishEnvironmentBatches);
        int afterFailedNotices = notices.Count;
        string overwritePending = EnvironmentBatchException(() => terrain.SetEnvironmentBatch("moisture",
            new[] { new EnvironmentCellUpdate(0, .9f) }, true));
        string singleOverwritePending = EnvironmentBatchException(() => terrain.SetEnvironmentValue("moisture", 0, 0, .9f));
        JObject afterFailedMoisture = EnvironmentBatchLayer(terrain, "moisture");
        string retryPublish = EnvironmentBatchException(terrain.PublishEnvironmentBatches);
        int afterRetryNotices = notices.Count;
        terrain.PublishEnvironmentBatches();
        int afterRepublishNotices = notices.Count;
        long afterRetryRevision = terrain.Revision;
        int reusedChanges = terrain.SetEnvironmentBatch("moisture", new[] { new EnvironmentCellUpdate(3, .8f) }, true);
        terrain.PublishEnvironmentBatches();
        int afterReuseNotices = notices.Count;
        terrain.SetEnvironmentBatch("moisture", new[] { new EnvironmentCellUpdate(0, .9f) }, true);
        terrain.Dispose();
        return new JObject
        {
            ["firstPublish"] = firstPublish, ["afterFailedNotices"] = afterFailedNotices,
            ["overwritePending"] = overwritePending, ["singleOverwritePending"] = singleOverwritePending,
            ["afterFailedMoisture"] = afterFailedMoisture, ["retryPublish"] = retryPublish,
            ["afterRetryNotices"] = afterRetryNotices, ["afterRepublishNotices"] = afterRepublishNotices,
            ["beforePublishRevision"] = beforePublishRevision, ["afterRetryRevision"] = afterRetryRevision,
            ["reusedChanges"] = reusedChanges, ["afterReuseNotices"] = afterReuseNotices,
            ["afterDisposeNotices"] = notices.Count, ["disposed"] = terrain.IsDisposed,
            ["disposedWrite"] = EnvironmentBatchException(() => terrain.SetEnvironmentBatch("moisture",
                new[] { new EnvironmentCellUpdate(0, .2f) })),
            ["disposedRead"] = EnvironmentBatchException(() => terrain.TryGetEnvironmentLayer("moisture", out _)),
            ["disposedPublish"] = EnvironmentBatchException(terrain.PublishEnvironmentBatches), ["notices"] = notices
        };
    }
    #endregion

    #region 液体缓存版本边界
    private static JObject EnvironmentBatchRevision(JObject args)
    {
        using ChunkTerrainData terrain = EnvironmentBatchFixture(2, 2);
        long initialRevision = terrain.SurfaceLiquidRevision;
        int soilChanges = terrain.SetEnvironmentBatch("agriculture.water", new[]
            { new EnvironmentCellUpdate(0, 20f), new EnvironmentCellUpdate(3, 40f) });
        terrain.SetEnvironmentBatch("moisture", new[]
            { new EnvironmentCellUpdate(0, .2f), new EnvironmentCellUpdate(3, .4f) });
        long soilRevision = terrain.SurfaceLiquidRevision;
        terrain.SetEnvironmentBatch(TerrainSupportLayer.CostLayer, new[]
            { new EnvironmentCellUpdate(0, 2f), new EnvironmentCellUpdate(3, 2f) });
        long costRevision = terrain.SurfaceLiquidRevision;
        var support = new[] { new EnvironmentCellUpdate(0, 8f), new EnvironmentCellUpdate(3, 8f) };
        int supportChanges = terrain.SetEnvironmentBatch(TerrainSupportLayer.TileIdLayer, support);
        long supportRevision = terrain.SurfaceLiquidRevision;
        int supportNoopChanges = terrain.SetEnvironmentBatch(TerrainSupportLayer.TileIdLayer, support);
        long supportNoopRevision = terrain.SurfaceLiquidRevision;
        int water = terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        bool liquidChanged = terrain.SetLiquid(0, 0, water, .6f);
        long liquidRevision = terrain.SurfaceLiquidRevision;
        bool repeatedLiquidChanged = terrain.SetLiquid(0, 0, water, .6f);
        return new JObject
        {
            ["initialRevision"] = initialRevision, ["soilChanges"] = soilChanges, ["soilRevision"] = soilRevision,
            ["costRevision"] = costRevision, ["supportChanges"] = supportChanges, ["supportRevision"] = supportRevision,
            ["supportNoopChanges"] = supportNoopChanges, ["supportNoopRevision"] = supportNoopRevision,
            ["liquidChanged"] = liquidChanged, ["liquidRevision"] = liquidRevision,
            ["repeatedLiquidChanged"] = repeatedLiquidChanged, ["repeatedLiquidRevision"] = terrain.SurfaceLiquidRevision,
            ["blockingRevision"] = terrain.BlockingRevision
        };
    }
    #endregion

    #region 地表补水共用数值规则
    private static JObject AgricultureSurfaceWaterRule(JObject args)
    {
        float water = (float)Number(args, "water", 20d);
        float maximum = (float)Number(args, "maximum", 100d);
        string supplyKind = Text(args, "supplyKind", "number");
        float supply = supplyKind switch
        {
            "number" => (float)Number(args, "supply", 5d),
            "nan" => float.NaN,
            "positive_infinity" => float.PositiveInfinity,
            "negative_infinity" => float.NegativeInfinity,
            _ => throw new ArgumentException("未登记的补水测试输入：" + supplyKind)
        };
        bool changed = FarmlandSystem.TryCalculateSurfaceWater(water, supply, maximum, out float nextWater);
        return new JObject { ["changed"] = changed, ["nextWater"] = nextWater,
            ["water"] = water, ["maximum"] = maximum, ["supplyKind"] = supplyKind };
    }
    #endregion
}
