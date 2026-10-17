using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Audio;
using FlatWorld.Gameplay.InventoryFeedback;
using FlatWorld.Gameplay.Presentation.Audio;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Localization;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using MemoryPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Rendering;

/// <summary>基础服务采样使用独立数据和正式规则，不访问正在玩的世界或玩家磁盘存档。</summary>
public static partial class GameplayTestSystemProbe
{
    #region 基础服务注册

    private static void RegisterServiceOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("core_lifecycle/resource_plan", ServiceResourcePlan);
        operations.Add("core_lifecycle/resource_candidate", ServiceResourceCandidate);
        operations.Add("item_runtime/module_structure", ServiceModuleStructure);
        operations.Add("item_runtime/clone_runtime", ServiceCloneRuntime);
        operations.Add("world_streaming/chunk_leases", ServiceChunkLeases);
        operations.Add("world_streaming/generation_lifecycle", ServiceGenerationLifecycle);
        operations.Add("world_streaming/commit_snapshot", ServiceCommitSnapshot);
        operations.Add("save/snapshot_memory", ServiceSnapshotMemory);
        operations.Add("save/special_namespace", ServiceSpecialNamespace);
        operations.Add("save/space_item_chain", ServiceSpaceItemChain);
        operations.Add("networking/endpoint", ServiceNetworkEndpoint);
        operations.Add("networking/item_payload", ServiceNetworkPayload);
        operations.Add("modding/patch", ServiceModPatch);
        operations.Add("modding/package_path", ServiceModPath);
        operations.Add("modding/candidate_patch", ServiceModCandidatePatch);
        operations.Add("ui_settings/mobile_size", ServiceMobileSize);
        operations.Add("ui_settings/navigation_policy", ServiceNavigationPolicy);
        operations.Add("ui_settings/panel_lifecycle", ServicePanelLifecycle);
        operations.Add("ui_settings/dropdown_layout", ServiceDropdownLayout);
        operations.Add("ui_settings/edit_session_values", ServiceSettingsSessionValues);
        operations.Add("ui_settings/edit_session_commit", ServiceSettingsSessionCommit);
        operations.Add("ui_settings/edit_session_participants", ServiceSettingsSessionParticipants);
        operations.Add("ui_settings/edit_session_errors", ServiceSettingsSessionErrors);
        operations.Add("ui_settings/input_binding_session", ServiceInputBindingEditSession);
        operations.Add("audio/bus_volume", ServiceAudioVolume);
        operations.Add("audio/catalog_lifecycle", ServiceAudioCatalog);
        operations.Add("audio/footstep_surface", ServiceFootstepSurface);
        operations.Add("audio/inventory_cue", ServiceAudioInventoryCue);
        operations.Add("localization/stable_identity", ServiceLocalizationIdentity);
        operations.Add("rendering/fog_config", ServiceFogConfiguration);
        operations.Add("rendering/zoom_cloud_mask", ServiceZoomCloudMask);
        operations.Add("rendering/universe_view_config", ServiceUniverseViewConfiguration);
        operations.Add("rendering/universe_view_transition", ServiceUniverseViewTransition);
        operations.Add("rendering/group_projection", ServiceGroupProjection);
    }

    #endregion

    #region 资源生命周期

    private static JObject ServiceResourcePlan(JObject args)
    {
        var order = new JArray();
        var progress = new JArray();
        int finished = 0;
        string mode = Text(args, "mode", "ordered");
        using var pipeline = new ResourceLoadPipeline((title, value) => progress.Add(value));
        IEnumerator Load(string id, bool fail)
        {
            try
            {
                order.Add(id);
                pipeline.Report(0.5f);
                pipeline.Report(0.25f);
                yield return null;
                if (fail) throw new InvalidOperationException("Lua 资源阶段异常夹具");
            }
            finally { finished++; }
        }
        // 倒序注册让正式依赖排序决定执行顺序，并检查异常后的 finally。
        pipeline.Add("content", "内容", 3, () => Load("content", mode == "throw"), "startup");
        pipeline.Add("startup", "菜单", 1, () => Load("startup", false),
            mode == "cycle" ? new[] { "content" } : mode == "missing" ? new[] { "absent" } : Array.Empty<string>());
        IEnumerator runner = pipeline.Run();
        try
        {
            int moves = 0;
            while (runner.MoveNext())
                if (++moves > 128) throw new InvalidOperationException("资源测试夹具超过同步步数上限。");
        }
        finally { (runner as IDisposable)?.Dispose(); }
        return new JObject
        {
            ["order"] = order, ["progress"] = progress, ["finished"] = finished,
            ["failure"] = pipeline.Failure?.GetType().Name, ["stage"] = pipeline.CurrentStage
        };
    }

    private static JObject ServiceResourceCandidate(JObject args)
    {
        var active = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["official"] = 7 };
        Dictionary<string, int> original = active;
        var context = new ResourceReloadContext();
        context.AddDictionary(() => active, value => active = value);
        int disposed = 0;
        IEnumerator Candidate()
        {
            try
            {
                active["Candidate"] = 11;
                yield return null;
                active["second"] = 13;
                if (Flag(args, "fail", false)) throw new InvalidOperationException("候选加载失败");
            }
            finally { disposed++; active["cleanup"] = 17; }
        }
        var officialBetweenFrames = new JArray();
        string error = null;
        IEnumerator routine = context.Run(Candidate(), () => true);
        try
        {
            while (routine.MoveNext()) officialBetweenFrames.Add(ReferenceEquals(active, original) && active["official"] == 7);
        }
        catch (InvalidOperationException exception) { error = exception.GetType().Name; }
        finally { (routine as IDisposable)?.Dispose(); }
        bool preservedBeforeCommit = ReferenceEquals(active, original);
        if (error == null && Flag(args, "commit", true)) context.Commit();
        return new JObject
        {
            ["betweenFrames"] = officialBetweenFrames, ["disposed"] = disposed, ["error"] = error,
            ["preservedBeforeCommit"] = preservedBeforeCommit, ["officialPreserved"] = original.Count == 1 && original["official"] == 7,
            ["committed"] = !ReferenceEquals(active, original), ["hasCandidate"] = active.ContainsKey("candidate"),
            ["hasSecond"] = active.ContainsKey("second"), ["hasCleanup"] = active.ContainsKey("cleanup")
        };
    }

    #endregion

    #region 物品模块与独立运行态

    private static JObject ServiceModuleStructure(JObject args)
    {
        var item = new Data_GeneralItem { IDName = "probe:item" };
        var module = new CollectableModuleData { StableName = "stock", ModuleId = "collectable", CurrentStock = 5 };
        uint before = item.ModuleStructureVersion;
        item.ModuleDataDic.Add(module.StableName, module);
        uint added = item.ModuleStructureVersion;
        item.ModuleDataDic[module.StableName] = module;
        uint same = item.ModuleStructureVersion;
        module.Enabled = false;
        uint disabled = item.ModuleStructureVersion;
        module.CurrentStock = (int)Number(args, "stock", 2);
        module.NotifyPayloadChanged();
        uint payload = item.ModuleStructureVersion;
        bool removed = item.ModuleDataDic.Remove(module.StableName);
        uint afterRemove = item.ModuleStructureVersion;
        module.Enabled = true;
        return new JObject
        {
            ["before"] = before, ["added"] = added, ["same"] = same,
            ["disabled"] = disabled, ["payload"] = payload, ["removed"] = removed,
            ["afterRemove"] = afterRemove, ["afterDetachedEnable"] = item.ModuleStructureVersion,
            ["remaining"] = item.ModuleDataDic.Count, ["stock"] = module.CurrentStock
        };
    }

    private static Data_GeneralItem ServiceItem(JObject args) => new()
    {
        IDName = Text(args, "itemId", "probe:item"), Guid = (int)Number(args, "guid", 143), code = "probe",
        MaxDurability = (float)Number(args, "maximumDurability", 100),
        Durability = (float)Number(args, "durability", 25),
        Stack = new ItemStack
        {
            Amount = (float)Number(args, "amount", 3), Weight = (float)Number(args, "weight", 2),
            Volume = (float)Number(args, "volume", 4)
        },
        ModuleDataDic = new ModuleDataCollection
        {
            ["stock"] = new CollectableModuleData
            {
                StableName = "stock", ModuleId = "collectable", CurrentStock = (int)Number(args, "stock", 7),
                IsInitialized = true
            }
        },
        ItemSpecialData = "{\"probe\":true}"
    };

    private static JObject ServiceCloneRuntime(JObject args)
    {
        Data_GeneralItem source = ServiceItem(args);
        ItemData clone = ItemInstanceDataFactory.CloneRuntime(source);
        bool independentModules = !ReferenceEquals(source.ModuleDataDic, clone.ModuleDataDic) &&
            !ReferenceEquals(source.ModuleDataDic["stock"], clone.ModuleDataDic["stock"]);
        float clonedAmount = clone.Stack.Amount;
        int clonedStock = ((CollectableModuleData)clone.ModuleDataDic["stock"]).CurrentStock;
        clone.Stack.Amount += 1;
        clone.Tags.Add("probe.added");
        ((CollectableModuleData)clone.ModuleDataDic["stock"]).CurrentStock += 1;
        return new JObject
        {
            ["guid"] = clone.Guid, ["sourceGuid"] = source.Guid, ["independentStack"] = !ReferenceEquals(source.Stack, clone.Stack),
            ["independentModules"] = independentModules, ["initialAmount"] = clonedAmount, ["initialStock"] = clonedStock,
            ["sourceAmount"] = source.Stack.Amount, ["sourceStock"] = ((CollectableModuleData)source.ModuleDataDic["stock"]).CurrentStock,
            ["sourceHasAddedTag"] = source.Tags.Contains("probe.added"), ["sourceWeight"] = source.Stack.CurrentWeight,
            ["sourceVolume"] = source.Stack.CurrentVolume
        };
    }

    #endregion

    #region 区块流送租约

    private static JObject ServiceChunkLeases(JObject args)
    {
        var address = new FlatWorld.WorldModel.WorldAddress("probe", new Int2((int)Number(args, "x", -64), (int)Number(args, "y", 64)));
        using var world = new WorldRuntime("probe", 1);
        using ChunkLease first = world.AcquireChunkLease(address, ChunkLeaseKind.Simulation);
        using ChunkLease second = world.AcquireChunkLease(address, ChunkLeaseKind.Simulation);
        using ChunkLease presentation = world.AcquireChunkLease(address, ChunkLeaseKind.Presentation);
        using ChunkLease navigation = world.AcquireChunkLease(address, ChunkLeaseKind.Navigation);
        world.TryGetChunk(address, out ChunkRuntime chunk);
        chunk.MarkPresentationBound();
        var result = new JObject
        {
            ["simulationCount"] = chunk.SimulationLeaseCount, ["active"] = chunk.SimulationStatus.ToString(),
            ["bound"] = chunk.PresentationStatus.ToString(), ["navigationCount"] = chunk.NavigationLeaseCount,
            ["evictWhileLeased"] = world.EvictChunk(address), ["dataStatus"] = chunk.DataStatus.ToString()
        };
        first.Dispose();
        first.Dispose();
        result["afterFirst"] = chunk.SimulationLeaseCount;
        result["stillActive"] = chunk.SimulationStatus.ToString();
        second.Dispose();
        presentation.Dispose();
        result["dormant"] = chunk.SimulationStatus.ToString();
        result["unbound"] = chunk.PresentationStatus.ToString();
        result["evictWhileNavigation"] = world.EvictChunk(address);
        navigation.Dispose();
        result["evictReleased"] = world.EvictChunk(address);
        result["remainingChunks"] = world.Chunks.Count;
        return result;
    }

    private static JObject ServiceGenerationLifecycle(JObject args)
    {
        using var world = new WorldRuntime("probe", 1);
        var address = new FlatWorld.WorldModel.WorldAddress("probe", new Int2(0, 0));
        var profile = new ChunkGenerationProfileSnapshot("probe", 1, 8, 8);
        ChunkGenerationRequest first = world.BeginChunkGeneration(address, 1, profile);
        ChunkGenerationRequest second = world.BeginChunkGeneration(address, 1, profile);
        world.TryGetChunk(address, out ChunkRuntime chunk);
        world.RejectFailedGeneration(first, new InvalidOperationException("旧请求"));
        var result = new JObject
        {
            ["newVersionAdvanced"] = second.RequestVersion > first.RequestVersion,
            ["afterOldFailure"] = chunk.DataStatus.ToString(), ["oldFailureReason"] = chunk.FailureReason
        };
        world.RejectFailedGeneration(second, new InvalidOperationException("当前请求"));
        result["afterCurrentFailure"] = chunk.DataStatus.ToString();
        result["currentFailureReason"] = chunk.FailureReason;
        ChunkGenerationRequest third = world.BeginChunkGeneration(address, 1, profile);
        result["retryReasonCleared"] = chunk.FailureReason == null;
        world.CancelChunkGeneration(address);
        world.RejectFailedGeneration(third, new InvalidOperationException("已取消请求"));
        result["afterCancelledFailure"] = chunk.DataStatus.ToString();
        result["cancelledReason"] = chunk.FailureReason;
        ChunkGenerationRequest fourth = world.BeginChunkGeneration(address, 1, profile);
        world.BeginNewEpoch(2);
        world.RejectFailedGeneration(fourth, new InvalidOperationException("旧世界"));
        result["afterOldEpochFailure"] = chunk.DataStatus.ToString();
        result["oldEpochReason"] = chunk.FailureReason;
        return result;
    }

    private static JObject ServiceCommitSnapshot(JObject args)
    {
        int tile = (int)Number(args, "tile", 23);
        float depth = (float)Number(args, "depth", 0.25);
        float changedDepth = (float)Number(args, "changedDepth", 0.75);
        using var world = new WorldRuntime("probe", 1);
        var address = new FlatWorld.WorldModel.WorldAddress("probe", new Int2(-8, 8));
        var profile = new ChunkGenerationProfileSnapshot("probe", 1, 2, 2);
        var liquids = new LiquidTypeCatalog(new[] { "probe:water" });
        ChunkGenerationRequest old = world.BeginChunkGeneration(address, 1, profile);
        ChunkGenerationRequest current = world.BeginChunkGeneration(address, 1, profile);
        using var staleBuffer = new ChunkTerrainBuffer(2, 2, liquids);
        using var stale = new ChunkGenerationResult(old, staleBuffer);
        bool staleAccepted = world.TryCommit(stale, out string staleReason);
        using var buffer = new ChunkTerrainBuffer(2, 2, liquids);
        buffer.SetCell(0, 0, new TerrainCell(tile, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        buffer.SetLiquid(0, 0, liquids.GetIndex("probe:water"), depth);
        using var generated = new ChunkGenerationResult(current, buffer);
        bool committed = world.TryCommit(generated, out string reason);
        world.TryGetChunk(address, out ChunkRuntime chunk);
        WorldRuntimeSnapshot snapshot = world.CaptureSnapshot();
        chunk.Terrain.SetCell(0, 0, new TerrainCell(tile + 1, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        chunk.Terrain.SetLiquid(0, 0, liquids.GetIndex("probe:water"), changedDepth);
        float actualChangedDepth = chunk.Terrain.GetLiquidDepth(0, 0);
        bool repeatedAccepted = world.TryCommit(generated, out string repeatedReason);
        ChunkTerrainData terrain = chunk.Terrain;
        bool evicted = world.EvictChunk(address);
        ChunkGenerationRequest epochRequest = world.BeginChunkGeneration(address, 1, profile);
        using var epochBuffer = new ChunkTerrainBuffer(2, 2, liquids);
        using var epochResult = new ChunkGenerationResult(epochRequest, epochBuffer);
        world.BeginNewEpoch(2);
        bool oldEpochAccepted = world.TryCommit(epochResult, out string epochReason);
        return new JObject
        {
            ["staleAccepted"] = staleAccepted, ["staleDisposed"] = stale.IsDisposed && staleBuffer.IsDisposed,
            ["committed"] = committed, ["reason"] = reason, ["consumed"] = generated.WasConsumed && generated.EcologyWasConsumed,
            ["resultDisposed"] = generated.IsDisposed, ["repeatedAccepted"] = repeatedAccepted,
            ["evicted"] = evicted, ["terrainDisposed"] = terrain.IsDisposed,
            ["snapshotChunks"] = snapshot.Chunks.Count, ["snapshotGround"] = snapshot.Chunks[0].TerrainCells[0].GroundTileId,
            ["snapshotLiquid"] = snapshot.Chunks[0].LiquidIds[0], ["snapshotDepth"] = snapshot.Chunks[0].LiquidDepth[0],
            ["changedDepth"] = actualChangedDepth,
            ["oldEpochAccepted"] = oldEpochAccepted, ["epochDisposed"] = epochResult.IsDisposed && epochBuffer.IsDisposed
        };
    }

    #endregion

    #region 内存存档与命名空间

    private static JObject ServiceSnapshotMemory(JObject args)
    {
        Data_GeneralItem source = ServiceItem(args);
        ItemInstanceSnapshot snapshot = ItemInstanceSnapshot.Capture(source);
        byte[] payload = ItemSnapshotSerialization.SerializePayload(snapshot);
        snapshot = MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(payload);
        var target = ServiceItem(args);
        target.MaxDurability = (float)Number(args, "targetMaximumDurability", 200);
        target.Guid = -1;
        snapshot.RestoreTo(target);
        float capturedAmount = target.Stack.Amount;
        target.Stack.Amount += 1;
        ((CollectableModuleData)target.ModuleDataDic["stock"]).CurrentStock += 1;
        return new JObject
        {
            ["bytes"] = payload.Length, ["definitionId"] = snapshot.DefinitionId, ["guid"] = target.Guid,
            ["amount"] = capturedAmount, ["maximumDurability"] = target.MaxDurability, ["durability"] = target.Durability,
            ["sourceAmount"] = source.Stack.Amount, ["sourceStock"] = ((CollectableModuleData)source.ModuleDataDic["stock"]).CurrentStock,
            ["targetStock"] = ((CollectableModuleData)target.ModuleDataDic["stock"]).CurrentStock,
            ["specialData"] = target.ItemSpecialData
        };
    }

    private static JObject ServiceSpecialNamespace(JObject args)
    {
        var player = new Data_Player { ItemSpecialData = Text(args, "raw", "{\"other\":{\"keep\":42}}") };
        string key = Text(args, "namespace", "probe.service");
        var payload = new JObject { ["counter"] = Number(args, "counter", 3) };
        ItemSpecialDataJsonStore.WriteNamespace(player, key, payload);
        payload["counter"] = -100;
        JObject read = ItemSpecialDataJsonStore.ReadNamespace(player, key);
        double copiedCounter = read.Value<double>("counter");
        read["counter"] = -200;
        return new JObject
        {
            ["root"] = ItemSpecialDataJsonStore.ReadRoot(player.ItemSpecialData), ["copiedCounter"] = copiedCounter,
            ["storedCounter"] = ItemSpecialDataJsonStore.ReadNamespace(player, key).Value<double>("counter")
        };
    }

    private static JObject ServiceSpaceItemChain(JObject args)
    {
        Data_GeneralItem source = ServiceItem(args);
        Data_GeneralItem child = ServiceItem(args);
        child.Guid += 1;
        var inventory = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = child }, new(1) }, "cargo")
        { Index = 1, PanelPosition = new Vector3(3, 4, 0), PanelIsOpen = false };
        source.ModuleDataDic.Add("cargo", new Inventory_ModuleData
        {
            StableName = "cargo", ModuleId = "inventory", Data = new Dictionary<string, Inventory_Data> { ["cargo"] = inventory }
        });
        ItemData cloned = ItemInstanceDataFactory.CloneRuntime(source);
        string encoded = SpaceSession.Encode(source);
        var state = new SpaceSessionState { SimulationSeconds = 12.5 };
        var sector = new SpaceSectorId(-3, 4);
        state.Sectors.Add(new UniverseState { Sector = sector, TemplateId = "probe", Seed = 1 });
        state.LooseItems.Add(new SpaceLooseItemState
        {
            Id = "probe-item", InSpace = true, WorldKey = SpaceSectorCoordinates.SceneKey(sector), Snapshot = encoded,
            Scale = new Vector3(-2, 0, 3.5f), Motion = new SpaceMotionState { Sector = sector }
        });
        string json = JsonConvert.SerializeObject(state);
        SpaceSessionState restored = SaveDataMgr.DeserializeSpaceState(json);
        ItemInstanceSnapshot payload = MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(Convert.FromBase64String(restored.LooseItems[0].Snapshot));
        payload.RestoreTo(cloned);
        Inventory_Data clonedInventory = ((Inventory_ModuleData)cloned.ModuleDataDic["cargo"]).Data["cargo"];
        ItemData clonedChild = clonedInventory.itemSlots[0].itemData;
        float childAmount = clonedChild.Stack.Amount;
        clonedChild.Stack.Amount += 1;
        JObject document = JObject.Parse(json);
        document.Remove("Version");
        string missingVersion = ServiceException(() => SaveDataMgr.DeserializeSpaceState(document.ToString()));
        document["Version"] = 1;
        string wrongVersion = ServiceException(() => SaveDataMgr.DeserializeSpaceState(document.ToString()));
        document["Version"] = 2;
        document["SimulationSeconds"] = -1;
        string invalidClock = ServiceException(() => SaveDataMgr.DeserializeSpaceState(document.ToString()));
        return new JObject
        {
            ["version"] = restored.Version, ["clock"] = restored.SimulationSeconds,
            ["sectorX"] = restored.Sectors[0].Sector.X.ToString(), ["sectorY"] = restored.Sectors[0].Sector.Y.ToString(),
            ["encodedPreserved"] = restored.LooseItems[0].Snapshot == encoded,
            ["childAmount"] = childAmount, ["sourceAmount"] = child.Stack.Amount,
            ["childGuid"] = clonedChild.Guid, ["sourceGuid"] = child.Guid,
            ["independentChild"] = !ReferenceEquals(child, clonedChild), ["selected"] = clonedInventory.Index,
            ["slotCount"] = clonedInventory.itemSlots.Count, ["secondEmpty"] = clonedInventory.itemSlots[1].itemData == null,
            ["scaleX"] = restored.LooseItems[0].Scale.x, ["scaleY"] = restored.LooseItems[0].Scale.y,
            ["scaleZ"] = restored.LooseItems[0].Scale.z, ["scaleFields"] = ((JObject)JObject.Parse(json)["LooseItems"][0]["Scale"]).Count,
            ["missingVersion"] = missingVersion, ["wrongVersion"] = wrongVersion, ["invalidClock"] = invalidClock
        };
    }

    private static string ServiceException(Action action)
    {
        try { action(); return null; }
        catch (Exception exception) { return exception.GetType().Name; }
    }

    #endregion

    #region 联机协议输入与实例负载

    private static JObject ServiceNetworkEndpoint(JObject args)
    {
        bool accepted = NetworkConnectionEndpoint.TryParse(Text(args, "address", "localhost"),
            (ushort)Number(args, "fallbackPort", 7777), out NetworkConnectionEndpoint endpoint, out string error);
        return new JObject
        {
            ["accepted"] = accepted, ["host"] = endpoint.Host, ["port"] = endpoint.Port,
            ["display"] = accepted ? endpoint.ToString() : null, ["error"] = error
        };
    }

    private static JObject ServiceNetworkPayload(JObject args)
    {
        Data_GeneralItem source = ServiceItem(args);
        bool serialized = ItemNetworkStateSerialization.TrySerializeItemData(source, out byte[] bytes);
        bool read = ItemNetworkStateSerialization.TryReadIdentity(bytes, out int guid, out string itemId);
        bool valid = ItemNetworkStateSerialization.IsValidPayload(bytes);
        if (bytes == null || bytes.Length == 0) throw new InvalidOperationException("正式网络实例序列化没有产出负载。");
        byte[] corrupted = (byte[])bytes.Clone();
        corrupted[0] ^= 0xFF;
        return new JObject
        {
            ["serialized"] = serialized, ["valid"] = valid, ["read"] = read, ["guid"] = guid,
            ["itemId"] = itemId, ["bytes"] = bytes.Length,
            ["corruptedAccepted"] = ItemNetworkStateSerialization.IsValidPayload(corrupted),
            ["emptyAccepted"] = ItemNetworkStateSerialization.IsValidPayload(Array.Empty<byte>()),
            ["sourceAmount"] = source.Stack.Amount
        };
    }

    #endregion

    #region MOD 内容补丁与包路径

    private static JObject ServiceModPatch(JObject args)
    {
        var target = args["target"] is JObject input ? (JObject)input.DeepClone() : new JObject();
        var patch = new ModPatchOperation
        {
            Target = "probe", Path = Text(args, "path", "count"), Operation = Text(args, "operation", "set"),
            Value = args["value"]?.DeepClone(), Expect = args["expect"]?.DeepClone()
        };
        string error = null;
        try { ModRuntimeManager.ApplyPatchOperation(target, patch, "Lua 系统回归", 0); }
        catch (InvalidDataException exception) { error = exception.GetType().Name; }
        return new JObject { ["target"] = target, ["accepted"] = error == null, ["error"] = error };
    }

    private static JObject ServiceModPath(JObject args)
    {
        string root = Path.Combine(Path.GetTempPath(), "FlatWorldLuaProbePackage");
        string resolved = null, error = null;
        try { resolved = ModPathUtility.ResolvePackagePath(root, Text(args, "path", "Scripts/main.lua"), false); }
        catch (InvalidDataException exception) { error = exception.GetType().Name; }
        return new JObject
        {
            ["accepted"] = error == null, ["error"] = error,
            ["relative"] = resolved == null ? null : Path.GetRelativePath(root, resolved).Replace('\\', '/')
        };
    }

    private static JObject ServiceModCandidatePatch(JObject args)
    {
        var officialDocument = new JObject { ["count"] = 1, ["other"] = 9 };
        var official = new Dictionary<string, JObject>(StringComparer.Ordinal) { ["probe"] = officialDocument };
        Dictionary<string, JObject> published = official;
        var candidate = new ResourceReloadContext();
        candidate.AddDictionary(() => published, value => published = value);
        string error = null;
        try
        {
            candidate.InCandidate(() =>
            {
                published["probe"] = (JObject)officialDocument.DeepClone();
                ModRuntimeManager.ApplyPatchOperation(published["probe"], new ModPatchOperation
                { Target = "probe", Path = "count", Operation = "replace", Expect = 1, Value = 3 }, "Lua候选", 0);
                ModRuntimeManager.ApplyPatchOperation(published["probe"], new ModPatchOperation
                { Target = "probe", Path = "count", Operation = "replace", Expect = Flag(args, "fail", false) ? 4 : 3, Value = 5 }, "Lua候选", 1);
            });
            candidate.Commit();
        }
        catch (InvalidDataException exception) { error = exception.GetType().Name; }
        return new JObject
        {
            ["error"] = error, ["published"] = !ReferenceEquals(published, official),
            ["officialCount"] = officialDocument.Value<int>("count"), ["actualCount"] = published["probe"].Value<int>("count"),
            ["other"] = published["probe"].Value<int>("other")
        };
    }

    #endregion

    #region UI 设置音频与本地化

    private static JObject ServiceMobileSize(JObject args)
    {
        float requested = (float)Number(args, "size", 1);
        if (Flag(args, "nan", false)) requested = float.NaN;
        if (Flag(args, "infinity", false)) requested = float.PositiveInfinity;
        return new JObject { ["size"] = UIUserSettings.SanitizeMobileControlSize(requested) };
    }

    private static JObject ServiceAudioVolume(JObject args)
    {
        var settings = new AudioUserSettings { Master = (float)Number(args, "master", 1) };
        var values = new JArray();
        foreach (AudioBus bus in Enum.GetValues(typeof(AudioBus)))
        {
            settings.SetBusVolume(bus, (float)Number(args, "volume", 0.5));
            values.Add(new JObject { ["bus"] = bus.ToString(), ["volume"] = settings.GetBusVolume(bus) });
        }
        settings.Clamp();
        AudioPlayOptions global = AudioPlayOptions.Global(0.6f, 1.2f);
        AudioPlayOptions positioned = AudioPlayOptions.At(new Vector3(3, 4, 0), 0.6f, 1.2f);
        return new JObject
        {
            ["master"] = settings.Master, ["buses"] = values, ["globalHasPosition"] = global.HasWorldPosition,
            ["positionedHasPosition"] = positioned.HasWorldPosition, ["x"] = positioned.WorldPosition.x,
            ["y"] = positioned.WorldPosition.y, ["volume"] = positioned.VolumeScale, ["pitch"] = positioned.PitchScale
        };
    }

    private static JObject ServiceFootstepSurface(JObject args)
    {
        // 合成边界输入交给正式选音规则，Lua 自行断言深水不发脚步等语义。
        string cueId = Mod_Mover.ResolveFootstepCue(
            (int)Number(args, "biome", 4),
            (float)Number(args, "water", 0),
            (float)Number(args, "snow", 0),
            Flag(args, "support", false),
            (byte)Math.Clamp((int)Number(args, "grass", 0), 0, 255));
        return new JObject { ["cue"] = cueId ?? string.Empty, ["silent"] = cueId == null };
    }

    private static JObject ServiceAudioInventoryCue(JObject args)
    {
        if (!Enum.TryParse(Text(args, "action", "Pickup"), true, out InventoryAudioAction action))
            throw new ArgumentException("未知的库存音效操作");

        var tags = new List<string>();
        if (args["tags"] is JArray values)
            foreach (JToken value in values)
                if (value.Type == JTokenType.String)
                    tags.Add(value.Value<string>());
        string cue = InventoryInteractionAudio.ResolveCue(action,
            Text(args, "itemId", ""), tags,
            (float)Number(args, "kg", 0f),
            (float)Number(args, "liters", 0f),
            (float)Number(args, "amount", 1f));
        return new JObject { ["cue"] = cue ?? string.Empty, ["valid"] = cue != null };
    }

    [Serializable]
    private sealed class ServiceAudioCatalogDocument { public AudioCue[] cues; }

    private static JObject ServiceAudioCatalog(JObject args)
    {
        string firstId = Text(args, "first", "probe.first"), secondId = Text(args, "second", "probe.second");
        var first = ScriptableObject.CreateInstance<AudioCue>();
        var second = ScriptableObject.CreateInstance<AudioCue>();
        var catalog = ScriptableObject.CreateInstance<AudioCatalog>();
        first.hideFlags = second.hideFlags = catalog.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            // Unity 正式 JSON 装配私有序列化字段，验证独立目录的缓存重建和重复事件拒绝。
            JsonUtility.FromJsonOverwrite(new JObject { ["id"] = firstId }.ToString(), first);
            JsonUtility.FromJsonOverwrite(new JObject { ["id"] = secondId }.ToString(), second);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new ServiceAudioCatalogDocument { cues = new[] { first, second } }), catalog);
            catalog.RebuildIndex();
            bool found = catalog.TryGet("  " + firstId.ToUpperInvariant() + "  ", out AudioCue resolved);
            List<string> errors = catalog.GetValidationErrors();
            return new JObject
            {
                ["cueCount"] = catalog.Cues.Count, ["errors"] = errors.Count,
                ["found"] = found, ["resolvedFirst"] = ReferenceEquals(resolved, first),
                ["emptyAccepted"] = catalog.TryGet("", out _), ["firstId"] = first.Id, ["secondId"] = second.Id
            };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(second);
            UnityEngine.Object.DestroyImmediate(first);
        }
    }

    private static JObject ServiceNavigationPolicy(JObject args)
    {
        var root = new GameObject(Text(args, "root", "ProbePanel"), typeof(RectTransform));
        root.SetActive(false);
        root.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var child = new GameObject("ProbeControl", typeof(RectTransform));
            child.transform.SetParent(root.transform, false);
            string kind = Text(args, "kind", "button");
            Selectable selectable = kind switch
            {
                "slider" => child.AddComponent<Slider>(),
                "scrollbar" => child.AddComponent<Scrollbar>(),
                "button" => child.AddComponent<Button>(),
                _ => throw new ArgumentException("未知的 UI 控件夹具类型：" + kind)
            };
            // 停用的独立控件只查询正式导航策略，不注册到 UIManager。
            return new JObject { ["excluded"] = FlatWorldUITheme.IsGamepadNavigationExcluded(selectable) };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static JObject ServicePanelLifecycle(JObject args)
    {
        var root = new GameObject("[LuaPanelProbe]", typeof(RectTransform), typeof(CanvasGroup));
        root.SetActive(false);
        root.hideFlags = HideFlags.HideAndDontSave;
        int originalOrder = BasePanel.CurrentOrder;
        try
        {
            BasePanel panel = root.AddComponent<BasePanel>();
            panel.rectTransform = root.GetComponent<RectTransform>();
            panel.canvasGroup = root.GetComponent<CanvasGroup>();
            int opened = 0, closed = 0;
            panel.Opened += () => opened++;
            panel.Closed += () => closed++;
            panel.InitClosed();
            panel.InitClosed();
            var result = new JObject
            {
                ["initialAlpha"] = panel.canvasGroup.alpha,
                ["initialInteraction"] = panel.canvasGroup.interactable || panel.canvasGroup.blocksRaycasts
            };
            panel.SetOpenVisualAlpha((float)Number(args, "alpha", 0.75));
            panel.Open();
            panel.Open();
            result["openAlpha"] = panel.canvasGroup.alpha;
            result["openInteraction"] = panel.canvasGroup.interactable && panel.canvasGroup.blocksRaycasts;
            result["openEvents"] = opened;
            panel.Close();
            panel.Close();
            result["closedAlpha"] = panel.canvasGroup.alpha;
            result["closedInteraction"] = panel.canvasGroup.interactable || panel.canvasGroup.blocksRaycasts;
            result["closeEvents"] = closed;
            result["inactiveBlocksGameplay"] = panel.IsGameplayInputBlocking;
            return result;
        }
        finally
        {
            try { UnityEngine.Object.DestroyImmediate(root); }
            finally { BasePanel.CurrentOrder = originalOrder; }
        }
    }

    #region 下拉列表正式控件采样

    private static JObject ServiceDropdownLayout(JObject args)
    {
        string kind = Text(args, "kind", "tmp");
        if (kind != "tmp" && kind != "legacy")
            throw new ArgumentException("未知的下拉控件类型：" + kind);
        JArray counts = args["counts"] as JArray ?? new JArray((int)Number(args, "count", 3));
        if (counts.Count == 0 || counts.Count > 16)
            throw new ArgumentException("下拉夹具必须提供 1 至 16 次展开的选项数量。");
        foreach (JToken count in counts)
            if (count.Type != JTokenType.Integer || count.Value<int>() < 1 || count.Value<int>() > 32)
                throw new ArgumentException("下拉夹具选项数量必须为 1 至 32 的整数。");

        EventSystem originalEvents = EventSystem.current;
        GameObject originalSelection = originalEvents != null ? originalEvents.currentSelectedGameObject : null;
        var root = new GameObject("[LuaDropdownProbe]", typeof(RectTransform));
        root.SetActive(false);
        root.hideFlags = HideFlags.HideAndDontSave;
        var result = new JObject { ["kind"] = kind, ["samples"] = new JArray() };
        try
        {
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.enabled = false;
            ((RectTransform)root.transform).sizeDelta = new Vector2(4096f, 4096f);
            RectTransform eventsRoot = ServiceDropdownRect("EventSystem", root.transform);
            EventSystem probeEvents = eventsRoot.gameObject.AddComponent<GameplayTestDropdownEventSystem>();
            RectTransform control = ServiceDropdownRect("Control", root.transform);
            control.sizeDelta = new Vector2(240f, 64f);
            RectTransform template = ServiceDropdownRect("Template", control);
            template.gameObject.SetActive(false);
            template.anchorMin = Vector2.zero;
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.sizeDelta = new Vector2(0f, 360f);
            ItemStepScrollRect scroll = template.gameObject.AddComponent<ItemStepScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = false;
            RectTransform viewport = ServiceDropdownRect("Viewport", template);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(3f, 3f);
            viewport.offsetMax = new Vector2(-3f, -3f);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = ServiceDropdownRect("Content", viewport);
            content.gameObject.AddComponent<VerticalLayoutGroup>();
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            RectTransform item = ServiceDropdownRect("Item", content);
            item.sizeDelta = new Vector2(100f, 100f);
            item.gameObject.AddComponent<Toggle>();
            item.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
            scroll.viewport = viewport;
            scroll.content = content;
            TMP_Dropdown tmp = kind == "tmp" ? control.gameObject.AddComponent<GameplayTestTmpDropdownControl>() : null;
            Dropdown legacy = kind == "legacy" ? control.gameObject.AddComponent<GameplayTestLegacyDropdownControl>() : null;
            if (tmp != null) tmp.template = template;
            if (legacy != null) legacy.template = template;
            DropdownListLayout.EnsureFor(template);
            DropdownListLayout.EnsureFor(template);
            root.SetActive(true);
            // 独立事件系统接收原生 Show 的选择事件，当前界面的焦点从始至终留在原事件系统中。
            EventSystem.current = probeEvents;
            if (tmp is GameplayTestTmpDropdownControl tmpControl) tmpControl.InitializeForRules();
            if (legacy is GameplayTestLegacyDropdownControl legacyControl) legacyControl.InitializeForRules();
            foreach (JToken count in counts)
            {
                control.gameObject.SetActive(false);
                var labels = new List<string>();
                for (int index = 0; index < count.Value<int>(); index++) labels.Add("Option " + index);
                if (tmp != null) { tmp.ClearOptions(); tmp.AddOptions(labels); }
                if (legacy != null) { legacy.ClearOptions(); legacy.AddOptions(labels); }
                control.gameObject.SetActive(true);
                // 编辑态不依赖普通 MonoBehaviour 回调，显式调用正式准备入口后仍由原生 Show 创建列表。
                template.GetComponent<DropdownListLayout>().PrepareForOpen();
                if (tmp != null) tmp.Show(); else legacy.Show();
                Transform popup = control.Find("Dropdown List");
                if (popup == null) throw new InvalidOperationException("正式下拉 Show 没有创建列表。");
                ScrollRect actualScroll = popup.GetComponent<ScrollRect>();
                actualScroll.Rebuild(CanvasUpdate.Prelayout);
                actualScroll.Rebuild(CanvasUpdate.PostLayout);
                actualScroll.verticalNormalizedPosition = 1f;
                Toggle[] rows = actualScroll.content.GetComponentsInChildren<Toggle>(false);
                if (rows.Length == 0) throw new InvalidOperationException("正式下拉没有生成活动选项。");
                RectTransform first = (RectTransform)rows[0].transform;
                RectTransform last = (RectTransform)rows[rows.Length - 1].transform;
                bool allRowsSameHeight = true;
                foreach (Toggle row in rows)
                    allRowsSameHeight &= Mathf.Abs(((RectTransform)row.transform).rect.height - first.rect.height) < 0.01f;
                var sample = new JObject
                {
                    ["optionCount"] = count.Value<int>(), ["activeItemCount"] = rows.Length,
                    ["rowHeight"] = first.rect.height, ["allRowsSameHeight"] = allRowsSameHeight,
                    ["viewportHeight"] = actualScroll.viewport.rect.height,
                    ["contentHeight"] = actualScroll.content.rect.height,
                    ["firstFullyVisible"] = ServiceDropdownContains(actualScroll.viewport, first),
                    ["lastInitiallyVisible"] = ServiceDropdownContains(actualScroll.viewport, last),
                    ["layoutDisabled"] = !actualScroll.content.GetComponent<VerticalLayoutGroup>().enabled,
                    ["fitterDisabled"] = !actualScroll.content.GetComponent<ContentSizeFitter>().enabled
                };
                actualScroll.verticalNormalizedPosition = 0f;
                sample["lastVisibleAtBottom"] = ServiceDropdownContains(actualScroll.viewport, last);
                ((JArray)result["samples"]).Add(sample);
            }
            result["layoutComponentCount"] = template.GetComponents<DropdownListLayout>().Length;
        }
        finally
        {
            try
            {
                root.SetActive(false);
                UnityEngine.Object.DestroyImmediate(root);
            }
            finally { if (originalEvents != null) EventSystem.current = originalEvents; }
        }
        result["originalSelectionPreserved"] = originalEvents == null || originalEvents.currentSelectedGameObject == originalSelection;
        return result;
    }

    private static RectTransform ServiceDropdownRect(string name, Transform parent)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }

    private static bool ServiceDropdownContains(RectTransform viewport, RectTransform item)
    {
        var corners = new Vector3[4];
        item.GetWorldCorners(corners);
        foreach (Vector3 corner in corners)
        {
            float y = viewport.InverseTransformPoint(corner).y;
            if (y < viewport.rect.yMin - 0.01f || y > viewport.rect.yMax + 0.01f) return false;
        }
        return true;
    }

    #endregion

    private static JObject ServiceLocalizationIdentity(JObject args)
    {
        string tag = Text(args, "tag", "resource.wood");
        string item = Text(args, "item", " Stick_Wood ");
        string text = Text(args, "text", "游戏设置");
        return new JObject
        {
            ["stable"] = ItemTagLocalizationCatalog.IsStableId(tag),
            ["itemNameKey"] = FlatWorldLocalizationService.GetItemLabelKey(item),
            ["itemDescriptionKey"] = FlatWorldLocalizationService.GetItemDescriptionKey(item),
            ["tagKey"] = FlatWorldLocalizationService.GetItemTagLabelKey(tag),
            ["uiKey"] = FlatWorldLocalizationService.GetUiTextKey(text),
            ["paddedUiKey"] = FlatWorldLocalizationService.GetUiTextKey("  " + text + "  "),
            ["otherUiKey"] = FlatWorldLocalizationService.GetUiTextKey(text + "x")
        };
    }

    #endregion

    #region 渲染配置正式约束

    private static JObject ServiceFogConfiguration(JObject args)
    {
        var config = new WorldRenderingConfig.RenderingDenseFog
        {
            clearRadius = (float)Number(args, "clearRadius", 10), opaqueRadius = (float)Number(args, "opaqueRadius", 20),
            transitionSeconds = (float)Number(args, "transitionSeconds", 0.3), noiseScale = (float)Number(args, "noiseScale", 1),
            driftSpeed = (float)Number(args, "driftSpeed", 0.1), noiseStrength = (float)Number(args, "noiseStrength", 0.1),
            color = new Color((float)Number(args, "red", 0.5), 0.5f, 0.5f, 1)
        };
        return new JObject { ["valid"] = config.IsValid };
    }

    private static JObject ServiceUniverseViewConfiguration(JObject args)
    {
        var config = new WorldRenderingConfig.RenderingUniverseView
        {
            startOrthographicSize = (float)Number(args, "start", 10), opaqueOrthographicSize = (float)Number(args, "opaque", 20),
            maximumOrthographicSize = (float)Number(args, "maximum", 100), zoomMultiplier = (float)Number(args, "zoom", 1.5),
            transitionSeconds = (float)Number(args, "transitionSeconds", 0),
            entryPlanetRadiusMultiplier = (float)Number(args, "entry", 2), minimumBodyPixels = (float)Number(args, "pixels", 0)
        };
        return new JObject { ["valid"] = config.IsValid };
    }

    private static JObject ServiceZoomCloudMask(JObject args)
    {
        Shader shader = Shader.Find("Hidden/FlatWorld/ZoomClouds");
        if (shader == null) throw new InvalidOperationException("缺少正式高视野云圈 Shader。");
        var root = new GameObject("[LuaCloudMaskProbe]");
        root.SetActive(false);
        root.hideFlags = HideFlags.HideAndDontSave;
        Material material = null;
        try
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = root.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = (float)Number(args, "size", 20);
            camera.aspect = (float)Number(args, "aspect", 1);
            Vector3 player = new Vector3(13f, -7f, 0f);
            root.transform.position = player + Vector3.back * 10f;
            var settings = WorldRenderingConfigCatalog.Default.zoomClouds;
            // 与真实绘制共用参数提交，采样不同镜头下 Shader 实际收到的世界半径与投影。
            ZoomCloudMaterialParameters.Apply(material, camera, args.Value<bool?>("flipped") ?? false,
                player, player, Vector2.zero, settings);
            Vector4 radii = material.GetVector("_CloudRadii");
            Vector4 origin = material.GetVector("_CloudViewportOrigin");
            Vector4 right = material.GetVector("_CloudViewportRight");
            Vector4 up = material.GetVector("_CloudViewportUp");
            Vector2 center = new Vector2(origin.x + 0.5f * (right.x + up.x), origin.y + 0.5f * (right.y + up.y));
            return new JObject
            {
                ["clearRadius"] = radii.x, ["opaqueRadius"] = radii.y,
                ["noiseScale"] = material.GetVector("_CloudShape").x, ["expectedNoiseScale"] = settings.noiseScale,
                ["viewportHeight"] = new Vector2(up.x, up.y).magnitude,
                ["viewportWidth"] = new Vector2(right.x, right.y).magnitude,
                ["expectedHeight"] = camera.orthographicSize * 2f,
                ["expectedWidth"] = camera.orthographicSize * 2f * camera.aspect,
                ["centerAligned"] = center.sqrMagnitude < 0.000001f
            };
        }
        finally
        {
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static JObject ServiceUniverseViewTransition(JObject args)
    {
        float opacity = SurfaceZoomTransition.ResolveOpacity((float)Number(args, "size", 60),
            WorldRenderingConfigCatalog.Default.universeView);
        return new JObject { ["opacity"] = opacity };
    }

    private static JObject ServiceGroupProjection(JObject args)
    {
        var root = new GameObject("[LuaSortingProbe]");
        root.SetActive(false);
        root.AddComponent<SortingGroup>();
        root.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var middle = new GameObject("InnerGroup");
            middle.SetActive(false);
            middle.transform.SetParent(root.transform, false);
            middle.AddComponent<SortingGroup>();
            var child = new GameObject("Sprite");
            child.SetActive(false);
            child.transform.SetParent(middle.transform, false);
            child.AddComponent<SpriteRenderer>();
            SortingGroup outer = root.GetComponent<SortingGroup>(), inner = middle.GetComponent<SortingGroup>();
            SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
            SortingLayer[] layers = SortingLayer.layers;
            outer.sortingLayerID = layers[0].id;
            inner.sortingLayerID = layers[Math.Min(1, layers.Length - 1)].id;
            renderer.sortingLayerID = layers[Math.Min(2, layers.Length - 1)].id;
            outer.sortingOrder = (int)Number(args, "outerOrder", 77);
            inner.sortingOrder = (int)Number(args, "innerOrder", 50);
            renderer.sortingOrder = (int)Number(args, "spriteOrder", 31);
            WorldSortingManager.ReadExternalRendererKey(renderer, out int outerLayer, out int outerOrder);
            outer.enabled = false;
            WorldSortingManager.ReadExternalRendererKey(renderer, out int innerLayer, out int innerOrder);
            inner.enabled = false;
            WorldSortingManager.ReadExternalRendererKey(renderer, out int spriteLayer, out int spriteOrder);
            return new JObject
            {
                ["outerOrder"] = outerOrder, ["innerOrder"] = innerOrder, ["spriteOrder"] = spriteOrder,
                ["outerLayerMatches"] = outerLayer == outer.sortingLayerID,
                ["innerLayerMatches"] = innerLayer == inner.sortingLayerID,
                ["spriteLayerMatches"] = spriteLayer == renderer.sortingLayerID
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    #endregion
}

/// <summary>同步规则只补齐原生下拉的 Start，并立即清理独立弹出对象。</summary>
[AddComponentMenu("")]
public sealed class GameplayTestTmpDropdownControl : TMP_Dropdown
{
    #region 独立夹具生命周期

    public void InitializeForRules() { alphaFadeSpeed = 0f; base.Start(); }
    protected override void DestroyDropdownList(GameObject dropdownList) => DestroyImmediate(dropdownList);
    protected override void DestroyBlocker(GameObject blocker) => DestroyImmediate(blocker);

    #endregion
}

/// <summary>Legacy 下拉复用正式 Show，仅把编辑态收尾改为同步销毁。</summary>
[AddComponentMenu("")]
public sealed class GameplayTestLegacyDropdownControl : Dropdown
{
    #region 独立夹具生命周期

    public void InitializeForRules() { alphaFadeSpeed = 0f; base.Start(); }
    protected override void DestroyDropdownList(GameObject dropdownList) => DestroyImmediate(dropdownList);
    protected override void DestroyBlocker(GameObject blocker) => DestroyImmediate(blocker);

    #endregion
}

/// <summary>无输入模块的临时事件系统只承接独立控件的选择事件。</summary>
[ExecuteAlways, AddComponentMenu("")]
public sealed class GameplayTestDropdownEventSystem : EventSystem { }
