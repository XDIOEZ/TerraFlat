using System;
using System.Collections.Generic;
using Force.DeepCloner;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
[HideReferenceObjectPicker]
/// <summary>
/// 晾肉架单条风干规则：定义匹配条件、耗时、成功率与产出。
/// </summary>
public class MeatrackDryingRule
{
    [TableColumnWidth(100, Resizable = false)]
    [LabelText("规则名")]
    [Tooltip("规则名，便于在Inspector中区分")]
    public string RuleName = "完整肉块";

    [PropertySpace(SpaceBefore = 3)]
    [BoxGroup("输入条件")]
    [TableColumnWidth(220)]
    [LabelText("输入物品ID（任意命中）")]
    [ListDrawerSettings(DraggableItems = true, ShowFoldout = true, DefaultExpandedState = false, ShowPaging = true, NumberOfItemsPerPage = 4)]
    [Tooltip("输入物品ID列表，任意一个命中即可")]
    public List<string> InputItemIds = new List<string>();

    [BoxGroup("输入条件")]
    [TableColumnWidth(220)]
    [LabelText("输入Tag（任意命中）")]
    [ListDrawerSettings(DraggableItems = true, ShowFoldout = true, DefaultExpandedState = false, ShowPaging = true, NumberOfItemsPerPage = 4)]
    [Tooltip("输入Tag列表，任意一个命中即可")]
    public List<string> InputTags = new List<string>();

    [PropertySpace(SpaceBefore = 3)]
    [BoxGroup("输出与耗时")]
    [TableColumnWidth(120, Resizable = false)]
    [LabelText("输出物品ID")]
    [Required("输出物品ID不能为空")]
    [Tooltip("风干后输出物品ID（对应ItemData.IDName）")]
    public string OutputItemId = "Meat_Cooked";

    [BoxGroup("输出与耗时")]
    [TableColumnWidth(90, Resizable = false)]
    [Min(0.01f)]
    [LabelText("基础风干时长")]
    [SuffixLabel("秒", true)]
    [Tooltip("基础风干时长（秒）")]
    public float RequiredDryingSeconds = 120f;

    [BoxGroup("输出与耗时")]
    [TableColumnWidth(90, Resizable = false)]
    [Range(0f, 1f)]
    [LabelText("风干成功率")]
    [ProgressBar(0f, 1f)]
    [Tooltip("风干成功率，完整大肉块推荐0.5，肉条推荐1")]
    public float SuccessRate = 1f;

    [BoxGroup("输出与耗时")]
    [TableColumnWidth(90, Resizable = false)]
    [Min(0f)]
    [LabelText("食材风干倍率")]
    [SuffixLabel("x", true)]
    [Tooltip("该类型食材自身的风干速度倍率")]
    public float DrySpeedMultiplier = 1f;

    [BoxGroup("显示")]
    [TableColumnWidth(80, Resizable = false)]
    [LabelText("展示Sprite")]
    [PreviewField(50, ObjectFieldAlignment.Left)]
    [Tooltip("槽位物品展示Sprite，留空则尝试自动读取对应Prefab的Sprite")]
    public Sprite DisplaySprite;

    [BoxGroup("显示")]
    [TableColumnWidth(80, Resizable = false)]
    [LabelText("熏制覆盖Sprite")]
    [PreviewField(50, ObjectFieldAlignment.Left)]
    [Tooltip("熏制状态覆盖Sprite，留空则使用默认熏制Sprite或物品Sprite")]
    public Sprite SmokedStateSprite;
}

[Serializable]
[MemoryPackable]
/// <summary>
/// 晾肉架运行时存档数据：库存快照 + 每个槽位累计风干时间。
/// </summary>
public partial class MeatrackSaveData
{
    public Inventory_Data RackInventoryData;
    public List<float> SlotElapsedSeconds = new List<float>();
}

/// <summary>
/// 晾肉架模块：负责槽位交互、风干进度推进、热源加速与挂架显示刷新。
/// </summary>
public class Meatrack : Module, IInteractable, IInteract
{
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.5f;

#region 常量

    private const string ModName = "晾肉架模块";
    private const string InventoryName = "晾肉架";
    private const float MinDryingSeconds = 0.01f;

#endregion

#region 配置字段

    [TabGroup("检查器", "高级")]
    [LabelText("模块存档数据")]
    public Ex_ModData_MemoryPackable ModSaveData = new Ex_ModData_MemoryPackable(); // 模块存档数据
    public override ModuleData _Data { get => ModSaveData; set => ModSaveData = (Ex_ModData_MemoryPackable)value; }

    [TabGroup("检查器", "高级")]
    [LabelText("晾肉架库存")]
    public Inventory RackInventory = new Inventory(); // 晾肉架库存（固定3槽）

    [TabGroup("检查器", "基础配置")]
    [LabelText("交互面板预制体")]
    public GameObject InventoryPanelPrefab; // 交互面板预制体

    [TabGroup("检查器", "基础配置")]
    [ReadOnly]
    [LabelText("槽位数量（固定）")]
    public int SlotCount = 3; // 晾肉槽位数量

