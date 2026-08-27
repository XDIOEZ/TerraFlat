using FastCloner.Code;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class CompostBinSlotSnapshot
{
    public string ItemId = string.Empty; // 物品ID
    public float Amount = 1f; // 数量
    public string ItemSpecialData = string.Empty; // 特殊数据
    public float Durability = 1f; // 耐久
    public float MaxDurability = 1f; // 最大耐久
    public bool CanBePickedUp = true; // 是否可拾取
}

[Serializable]
public class CompostBinSaveState
{
    public string InventoryName = "堆肥桶"; // 库存名
    public List<CompostBinSlotSnapshot> Slots = new List<CompostBinSlotSnapshot>(); // 槽位快照
    public List<float> SlotElapsedSeconds = new List<float>(); // 槽位计时
}

public class Mod_CompostBin : Module, IInteractable
{
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.5f;

#region 基础参数

    public Ex_ModData ModSaveData = new Ex_ModData(); // 通用存档数据

    public override ModuleData _Data
    {
        get => ModSaveData;
        set => ModSaveData = value as Ex_ModData ?? new Ex_ModData();
    }

    [ShowInInspector]
    public CompostBinSaveState Data = new CompostBinSaveState(); // 堆肥桶运行时/存档数据

    public Inventory CompostInventory = new Inventory(); // 堆肥桶库存
    public GameObject UI_Prefab; // 堆肥桶UI预制体
    public int SlotCount = 3; // 堆肥槽位数量
    public float CompostSeconds = 900f; // 单个槽位完成一次堆肥所需时间
    public string OutputItemId = string.Empty; // 化肥输出物品ID
    public List<string> OutputItemFallbackIds = new List<string> { "Fertilizer", "肥料", "Compost", "Compost_Fertilizer" }; // 输出候选ID
    public List<string> AcceptItemIds = new List<string>(); // 可堆肥物品ID
    public List<string> AcceptTags = new List<string>(); // 可堆肥Tag
    public List<string> AcceptKeywords = new List<string> 
    { "Leaf", "leaf", "Plant", "plant", "Rotten", "rotten", "腐", "肉", "叶" }; // 关键字兜底
    public bool EnableKeywordFallback = true; // 是否启用关键字兜底

#endregion

#region 运行时字段

    private bool _inventoryInitialized; // 库存是否已初始化
    private bool _panelInitialized; // UI是否已初始化
    private bool _outputResolveWarningLogged; // 输出物品缺失警告是否已提示
    private readonly List<float> _slotElapsedSeconds = new List<float>(); // 每个槽位的计时缓存

#endregion

#region 生命周期

    public override void Awake()
    {
        EnsureModuleData();
        base.Awake();
        EnsureInventoryStructure();
    }

    private void OnValidate()
    {
        EnsureModuleData();
        SlotCount = Mathf.Max(1, SlotCount);
        EnsureInventoryStructure();
    }

    public override void Load()
    {
        EnsureModuleData();
        ModSaveData.ReadData(ref Data);
        Data ??= new CompostBinSaveState();

        EnsureInventoryStructure();
        RestoreInventoryFromState();
        EnsureInventoryRuntime();
    }

    public override void Save()
    {
        EnsureModuleData();
        EnsureInventoryStructure();

        Data.Slots = BuildSlotSnapshots();
        Data.SlotElapsedSeconds = new List<float>(_slotElapsedSeconds);
        ModSaveData.WriteData(Data);
    }

    public override void ModUpdate(float deltaTime)
    {
        EnsureModuleData();
        EnsureInventoryStructure();
        EnsureInventoryRuntime();

        CompostInventory?.ModUpdate(deltaTime);
        UpdateCompostProgress(deltaTime);
    }

#endregion

#region 交互

    /// <summary>
    /// 开始交互：打开堆肥桶UI，并把玩家手部库存设为默认目标。
    /// </summary>
    public void OnInteractStart(Item playerItem)
    {
        if (playerItem == null)
        {
            throw new ArgumentNullException(nameof(playerItem), "[Mod_CompostBin] 交互失败：playerItem 为空。");
        }

        EnsureModuleData();
        EnsureInventoryStructure();
        EnsureInventoryRuntime();

        Inventory handInventory = playerItem.GetComponentInChildren<Mod_Hand>()?.HandInventory ?? Inventory_Hand.PlayerHand;
        if (handInventory == null)
        {
            throw new MissingReferenceException("[Mod_CompostBin] 交互失败：未找到玩家手部库存。");
        }

        CompostInventory.DefaultTarget_Inventory = handInventory;
        CompostInventory.SwitchUI();
    }

