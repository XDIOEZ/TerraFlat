// AI-Context: 玩家世界物品拾取入口；联机时只发起服务端事务，收到授权后才写入背包，禁止直接 Destroy 绕过 ItemMgr/网络生命周期。
using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Audio;
using FlatWorld.Gameplay.Progress;
using UnityEngine;

/// <summary>
/// 物品拾取器组件，用于自动收集可拾取物品
/// </summary>
public class ItemPicker : Module
{

    #region 基础参数

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    public string[] Data;
    #endregion

    #region 生命周期

    public override void Awake()
    {
        _Data.ID = ModText.Picker;
    }

    public override void Load()
    {
        ModSaveData.ReadData(ref Data);
        RebuildTargetInventories();

        // 默认优先级：Hotbar -> Bag
        // 无论列表是否已有配置，都补齐这两个核心容器（内部会去重）
        TryAddInventoryById(ModText.Hotbar);
        TryAddInventoryById(ModText.Bag);
    }

    /// <summary>
    /// 初始化时尝试获取自身的Inventory组件
    /// </summary>
    private void Start()
    {
    }
    public override void Save()
    {
        ModSaveData.WriteData(Data);
    }
    public override void Act()
    {
        base.Act();
    }
    #endregion


    /// <summary>
    /// 尝试根据模块 ID 获取对应的 Mod_Inventory，并将其 inventory 加入目标列表
    /// </summary>
    /// <param name="modId">ModText 中定义的背包模块 ID</param>
    private void TryAddInventoryById(string modId)
    {
        var module = item.itemMods.GetMod_ByID(modId);
        if (module == null)
            return;

        if (module is not IInventory inventoryProvider)
            return;

        if (!AddTargetInventories.Contains(inventoryProvider))
            AddTargetInventories.Add(inventoryProvider);
    }


    #region 字段与属性

    [Header("目标物品栏（按优先级排列）")]
    [SerializeField]
    public List<Module> AddTargetInventoryModules = new List<Module>(); // 检查器可拖拽的目标库存模块

    [System.NonSerialized]
    public List<IInventory> AddTargetInventories = new List<IInventory>(); // 运行时使用的目标库存接口列表

    [Header("拾取吸入动画")]
    [SerializeField, Min(0.05f)]
    private float pickupSuctionDuration = 0.28f;

    [SerializeField, Min(0f)]
    private float pickupSuctionCurveOffset = 0.3f;

    [SerializeField]
    private float pickupSuctionRotation = 90f;

    /// <summary>
    /// 基础拾取权限控制变量
    /// </summary>
    private bool canPickUp = true;

    /// <summary>
    /// 综合判断是否可以拾取物品
    /// 1. 检查基础权限
    /// 2. 检查是否有可用的物品栏
    /// </summary>
    public bool CanPickUp
    {
        get
        {
            if (!canPickUp)
                return false;

            // 只要存在有效背包就允许进入拾取流程。
            // 是否能实际放入（包括满包堆叠）由 TryAddItem 决定。
            foreach (var inventory in AddTargetInventories)
            {
                var targetInventory = inventory?.GetDefaultTargetInventory();
                if (targetInventory != null && targetInventory.Data != null)
                {
                    return true;
                }
            }
            return false;
        }
        set => canPickUp = value;
    }

    #endregion


#region 私有方法

    private void RebuildTargetInventories()
    {
        AddTargetInventories.Clear();

        for (int i = 0; i < AddTargetInventoryModules.Count; i++)
        {
            Module module = AddTargetInventoryModules[i];
            if (module is not IInventory inventoryProvider)
            {
                continue;
            }

            if (!AddTargetInventories.Contains(inventoryProvider))
            {
                AddTargetInventories.Add(inventoryProvider);
            }
        }
    }

#endregion



    #region 物品交互