    [TabGroup("检查器", "风干规则")]
    [InfoBox("规则从上到下依次匹配，命中第一条后停止匹配。")]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 240, MaxScrollViewHeight = 520)]
    [LabelText("风干规则表")]
    public List<MeatrackDryingRule> DryingRules = new List<MeatrackDryingRule>(); // 食材风干规则表

    [TabGroup("检查器", "热源加速")]
    [Min(0.1f)]
    [LabelText("热源检测半径")]
    [SuffixLabel("格", true)]
    public float HeatSourceRadius = 8f; // 热源检测半径（格）

    [TabGroup("检查器", "热源加速")]
    [Min(0.05f)]
    [LabelText("热源扫描间隔")]
    [SuffixLabel("秒", true)]
    public float HeatSourceScanInterval = 0.5f; // 热源扫描间隔（秒）

    [TabGroup("检查器", "热源加速")]
    [Min(0f)]
    [LabelText("每个热源倍率增量")]
    [SuffixLabel("x", true)]
    public float HeatSourceSpeedBoostPerSource = 1f; // 每个热源提升倍率，1=+100%

    [TabGroup("检查器", "热源加速")]
    [LabelText("热源检测层")]
    public LayerMask HeatSourceLayerMask = ~0; // 热源扫描层

    [TabGroup("检查器", "热源加速")]
    [LabelText("热源物品ID")]
    [ListDrawerSettings(DraggableItems = true, ShowFoldout = true, DefaultExpandedState = true, ShowPaging = false)]
    public List<string> HeatSourceItemIds = new List<string> { "Bonfire", "Smelter" }; // 直接视作热源的物品ID

    [TabGroup("检查器", "热源加速")]
    [LabelText("热源模块ID")]
    [ListDrawerSettings(DraggableItems = true, ShowFoldout = true, DefaultExpandedState = true, ShowPaging = false)]
    public List<string> HeatSourceModuleIds = new List<string> { "熔炼模块", "熔炉模块" }; // 带这些模块ID的物品也视作热源

    [TabGroup("检查器", "挂架显示")]
    [LabelText("槽位物品渲染器")]
    public List<SpriteRenderer> SlotItemRenderers = new List<SpriteRenderer>(); // 每个槽位的物品显示

    [TabGroup("检查器", "挂架显示")]
    [LabelText("槽位熏制渲染器")]
    public List<SpriteRenderer> SlotSmokeStateRenderers = new List<SpriteRenderer>(); // 每个槽位的熏制状态显示

    [TabGroup("检查器", "挂架显示")]
    [LabelText("默认熏制Sprite")]
    public Sprite DefaultSmokeStateSprite; // 默认熏制状态Sprite

    [TabGroup("检查器", "挂架显示")]
    [LabelText("初始熏制色")]
    public Color SmokeStartColor = new Color(1f, 1f, 1f, 0f); // 初始熏制色

    [TabGroup("检查器", "挂架显示")]
    [LabelText("完成熏制色")]
    public Color SmokeDoneColor = new Color(0.45f, 0.3f, 0.2f, 0.9f); // 完成熏制色

    [TabGroup("检查器", "挂架显示")]
    [LabelText("显示锚点偏移")]
    [OnValueChanged(nameof(ApplyVisualSlotLayout))]
    public Vector3 VisualAnchorOffset = new Vector3(0f, 0.8f, 0f); // 显示锚点偏移

    [TabGroup("检查器", "挂架显示")]
    [Min(0.05f)]
    [LabelText("槽位显示间距")]
    [SuffixLabel("格", true)]
    [OnValueChanged(nameof(ApplyVisualSlotLayout))]
    public float VisualSlotSpacing = 0.45f; // 槽位显示间距

    [TabGroup("检查器", "挂架显示")]
    [LabelText("物品层级")]
    [OnValueChanged(nameof(ApplyRendererSortingOrders))]
    public int ItemSpriteSortingOrder = 10; // 物品显示层级

    [TabGroup("检查器", "挂架显示")]
    [LabelText("熏制层级")]
    [OnValueChanged(nameof(ApplyRendererSortingOrders))]
    public int SmokeSpriteSortingOrder = 11; // 熏制显示层级

#endregion

#region 运行时字段

    [TabGroup("检查器", "运行时")]
    [ReadOnly]
    [LabelText("槽位风干进度")]
    [ListDrawerSettings(DraggableItems = false, ShowFoldout = true, DefaultExpandedState = true, ShowPaging = false)]
    public List<float> SlotProgress01 = new List<float>(); // 各槽位风干进度0~1

    [TabGroup("检查器", "运行时")]
    [ReadOnly]
    [LabelText("附近热源数量")]
    public int NearbyHeatSourceCount; // 附近热源数量

    [TabGroup("检查器", "运行时")]
    [ReadOnly]
    [LabelText("当前总风干倍率")]
    [SuffixLabel("x", true)]
    public float CurrentDryingSpeedMultiplier = 1f; // 当前环境总速度倍率

    [TabGroup("检查器", "运行时")]
    [ShowInInspector]
    [ReadOnly]
    [LabelText("环境加速百分比")]
    [SuffixLabel("%", true)]
    private float RuntimeDryingBoostPercent => (CurrentDryingSpeedMultiplier - 1f) * 100f;

    private readonly List<float> _slotElapsedSeconds = new List<float>();
    private readonly List<string> _slotSignatures = new List<string>();
    private readonly HashSet<int> _heatSourceItemInstanceIds = new HashSet<int>();
    private readonly Dictionary<string, Sprite> _itemSpriteCache = new Dictionary<string, Sprite>();
    private float _heatSourceScanTimer;
    private bool _inventoryInitialized;

