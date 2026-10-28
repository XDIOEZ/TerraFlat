using UnityEngine;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Gameplay.InventoryFeedback;

// 库存指针、拖拽与手柄交互。
public partial class Inventory
{
    #region 库存指针、拖拽与手柄交互
    // 只发布用户动作已成功的结果；独立的表现桥负责判断本地玩家和实际音色。
    private static void PublishInventoryAudio(Inventory source, Inventory target,
        ItemData movedItem, float amount, bool swapped = false)
    {
        if (source == null || target == null ||
            source is Inventory_Equipment || target is Inventory_Equipment)
            return;

        InventoryAudioAction action = swapped ? InventoryAudioAction.Swap :
            target.IsHandInventory() ? InventoryAudioAction.Pickup :
            target.IsHotBarInventory() || target.IsPlayerBagInventory()
                ? InventoryAudioAction.Backpack : InventoryAudioAction.Chest;
        InventoryAudioFeedbackEvents.Publish(movedItem, amount, action);
    }

    private static float GetCommittedTransferAmount(ItemSlot sourceSlot, ItemData originalItem, float before)
    {
        float after = ReferenceEquals(sourceSlot?.itemData, originalItem)
            ? sourceSlot.itemData?.Stack?.Amount ?? 0f : 0f;
        return Mathf.Max(0f, before - after);
    }

    private static bool WasSlotChanged(ItemSlot slot, ItemData oldItem, float oldAmount)
        => !ReferenceEquals(slot?.itemData, oldItem) ||
           !Mathf.Approximately(slot?.itemData?.Stack?.Amount ?? 0f, oldAmount);

    // 挂在玩家主动操作入口，保留原有库存事务实现且支持容器把放置重定向到已有堆叠。
    private bool TryDropSlotWithAudio(ItemSlot sourceSlot, ItemData draggedItem,
        Inventory target, int targetIndex, bool alreadyPickedUpByDrag = false)
    {
        ItemData sourceItem = ResolveCurrentDragSource(sourceSlot, draggedItem);
        if (sourceItem?.Stack == null || target?.Data?.itemSlots == null ||
            targetIndex < 0 || targetIndex >= target.Data.itemSlots.Count)
            return false;
        int resolvedIndex = target.ResolveIncomingSlotIndex(sourceSlot, targetIndex);
        if (resolvedIndex < 0 || resolvedIndex >= target.Data.itemSlots.Count)
            return false;

        ItemSlot targetSlot = target.Data.itemSlots[resolvedIndex];
        ItemData targetBefore = targetSlot?.itemData;
        float oldAmount = targetBefore?.Stack?.Amount ?? 0f;
        float sourceBefore = sourceItem.Stack.Amount;
        bool swapped = targetBefore != null && !targetBefore.CanStackWith(sourceItem);
        bool committed = TryDropSlotTo(sourceSlot, draggedItem, target, targetIndex);
        if (committed && WasSlotChanged(targetSlot, targetBefore, oldAmount) &&
            !(alreadyPickedUpByDrag && target.IsHandInventory()))
            PublishInventoryAudio(this, target, sourceItem,
                GetCommittedTransferAmount(sourceSlot, sourceItem, sourceBefore), swapped);
        return committed;
    }

