using System;

/// <summary>
/// 保存一次物品拖拽的事务边界；命中槽位时直接提交来源与目标，未命中时才切换到手部携带流程。
/// </summary>
public sealed class InventoryDragTransaction
{
    private Func<Inventory, int, bool> _dropHandler;
    private readonly Func<bool> _prepareFallbackHandler;
    private readonly Func<Inventory, int, bool> _fallbackDropHandler;
    private readonly Func<ItemData> _sourceItemProvider;
    private Action _onCompleted;
    private Func<bool> _accessGuard;
    private bool _fallbackPrepared;
    private bool _completed;

    /// <summary>拖拽时是否需要隐藏来源槽内容，避免原物品与拖拽图标重复显示。</summary>
    public bool HideSourceVisual { get; private set; }

    /// <summary>本次实际拖动数量，半组拖拽使用拆分后的数量。</summary>
    public float DraggedAmount { get; }

    /// <summary>创建一次可提交、可按需转入手部槽的拖拽事务。</summary>
    public InventoryDragTransaction(
        float draggedAmount,
        Func<Inventory, int, bool> dropHandler,
        Func<bool> prepareFallbackHandler,
        Func<Inventory, int, bool> fallbackDropHandler,
        bool hideSourceVisual,
        bool fallbackPrepared = false,
        Func<ItemData> sourceItemProvider = null,
        Action onCompleted = null)
    {
        DraggedAmount = draggedAmount;
        _dropHandler = dropHandler;
        _prepareFallbackHandler = prepareFallbackHandler;
        _fallbackDropHandler = fallbackDropHandler;
        _sourceItemProvider = sourceItemProvider;
        _onCompleted = onCompleted;
        HideSourceVisual = hideSourceVisual;
        _fallbackPrepared = fallbackPrepared;
    }

    #region 组合访问守卫

    /// <summary>附加来源访问条件，并保留原事务的真实物品来源和手部回退状态。</summary>
    public InventoryDragTransaction WithAccessGuard(Func<bool> canAccess)
    {
        if (canAccess == null) throw new ArgumentNullException(nameof(canAccess));
        if (_accessGuard == canAccess) return this;
        Func<bool> previous = _accessGuard;
        _accessGuard = previous == null ? canAccess : () => previous() && canAccess();
        return this;
    }

    #endregion

    /// <summary>尝试把当前拖拽物直接提交到指定库存槽位。</summary>
    public bool TryDrop(Inventory targetInventory, int targetIndex)
    {
        if (_completed || _accessGuard?.Invoke() == false || _dropHandler == null || targetInventory == null)
            return false;

        if (!_dropHandler(targetInventory, targetIndex))
            return false;

        _completed = true;
        ReleaseSourceDrag();
        return true;
    }

    /// <summary>
    /// 让非库存槽玩法目标直接消费当前拖拽物品的状态；成功后结束事务，但不移动来源物品。
    /// </summary>
    public bool TryConsumeSourceItem(Func<ItemData, bool> consumer)
    {
        if (_completed || _accessGuard?.Invoke() == false || consumer == null || _sourceItemProvider == null)
            return false;

        ItemData sourceItem = _sourceItemProvider();
        if (sourceItem == null || !consumer(sourceItem))
            return false;

        _completed = true;
        ReleaseSourceDrag();
        return true;
    }

    /// <summary>按需把尚未提交的来源物品转入手部槽，并切换后续放置来源。</summary>
    public bool PrepareFallback()
    {
        if (_completed || _accessGuard?.Invoke() == false)
            return false;

        if (_fallbackPrepared)
            return true;

        if (_prepareFallbackHandler == null || !_prepareFallbackHandler())
            return false;

        if (_fallbackDropHandler != null)
            _dropHandler = _fallbackDropHandler;

        _fallbackPrepared = true;
        HideSourceVisual = false;
        ReleaseSourceDrag();
        return true;
    }

    /// <summary>标记拖拽物已由世界交互消费，阻止后续重复放置。</summary>
    public void Resolve()
    {
        _completed = true;
        ReleaseSourceDrag();
    }

    /// <summary>结束拖拽；仅在未命中槽位时按要求准备手部携带回退。</summary>
    public void Complete(bool prepareFallback)
    {
        if (!_completed && prepareFallback)
            PrepareFallback();

        _completed = true;
        ReleaseSourceDrag();
    }

    /// <summary>来源槽占用只释放一次，支持目标提交、转入手部及取消三种结束方式。</summary>
    private void ReleaseSourceDrag()
    {
        Action onCompleted = _onCompleted;
        _onCompleted = null;
        onCompleted?.Invoke();
    }
}