#endregion

#region 生命周期

    /// <summary>
    /// 编辑器参数校验：补齐默认配置、库存结构和显示位。
    /// </summary>
    public void OnValidate()
    {
        EnsureDataContainer();
        EnsureDefaultConfig();
        EnsureInventoryStructure();
        EnsureVisualRenderers();
        ApplyVisualSlotLayout();
        ApplyRendererSortingOrders();
    }

    /// <summary>
    /// 运行期初始化：确保默认配置存在并准备运行时缓存。
    /// </summary>
    public override void Awake()
    {
        EnsureDataContainer();
        base.Awake();
        EnsureDefaultConfig();
        EnsureInventoryStructure();
        EnsureRuntimeState();
    }

    /// <summary>
    /// 模块加载：读取存档并恢复库存/进度状态。
    /// </summary>
    public override void Load()
    {
        EnsureDataContainer();
        EnsureDefaultConfig();
        _inventoryInitialized = false;
        LoadSaveData();
        EnsureInventoryStructure();
        EnsureRuntimeState();
        InitInventoryRuntime();
        RefreshHeatSourceBoost(force: true);
    }

    /// <summary>
    /// 模块保存：持久化库存与每槽位已累计的风干时长。
    /// </summary>
    public override void Save()
    {
        if (RackInventory != null)
        {
            RackInventory.Save();
        }

        MeatrackSaveData saveData = new MeatrackSaveData
        {
            RackInventoryData = RackInventory?.Data,
            SlotElapsedSeconds = new List<float>(_slotElapsedSeconds)
        };
        ModSaveData.WriteData(saveData);
    }

    /// <summary>
    /// 每帧更新：刷新热源倍率、推进风干、同步可视表现。
    /// </summary>
    public override void ModUpdate(float deltaTime)
    {
        EnsureInventoryStructure();
        EnsureRuntimeState();

        if (!_inventoryInitialized)
        {
            InitInventoryRuntime();
        }

        RefreshHeatSourceBoost(deltaTime);
        UpdateDryingSlots(deltaTime);
        RefreshSlotSprites();
    }

#endregion

#region 交互实现

    /// <summary>
    /// 玩家开始交互：绑定默认目标为玩家手部槽位，可选打开容器UI。
    /// </summary>
    public void OnInteractStart(Item playerItem)
    {
        if (playerItem == null)
        {
            throw new ArgumentNullException(nameof(playerItem), "[Meatrack] 交互失败：playerItem 为空。");
        }

        EnsureInventoryStructure();
        EnsureRuntimeState();
        if (!_inventoryInitialized)
        {
            InitInventoryRuntime();
        }

        Inventory handInventory = playerItem.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (handInventory == null)
        {
            throw new MissingReferenceException("[Meatrack] 交互失败：玩家缺少 Mod_Hand.HandInventory。");
        }

        RackInventory.DefaultTarget_Inventory = handInventory;

        if (RackInventory.basePanel != null || RackInventory.InventoryPanel_Prefab != null)
        {
            RackInventory.Interact_Start(playerItem);
        }
    }

    /// <summary>
    /// 玩家结束交互：解除目标库存并关闭面板。
    /// </summary>
    public void OnInteractCancel(Item playerItem)
    {
        if (RackInventory == null)
        {
            return;
        }

        RackInventory.DefaultTarget_Inventory = null;
        RackInventory.basePanel?.Close();
    }

    /// <summary>
    /// 兼容 IInteract 的入口，内部转发到 IInteractable。
    /// </summary>
    public void Interact_Start(IInteractor interacter = null)
    {
        if (interacter == null || interacter.Item == null)
        {
            Debug.LogError("[Meatrack] IInteract.Start 失败：interacter 或 interacter.Item 为空。");
            return;
        }

        OnInteractStart(interacter.Item);
    }

    public void Interact_Update(IInteractor interacter = null)
    {

    }

    public void Interact_Cancel(IInteractor interacter = null)
    {
        OnInteractCancel(interacter?.Item);
    }

#endregion

