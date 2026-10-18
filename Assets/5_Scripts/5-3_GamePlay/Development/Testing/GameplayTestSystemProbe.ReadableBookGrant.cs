#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 初始书页与发放规则采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterReadableBookGrantRuntime() => RegisterReadableBookGrantRules();

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterReadableBookGrantEditor() => RegisterReadableBookGrantRules();
#endif

    private static void RegisterReadableBookGrantRules()
    {
        Register("inventory", "readable_book_snapshot", CaptureReadableBookSnapshot);
        Register("inventory", "readable_book_grant", CaptureReadableBookGrant);
    }

    // 直接采样生产写页工厂和库存快照往返，页数、文字与拒绝结果由 Lua 断言。
    private static JObject CaptureReadableBookSnapshot(JObject args)
    {
        ItemData book = ReadableBookProbeItem("probe:book", 101);
        var module = new Ex_ModData_MemoryPackable
        {
            StableName = "readableBook", ModuleId = Mod_ReadableBook.ModuleId
        };
        book.ModuleDataDic.Add(module.StableName, module);
        var pages = new List<ReadableBookPageContent>
        {
            new() { PageIndex = 42, Title = "聊天命令", Body = "/carryweight 5000\n/carryvolume 5000" },
            new() { PageIndex = -1, Title = "管理员操作", Body = "F4：打开 GM 面板。" },
            new() { Title = "说明", Body = "中文与换行\n完整保留。" }
        };
        ItemData material = null;
        if (Flag(args, "withMaterial", false))
        {
            material = ReadableBookProbeItem("Charcoal", 104);
        }
        bool written = ReadableBookInitialContent.TryWrite(module, 1, pages, out string failure, material);
        if (material != null) material.Stack.Amount = 7f;
        byte[] beforeRejected = module.BitData;
        bool emptyAccepted = ReadableBookInitialContent.TryWrite(module, 1,
            Array.Empty<ReadableBookPageContent>(), out _);
        bool nullPageAccepted = ReadableBookInitialContent.TryWrite(module, 1,
            new ReadableBookPageContent[] { null }, out _);
        bool rejectedUnchanged = ReferenceEquals(beforeRejected, module.BitData);
        pages[0].Body = "外部已改动";
        pages.Clear();

        var bag = new Inventory_Data(new List<ItemSlot> { new ItemSlot(0) { itemData = book } }, "probe:book_save");
        InventoryInstanceSnapshot snapshot = MemoryPackSerializer.Deserialize<InventoryInstanceSnapshot>(
            MemoryPackSerializer.Serialize(InventoryInstanceSnapshot.Capture(bag)));
        ItemData restoredBook = snapshot.CreateColdData().GetItemSlot(0).itemData;
        var restoredModule = (Ex_ModData_MemoryPackable)restoredBook.ModuleDataDic["readableBook"];
        ReadableBookInstanceSnapshot restored = restoredModule.GetData<ReadableBookInstanceSnapshot>();
        Inventory_Data restoredInputs = restored.InputInventory.CreateColdData();
        ItemData restoredMaterial = restoredInputs.GetItemSlot(0).itemData;
        var restoredPages = new JArray();
        foreach (ReadableBookPageContent page in restored.PageContents)
            restoredPages.Add(new JObject
            {
                ["index"] = page.PageIndex, ["title"] = page.Title, ["body"] = page.Body,
                ["customTitle"] = page.HasCustomTitle, ["customBody"] = page.HasCustomBody
            });
        var anotherBook = new Ex_ModData_MemoryPackable();
        ReadableBookInitialContent.TryWrite(anotherBook, 1,
            new[] { new ReadableBookPageContent { Title = "另一本", Body = "独立内容" } }, out _);
        bool noPlayerAccepted = ReadableBookGrantService.TryGive(null, "CommandManual",
            Array.Empty<ReadableBookPageContent>(), out _);
        return new JObject
        {
            ["written"] = written, ["failure"] = failure, ["addedPages"] = restored.AddedPageCount,
            ["pages"] = restoredPages, ["materialSlots"] = restored.InputInventory.SlotCount,
            ["materialId"] = restoredMaterial?.IDName ?? string.Empty,
            ["materialAmount"] = restoredMaterial?.Stack?.Amount ?? 0f,
            ["paperEmpty"] = restoredInputs.GetItemSlot(1).itemData == null,
            ["emptyAccepted"] = emptyAccepted, ["nullPageAccepted"] = nullPageAccepted,
            ["rejectedUnchanged"] = rejectedUnchanged, ["guid"] = restoredBook.Guid,
            ["independentBody"] = module.GetData<ReadableBookInstanceSnapshot>().PageContents[0].Body,
            ["noPlayerAccepted"] = noPlayerAccepted
        };
    }

    private static JObject CaptureReadableBookGrant(JObject args)
    {
        string mode = Text(args, "mode", "success");
        var data = new Inventory_Data(new List<ItemSlot>(), "probe:book_grant");
        data.ConfigurePlayerBagCapacity(100f, 100f);
        ItemData existing = ReadableBookProbeItem("probe:existing", 102);
        data.GetItemSlot(0).itemData = existing;
        ItemData book = ReadableBookProbeItem("probe:book", 103);
        for (int index = 1; index < data.itemSlots.Count; index++)
        {
            ItemSlot slot = data.itemSlots[index];
            if (mode == "full") slot.itemData = ReadableBookProbeItem("probe:filler", 103 + index);
            if (mode == "tags") slot.CanAcceptTags.Add("probe:rejected_tag");
            if (mode == "slot_limit") slot.SlotMaxVolume = .5f;
        }
        data.IsDepositBlocked = mode == "deposit";
        var bag = new Inventory { Data = data };
        var capacity = new PlayerCarryCapacitySnapshot(data,
            mode == "weight" ? data.MaxAllowedCarryWeight : data.CurrentCarryWeight,
            mode == "volume" ? data.MaxCarryVolume : data.CurrentCarryVolume);
        int changed = 0, refreshed = 0, slotsBefore = data.itemSlots.Count;
        data.Event_OnDataChanged += _ => changed++;
        data.Event_RefreshUI += _ => refreshed++;
        Func<bool> previousPending = ItemNetworkStateSerialization.IsLocalInventoryTransactionPending;
        bool inserted;
        string failure;
        try
        {
            ItemNetworkStateSerialization.IsLocalInventoryTransactionPending = () => mode == "pending";
            inserted = ReadableBookGrantService.TryInsert(bag, capacity, book, out failure);
        }
        finally { ItemNetworkStateSerialization.IsLocalInventoryTransactionPending = previousPending; }

        int received = 0;
        foreach (ItemSlot slot in data.itemSlots)
            if (slot.itemData?.IDName == book.IDName) received++;
        return new JObject
        {
            ["inserted"] = inserted, ["failure"] = failure, ["received"] = received,
            ["sameBook"] = ReferenceEquals(data.GetItemSlot(1).itemData, book),
            ["existingUnchanged"] = ReferenceEquals(data.GetItemSlot(0).itemData, existing) && existing.Stack.Amount == 1f,
            ["changed"] = changed, ["refreshed"] = refreshed,
            ["slotsUnchanged"] = slotsBefore == data.itemSlots.Count,
            ["amount"] = book.Stack.Amount, ["pickable"] = book.Stack.CanBePickedUp
        };
    }

    private static ItemData ReadableBookProbeItem(string id, int guid)
    {
        // 使用已注册序列化类型的通用物品数据创建测试物品。
        return new Data_GeneralItem
        {
            IDName = id, Guid = guid,
            Stack = new ItemStack { Amount = 1f, Weight = .1f, Volume = .2f, Stackable = false },
            ModuleDataDic = new Dictionary<string, ModuleData>()
        };
    }

    #endregion
}
#endif