    /// <summary>
    /// 结束交互：关闭堆肥桶UI。
    /// </summary>
    public void OnInteractCancel(Item playerItem)
    {
        CompostInventory?.basePanel?.Close();
    }

    /// <summary>
    /// 统一 Act 入口，直接切换堆肥桶 UI。
    /// </summary>
    public override void Act()
    {
        CompostInventory?.SwitchUI();
    }

#endregion

#region 堆肥逻辑

    private void UpdateCompostProgress(float deltaTime)
    {
        if (CompostInventory?.Data?.itemSlots == null)
        {
            return;
        }

        EnsureProgressCacheSize(CompostInventory.Data.itemSlots.Count);

        for (int i = 0; i < CompostInventory.Data.itemSlots.Count; i++)
        {
            ItemSlot slot = CompostInventory.Data.itemSlots[i];
            if (slot == null || slot.itemData == null)
            {
                _slotElapsedSeconds[i] = 0f;
                continue;
            }

            ItemData itemData = slot.itemData;
            if (!CanCompost(itemData) || IsOutputItem(itemData))
            {
                _slotElapsedSeconds[i] = 0f;
                continue;
            }

            _slotElapsedSeconds[i] += Mathf.Max(0f, deltaTime);
            if (_slotElapsedSeconds[i] < CompostSeconds)
            {
                continue;
            }

            if (TryConvertSlotToFertilizer(i))
            {
                _slotElapsedSeconds[i] = 0f;
            }
        }
    }