#region 核心逻辑

    /// <summary>
    /// 按间隔刷新附近热源数量，计算总风干倍率。
    /// 公式：1 + 热源数 * 每个热源倍率。
    /// </summary>
    private void RefreshHeatSourceBoost(float deltaTime = 0f, bool force = false)
    {
        _heatSourceScanTimer -= deltaTime;
        if (!force && _heatSourceScanTimer > 0f)
        {
            return;
        }

        _heatSourceScanTimer = Mathf.Max(0.05f, HeatSourceScanInterval);
        NearbyHeatSourceCount = CountNearbyHeatSources();
        CurrentDryingSpeedMultiplier = 1f + Mathf.Max(0, NearbyHeatSourceCount) * Mathf.Max(0f, HeatSourceSpeedBoostPerSource);
    }

    /// <summary>
    /// 统计范围内热源数量。
    /// 同一个 Item 可能有多个 Collider，这里按实例去重避免重复计数。
    /// </summary>
    private int CountNearbyHeatSources()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, HeatSourceRadius, HeatSourceLayerMask);
        if (hits == null || hits.Length == 0)
        {
            return 0;
        }

        _heatSourceItemInstanceIds.Clear();
        int count = 0;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null)
            {
                continue;
            }

            Item nearbyItem = WorldTopologyColliderProxy.ResolveComponent<Item>(hit);
            if (nearbyItem == null || nearbyItem == item || nearbyItem.itemData == null)
            {
                continue;
            }

            int instanceId = nearbyItem.GetInstanceID();
            if (!_heatSourceItemInstanceIds.Add(instanceId))
            {
                continue;
            }

            if (IsHeatSourceItem(nearbyItem))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 判断某个物品是否是热源：命中热源ID或包含热源模块ID即视为热源。
    /// </summary>
    private bool IsHeatSourceItem(Item targetItem)
    {
        if (targetItem == null || targetItem.itemData == null)
        {
            return false;
        }

        string itemId = targetItem.itemData.IDName;
        if (ContainsAnyIgnoreCase(HeatSourceItemIds, itemId))
        {
            return true;
        }

        if (targetItem.itemData.ModuleDataDic == null)
        {
            return false;
        }

        foreach (ModuleData moduleData in targetItem.itemData.ModuleDataDic.Values)
        {
            if (moduleData == null || string.IsNullOrWhiteSpace(moduleData.ID))
            {
                continue;
            }

            if (ContainsAnyIgnoreCase(HeatSourceModuleIds, moduleData.ID))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 推进每个槽位的风干进度。
    /// 当槽位物品变化时重置该槽位进度，避免把旧物品进度带到新物品。
    /// </summary>
    private void UpdateDryingSlots(float deltaTime)
    {
        if (RackInventory == null || RackInventory.Data == null || RackInventory.Data.itemSlots == null)
        {
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            ItemSlot slot = RackInventory.Data.itemSlots[i];
            ItemData currentItemData = slot?.itemData;
            string signature = BuildSlotSignature(currentItemData);

            // 用签名追踪槽位物品变更（Guid/ID/特殊数据），变化即清零进度。
            if (_slotSignatures[i] != signature)
            {
                _slotSignatures[i] = signature;
                _slotElapsedSeconds[i] = 0f;
                SlotProgress01[i] = 0f;
            }

            if (currentItemData == null)
            {
                _slotElapsedSeconds[i] = 0f;
                SlotProgress01[i] = 0f;
                continue;
            }

            if (!TryGetRule(currentItemData, out MeatrackDryingRule rule))
            {
                _slotElapsedSeconds[i] = 0f;
                SlotProgress01[i] = 0f;
                continue;
            }

            float requiredSeconds = Mathf.Max(MinDryingSeconds, rule.RequiredDryingSeconds);
            float slotSpeedMultiplier = Mathf.Max(0f, rule.DrySpeedMultiplier) * Mathf.Max(1f, CurrentDryingSpeedMultiplier);

            _slotElapsedSeconds[i] += deltaTime * slotSpeedMultiplier;
            SlotProgress01[i] = Mathf.Clamp01(_slotElapsedSeconds[i] / requiredSeconds);

            if (_slotElapsedSeconds[i] < requiredSeconds)
            {
                continue;
            }

            CompleteSlotDrying(i, slot, rule);
            _slotElapsedSeconds[i] = 0f;
            SlotProgress01[i] = 0f;
            _slotSignatures[i] = BuildSlotSignature(slot.itemData);
        }
    }

    /// <summary>
    /// 完成单槽风干结算。
    /// 以“每单位堆叠一次随机判定”计算成功数量，支持 50%/100% 等规则。
    /// </summary>
    private void CompleteSlotDrying(int slotIndex, ItemSlot slot, MeatrackDryingRule rule)
    {
        if (slot == null || slot.itemData == null)
        {
            return;
        }

        int inputCount = Mathf.Max(1, Mathf.RoundToInt(slot.itemData.Stack.Amount));
        int successCount = 0;
        float successRate = Mathf.Clamp01(rule.SuccessRate);

        // 每个单位独立随机，模拟完整大肉块可能腐坏、肉条更稳定的效果。
        for (int i = 0; i < inputCount; i++)
        {
            if (UnityEngine.Random.value <= successRate)
            {
                successCount++;
            }
        }

        if (successCount <= 0)
        {
            string lostItemId = slot.itemData.IDName;
            slot.ClearData();
            slot.RefreshUI();
            Debug.LogWarning($"[Meatrack] 风干失败：槽位={slotIndex}, 物品={lostItemId}, 数量={inputCount}, 成功率={successRate:P0}");
            return;
        }

        ItemData outputItemData = CreateOutputItemData(rule.OutputItemId, successCount);
        if (outputItemData == null)
        {
            Debug.LogError($"[Meatrack] 风干完成但产物创建失败：槽位={slotIndex}, 输出ID={rule.OutputItemId}");
            return;
        }

        slot.itemData = outputItemData;
        slot.RefreshUI();

        Debug.Log($"[Meatrack] 风干完成：槽位={slotIndex}, 规则={rule.RuleName}, 输入={inputCount}, 成功={successCount}, 输出={outputItemData.IDName}");
    }

    /// <summary>
    /// 根据产物ID克隆模板 ItemData，生成可直接写回槽位的产物数据。
    /// </summary>
    private ItemData CreateOutputItemData(string outputItemId, int amount)
    {
        if (string.IsNullOrWhiteSpace(outputItemId))
        {
            Debug.LogError("[Meatrack] 产物ID为空，请在 DryingRules 中填写 OutputItemId。");
            return null;
        }

        if (GameRes.Instance == null)
        {
            Debug.LogError("[Meatrack] GameRes.Instance 为空，无法生成风干产物。");
            return null;
        }

        if (!GameRes.Instance.AllPrefabs.TryGetValue(outputItemId, out GameObject prefab) || prefab == null)
        {
            Debug.LogError($"[Meatrack] 未找到产物Prefab：{outputItemId}");
            return null;
        }

        Item prefabItem = prefab.GetComponent<Item>();
        if (prefabItem == null || prefabItem.itemData == null)
        {
            Debug.LogError($"[Meatrack] 产物Prefab缺少Item或itemData：{outputItemId}");
            return null;
        }

        // 复制模板并给新 Guid，避免与模板或其他实例共享引用。
        ItemData output = prefabItem.itemData.DeepClone();
        output.Guid = Guid.NewGuid().GetHashCode();
        output.Stack.Amount = Mathf.Max(1, amount);
        output.Stack.CanBePickedUp = false;
        return output;
    }

#endregion

#region 显示刷新

    /// <summary>
    /// 刷新所有槽位的挂架显示与熏制状态覆盖层。
    /// </summary>
    private void RefreshSlotSprites()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            ItemSlot slot = RackInventory?.Data?.itemSlots != null && i < RackInventory.Data.itemSlots.Count
                ? RackInventory.Data.itemSlots[i]
                : null;

            MeatrackDryingRule rule = null;
            if (slot?.itemData != null)
            {
                TryGetRule(slot.itemData, out rule);
            }

            UpdateSingleSlotSprite(i, slot, rule);
        }
    }

    /// <summary>
    /// 刷新单个槽位对应的两个 SpriteRenderer：物品层 + 熏制层。
    /// </summary>
    private void UpdateSingleSlotSprite(int slotIndex, ItemSlot slot, MeatrackDryingRule rule)
    {
        SpriteRenderer itemRenderer = slotIndex < SlotItemRenderers.Count ? SlotItemRenderers[slotIndex] : null;
        SpriteRenderer smokeRenderer = slotIndex < SlotSmokeStateRenderers.Count ? SlotSmokeStateRenderers[slotIndex] : null;
        bool hasItem = slot?.itemData != null;

        if (itemRenderer != null)
        {
            if (!hasItem)
            {
                itemRenderer.enabled = false;
                itemRenderer.sprite = null;
            }
            else
            {
                Sprite itemSprite = ResolveItemDisplaySprite(slot.itemData, rule);
                itemRenderer.sprite = itemSprite;
                itemRenderer.enabled = itemSprite != null;
                itemRenderer.sortingOrder = ItemSpriteSortingOrder;
            }
        }

        if (smokeRenderer != null)
        {
            if (!hasItem || rule == null)
            {
                smokeRenderer.enabled = false;
                smokeRenderer.sprite = null;
            }
            else
            {
                Sprite smokeSprite = rule.SmokedStateSprite != null
                    ? rule.SmokedStateSprite
                    : (DefaultSmokeStateSprite != null ? DefaultSmokeStateSprite : itemRenderer?.sprite);

                smokeRenderer.sprite = smokeSprite;
                smokeRenderer.color = Color.Lerp(SmokeStartColor, SmokeDoneColor, SlotProgress01[slotIndex]);
                smokeRenderer.enabled = smokeSprite != null;
                smokeRenderer.sortingOrder = SmokeSpriteSortingOrder;
            }
        }
    }

    /// <summary>
    /// 获取槽位显示用 Sprite：规则显式指定优先，其次尝试从物品Prefab提取并缓存。
    /// </summary>
    private Sprite ResolveItemDisplaySprite(ItemData itemData, MeatrackDryingRule rule)
    {
        if (rule != null && rule.DisplaySprite != null)
        {
            return rule.DisplaySprite;
        }

        if (itemData == null || string.IsNullOrWhiteSpace(itemData.IDName))
        {
            return null;
        }

        if (_itemSpriteCache.TryGetValue(itemData.IDName, out Sprite cacheSprite))
        {
            return cacheSprite;
        }

        Sprite sprite = null;
        if (GameRes.Instance != null &&
            GameRes.Instance.AllPrefabs.TryGetValue(itemData.IDName, out GameObject prefab) &&
            prefab != null)
        {
            SpriteRenderer sr = prefab.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                sprite = sr.sprite;
            }
        }

        _itemSpriteCache[itemData.IDName] = sprite;
        return sprite;
    }

