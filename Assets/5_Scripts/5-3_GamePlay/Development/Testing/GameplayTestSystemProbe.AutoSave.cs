using System;
using System.Collections.Generic;
using System.Text;
using FlatWorld.WorldModel;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 自动保存预算与冻结载荷采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterAutoSaveRules()
    {
        Register("save", "autosave_budget", ServiceAutoSaveBudget);
        Register("save", "autosave_chunk_payload", ServiceAutoSaveChunkPayload);
    }

    // 使用 Lua 提供的确定性时间采样正式预算，规则不驱动正在游玩的帧循环。
    private static JObject ServiceAutoSaveBudget(JObject args)
    {
        var budget = new AutoSaveWorkBudget(Number(args, "startTime", 0d));
        var decisions = new JArray();
        if (args["steps"] is not JArray steps)
            throw new ArgumentException("自动保存预算采样必须提供 steps。");
        foreach (JToken token in steps)
        {
            if (token is not JObject step)
                throw new ArgumentException("自动保存预算 steps 必须是对象。");
            double now = Number(step, "now", 0d);
            if (Flag(step, "restart", false))
                budget.Restart(now);
            else
                decisions.Add(budget.ShouldYield(now));
        }
        return new JObject
        {
            ["sliceSeconds"] = AutoSaveWorkBudget.SliceSeconds,
            ["maxWorkUnits"] = AutoSaveWorkBudget.MaxWorkUnits,
            ["yieldFrames"] = AutoSaveWorkBudget.YieldFrames,
            ["decisions"] = decisions
        };
    }

    // 真实实例与地形先各自冻结为区块字节，再调用后台实际使用的纯字节封装入口。
    private static JObject ServiceAutoSaveChunkPayload(JObject args)
    {
        Data_GeneralItem item = ServiceItem(args);
        Data_GeneralItem child = ServiceItem(args);
        child.Guid += 10;
        var inventory = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = child }, new(1) }, "cargo")
        { Index = 1 };
        item.ModuleDataDic.Add("cargo", new Inventory_ModuleData
        {
            StableName = "cargo", ModuleId = "inventory",
            Data = new Dictionary<string, Inventory_Data> { ["cargo"] = inventory }
        });
        var terrain = new TerrainCell(101, 102, 103, 104, 105, TerrainCellFlags.Blocking);
        var source = new ChunkSaveRecord
        {
            PlanetName = "probe:autosave", ChunkName = "1_0", ChunkPosition = new Vector2Int(1, 0),
            ChangedItems = new List<ItemData> { item }, RemovedItemGuids = new List<int> { 41 },
            RuntimeTileDeltas = new List<RuntimeTileCellSaveDelta>
            {
                RuntimeTileCellSaveDelta.Capture(new Vector2Int(2, 3), terrain, 12.5f)
            }
        };
        var second = new ChunkSaveRecord
        {
            PlanetName = "probe:autosave", ChunkName = "0_0", ChunkPosition = Vector2Int.zero,
            RemovedItemGuids = new List<int> { 42 }
        };
        byte[] firstBytes = MemoryPackSerializer.Serialize(source);
        byte[] secondBytes = MemoryPackSerializer.Serialize(second);
        var core = new GameSaveData { saveName = "probe:autosave_core", Seed = 2468 };
        byte[] coreBytes = MemoryPackSerializer.Serialize(core);
        byte[] machines = { 1, 2, 3 };
        byte[] drops = { 4, 5 };
        byte[] mods = { 6, 7, 8, 9 };

        item.Guid += 1000;
        item.Stack.Amount += 7;
        child.Stack.Amount += 11;
        source.RuntimeTileDeltas[0].GroundTileId = 901;
        source.RemovedItemGuids.Add(99);
        second.ChunkName = "changed_after_capture";
        core.Seed = -1;

        byte[] payload = SaveDataMgr.BuildCapturedSavePayload(coreBytes,
            new List<byte[]> { firstBytes, secondBytes }, machines, drops, mods);
        ModdedSaveEnvelope outer = MemoryPackSerializer.Deserialize<ModdedSaveEnvelope>(ServiceAutoSaveBody(payload));
        CompactSaveEnvelope compact = MemoryPackSerializer.Deserialize<CompactSaveEnvelope>(ServiceAutoSaveBody(outer.CoreSavePayload));
        GameSaveData restoredCore = MemoryPackSerializer.Deserialize<GameSaveData>(compact.CoreSaveData);
        ChunkSaveRecord restored = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(compact.ChunkRecords[0]);
        ChunkSaveRecord restoredSecond = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(compact.ChunkRecords[1]);
        ItemData restoredItem = restored.ChangedItems[0];
        Inventory_Data restoredInventory = ((Inventory_ModuleData)restoredItem.ModuleDataDic["cargo"]).Data["cargo"];
        ItemData restoredChild = restoredInventory.itemSlots[0].itemData;
        RuntimeTileCellSaveDelta delta = restored.RuntimeTileDeltas[0];
        return new JObject
        {
            ["bytes"] = payload.Length,
            ["outerMagic"] = Encoding.ASCII.GetString(payload, 0, 4), ["outerVersion"] = outer.Version,
            ["compactMagic"] = Encoding.ASCII.GetString(outer.CoreSavePayload, 0, 4), ["compactVersion"] = compact.Version,
            ["coreName"] = restoredCore.saveName, ["coreSeed"] = restoredCore.Seed,
            ["recordCount"] = compact.ChunkRecords.Count,
            ["firstChunk"] = restored.ChunkName, ["secondChunk"] = restoredSecond.ChunkName,
            ["planet"] = restored.PlanetName, ["chunkX"] = restored.ChunkPosition.x,
            ["itemCount"] = restored.ChangedItems.Count, ["guid"] = restoredItem.Guid,
            ["amount"] = restoredItem.Stack.Amount, ["definitionId"] = restoredItem.IDName,
            ["sourceGuid"] = item.Guid, ["sourceAmount"] = item.Stack.Amount,
            ["childGuid"] = restoredChild.Guid, ["childAmount"] = restoredChild.Stack.Amount,
            ["sourceChildAmount"] = child.Stack.Amount,
            ["slotCount"] = restoredInventory.itemSlots.Count, ["selected"] = restoredInventory.Index,
            ["secondEmpty"] = restoredInventory.itemSlots[1].itemData == null,
            ["removedCount"] = restored.RemovedItemGuids.Count, ["removedGuid"] = restored.RemovedItemGuids[0],
            ["terrainCount"] = restored.RuntimeTileDeltas.Count,
            ["localX"] = delta.LocalPosition.x, ["localY"] = delta.LocalPosition.y,
            ["ground"] = delta.GroundTileId, ["back"] = delta.BackTileId, ["blocking"] = delta.BlockingTileId,
            ["biome"] = delta.BiomeId, ["navigationCost"] = delta.NavigationCost,
            ["flags"] = (int)delta.Flags, ["damage"] = delta.AccumulatedDamage,
            ["firstBytesPreserved"] = Convert.ToBase64String(compact.ChunkRecords[0]) == Convert.ToBase64String(firstBytes),
            ["machinesPreserved"] = Convert.ToBase64String(compact.MechanicalNetworks) == Convert.ToBase64String(machines),
            ["dropsPreserved"] = Convert.ToBase64String(compact.DroppedItems) == Convert.ToBase64String(drops),
            ["modsPreserved"] = Convert.ToBase64String(outer.ModMetadata) == Convert.ToBase64String(mods)
        };
    }

    private static byte[] ServiceAutoSaveBody(byte[] payload)
    {
        var body = new byte[payload.Length - 4];
        Buffer.BlockCopy(payload, 4, body, 0, body.Length);
        return body;
    }

    #endregion
}