    private bool TryConvertSlotToFertilizer(int slotIndex)
    {
        if (CompostInventory?.Data?.itemSlots == null)
        {
            return false;
        }

        if (slotIndex < 0 || slotIndex >= CompostInventory.Data.itemSlots.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex), $"[Mod_CompostBin] 槽位索引越界：{slotIndex}");
        }

        ItemSlot slot = CompostInventory.Data.itemSlots[slotIndex];
        if (slot == null || slot.itemData == null)
        {
            return false;
        }

        string outputItemId = ResolveOutputItemId();
        if (string.IsNullOrWhiteSpace(outputItemId))
        {
            if (!_outputResolveWarningLogged)
            {
                _outputResolveWarningLogged = true;
                Debug.LogError($"[Mod_CompostBin] 找不到化肥输出物品，当前配置={OutputItemId}, 候选数量={OutputItemFallbackIds.Count}");
            }

            return false;
        }

        ItemData outputItemData = CreateItemDataFromPrefab(outputItemId);
        if (outputItemData == null || outputItemData.Stack == null)
        {
            return false;
        }

        ItemData sourceItemData = slot.itemData;
        float sourceAmount = Mathf.Max(1f, sourceItemData.Stack.Amount);

        outputItemData.Stack.Amount = sourceAmount;
        outputItemData.Stack.CanBePickedUp = sourceItemData.Stack.CanBePickedUp;
        outputItemData.ItemSpecialData = sourceItemData.ItemSpecialData;
        outputItemData.Durability = sourceItemData.Durability;
        outputItemData.MaxDurability = sourceItemData.MaxDurability;
        outputItemData.Guid = Guid.NewGuid().GetHashCode();

        slot.itemData = outputItemData;
        slot.RefreshUI();
        CompostInventory.Data.Event_RefreshUI?.Invoke(slotIndex);

        Debug.Log($"[Mod_CompostBin] 堆肥完成，槽位={slotIndex}, 原物品={sourceItemData.IDName}, 数量={sourceAmount:F0}, 产物={outputItemId}");
        return true;
    }

    private bool CanCompost(ItemData itemData)
    {
        if (itemData == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(itemData.IDName) && ContainsIgnoreCase(AcceptItemIds, itemData.IDName))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(itemData.GameName) && ContainsIgnoreCase(AcceptItemIds, itemData.GameName))
        {
            return true;
        }

        if (itemData.Tags != null)
        {
            foreach (string tag in AcceptTags)
            {
                if (!string.IsNullOrWhiteSpace(tag) && itemData.Tags.ContainsTag(tag))
                {
                    return true;
                }
            }
        }

        if (!EnableKeywordFallback)
        {
            return false;
        }

        string combinedText = $"{itemData.IDName} {itemData.GameName} {itemData.Description}";
        foreach (string keyword in AcceptKeywords)
        {
            if (!string.IsNullOrWhiteSpace(keyword) && combinedText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsOutputItem(ItemData itemData)
    {
        if (itemData == null)
        {
            return false;
        }

        string outputItemId = ResolveOutputItemId();
        return !string.IsNullOrWhiteSpace(outputItemId) && string.Equals(itemData.IDName, outputItemId, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveOutputItemId()
    {
        if (!string.IsNullOrWhiteSpace(OutputItemId) && IsPrefabAvailable(OutputItemId))
        {
            return OutputItemId;
        }

        foreach (string candidate in OutputItemFallbackIds)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && IsPrefabAvailable(candidate))
            {
                return candidate;
            }
        }

        return OutputItemId;
    }

#endregion

#region 数据与UI

    private void EnsureModuleData()
    {
        ModSaveData ??= new Ex_ModData();
        if (string.IsNullOrWhiteSpace(ModSaveData.ID))
        {
            ModSaveData.ID = gameObject.name;
        }
    }

    private void EnsureInventoryStructure()
    {
        CompostInventory ??= new Inventory();

        if (CompostInventory.Data == null)
        {
            CompostInventory.Data = new Inventory_Data(new List<ItemSlot>(), Data?.InventoryName ?? "堆肥桶");
        }

        if (string.IsNullOrWhiteSpace(CompostInventory.Data.Name))
        {
            CompostInventory.Data.Name = Data?.InventoryName ?? "堆肥桶";
        }

        CompostInventory.item = item;
        CompostInventory.InventoryPanel_Prefab = ResolvePanelPrefab();

        int targetSlotCount = Mathf.Max(1, SlotCount);
        if (CompostInventory.Data.itemSlots == null)
        {
            CompostInventory.Data.itemSlots = new List<ItemSlot>();
        }

        while (CompostInventory.Data.itemSlots.Count < targetSlotCount)
        {
            CompostInventory.Data.itemSlots.Add(new ItemSlot(CompostInventory.Data.itemSlots.Count));
        }

        if (CompostInventory.Data.itemSlots.Count > targetSlotCount)
        {
            targetSlotCount = CompostInventory.Data.itemSlots.Count;
            SlotCount = targetSlotCount;
        }

        for (int i = 0; i < CompostInventory.Data.itemSlots.Count; i++)
        {
            ItemSlot slot = CompostInventory.Data.itemSlots[i];
            if (slot == null)
            {
                slot = new ItemSlot(i);
                CompostInventory.Data.itemSlots[i] = slot;
            }

            slot.Index = i;
            slot.SlotMaxVolume = 100f;
        }

        EnsureProgressCacheSize(CompostInventory.Data.itemSlots.Count);
    }

    private void EnsureInventoryRuntime()
    {
        if (CompostInventory == null || CompostInventory.Data == null)
        {
            return;
        }

        if (!_inventoryInitialized)
        {
            CompostInventory.InitData();
            _inventoryInitialized = true;
        }

        if (!_panelInitialized && CompostInventory.InventoryPanel_Prefab != null)
        {
            _panelInitialized = true;
        }
    }

    private void RestoreInventoryFromState()
    {
        if (CompostInventory?.Data?.itemSlots == null)
        {
            return;
        }

        EnsureProgressCacheFromState();

        int slotCount = CompostInventory.Data.itemSlots.Count;
        int snapshotCount = Data?.Slots?.Count ?? 0;
        int bindCount = Mathf.Min(slotCount, snapshotCount);

        for (int i = 0; i < bindCount; i++)
        {
            CompostBinSlotSnapshot snapshot = Data.Slots[i];
            CompostInventory.Data.itemSlots[i].itemData = CreateItemDataFromSnapshot(snapshot);
        }

        for (int i = bindCount; i < slotCount; i++)
        {
            CompostInventory.Data.itemSlots[i].ClearData();
        }
    }

    private List<CompostBinSlotSnapshot> BuildSlotSnapshots()
    {
        List<CompostBinSlotSnapshot> snapshots = new List<CompostBinSlotSnapshot>();

        if (CompostInventory?.Data?.itemSlots == null)
        {
            return snapshots;
        }

        foreach (ItemSlot slot in CompostInventory.Data.itemSlots)
        {
            snapshots.Add(CreateSnapshot(slot?.itemData));
        }

        return snapshots;
    }

    private void EnsureProgressCacheFromState()
    {
        _slotElapsedSeconds.Clear();
        if (Data?.SlotElapsedSeconds != null)
        {
            _slotElapsedSeconds.AddRange(Data.SlotElapsedSeconds);
        }

        EnsureProgressCacheSize(CompostInventory?.Data?.itemSlots?.Count ?? 0);
    }

    private void EnsureProgressCacheSize(int slotCount)
    {
        if (slotCount < 0)
        {
            slotCount = 0;
        }

        while (_slotElapsedSeconds.Count < slotCount)
        {
            _slotElapsedSeconds.Add(0f);
        }

        if (_slotElapsedSeconds.Count > slotCount)
        {
            _slotElapsedSeconds.RemoveRange(slotCount, _slotElapsedSeconds.Count - slotCount);
        }
    }

    private GameObject ResolvePanelPrefab()
    {
        if (UI_Prefab != null)
        {
            return UI_Prefab;
        }

        if (!Application.isPlaying)
        {
            return null;
        }

        if (GameRes.Instance != null)
        {
            GameObject prefab = GameRes.Instance.GetPrefab("UI_CompostBin", false);
            if (prefab != null)
            {
                return prefab;
            }
        }

        return null;
    }

    private ItemData CreateItemDataFromSnapshot(CompostBinSlotSnapshot snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ItemId))
        {
            return null;
        }

        return CreateItemDataFromPrefab(snapshot.ItemId, snapshot);
    }

    private ItemData CreateItemDataFromPrefab(string itemId, CompostBinSlotSnapshot snapshot = null)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        if (GameRes.Instance == null)
        {
            Debug.LogError($"[Mod_CompostBin] 构建物品失败，GameRes.Instance 为空，物品ID={itemId}");
            return null;
        }

        GameObject prefab = GameRes.Instance.GetPrefab(itemId);
        if (prefab == null)
        {
            Debug.LogError($"[Mod_CompostBin] 构建物品失败，未找到预制体，物品ID={itemId}");
            return null;
        }

        Item itemPrefab = prefab.GetComponent<Item>();
        if (itemPrefab == null || itemPrefab.itemData == null)
        {
            Debug.LogError($"[Mod_CompostBin] 构建物品失败，预制体缺少 Item 或 itemData，物品ID={itemId}");
            return null;
        }

        ItemData clonedData = FastCloner.FastCloner.DeepClone(itemPrefab.itemData);
        if (clonedData == null || clonedData.Stack == null)
        {
            Debug.LogError($"[Mod_CompostBin] 构建物品失败，克隆 itemData 失败，物品ID={itemId}");
            return null;
        }

        if (snapshot != null)
        {
            clonedData.Stack.Amount = Mathf.Max(1f, snapshot.Amount);
            clonedData.ItemSpecialData = snapshot.ItemSpecialData;
            clonedData.Durability = snapshot.Durability;
            clonedData.MaxDurability = Mathf.Max(0.01f, snapshot.MaxDurability);
            clonedData.Stack.CanBePickedUp = snapshot.CanBePickedUp;
        }

        return clonedData;
    }

    private CompostBinSlotSnapshot CreateSnapshot(ItemData itemData)
    {
        if (itemData == null)
        {
            return new CompostBinSlotSnapshot();
        }

        return new CompostBinSlotSnapshot
        {
            ItemId = itemData.IDName,
            Amount = Mathf.Max(1f, itemData.Stack != null ? itemData.Stack.Amount : 1f),
            ItemSpecialData = itemData.ItemSpecialData,
            Durability = itemData.Durability,
            MaxDurability = itemData.MaxDurability,
            CanBePickedUp = itemData.Stack != null && itemData.Stack.CanBePickedUp
        };
    }

    private bool IsPrefabAvailable(string itemId)
    {
        return GameRes.Instance != null && !string.IsNullOrWhiteSpace(itemId) && GameRes.Instance.GetPrefab(itemId) != null;
    }

    private static bool ContainsIgnoreCase(List<string> values, string target)
    {
        if (values == null || values.Count == 0 || string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && string.Equals(value, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

#endregion
}