#endregion

#region 数据与初始化

    /// <summary>
    /// 保证模块数据容器可用，并补齐模块 Name/ID。
    /// </summary>
    private void EnsureDataContainer()
    {
        if (ModSaveData == null)
        {
            ModSaveData = new Ex_ModData_MemoryPackable();
        }

        if (string.IsNullOrWhiteSpace(ModSaveData.Name))
        {
            ModSaveData.Name = ModName;
        }

        if (string.IsNullOrWhiteSpace(ModSaveData.ID))
        {
            ModSaveData.ID = ModName;
        }
    }

    /// <summary>
    /// 注入默认配置。
    /// 包含槽位数量、热源识别配置、以及两条默认风干规则。
    /// </summary>
    private void EnsureDefaultConfig()
    {
        SlotCount = 3;

        if (HeatSourceItemIds == null)
        {
            HeatSourceItemIds = new List<string>();
        }

        if (HeatSourceItemIds.Count == 0)
        {
            HeatSourceItemIds.Add("Bonfire");
            HeatSourceItemIds.Add("Smelter");
        }

        if (HeatSourceModuleIds == null)
        {
            HeatSourceModuleIds = new List<string>();
        }

        if (HeatSourceModuleIds.Count == 0)
        {
            HeatSourceModuleIds.Add("熔炼模块");
            HeatSourceModuleIds.Add("熔炉模块");
        }

        if (DryingRules == null)
        {
            DryingRules = new List<MeatrackDryingRule>();
        }

        if (DryingRules.Count == 0)
        {
            DryingRules.Add(new MeatrackDryingRule
            {
                RuleName = "完整大肉块",
                InputItemIds = new List<string> { "Meat" },
                InputTags = new List<string> { "完整肉块", "大肉块" },
                OutputItemId = "Meat_Cooked",
                RequiredDryingSeconds = 120f,
                SuccessRate = 0.5f,
                DrySpeedMultiplier = 1f
            });

            DryingRules.Add(new MeatrackDryingRule
            {
                RuleName = "切割肉条",
                InputItemIds = new List<string> { "MeatStrip", "Meat_Strip" },
                InputTags = new List<string> { "肉条", "切割肉条" },
                OutputItemId = "Meat_Cooked",
                RequiredDryingSeconds = 120f,
                SuccessRate = 1f,
                DrySpeedMultiplier = 2f
            });
        }
    }

    /// <summary>
    /// 保证库存结构固定为3槽，并同步每个槽位索引/容量。
    /// </summary>
    private void EnsureInventoryStructure()
    {
        if (RackInventory == null)
        {
            RackInventory = new Inventory();
        }

        if (RackInventory.Data == null)
        {
            RackInventory.Data = new Inventory_Data(new List<ItemSlot>(), InventoryName);
        }

        if (string.IsNullOrWhiteSpace(RackInventory.Data.Name))
        {
            RackInventory.Data.Name = InventoryName;
        }

        if (RackInventory.InventoryPanel_Prefab == null && InventoryPanelPrefab != null)
        {
            RackInventory.InventoryPanel_Prefab = InventoryPanelPrefab;
        }

        if (RackInventory.Data.itemSlots == null)
        {
            RackInventory.Data.itemSlots = new List<ItemSlot>();
        }

        while (RackInventory.Data.itemSlots.Count < SlotCount)
        {
            RackInventory.Data.itemSlots.Add(new ItemSlot(RackInventory.Data.itemSlots.Count));
        }

        while (RackInventory.Data.itemSlots.Count > SlotCount)
        {
            RackInventory.Data.itemSlots.RemoveAt(RackInventory.Data.itemSlots.Count - 1);
        }

        for (int i = 0; i < RackInventory.Data.itemSlots.Count; i++)
        {
            if (RackInventory.Data.itemSlots[i] == null)
            {
                RackInventory.Data.itemSlots[i] = new ItemSlot(i);
            }

            RackInventory.Data.itemSlots[i].Index = i;
            RackInventory.Data.itemSlots[i].SlotMaxVolume = 100f;
        }
    }

    /// <summary>
    /// 保证运行时缓存数组长度与槽位数量一致。
    /// </summary>
    private void EnsureRuntimeState()
    {
        EnsureListLength(_slotElapsedSeconds, SlotCount, 0f);
        EnsureListLength(_slotSignatures, SlotCount, string.Empty);
        EnsureListLength(SlotProgress01, SlotCount, 0f);
    }

    /// <summary>
    /// 初始化 Inventory 的运行期引用与事件。
    /// </summary>
    private void InitInventoryRuntime()
    {
        RackInventory.item = item;
        RackInventory.InitData();
        _inventoryInitialized = true;
    }

    /// <summary>
    /// 读取存档并恢复库存与槽位累计风干时长。
    /// </summary>
    private void LoadSaveData()
    {
        MeatrackSaveData saveData = null;
        ModSaveData.ReadData(ref saveData);
        if (saveData == null)
        {
            return;
        }

        if (saveData.RackInventoryData != null)
        {
            RackInventory.Data = saveData.RackInventoryData;
        }

        if (saveData.SlotElapsedSeconds != null && saveData.SlotElapsedSeconds.Count > 0)
        {
            _slotElapsedSeconds.Clear();
            _slotElapsedSeconds.AddRange(saveData.SlotElapsedSeconds);
        }
    }