    /// <summary>
    /// 当有物体进入触发器时尝试拾取物品
    /// </summary>
    /// <param name="other">进入触发器的碰撞体</param>
    private void OnTriggerEnter2D(Collider2D other)
    {
        // 检查物品栏列表是否为空
        if (AddTargetInventories.Count == 0)
        {
            Debug.LogWarning($"[{nameof(ItemPicker)}] AddTargetInventories is empty on {gameObject.name}");
            return;
        }

        // 检查是否具有拾取权限
        if (!CanPickUp)
        {
            return;
        }

        // 获取物品组件
        Item pickAble = WorldTopologyColliderProxy.ResolveComponent<Item>(other);

        if (pickAble != null && pickAble.itemData.Stack.CanBePickedUp)
        {
            if (ItemNetworkStateSerialization.BeginNetworkPickup(this, pickAble))
            {
                return;
            }

            pickAble.ModuleSave();
            if (TryAcceptNetworkPickup(pickAble.itemData))
            {
                PlayPickupSuction(pickAble);
                ItemMgr.Instance.DespawnItem(pickAble);
                return;
            }

            Debug.Log($"[{nameof(ItemPicker)}] All target inventories are full, cannot pick up item: {pickAble.itemData.IDName}");
        }
    }

    /// <summary>
    /// 网络拾取授权和单机拾取共用的唯一入包入口。
    /// </summary>
    public bool TryAcceptNetworkPickup(ItemData itemData)
    {
        if (itemData == null || itemData.Stack == null)
            return false;

        foreach (IInventory inventory in AddTargetInventories)
        {
            Inventory targetInventory = inventory?.GetDefaultTargetInventory();
            if (targetInventory?.Data == null)
                continue;

            float amountBeforePickup = CountItemAmount(targetInventory.Data, itemData.IDName);
            if (!targetInventory.Data.TryAddItem(itemData))
                continue;

            float addedAmount = Mathf.Max(
                0f,
                CountItemAmount(targetInventory.Data, itemData.IDName) - amountBeforePickup);
            itemData.Stack.CanBePickedUp = false;
            targetInventory.RefreshUI();
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
            GameplayProgressEvents.PublishPickupSucceeded(item as Player, itemData.IDName, addedAmount);
            return true;
        }

        return false;
    }

    /// <summary>统计库存内指定物品数量，用于发布实际成功入包的增量。</summary>
    private static float CountItemAmount(Inventory_Data inventoryData, string itemId)
    {
        if (inventoryData?.itemSlots == null || string.IsNullOrWhiteSpace(itemId))
            return 0f;

        float total = 0f;
        foreach (ItemSlot slot in inventoryData.itemSlots)
        {
            ItemData stored = slot?.itemData;
            if (stored?.Stack != null &&
                string.Equals(stored.IDName, itemId, StringComparison.Ordinal))
            {
                total += stored.Stack.Amount;
            }
        }

        return total;
    }

    /// <summary>
    /// 创建不含碰撞与业务脚本的视觉快照，并将其加速缩小吸入玩家身体。
    /// 真实物品可立即按原生命周期回收，动画不会污染对象池状态。
    /// </summary>
    public void PlayPickupSuction(Item worldItem, bool hideSourceRenderers = false)
    {
        if (worldItem == null)
            return;

        AudioService.Instance.PlayAt(AudioEventIds.ItemPickup, worldItem.transform.position);

        SpriteRenderer[] sourceRenderers = worldItem.GetComponentsInChildren<SpriteRenderer>(false);
        GameObject visualRoot = CreatePickupVisualSnapshot(worldItem, sourceRenderers);
        if (visualRoot == null)
            return;

        if (hideSourceRenderers)
        {
            for (int i = 0; i < sourceRenderers.Length; i++)
            {
                if (sourceRenderers[i] != null)
                    sourceRenderers[i].enabled = false;
            }
        }

        Transform target = item != null ? item.transform : transform;
        Vector3 targetLocalPoint = Vector3.zero;
        SpriteRenderer bodyRenderer = item != null
            ? (item.Sprite != null ? item.Sprite : item.GetComponentInChildren<SpriteRenderer>())
            : GetComponentInChildren<SpriteRenderer>();

        if (bodyRenderer != null)
            targetLocalPoint = target.InverseTransformPoint(bodyRenderer.bounds.center);

        StartCoroutine(AnimatePickupSuction(
            visualRoot,
            target,
            targetLocalPoint,
            worldItem.itemData?.Guid ?? worldItem.GetInstanceID()));
    }

