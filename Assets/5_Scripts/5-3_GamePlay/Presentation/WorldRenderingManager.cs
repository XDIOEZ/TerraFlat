using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>物品共用的渲染功能入口，只分发表现请求，不参与游戏状态结算。</summary>
[DisallowMultipleComponent]
public sealed partial class WorldRenderingManager : SingletonMono<WorldRenderingManager>
{
    #region 会话状态与正式启动

    private readonly Dictionary<Item, ItemRenderingRegistration> itemRenderingRegistrations =
        new(ItemReferenceComparer.Instance);
    private GameManager lifecycleGameManager;
    private bool renderingServicesActive;

    public static WorldRenderingManager ExistingInstance => GetExistingInstance();
    public int RegisteredItemCount => itemRenderingRegistrations.Count;

    /// <summary>由正式会话拥有者安装统一入口，销毁清理只读取 ExistingInstance。</summary>
    public static WorldRenderingManager EnsureForOwner(Component owner)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        WorldRenderingManager manager = GetExistingInstance() ?? GetInstance();
        if (manager == null)
            manager = owner.gameObject.AddComponent<WorldRenderingManager>();
        manager.EnsureRenderingServicesActive();
        GameManager gameManager = owner as GameManager ?? owner.GetComponentInParent<GameManager>();
        if (gameManager != null) manager.BindGameManager(gameManager);
        return manager;
    }

    protected override void Awake()
    {
        base.Awake();
        if (GetExistingInstance() == this) EnsureRenderingServicesActive();
    }

    private void OnEnable() => EnsureRenderingServicesActive();

    private void EnsureRenderingServicesActive()
    {
        if (renderingServicesActive) return;
        // 编辑态独立规则与 Unity Awake 共用同一个幂等初始化入口。
        if (GetExistingInstance() != this)
        {
            base.Awake();
            if (GetExistingInstance() != this) return;
        }
        renderingServicesActive = true;
        InitializeSortingRenderingModule();
        InitializeShadowRenderingModule();
        InitializeRenderingFeatureModules();
        ItemMgr.RuntimeItemRegistered -= HandleRuntimeItemRegistered;
        ItemMgr.RuntimeItemRegistered += HandleRuntimeItemRegistered;
        ItemMgr.RuntimeItemUnregistered -= UnregisterItemRendering;
        ItemMgr.RuntimeItemUnregistered += UnregisterItemRendering;
        Item.RuntimeStructureChanged -= RefreshItemRendering;
        Item.RuntimeStructureChanged += RefreshItemRendering;
        RegisterRuntimeItems();
    }

    private void BindGameManager(GameManager manager)
    {
        if (lifecycleGameManager == manager) return;
        UnbindGameManager();
        lifecycleGameManager = manager;
        lifecycleGameManager.Event_GameWorldEnter += RegisterRuntimeItems;
        lifecycleGameManager.Event_GameWorldExit += ClearWorldRenderingSession;
    }

    private void UnbindGameManager()
    {
        if (lifecycleGameManager != null)
        {
            lifecycleGameManager.Event_GameWorldEnter -= RegisterRuntimeItems;
            lifecycleGameManager.Event_GameWorldExit -= ClearWorldRenderingSession;
        }
        lifecycleGameManager = null;
    }

    private void OnDisable()
    {
        ItemMgr.RuntimeItemRegistered -= HandleRuntimeItemRegistered;
        ItemMgr.RuntimeItemUnregistered -= UnregisterItemRendering;
        Item.RuntimeStructureChanged -= RefreshItemRendering;
        try { ClearItemRenderingRegistrations(); }
        finally
        {
            try { ReleaseRenderingFeatureModules(); }
            finally { renderingServicesActive = false; }
        }
    }

    protected override void OnDestroy()
    {
        UnbindGameManager();
        base.OnDestroy();
    }

    #endregion

    #region 物品注册与复用

    public bool IsItemRenderingRegistered(Item item) =>
        item != null && itemRenderingRegistrations.TryGetValue(item, out ItemRenderingRegistration registration) &&
        registration.RuntimeGeneration == item.RuntimeGeneration;

    /// <summary>注册物品的通用表现；相同物品同一代重复调用不会重复创建资源。</summary>
    public bool RegisterItemRendering(Item item)
    {
        if (item == null || item is Map || item.itemData == null || item.IsInPool ||
            item.IsPreparedCandidate || item.IsBeingDestroyed) return false;
        if (itemRenderingRegistrations.TryGetValue(item, out ItemRenderingRegistration registration))
        {
            if (registration.RuntimeGeneration == item.RuntimeGeneration) return true;
            UnregisterItemRendering(item);
        }
        registration = new ItemRenderingRegistration(item.RuntimeGeneration);
        itemRenderingRegistrations.Add(item, registration);
        try
        {
            RegisterItemSorting(item);
            ApplyItemShadowRegistration(item, registration, false);
            return true;
        }
        catch
        {
            UnregisterItemRendering(item);
            throw;
        }
    }

    /// <summary>结构更新或重新显示时只重建该物品的绑定，保留调用方的表现请求。</summary>
    public void RefreshItemRendering(Item item)
    {
        if (!IsItemRenderingRegistered(item)) return;
        RegisterItemSorting(item);
        ApplyItemShadowRegistration(item, itemRenderingRegistrations[item], true);
        RefreshItemRenderingFeatures(item);
    }

    /// <summary>死亡、回池和世界退出共用回收入口，不查找或创建新的管理器。</summary>
    public void UnregisterItemRendering(Item item)
    {
        if (ReferenceEquals(item, null)) return;
        itemRenderingRegistrations.Remove(item);
        try { UnregisterItemShadows(item); }
        finally
        {
            try { UnregisterItemSorting(item); }
            finally { UnregisterItemRenderingFeatures(item); }
        }
    }

    private void HandleRuntimeItemRegistered(Item item) => RegisterItemRendering(item);

    private void RegisterRuntimeItems()
    {
        if (!renderingServicesActive) return;
        ItemMgr itemManager = ItemMgr.GetExistingInstance();
        if (itemManager == null) return;
        foreach (Item item in itemManager.WorldRunTimeItems.Values)
        {
            if (IsItemRenderingRegistered(item)) RefreshItemRendering(item);
            else RegisterItemRendering(item);
        }
    }

    private void ClearItemRenderingRegistrations()
    {
        using var scope = UnityEngine.Pool.ListPool<Item>.Get(out var items);
        using var errorScope = UnityEngine.Pool.ListPool<Exception>.Get(out var errors);
        items.AddRange(itemRenderingRegistrations.Keys);
        for (int i = 0; i < items.Count; i++)
        {
            try { UnregisterItemRendering(items[i]); }
            catch (Exception error) { errors.Add(error); }
        }
        try { contactShadowRenderingBackend?.ClearWorldRendering(); }
        catch (Exception error) { errors.Add(error); }
        try { sunShadowRenderingBackend?.ClearWorldRendering(); }
        catch (Exception error) { errors.Add(error); }
        if (errors.Count > 0)
            throw new AggregateException("世界渲染清理出现错误，其余物品仍已完成回收。", errors);
    }

    private void ClearWorldRenderingSession()
    {
        try { ClearItemRenderingRegistrations(); }
        finally
        {
            ReleaseRenderingFeatureModules();
            if (renderingServicesActive) InitializeRenderingFeatureModules();
        }
    }

    private sealed class ItemRenderingRegistration
    {
        public readonly uint RuntimeGeneration;
        public bool ShadowEnabled = true;
        public bool InWater;

        public ItemRenderingRegistration(uint generation) => RuntimeGeneration = generation;
    }

    #endregion

    #region 独立渲染功能模块

    partial void InitializeRenderingFeatureModules();
    partial void ReleaseRenderingFeatureModules();
    partial void UnregisterItemRenderingFeatures(Item item);
    partial void RefreshItemRenderingFeatures(Item item);

    #endregion
}