    /// <summary>
    /// 解析外部物品真正应进入的目标槽位。普通库存严格使用玩家命中的槽位；
    /// 具备“整块投放区域”的专用容器可覆盖此入口，优先把同类物品并入已有堆叠。
    /// </summary>
    protected virtual int ResolveIncomingSlotIndex(ItemSlot sourceSlot, int requestedIndex)
    {
        return requestedIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnScroll(int index, float direction)
    {
        TransferScrollQuantity(index, direction, splitStack: false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnShiftScroll(int index, float direction)
    {
        TransferScrollQuantity(index, direction, splitStack: true);
    }

    // 普通滚轮搬一件，Shift滚轮搬来源堆叠的一半，事务仍走库存原有接收和同步规则。
    private void TransferScrollQuantity(int index, float direction, bool splitStack)
    {
        if (direction == 0f ||
            !TryGetPlayerHandSlots(index, out Inventory handInventory, out ItemSlot localSlot, out ItemSlot handSlot))
            return;

        if (direction > 0f && handSlot.itemData != null)
        {
            int resolvedIndex = ResolveIncomingSlotIndex(handSlot, index);
            if (resolvedIndex < 0 || resolvedIndex >= Data.itemSlots.Count)
                return;
            index = resolvedIndex;
            localSlot = Data.itemSlots[index];
        }

        // 滚轮只在当前槽与鼠标携带槽间转移，不借用快捷栏目标。
        ItemSlot sourceSlot = direction > 0f ? handSlot : localSlot;
        ItemData sourceItem = sourceSlot.itemData;
        float before = sourceItem?.Stack?.Amount ?? 0f;
        int amount = 1;
        if (splitStack)
        {
            int sourceAmount = Inventory_Data.GetWholeStackAmount(before);
            amount = Mathf.Max(1, Mathf.CeilToInt(sourceAmount * 0.5f));
        }

        bool transferred = direction > 0f
            ? handInventory.TryTransferQuickQuantity(handSlot, this, localSlot, amount)
            : TryTransferQuickQuantity(localSlot, handInventory, handSlot, amount);
        if (!transferred)
            return;

        PublishInventoryAudio(direction > 0f ? handInventory : this,
            direction > 0f ? this : handInventory,
            sourceItem, GetCommittedTransferAmount(sourceSlot, sourceItem, before));
        _touchTapFlow = !HasHeldItem(handInventory)
            ? TouchTapFlow.None
            : direction < 0f ? TouchTapFlow.Pickup : TouchTapFlow.PutDown;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void OnLeftClick(int index)
    {
        ProcessSlotInteraction(
            index,
            _isProcessingGamepadSubmit
                ? SlotInteractionSource.Gamepad
                : SlotInteractionSource.KeyboardMouse);
    }

    /// <summary>触屏轻触执行单件取放、同类追加或异类交换。</summary>
    public virtual void OnTouchTap(int index)
    {
        if (!TryGetPlayerHandSlots(index, out Inventory handInventory, out ItemSlot localSlot, out ItemSlot handSlot))
            return;

        bool handWasEmpty = handSlot.itemData == null;
        bool requestedSlotHadItem = localSlot.itemData != null;
        bool requestedSlotSameType = requestedSlotHadItem && !handWasEmpty &&
                                     localSlot.itemData.CanStackWith(handSlot.itemData);
        bool preferPickupSameType = _touchTapFlow == TouchTapFlow.Pickup && requestedSlotSameType;
        ItemData localBefore = localSlot.itemData;
        ItemData handBefore = handSlot.itemData;

        // 点击空白投放区域时语义仍然是“放下”；专用容器只替换真正写入的槽位，
        // 不能因为重定向到已有同类堆叠而误切回连续拿取方向。
        if (!handWasEmpty && !preferPickupSameType)
        {
            int resolvedIndex = ResolveIncomingSlotIndex(handSlot, index);
            if (resolvedIndex < 0 || resolvedIndex >= Data.itemSlots.Count)
                return;
            index = resolvedIndex;
            localSlot = Data.itemSlots[index];
        }

        // 手上物品放入带限制的目标槽位前，复用库存接收规则。
        if (handSlot.itemData != null && !CanAcceptQuickTransfer(handSlot, localSlot))
            return;

        ItemData effectiveTargetBefore = localSlot.itemData;
        bool swapsDifferentItems = handBefore != null && effectiveTargetBefore != null &&
                                   !effectiveTargetBefore.CanStackWith(handBefore);
        if (!Data.TouchTapItem(localSlot, handInventory.Data, handSlot, preferPickupSameType))
            return;

        RefreshUI(index);
        handInventory.RefreshUI(handSlot.Index);

        bool pickedUp = handWasEmpty || preferPickupSameType;
        PublishInventoryAudio(pickedUp ? this : handInventory,
            pickedUp ? handInventory : this,
            pickedUp ? localBefore : handBefore,
            swapsDifferentItems ? Mathf.Max(0f, handBefore?.Stack?.Amount ?? 0f) : 1f,
            swapsDifferentItems);

        if (!HasHeldItem(handInventory))
        {
            _touchTapFlow = TouchTapFlow.None;
        }
        else if ((handWasEmpty && requestedSlotHadItem) || preferPickupSameType)
        {
            // 空手拿起或在拿取方向点击同类槽后，继续点击可继续拿取。
            _touchTapFlow = TouchTapFlow.Pickup;
        }
        else if (!requestedSlotHadItem || requestedSlotSameType)
        {
            // 点击空槽或放置方向的同类槽后，继续点击可连续分发。
            _touchTapFlow = TouchTapFlow.PutDown;
        }
        else
        {
            // 异类交换完成后重新等待下一次明确的取放方向。
            _touchTapFlow = TouchTapFlow.None;
        }
    }

    /// <summary>判断当前槽是否允许玩家把手部整组物品长按放入。</summary>
    public virtual bool CanTouchLongPressPutDown(int index)
    {
        if (!TryGetPlayerHandSlots(index, out Inventory handInventory, out ItemSlot localSlot, out ItemSlot handSlot) ||
            handSlot.itemData == null)
            return false;

        int resolvedIndex = ResolveIncomingSlotIndex(handSlot, index);
        if (resolvedIndex < 0 || resolvedIndex >= Data.itemSlots.Count)
            return false;
        localSlot = Data.itemSlots[resolvedIndex];
        if (!CanAcceptQuickTransfer(handSlot, localSlot))
            return false;

        return localSlot.itemData == null || localSlot.itemData.CanStackWith(handSlot.itemData);
    }

    /// <summary>只把手部携带库存为空视为空手，快捷栏当前装备不影响查看介绍。</summary>
    public bool CanTouchInspectItem(int index)
    {
        Inventory hand = GetPlayerHandInventory();
        if (Data?.GetItemSlot(index)?.itemData == null || !IsValidQuickTransferTarget(hand)) return false;
        foreach (ItemSlot slot in hand.Data.itemSlots)
            if (slot?.itemData != null) return false;
        return true;
    }

    /// <summary>目标槽只负责手势计时，进度统一显示在玩家手部插槽上。</summary>
    private void ForwardTouchLongPressProgressToHand(float normalizedProgress, bool visible)
    {
        Inventory handInventory = GetPlayerHandInventory();
        if (handInventory?.itemSlot_UI == null || handInventory.itemSlot_UI.Count == 0)
            return;

        ItemSlot_UI handSlotUI = handInventory.itemSlot_UI[0];
        if (handSlotUI != null)
            handSlotUI.SetTouchLongPressProgressVisual(normalizedProgress, visible);
    }

    /// <summary>触屏长按空槽或同类槽时，把玩家手上整组物品一次性放入目标槽。</summary>
    public virtual bool OnTouchLongPress(int index)
    {
        if (!TryGetPlayerHandSlots(index, out Inventory handInventory, out ItemSlot localSlot, out ItemSlot handSlot) ||
            handSlot.itemData == null)
            return false;

        int resolvedIndex = ResolveIncomingSlotIndex(handSlot, index);
        if (resolvedIndex < 0 || resolvedIndex >= Data.itemSlots.Count)
            return false;
        index = resolvedIndex;
        localSlot = Data.itemSlots[index];
        if (!CanAcceptQuickTransfer(handSlot, localSlot) ||
            (localSlot.itemData != null && !localSlot.itemData.CanStackWith(handSlot.itemData)))
            return false;

        ItemData sourceItem = handSlot.itemData;
        float before = sourceItem.Stack.Amount;
        if (!Data.DropDraggedItem(localSlot, handInventory.Data, handSlot))
            return false;

        RefreshUI(index);
        handInventory.RefreshUI(handSlot.Index);
        PublishInventoryAudio(handInventory, this, sourceItem,
            GetCommittedTransferAmount(handSlot, sourceItem, before));

        if (this is Mod_HotBar.HotBarRuntimeInventory hotBarInventory)
            hotBarInventory.SyncHeldItemImmediately();

        _touchTapFlow = HasHeldItem(handInventory)
            ? TouchTapFlow.PutDown
            : TouchTapFlow.None;

        return true;
    }

    /// <summary>触屏长按更久后拖拽时，只把源堆的一半转入玩家手部槽位并建立拖拽事务。</summary>
    public virtual InventoryDragTransaction OnTouchHalfDragBegin(int index)
    {
        if (!TryGetPlayerHandSlots(index, out Inventory handInventory, out ItemSlot localSlot, out ItemSlot handSlot) ||
            localSlot.itemData?.Stack == null)
            return null;

        // 手上已有物品时改用整组直接拖拽，避免半组拆分覆盖手上物品。
        if (handSlot.itemData != null)
            return OnMouseDragBegin(index);

        DefaultTarget_Inventory = handInventory;
        int sourceAmount = Inventory_Data.GetWholeStackAmount(localSlot.itemData.Stack.Amount);
        int halfAmount = Mathf.Max(1, Mathf.CeilToInt(sourceAmount * 0.5f));
        ItemData sourceItem = localSlot.itemData;
        float originalAmount = sourceItem.Stack.Amount;
        if (!Data.TransferItemQuantityTo(localSlot, handInventory.Data, handSlot, halfAmount))
            return null;

        RefreshUI(index);
        handInventory.RefreshUI(handSlot.Index);
        PublishInventoryAudio(this, handInventory, sourceItem,
            GetCommittedTransferAmount(localSlot, sourceItem, originalAmount));
        _touchTapFlow = HasHeldItem(handInventory)
            ? TouchTapFlow.PutDown
            : TouchTapFlow.None;

        ItemData draggedItem = handSlot.itemData;
        return new InventoryDragTransaction(
            draggedItem.Stack.Amount,
            (targetInventory, targetIndex) =>
                handInventory.TryDropSlotWithAudio(handSlot, draggedItem,
                    targetInventory, targetIndex, true),
            null,
            null,
            false,
            true,
            () => ReferenceEquals(handSlot.itemData, draggedItem) ? draggedItem : null)
            .WithAccessGuard(() => CanAccessRuntime);
    }

    public virtual bool OnTouchWorldLongPress(Vector2 screenPosition)
    {
        return false;
    }

    /// <summary>桌面左键整组拿取、放置、合并或交换；逐件取放由滚轮独立处理。</summary>
    public virtual void OnDesktopTap(int index)
    {
        if (!TryGetPlayerHandSlots(index, out Inventory handInventory, out ItemSlot localSlot, out ItemSlot handSlot))
            return;

        // 点击与拖放共用整组事务，统一校验容量、双向接收规则并同步快捷栏手持物。
        if (handSlot.itemData != null)
            handInventory.TryDropSlotWithAudio(handSlot, handSlot.itemData, this, index);
        else if (localSlot.itemData != null)
            TryDropSlotWithAudio(localSlot, localSlot.itemData, handInventory, handSlot.Index);
    }

    /// <summary>判断当前库存是否存在可继续执行单件取放的手持物品。</summary>
    public bool HasTouchHeldItem()
    {
        return HasHeldItem(GetPlayerHandInventory());
    }

    /// <summary>解析当前槽位、玩家手部库存和手部槽位。</summary>
    private bool TryGetPlayerHandSlots(int index, out Inventory handInventory, out ItemSlot localSlot, out ItemSlot handSlot)
    {
        handInventory = GetPlayerHandInventory();
        localSlot = null;
        handSlot = null;
        if (!CanAccessRuntime || FlatWorld.Spaceflight.SpaceSession.RejectClientGroundInventoryMutation()) return false;

        if (!IsValidQuickTransferTarget(handInventory) || Data == null || Data.itemSlots == null ||
            index < 0 || index >= Data.itemSlots.Count || handInventory == this)
            return false;

        localSlot = Data.itemSlots[index];
        handSlot = handInventory.Data.itemSlots[0];
        return localSlot != null && handSlot != null;
    }

    /// <summary>创建以当前槽为来源的直接拖拽事务，不在拖拽开始时改写手部槽。</summary>
    public virtual InventoryDragTransaction OnMouseDragBegin(int index)
    {
        if (!CanAccessRuntime || FlatWorld.Spaceflight.SpaceSession.RejectClientGroundInventoryMutation()) return null;
        if (Data == null || Data.itemSlots == null || index < 0 || index >= Data.itemSlots.Count ||
            Data.itemSlots[index]?.itemData?.Stack == null)
            return null;

        ItemSlot sourceSlot = Data.itemSlots[index];
        ItemData draggedItem = sourceSlot.itemData;
        Inventory fallbackInventory = null;
        ItemSlot fallbackSlot = null;
        ItemData fallbackItem = null;

        BeginSlotDrag(sourceSlot);
        PublishInventoryAudio(this, GetPlayerHandInventory(), draggedItem,
            draggedItem.Stack.Amount);

        return new InventoryDragTransaction(
            draggedItem.Stack.Amount,
            (targetInventory, targetIndex) =>
                TryDropSlotWithAudio(sourceSlot, draggedItem, targetInventory, targetIndex, true),
            () => TryMoveDraggedSlotToHand(
                sourceSlot,
                draggedItem,
                out fallbackInventory,
                out fallbackSlot,
                out fallbackItem),
            (targetInventory, targetIndex) =>
                fallbackInventory != null &&
                fallbackInventory.TryDropSlotWithAudio(fallbackSlot, fallbackItem,
                    targetInventory, targetIndex, true),
            true,
            sourceItemProvider: () => ResolveCurrentDragSource(sourceSlot, draggedItem),
            onCompleted: () => EndSlotDrag(sourceSlot)).WithAccessGuard(() => CanAccessRuntime);
    }

    /// <summary>判断指定来源槽是否正被拖拽，供持续加工在用户操作期间暂缓消费。</summary>
    public bool IsSlotBeingDragged(int index)
    {
        return Data?.itemSlots != null && index >= 0 && index < Data.itemSlots.Count &&
               _activeDragSourceCounts != null &&
               _activeDragSourceCounts.ContainsKey(Data.itemSlots[index]);
    }

    /// <summary>虚拟化网格在任意槽位拖拽期间暂停重绑，避免来源索引被滚动复用。</summary>
    public bool HasActiveSlotDrag => _activeDragSourceCounts != null && _activeDragSourceCounts.Count > 0;

    /// <summary>登记一次来源槽拖拽，支持多指同时起手。</summary>
    private void BeginSlotDrag(ItemSlot slot)
    {
        _activeDragSourceCounts ??= new Dictionary<ItemSlot, int>();
        _activeDragSourceCounts.TryGetValue(slot, out int count);
        _activeDragSourceCounts[slot] = count + 1;
    }

    /// <summary>结束一次来源槽拖拽，避免面板关闭后留下加工暂停状态。</summary>
    private void EndSlotDrag(ItemSlot slot)
    {
        if (_activeDragSourceCounts == null || !_activeDragSourceCounts.TryGetValue(slot, out int count))
            return;
        if (count <= 1) _activeDragSourceCounts.Remove(slot);
        else _activeDragSourceCounts[slot] = count - 1;
    }

    /// <summary>让目标槽提交来源拖拽事务。</summary>
    public virtual bool OnMouseDragDrop(int index, InventoryDragTransaction transaction)
    {
        return transaction?.TryDrop(this, index) == true;
    }

    /// <summary>把来源槽直接移动、合并或交换到目标槽，并校验交换双方的接收规则。</summary>
    private bool TryDropSlotTo(
        ItemSlot sourceSlot,
        ItemData draggedItem,
        Inventory targetInventory,
        int targetIndex)
    {
        ItemData sourceItem = ResolveCurrentDragSource(sourceSlot, draggedItem);
        if (sourceItem == null ||
            !IsValidQuickTransferTarget(targetInventory) ||
            targetIndex < 0 || targetIndex >= targetInventory.Data.itemSlots.Count)
            return false;

        targetIndex = targetInventory.ResolveIncomingSlotIndex(sourceSlot, targetIndex);
        if (targetIndex < 0 || targetIndex >= targetInventory.Data.itemSlots.Count)
            return false;

        return TryCommitSlotTransfer(sourceSlot, targetInventory,
            targetInventory.Data.itemSlots[targetIndex], int.MaxValue, allowSwap: true);
    }

    /// <summary>
    /// 通过与真实拖拽完全相同的校验、交换和同步流程，把一个槽位移动到另一库存的指定槽位。
    /// 供输入适配层和自动化控制复用，避免直接改写 Inventory_Data。
    /// </summary>
    public bool TryMoveSlotTo(int sourceIndex, Inventory targetInventory, int targetIndex)
    {
        if (Data?.itemSlots == null || sourceIndex < 0 || sourceIndex >= Data.itemSlots.Count)
            return false;

        ItemSlot sourceSlot = Data.itemSlots[sourceIndex];
        return sourceSlot?.itemData != null &&
               TryDropSlotTo(sourceSlot, sourceSlot.itemData, targetInventory, targetIndex);
    }

    /// <summary>指定数量搬运复用正式接收、容量和机器网络事务，输入适配层不直接修改库存数据。</summary>
    public bool TryTransferQuantityTo(int sourceIndex, Inventory targetInventory, int targetIndex, int amount)
    {
        if (FlatWorld.Spaceflight.SpaceSession.RejectClientGroundInventoryMutation() ||
            ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction() || amount <= 0 ||
            Data?.itemSlots == null || (uint)sourceIndex >= (uint)Data.itemSlots.Count ||
            !IsValidQuickTransferTarget(targetInventory) ||
            (uint)targetIndex >= (uint)targetInventory.Data.itemSlots.Count ||
            IsSlotBeingDragged(sourceIndex) || targetInventory.IsSlotBeingDragged(targetIndex))
            return false;

        ItemSlot source = Data.itemSlots[sourceIndex];
        ItemSlot target = targetInventory.Data.itemSlots[targetIndex];
        ItemData original = source?.itemData;
        float before = original?.Stack?.Amount ?? 0f;
        if (ReferenceEquals(source, target) || before <= 0f ||
            !TryTransferQuickQuantity(source, targetInventory, target, amount)) return false;
        float moved = GetCommittedTransferAmount(source, original, before);
        if (moved > 0f)
        {
            PublishInventoryAudio(this, targetInventory, original, moved);
        }
        return true;
    }

    /// <summary>未命中槽位时才把拖拽物转入玩家手部槽，并返回新的携带来源。</summary>
    private bool TryMoveDraggedSlotToHand(
        ItemSlot sourceSlot,
        ItemData draggedItem,
        out Inventory handInventory,
        out ItemSlot handSlot,
        out ItemData handItem)
    {
        handInventory = GetPlayerHandInventory();
        handSlot = null;
        handItem = null;
        if (ResolveCurrentDragSource(sourceSlot, draggedItem) == null ||
            !IsValidQuickTransferTarget(handInventory) ||
            ReferenceEquals(this, handInventory))
            return false;

        handSlot = handInventory.Data.itemSlots[0];
        if (!TryDropSlotTo(sourceSlot, draggedItem, handInventory, 0))
            return false;

        handItem = handSlot.itemData;
        _touchTapFlow = HasHeldItem(handInventory)
            ? TouchTapFlow.PutDown
            : TouchTapFlow.None;
        return handItem?.Stack != null;
    }

    /// <summary>识别同一来源物品；制作事务可替换对象引用，但会保留实例标识与定义 ID。</summary>
    private ItemData ResolveCurrentDragSource(ItemSlot sourceSlot, ItemData draggedItem)
    {
        if (Data?.itemSlots == null || sourceSlot == null || draggedItem?.Stack == null ||
            !Data.itemSlots.Contains(sourceSlot))
            return null;

        ItemData current = sourceSlot.itemData;
        if (current?.Stack == null)
            return null;
        if (ReferenceEquals(current, draggedItem))
            return current;

        return draggedItem.Guid != 0 && current.Guid == draggedItem.Guid &&
               string.Equals(draggedItem.IDName, current.IDName, StringComparison.Ordinal)
            ? current
            : null;
    }

    /// <summary>判断一个槽位能否完整容纳指定物品堆，供异类交换使用。</summary>
    private bool CanHoldWholeStack(ItemSlot slot, ItemData itemData)
    {
        if (slot == null || itemData?.Stack == null)
            return false;
        if (!itemData.Stack.Stackable)
            return itemData.Stack.Amount <= 1.0001f;
        return Data != null && itemData.Stack.Amount <= slot.SlotMaxVolume + 0.0001f;
    }

    /// <summary>
    /// 手柄 A/Submit 的槽位操作入口，使用独立的快捷栏目标，不改写键鼠手上槽位状态。
    /// </summary>
    public virtual void OnGamepadSubmit(int index)
    {
        bool previousValue = _isProcessingGamepadSubmit;
        _isProcessingGamepadSubmit = true;
        try
        {
            // 通过虚拟标记调用虚拟 OnLeftClick，保留装备栏、快捷栏等派生库存的专用校验。
            OnLeftClick(index);
        }
        finally
        {
            _isProcessingGamepadSubmit = previousValue;
        }
    }

    private enum SlotInteractionSource
    {
        KeyboardMouse,
        Gamepad
    }

    /// <summary>
    /// 执行一次槽位交换，并让输入设备决定本次使用的目标库存。
    /// </summary>
    private void ProcessSlotInteraction(int index, SlotInteractionSource source)
    {
        if (Data == null || Data.itemSlots == null || index < 0 || index >= Data.itemSlots.Count)
            return;

        if (!TryResolveSlotInteractionTarget(source))
        {
            Debug.LogWarning($"[Inventory.ProcessSlotInteraction] DefaultTarget_Inventory 未设置，当前库存={Data.Name}, 索引={index}, 输入={source}");
            return;
        }

        int targetIndex = GetLeftClickTargetSlotIndex(index);
        ItemSlot localSlot = Data.itemSlots[index];
        ItemSlot targetSlot = DefaultTarget_Inventory.Data.itemSlots[targetIndex];
        bool changed = false;
        if (localSlot?.itemData != null)
            changed = TryDropSlotWithAudio(localSlot, localSlot.itemData, DefaultTarget_Inventory, targetIndex);
        else if (targetSlot?.itemData != null)
            changed = DefaultTarget_Inventory.TryDropSlotWithAudio(targetSlot, targetSlot.itemData, this, index);

        if (!changed)
            return;

        // 玩家背包转入快捷栏后，立即刷新当前手持实例，避免等待下一次模块 Tick 才显示。
        if (DefaultTarget_Inventory is Mod_HotBar.HotBarRuntimeInventory hotBarInventory)
            hotBarInventory.SyncHeldItemImmediately();

        RefreshUI(index);
    }

    /// <summary>
    /// 键鼠始终使用鼠标携带槽；手柄确认玩家行囊时才固定使用当前快捷栏槽位。
    /// </summary>
    private bool TryResolveSlotInteractionTarget(SlotInteractionSource source)
    {
        if (IsPlayerBagInventory() && source == SlotInteractionSource.Gamepad)
        {
            Inventory hotBar = GetPlayerHotBarInventory();
            if (IsValidQuickTransferTarget(hotBar))
            {
                DefaultTarget_Inventory = hotBar;
                return true;
            }
        }

        if (IsPlayerBagInventory() && source == SlotInteractionSource.KeyboardMouse)
        {
            Inventory handInventory = GetPlayerHandInventory();
            if (IsValidQuickTransferTarget(handInventory))
            {
                DefaultTarget_Inventory = handInventory;
                return true;
            }
        }

        return TryEnsureDefaultTargetInventory();
    }

    private bool TryEnsureDefaultTargetInventory()
    {
        // 玩家背包的左键操作应进入快捷栏，否则物品只会落入不可见的手部缓冲槽。
        if (IsPlayerBagInventory())
        {
            if (DefaultTarget_Inventory?.Data?.Name == ModText.Hotbar &&
                IsValidQuickTransferTarget(DefaultTarget_Inventory))
                return true;

            Inventory hotBar = GetPlayerHotBarInventory();
            if (IsValidQuickTransferTarget(hotBar))
            {
                DefaultTarget_Inventory = hotBar;
                return true;
            }
        }

        if (DefaultTarget_Inventory != null && DefaultTarget_Inventory.Data != null && DefaultTarget_Inventory.Data.itemSlots != null && DefaultTarget_Inventory.Data.itemSlots.Count > 0)
            return true;

        Inventory handInventory = GetPlayerHandInventory();
        if (IsValidQuickTransferTarget(handInventory))
        {
            DefaultTarget_Inventory = handInventory;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 获取当前玩家的手上库存，优先使用背包所属玩家，避免静态引用残留到旧玩家。
    /// </summary>
    private Inventory GetPlayerHandInventory()
    {
        // C# 的 ?. 不会使用 UnityEngine.Object 重载的 null 语义；销毁后的 Player 托管引用仍可能非 null。
        // 必须先显式做 Unity 空判断，避免在场景/玩家卸载期间调用已销毁对象的 GetComponentInChildren。
        Inventory handInventory = null;
        if (item != null)
        {
            handInventory = item.GetComponentInChildren<Mod_Hand>()?.HandInventory;
            if (IsValidQuickTransferTarget(handInventory))
                return handInventory;

            if (item.itemMods != null && item.itemMods.ContainsKey_ID(ModText.Hand))
            {
                IInventory handInventoryProvider = item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>();
                handInventory = handInventoryProvider?.GetDefaultTargetInventory();
                if (IsValidQuickTransferTarget(handInventory))
                    return handInventory;
            }
        }

        return IsValidQuickTransferTarget(Inventory_Hand.PlayerHand)
            ? Inventory_Hand.PlayerHand
            : null;
    }

    /// <summary>
    /// 判断手上槽位是否确实持有可交换物品。
    /// </summary>
    private static bool HasHeldItem(Inventory handInventory)
    {
        if (!IsValidQuickTransferTarget(handInventory))
            return false;

        for (int i = 0; i < handInventory.Data.itemSlots.Count; i++)
        {
            ItemSlot slot = handInventory.Data.itemSlots[i];
            if (slot?.itemData != null && slot.itemData.Stack != null && slot.itemData.Stack.Amount > 0f)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 玩家背包点击时使用快捷栏当前选中槽；普通容器仍沿用原有手部槽位映射。
    /// </summary>
    private int GetLeftClickTargetSlotIndex(int sourceIndex)
    {
        if (DefaultTarget_Inventory is Mod_HotBar.HotBarRuntimeInventory hotBarInventory &&
            hotBarInventory.Data?.itemSlots != null &&
            hotBarInventory.Data.itemSlots.Count > 0)
        {
            return Mathf.Clamp(
                hotBarInventory.Data.Index,
                0,
                hotBarInventory.Data.itemSlots.Count - 1);
        }

        return DefaultTarget_Inventory.Data.itemSlots.Count > sourceIndex ? sourceIndex : 0;
    }
    #endregion
}