#endregion

#region 显示位自动预留

    /// <summary>
    /// 组件重置时自动补齐可视化显示位。
    /// </summary>
    private void Reset()
    {
        EnsureDataContainer();
        EnsureDefaultConfig();
        EnsureInventoryStructure();
        EnsureRuntimeState();
        EnsureVisualRenderers();
    }

    /// <summary>
    /// 保证两组渲染器数组长度正确，缺失时自动创建可视化节点。
    /// </summary>
    private void EnsureVisualRenderers()
    {
        if (SlotItemRenderers == null)
        {
            SlotItemRenderers = new List<SpriteRenderer>();
        }

        if (SlotSmokeStateRenderers == null)
        {
            SlotSmokeStateRenderers = new List<SpriteRenderer>();
        }

        EnsureRendererListLength(SlotItemRenderers, SlotCount);
        EnsureRendererListLength(SlotSmokeStateRenderers, SlotCount);

        for (int i = 0; i < SlotCount; i++)
        {
            if (SlotItemRenderers[i] == null || SlotSmokeStateRenderers[i] == null)
            {
                CreateVisualSlotIfMissing(i);
            }
        }
    }

    /// <summary>
    /// 为指定槽位创建可视化节点（Slot/ItemSprite/SmokeState）。
    /// </summary>
    private void CreateVisualSlotIfMissing(int index)
    {
        Transform visualRoot = transform.Find("Meatrack_VisualSlots");
        if (visualRoot == null)
        {
            GameObject root = new GameObject("Meatrack_VisualSlots");
            root.transform.SetParent(transform, false);
            visualRoot = root.transform;
        }

        string slotName = $"Slot_{index + 1}";
        Transform slotTransform = visualRoot.Find(slotName);
        if (slotTransform == null)
        {
            GameObject slot = new GameObject(slotName);
            slot.transform.SetParent(visualRoot, false);
            slotTransform = slot.transform;
        }

        slotTransform.localPosition = GetVisualLocalPosition(index);

        Transform itemTransform = slotTransform.Find("ItemSprite");
        if (itemTransform == null)
        {
            GameObject itemObj = new GameObject("ItemSprite");
            itemObj.transform.SetParent(slotTransform, false);
            itemTransform = itemObj.transform;
        }

        Transform smokeTransform = slotTransform.Find("SmokeState");
        if (smokeTransform == null)
        {
            GameObject smokeObj = new GameObject("SmokeState");
            smokeObj.transform.SetParent(slotTransform, false);
            smokeTransform = smokeObj.transform;
        }

        SpriteRenderer itemRenderer = itemTransform.GetComponent<SpriteRenderer>();
        if (itemRenderer == null)
        {
            itemRenderer = itemTransform.gameObject.AddComponent<SpriteRenderer>();
        }

        SpriteRenderer smokeRenderer = smokeTransform.GetComponent<SpriteRenderer>();
        if (smokeRenderer == null)
        {
            smokeRenderer = smokeTransform.gameObject.AddComponent<SpriteRenderer>();
        }

        itemRenderer.enabled = false;
        smokeRenderer.enabled = false;
        itemRenderer.sortingOrder = ItemSpriteSortingOrder;
        smokeRenderer.sortingOrder = SmokeSpriteSortingOrder;

        SlotItemRenderers[index] = itemRenderer;
        SlotSmokeStateRenderers[index] = smokeRenderer;
    }

    /// <summary>
    /// 根据槽位索引计算挂架上的本地显示位置。
    /// </summary>
    private Vector3 GetVisualLocalPosition(int index)
    {
        float centeredOffset = (index - (SlotCount - 1) * 0.5f) * VisualSlotSpacing;
        return VisualAnchorOffset + new Vector3(centeredOffset, 0f, 0f);
    }

