using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>真实流程采样仅在正式 Lua 场景的隔离存档世界内执行。</summary>
public static partial class GameplayTestRuntimeProbe
{
    #region 真实流程操作登记

    private static readonly Dictionary<string, Func<Player, JObject, JObject>> operations = new(StringComparer.Ordinal)
    {
        ["testing/resource_cache"] = (_, _) => GameplayTestResourceCache.Sample(),
        ["core_lifecycle/live_entry"] = CaptureLiveEntry,
        ["world_streaming/live_window"] = CaptureLiveWindow,
        ["save/isolated_roundtrip"] = CaptureIsolatedRoundtrip,
        ["industrial_fluids/selector_routes"] = GameplayTestFluidNetworks.CaptureSelectorRoutes,
        ["industrial_fluids/phase_preference"] = GameplayTestFluidNetworks.CapturePhasePreference,
        ["industrial_fluids/tank_ownership"] = GameplayTestFluidNetworks.CaptureTankOwnership,
        ["industrial_fluids/reaction_capacity"] = GameplayTestFluidDevices.CaptureReactionCapacity,
        ["industrial_fluids/residue_transfer"] = GameplayTestFluidDevices.CaptureResidueTransfer,
        ["industrial_fluids/liquid_pump_flow"] = GameplayTestFluidDevices.CaptureLiquidPump,
        ["industrial_fluids/surface_pump_flow"] = GameplayTestFluidSurface.CapturePumpFlow,
        ["industrial_fluids/surface_outlet_flow"] = GameplayTestFluidSurface.CaptureOutletFlow
    };

    public static void Register(string systemId, string operationId, Func<Player, JObject, JObject> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        string key = Key(systemId, operationId);
        if (asyncOperations.ContainsKey(key)) throw new InvalidOperationException("同步与跨帧操作重复：" + key);
        if (operations.TryGetValue(key, out var previous))
        {
            if (previous.Method == handler.Method && Equals(previous.Target, handler.Target)) return;
            throw new InvalidOperationException("真实流程测试操作重复登记：" + key);
        }
        operations.Add(key, handler);
    }

    public static bool Supports(string systemId, string operationId) => operations.ContainsKey(Key(systemId, operationId)) ||
        asyncOperations.ContainsKey(Key(systemId, operationId));

    public static JObject Capture(Player player, string systemId, string operationId, JObject arguments)
    {
        if (!operations.TryGetValue(Key(systemId, operationId), out var operation))
            throw new ArgumentException("未登记的真实流程测试操作：" + systemId + "/" + operationId);
        RequireIsolatedSaveRoot(player);
        JObject result = operation(player, arguments == null ? new JObject() : (JObject)arguments.DeepClone());
        if (result == null) throw new InvalidOperationException("真实流程测试未返回采样。");
        result["_system"] = systemId;
        result["_operation"] = operationId;
        result["scope"] = "live-isolated-world";
        return result;
    }

