using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 将通用 Item 生命周期事件翻译为导航网格占格更新。
/// 这是普通 C# 适配器而非 MonoBehaviour；占格来自纯整数定义，不读取 Collider。
/// </summary>
internal sealed class WorldNavigationItemFootprintBridge
{
    /// <summary>当前由导航网格占格的 Item 实例 ID。</summary>
    private readonly HashSet<int> registeredItemIds = new();

    /// <summary>每个已登记 Item 上次对应的根格。</summary>
    private readonly Dictionary<int, Vector2Int> registeredAnchorByItem = new();

    /// <summary>复用的占格缓冲；网格会在调用期间复制数据。</summary>
    private readonly List<Vector2Int> occupiedCellBuffer = new(8);

    /// <summary>当前绑定的非持久世界导航管理器。</summary>
    private WorldNavigationManager navigationManager;

    #region 生命周期绑定

    /// <summary>绑定导航网格并补齐当前已注册的运行时物品。</summary>
    public void Bind(WorldNavigationManager manager)
    {
        if (manager == null || navigationManager == manager)
            return;

        Unbind();
        navigationManager = manager;
        ItemMgr.RuntimeItemRegistered += RegisterOrUpdate;
        ItemMgr.RuntimeItemUnregistered += Unregister;
        ItemMgr.RuntimeItemMoved += OnRuntimeItemMoved;
        Item.RuntimeStructureChanged += OnRuntimeStructureChanged;

        ItemMgr itemManager = ItemMgr.Instance;
        if (itemManager == null)
            return;

        navigationManager.Grid.BeginBatchUpdate();
        try
        {
            foreach (Item item in itemManager.WorldRunTimeItems.Values)
                RegisterOrUpdate(item);
        }
        finally
        {
            navigationManager.Grid.EndBatchUpdate();
        }
    }

    /// <summary>解除生命周期订阅并注销由本适配器登记的全部占格。</summary>
    public void Unbind()
    {
        if (ReferenceEquals(navigationManager, null))
            return;

        ItemMgr.RuntimeItemRegistered -= RegisterOrUpdate;
        ItemMgr.RuntimeItemUnregistered -= Unregister;
        ItemMgr.RuntimeItemMoved -= OnRuntimeItemMoved;
        Item.RuntimeStructureChanged -= OnRuntimeStructureChanged;

        WorldNavigationManager boundManager = navigationManager;
        navigationManager = null;
        if (boundManager != null)
        {
            boundManager.Grid.BeginBatchUpdate();
            try
            {
                foreach (int itemId in registeredItemIds)
                    boundManager.UnregisterObstacle(itemId);
                registeredItemIds.Clear();
                registeredAnchorByItem.Clear();
            }
            finally
            {
                boundManager.Grid.EndBatchUpdate();
            }
        }
        else
        {
            registeredItemIds.Clear();
            registeredAnchorByItem.Clear();
        }
    }

    #endregion

    #region 占格同步

    /// <summary>按当前 Item 定义重新登记显式格偏移；没有占格定义时清除旧占格。</summary>
    private void RegisterOrUpdate(Item item)
    {
        if (navigationManager == null || item == null || item.itemData == null)
            return;

        GameRes gameRes = GameRes.ExistingInstance;
        if (gameRes == null ||
            !gameRes.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            definition.WorldGridOccupancy.Count == 0)
        {
            Unregister(item);
            return;
        }

        Vector2Int anchorCell = WorldNavigationGrid.WorldToCell(item.transform.position);
        IReadOnlyList<GridCellOffset> offsets = definition.WorldGridOccupancy;
        occupiedCellBuffer.Clear();
        for (int i = 0; i < offsets.Count; i++)
        {
            GridCellOffset offset = offsets[i];
            occupiedCellBuffer.Add(new Vector2Int(anchorCell.x + offset.X, anchorCell.y + offset.Y));
        }

        int itemId = item.GetInstanceID();
        navigationManager.RegisterObstacle(itemId, occupiedCellBuffer);
        registeredItemIds.Add(itemId);
        registeredAnchorByItem[itemId] = anchorCell;
    }

    /// <summary>移动事件只处理已占格物品，并跳过仍在同一根格内的连续位移。</summary>
    private void OnRuntimeItemMoved(Item item)
    {
        if (navigationManager == null || item == null || !registeredItemIds.Contains(item.GetInstanceID()))
            return;

        GameRes gameRes = GameRes.ExistingInstance;
        if (item.itemData == null ||
            gameRes == null ||
            !gameRes.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            definition.WorldGridOccupancy.Count == 0)
        {
            Unregister(item);
            return;
        }

        Vector2Int anchorCell = WorldNavigationGrid.WorldToCell(item.transform.position);
        if (registeredAnchorByItem.TryGetValue(item.GetInstanceID(), out Vector2Int previousAnchor) &&
            previousAnchor == anchorCell)
        {
            return;
        }

        RegisterOrUpdate(item);
    }

    /// <summary>在物品销毁或定义被移除占格时注销其导航阻挡。</summary>
    private void Unregister(Item item)
    {
        if (navigationManager == null || item == null)
            return;

        int itemId = item.GetInstanceID();
        if (registeredItemIds.Remove(itemId))
        {
            navigationManager.UnregisterObstacle(itemId);
            registeredAnchorByItem.Remove(itemId);
        }
    }

    /// <summary>活动物品的模块或配置结构变化时刷新已注册 Item 的占地。</summary>
    private void OnRuntimeStructureChanged(Item item)
    {
        if (item == null || item.itemData == null)
            return;

        ItemMgr itemManager = ItemMgr.Instance;
        if (itemManager != null &&
            itemManager.WorldRunTimeItems.TryGetValue(item.itemData.Guid, out Item registered) &&
            registered == item)
        {
            RegisterOrUpdate(item);
        }
    }

    #endregion
}