#endregion

#region 检查器工具

    [TabGroup("检查器", "策划工具")]
    [Button("补齐默认配置", ButtonSizes.Medium)]
    private void InspectorEnsureDefaultConfig()
    {
        EnsureDataContainer();
        EnsureDefaultConfig();
        EnsureInventoryStructure();
        EnsureRuntimeState();
        EnsureVisualRenderers();
        ApplyVisualSlotLayout();
        ApplyRendererSortingOrders();
    }

    [TabGroup("检查器", "策划工具")]
    [Button("重建显示位", ButtonSizes.Medium)]
    private void InspectorRebuildVisualSlots()
    {
        EnsureVisualRenderers();
        ApplyVisualSlotLayout();
        ApplyRendererSortingOrders();
        RefreshSlotSprites();
    }

    [TabGroup("检查器", "策划工具")]
    [Button("清空风干进度", ButtonSizes.Medium)]
    private void InspectorClearSlotProgress()
    {
        EnsureInventoryStructure();
        EnsureRuntimeState();

        for (int i = 0; i < SlotCount; i++)
        {
            _slotElapsedSeconds[i] = 0f;
            SlotProgress01[i] = 0f;

            ItemData itemData = RackInventory?.Data?.itemSlots != null && i < RackInventory.Data.itemSlots.Count
                ? RackInventory.Data.itemSlots[i]?.itemData
                : null;
            _slotSignatures[i] = BuildSlotSignature(itemData);
        }
    }