    private static string Key(string system, string operation)
    {
        foreach (string value in new[] { system, operation })
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 80) throw new ArgumentException("测试操作 ID 无效。");
            foreach (char character in value)
                if (!(character >= 'a' && character <= 'z') && !(character >= '0' && character <= '9') && character != '_')
                    throw new ArgumentException("测试操作 ID 含非法字符。");
        }
        return system + "/" + operation;
    }

    #endregion

    #region 隔离边界

    public static string RequireIsolatedSaveRoot(Player player)
    {
        if (!Application.isEditor || !Application.isPlaying || player == null || !player.IsLocalProfile)
            throw new InvalidOperationException("真实流程测试只允许 Editor PlayMode 中已进入隔离世界的本地测试角色。");
        SaveDataMgr saves = SaveDataMgr.TestExistingInstance;
        if (saves == null || saves.SaveData == null || string.IsNullOrWhiteSpace(saves.UserSavePath))
            throw new InvalidOperationException("测试存档管理器或目录未就绪。");
        string projectRoot = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));
        string boundary = Path.GetFullPath(Path.Combine(projectRoot,
            "Library", "FlatWorldGameplayTests", "Saves")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string root = Path.GetFullPath(saves.UserSavePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!root.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("拒绝访问正式存档：当前目录不属于 Library 测试隔离目录。");
        string runId = Path.GetRelativePath(boundary, root);
        GameplayTestSelection.ValidateRunId(runId);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("本轮测试隔离目录不存在。");
        string cursor = root;
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("测试写盘目录不允许符号链接或目录联接。");
            if (string.Equals(cursor, projectRoot, StringComparison.OrdinalIgnoreCase)) break;
            cursor = Path.GetDirectoryName(cursor);
        }
        if (!saves.SaveData.PlayerData_Dict.TryGetValue(player.ProfileName, out Data_Player stored) ||
            !ReferenceEquals(stored, player.Data))
            throw new InvalidOperationException("当前角色不属于本轮测试存档。");
        return root;
    }

    #endregion

    #region 正式入世与流送观察

    private static JObject CaptureLiveEntry(Player player, JObject args)
    {
        GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
        GameRes resources = GameRes.ExistingInstance;
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        bool footReady = chunks != null && chunks.TryGetRuntimeChunkView(player.transform.position, out var foot) &&
            foot != null && foot.gameObject.activeInHierarchy;
        return new JObject
        {
            ["resourceState"] = resources?.LoadState.ToString(), ["startupReady"] = resources != null && resources.IsStartupReady,
            ["inWorld"] = manager != null && manager.IsInGameWorld, ["gameplayReady"] = manager != null && manager.IsGameplayReady,
            ["entryInProgress"] = manager != null && manager.IsWorldEntryInProgress, ["localProfile"] = player.IsLocalProfile,
            ["profile"] = player.ProfileName, ["playerInitialized"] = player.IsInitialized,
            ["playerActive"] = player.gameObject.activeInHierarchy, ["footViewReady"] = footReady,
            ["windowReady"] = chunks != null && chunks.AreRuntimeWindowPresentationsReady,
            ["guid"] = player.itemData.Guid, ["isolationRoot"] = RequireIsolatedSaveRoot(player)
        };
    }

    private static JObject CaptureLiveWindow(Player player, JObject args)
    {
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || manager.WorldRuntime == null) throw new InvalidOperationException("真实世界流送未建立。");
        var result = new JObject
        {
            ["runtimeActive"] = manager.IsWorldModelRuntimeActive,
            ["windowReady"] = manager.AreRuntimeWindowPresentationsReady,
            ["world"] = manager.WorldRuntime.WorldId, ["epoch"] = manager.WorldRuntime.Epoch,
            ["pendingPresentation"] = manager.PendingRuntimeChunkPresentationCount,
            ["pendingData"] = manager.HasPendingChunkDataLoads,
            ["retainedChunks"] = manager.Chunks.Count
        };
        var address = manager.ResolveWorldAddress(player.transform.position);
        bool found = manager.TryGetChunkRuntime(address, out var chunk);
        result["footFound"] = found;
        result["footData"] = chunk?.DataStatus.ToString();
        result["footPresentation"] = chunk?.PresentationStatus.ToString();
        result["footSimulationLeases"] = chunk?.SimulationLeaseCount;
        result["footPresentationLeases"] = chunk?.PresentationLeaseCount;
        result["footView"] = manager.TryGetRuntimeChunkView(address, out var view) && view != null && view.gameObject.activeInHierarchy;
        return result;
    }

    #endregion

    #region 正式隔离写盘与非发布恢复

    private static JObject CaptureIsolatedRoundtrip(Player player, JObject args)
    {
        string root = RequireIsolatedSaveRoot(player);
        SaveDataMgr saves = SaveDataMgr.TestExistingInstance;
        GameSaveData original = saves.SaveData;
        string originalName = original.saveName, originalSeed = original.SaveSeed;
        string originalDirectory = saves.UserSavePath;
        int originalPlayers = original.PlayerData_Dict.Count, originalPlanets = original.PlanetData_Dict.Count;
        Data_Player originalPlayer = player.Data;
        int originalGuid = originalPlayer.Guid;
        string originalSpecial = originalPlayer.ItemSpecialData;
        var copy = MemoryPackSerializer.Deserialize<GameSaveData>(MemoryPackSerializer.Serialize(original));
        copy.Mechanical = MemoryPackSerializer.Deserialize<MachineArchive>(MemoryPackSerializer.Serialize(original.Mechanical));
        copy.DroppedItems = MemoryPackSerializer.Deserialize<DroppedItemArchive>(MemoryPackSerializer.Serialize(original.DroppedItems));
        string markerProfile = "player:lua-roundtrip-" + Guid.NewGuid().ToString("N");
        float amount = (float)GameplayTestSystemProbe.Number(args, "amount", 3);
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0)
            throw new ArgumentException("保存夹具的物品数量必须是非负有限单精度数。");
        var item = new Data_GeneralItem
        {
            IDName = "Stick_Wood", code = "lua-save-probe", Guid = 910143,
            Stack = new ItemStack { Amount = amount }, ItemSpecialData = "{\"roundtrip\":true}"
        };
        var marker = new Data_Player { IDName = "Player", Guid = 910144, Name_User = "Lua隔离恢复", ItemSpecialData = "{\"probe\":1}" };
        marker._inventoryData["probe"] = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = item } }, "probe");
        copy.PlayerData_Dict.Add(markerProfile, marker);
        string directory = Path.Combine(root, "Roundtrips", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "roundtrip.bytes");
        string parent = Path.GetDirectoryName(directory);
        if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("测试归档子目录不允许目录联接。");
        RequireIsolatedSaveRoot(player);
        bool firstWritten = saves.SaveToDisk(copy, directory, "roundtrip");
        if (!firstWritten || !File.Exists(file)) throw new IOException("正式隔离写盘没有生成测试文件。");
        byte[] firstBytes = File.ReadAllBytes(file);
        item.Stack.Amount = amount + 1;
        copy.PlayerData_Dict[markerProfile].ItemSpecialData = "{\"probe\":2}";
        RequireIsolatedSaveRoot(player);
        bool secondWritten = saves.SaveToDisk(copy, directory, "roundtrip");
        if (!secondWritten) throw new IOException("正式隔离替换写盘失败。");
        byte[] secondBytes = File.ReadAllBytes(file);
        string backup = file + ".bak";
        if (!File.Exists(backup)) throw new IOException("原子替换未保留上一版测试存档备份。");
        byte[] backupBytes = File.ReadAllBytes(backup);
        GameSaveData first, second;
        var shell = new GameObject("[LuaSaveReadProbe]");
        shell.SetActive(false);
        shell.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var reader = shell.AddComponent<SaveDataMgr>();
            if (!ReferenceEquals(SaveDataMgr.TestExistingInstance, saves) || !string.IsNullOrEmpty(reader.UserSavePath))
                throw new InvalidOperationException("恢复外壳意外触发了正式存档初始化。");
            first = reader.ReadTestArchiveWithoutPublishing(backupBytes);
            second = reader.ReadTestArchiveWithoutPublishing(secondBytes);
        }
        finally { UnityEngine.Object.DestroyImmediate(shell); }
        var firstItem = first.PlayerData_Dict[markerProfile]._inventoryData["probe"].itemSlots[0].itemData;
        var secondItem = second.PlayerData_Dict[markerProfile]._inventoryData["probe"].itemSlots[0].itemData;
        return new JObject
        {
            ["firstWritten"] = firstWritten, ["secondWritten"] = secondWritten, ["path"] = file,
            ["firstBytes"] = firstBytes.Length, ["secondBytes"] = secondBytes.Length,
            ["temporaryRemaining"] = File.Exists(file + ".tmp"), ["backupExists"] = File.Exists(file + ".bak"),
            ["backupMatchesFirst"] = backupBytes.SequenceEqual(firstBytes),
            ["firstAmount"] = firstItem.Stack.Amount, ["secondAmount"] = secondItem.Stack.Amount,
            ["firstGuid"] = firstItem.Guid, ["secondGuid"] = secondItem.Guid,
            ["firstId"] = firstItem.IDName, ["secondId"] = secondItem.IDName,
            ["firstSpecial"] = first.PlayerData_Dict[markerProfile].ItemSpecialData,
            ["secondSpecial"] = second.PlayerData_Dict[markerProfile].ItemSpecialData,
            ["firstPlayers"] = first.PlayerData_Dict.Count, ["secondPlayers"] = second.PlayerData_Dict.Count,
            ["expectedPlayers"] = originalPlayers + 1,
            ["activeSaveReferencePreserved"] = ReferenceEquals(saves.SaveData, original),
            ["activePlayerReferencePreserved"] = ReferenceEquals(originalPlayer, player.Data),
            ["activeManagerPreserved"] = ReferenceEquals(saves, SaveDataMgr.TestExistingInstance),
            ["activeNamePreserved"] = original.saveName == originalName,
            ["activeSeedPreserved"] = original.SaveSeed == originalSeed,
            ["activeCountsPreserved"] = original.PlayerData_Dict.Count == originalPlayers && original.PlanetData_Dict.Count == originalPlanets,
            ["activePlayerStatePreserved"] = originalPlayer.Guid == originalGuid && originalPlayer.ItemSpecialData == originalSpecial,
            ["activeDirectoryPreserved"] = string.Equals(saves.UserSavePath, originalDirectory, StringComparison.Ordinal) &&
                string.Equals(RequireIsolatedSaveRoot(player), root, StringComparison.OrdinalIgnoreCase)
        };
    }

    #endregion
}