    private static GameObject CreatePickupVisualSnapshot(
        Item worldItem,
        SpriteRenderer[] sourceRenderers)
    {
        if (sourceRenderers == null || sourceRenderers.Length == 0)
            return null;

        GameObject visualRoot = new GameObject($"PickupSuction_{worldItem.name}");
        visualRoot.layer = worldItem.gameObject.layer;
        visualRoot.transform.SetPositionAndRotation(worldItem.transform.position, Quaternion.identity);
        visualRoot.transform.localScale = Vector3.one;

        int copiedRendererCount = 0;
        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            SpriteRenderer source = sourceRenderers[i];
            if (source == null || !source.enabled || source.sprite == null ||
                !source.gameObject.activeInHierarchy)
            {
                continue;
            }

            GameObject rendererObject = new GameObject(source.gameObject.name);
            rendererObject.layer = source.gameObject.layer;
            rendererObject.transform.SetParent(visualRoot.transform, false);
            rendererObject.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            rendererObject.transform.localScale = source.transform.lossyScale;

            SpriteRenderer copy = rendererObject.AddComponent<SpriteRenderer>();
            copy.sprite = source.sprite;
            copy.sharedMaterial = source.sharedMaterial;
            copy.color = source.color;
            copy.flipX = source.flipX;
            copy.flipY = source.flipY;
            copy.drawMode = source.drawMode;
            copy.size = source.size;
            copy.maskInteraction = source.maskInteraction;
            copy.spriteSortPoint = source.spriteSortPoint;
            copy.sortingLayerID = source.sortingLayerID;
            copy.sortingOrder = source.sortingOrder + 1;
            copiedRendererCount++;
        }

        if (copiedRendererCount > 0)
            return visualRoot;

        Destroy(visualRoot);
        return null;
    }

    private IEnumerator AnimatePickupSuction(
        GameObject visualRoot,
        Transform target,
        Vector3 targetLocalPoint,
        int itemIdentity)
    {
        Vector3 initialScale = visualRoot.transform.localScale;
        float duration = Mathf.Max(0.05f, pickupSuctionDuration);
        float elapsed = 0f;

        Vector3 initialTarget = target != null
            ? target.TransformPoint(targetLocalPoint)
            : visualRoot.transform.position;
        Vector3 startPosition = WorldTopologyRuntime.NearestImagePosition(
            initialTarget,
            visualRoot.transform.position);
        startPosition.z = visualRoot.transform.position.z;
        visualRoot.transform.position = startPosition;
        Vector2 direction = initialTarget - startPosition;
        Vector2 perpendicular = direction.sqrMagnitude > 0.0001f
            ? new Vector2(-direction.y, direction.x).normalized
            : Vector2.up;
        float curveSign = (itemIdentity & 1) == 0 ? 1f : -1f;
        Vector3 curveOffset = perpendicular * (pickupSuctionCurveOffset * curveSign);

        while (elapsed < duration && visualRoot != null)
        {
            if (target == null)
                break;

            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float suctionTime = normalizedTime * normalizedTime * normalizedTime;
            Vector3 targetPosition = WorldTopologyRuntime.NearestImagePosition(
                visualRoot.transform.position,
                target.TransformPoint(targetLocalPoint));
            float arcWeight = Mathf.Sin(normalizedTime * Mathf.PI);

            visualRoot.transform.position =
                Vector3.LerpUnclamped(startPosition, targetPosition, suctionTime) +
                curveOffset * arcWeight;
            visualRoot.transform.localScale =
                initialScale * Mathf.Max(0.02f, 1f - normalizedTime * normalizedTime);
            visualRoot.transform.rotation =
                Quaternion.Euler(0f, 0f, pickupSuctionRotation * suctionTime * curveSign);
            yield return null;
        }

        if (visualRoot != null)
            Destroy(visualRoot);
    }

    #endregion
}