#endregion

#region 工具方法

    /// <summary>
    /// 将挂架显示位重新排布到最新锚点与间距参数。
    /// </summary>
    private void ApplyVisualSlotLayout()
    {
        Transform visualRoot = transform.Find("Meatrack_VisualSlots");
        if (visualRoot == null)
        {
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            Transform slotTransform = visualRoot.Find($"Slot_{i + 1}");
            if (slotTransform == null)
            {
                continue;
            }

            slotTransform.localPosition = GetVisualLocalPosition(i);
        }
    }

    /// <summary>
    /// 立即应用物品层/熏制层的排序层级。
    /// </summary>
    private void ApplyRendererSortingOrders()
    {
        if (SlotItemRenderers != null)
        {
            for (int i = 0; i < SlotItemRenderers.Count; i++)
            {
                SpriteRenderer itemRenderer = SlotItemRenderers[i];
                if (itemRenderer != null)
                {
                    itemRenderer.sortingOrder = ItemSpriteSortingOrder;
                }
            }
        }

        if (SlotSmokeStateRenderers != null)
        {
            for (int i = 0; i < SlotSmokeStateRenderers.Count; i++)
            {
                SpriteRenderer smokeRenderer = SlotSmokeStateRenderers[i];
                if (smokeRenderer != null)
                {
                    smokeRenderer.sortingOrder = SmokeSpriteSortingOrder;
                }
            }
        }
    }

    /// <summary>
    /// 将 float 列表长度修正到目标长度。
    /// </summary>
    private static void EnsureListLength(List<float> list, int targetLength, float defaultValue)
    {
        if (list == null)
        {
            return;
        }

        while (list.Count < targetLength)
        {
            list.Add(defaultValue);
        }

        while (list.Count > targetLength)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

    /// <summary>
    /// 将 string 列表长度修正到目标长度。
    /// </summary>
    private static void EnsureListLength(List<string> list, int targetLength, string defaultValue)
    {
        if (list == null)
        {
            return;
        }

        while (list.Count < targetLength)
        {
            list.Add(defaultValue);
        }

        while (list.Count > targetLength)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

    /// <summary>
    /// 将渲染器列表长度修正到目标长度。
    /// </summary>
    private static void EnsureRendererListLength(List<SpriteRenderer> list, int targetLength)
    {
        while (list.Count < targetLength)
        {
            list.Add(null);
        }

        while (list.Count > targetLength)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

    /// <summary>
    /// 在规则表中寻找首条命中当前物品的规则。
    /// </summary>
    private bool TryGetRule(ItemData itemData, out MeatrackDryingRule rule)
    {
        for (int i = 0; i < DryingRules.Count; i++)
        {
            MeatrackDryingRule currentRule = DryingRules[i];
            if (currentRule == null)
            {
                continue;
            }

            if (IsRuleMatch(itemData, currentRule))
            {
                rule = currentRule;
                return true;
            }
        }

        rule = null;
        return false;
    }

    /// <summary>
    /// 判断物品是否匹配规则：ID 或 Tag 任意命中即可。
    /// </summary>
    private static bool IsRuleMatch(ItemData itemData, MeatrackDryingRule rule)
    {
        if (itemData == null || rule == null)
        {
            return false;
        }

        bool hasCondition = false;
        bool idMatched = false;

        if (rule.InputItemIds != null && rule.InputItemIds.Count > 0)
        {
            hasCondition = true;
            idMatched = ContainsAnyIgnoreCase(rule.InputItemIds, itemData.IDName);
        }

        bool tagMatched = false;
        if (rule.InputTags != null && rule.InputTags.Count > 0)
        {
            hasCondition = true;

            if (itemData.Tags != null)
            {
                for (int i = 0; i < rule.InputTags.Count; i++)
                {
                    string tag = rule.InputTags[i];
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        continue;
                    }

                    if (itemData.Tags.Contains(tag))
                    {
                        tagMatched = true;
                        break;
                    }
                }
            }
        }

        return hasCondition && (idMatched || tagMatched);
    }

    /// <summary>
    /// 忽略大小写检测字符串是否命中列表中的任一项。
    /// </summary>
    private static bool ContainsAnyIgnoreCase(List<string> source, string target)
    {
        if (source == null || source.Count == 0 || string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        for (int i = 0; i < source.Count; i++)
        {
            string value = source[i];
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (string.Equals(value, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 构造槽位签名，用于判断槽位内容是否发生变化。
    /// </summary>
    private static string BuildSlotSignature(ItemData itemData)
    {
        if (itemData == null)
        {
            return string.Empty;
        }

        return $"{itemData.Guid}|{itemData.IDName}|{itemData.ItemSpecialData}";
    }

    /// <summary>
    /// 在编辑器中显示热源检测范围。
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, HeatSourceRadius);
    }

#endregion
}
