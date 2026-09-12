using System;
using FlatWorld.Networking;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 通用液体容器状态。容器身份与液体身份完全分离：空容器没有 LiquidId，非空容器只保存稳定液体 ID、数量和加工进度。
/// </summary>
[Serializable, MemoryPackable]
public partial class LiquidContainerState
{
    public string LiquidId; // 当前液体稳定 ID；空容器必须为空。
    public int Amount; // 当前份数。
    public float ProcessingSeconds; // 当前液体加热处理的累计秒数。
}

/// <summary>
/// 可装任意已注册液体的复用容器模块。历史类名仍由现有模块 Prefab 使用，但运行时语义已经是通用液体容器；
/// 液体属性全部来自 LiquidDefinition，新增 MOD 液体无需新增容器 Item 或修改本模块。
/// </summary>
public sealed class Mod_WaterVessel : Module, IInteractable
{
    #region 数据与生命周期

    public const string ModuleId = "Mod_WaterVessel";
    public const int DefaultCapacity = 8;
    public Ex_ModData_MemoryPackable ModData = new(); // 容器独立持久化载体。
    public LiquidContainerState Data = new(); // 液体 ID、数量与加工进度。
    public int capacity = DefaultCapacity; // 当前容器最大份数，由物品定义配置。
    public float reach = 2f; // 装液与转移距离。
    public static event Action<Mod_WaterVessel, Item> OpenRequested; // 表现层打开容器。
    public event Action Changed; // 当前容器状态变化。
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public int Capacity => capacity;
    public LiquidDefinition CurrentLiquid => ResolveLiquidDefinition(Data?.LiquidId, false);
    private WorldTileTargetOutline targetOutline; // 当前准心命中的单格液体来源轮廓。

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ?? throw new ArgumentException("液体容器数据类型错误。");
    }

    /// <summary>恢复容器状态并绑定统一使用入口。</summary>
    public override void Load()
    {
        ModData.ReadData(ref Data);
        Data ??= new LiquidContainerState();
        Validate(Data, capacity);
        RefreshVisual();
        item.OnAct += Act;
    }

    /// <summary>写入液体身份、数量与加工进度。</summary>
    public override void Save() => ModData.WriteData(Data);

    /// <summary>解除池化前的动作和视图订阅。</summary>
    public override void Unload()
    {
        if (item != null) item.OnAct -= Act;
        ReleaseTargetOutline();
        Changed = null;
    }

    /// <summary>准心目标属于连续变化的表现状态，直接按帧刷新而不启用 Module Tick。</summary>
    private void LateUpdate()
    {
        if (item?.Owner is not Player ownerPlayer || !ownerPlayer.IsLocalProfile)
        {
            targetOutline?.Hide();
            return;
        }

        if (!TryResolveCurrentWorldLiquidTarget(item?.Owner, out WorldLiquidSourceTarget target))
        {
            targetOutline?.Hide();
            return;
        }

        targetOutline ??= WorldTileTargetOutline.Create("Liquid Tile Target Outline");
        targetOutline.Show(target.WorldCell);
    }

    private void OnDisable() => ReleaseTargetOutline();

    private void OnDestroy() => ReleaseTargetOutline();

    /// <summary>拒绝非法容器状态；非空容器引用的液体必须已经存在于最终液体目录。</summary>
    public static void Validate(LiquidContainerState state, int containerCapacity)
    {
        if (state == null)
            throw new InvalidOperationException("液体容器状态为空。");
        if (containerCapacity < 1 || state.Amount < 0 || state.Amount > containerCapacity ||
            (state.Amount == 0) != string.IsNullOrWhiteSpace(state.LiquidId) ||
            float.IsNaN(state.ProcessingSeconds) || float.IsInfinity(state.ProcessingSeconds) || state.ProcessingSeconds < 0f)
        {
            throw new InvalidOperationException("液体容器容量、液体 ID、数量或加工状态无效。");
        }

        if (state.Amount > 0 && ResolveLiquidDefinition(state.LiquidId, false) == null)
            throw new InvalidOperationException($"液体容器引用了未注册液体：{state.LiquidId}");
    }

    #endregion

    #region 玩家操作

    /// <summary>对准可提取液体地块时装液，否则打开通用液体容器面板。</summary>
    public override void Act()
    {
        if (!GameNetwork.HasStateAuthority || !item.InHand || item.Owner == null)
            return;
        if (item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.TryHandlePlacementAction() == true)
            return;

        Item actor = item.Owner;
        if (!CanOperate(actor)) return;
        if (TryResolveCurrentWorldLiquidTarget(actor, out WorldLiquidSourceTarget target))
        {
            if (Data.Amount >= Capacity)
            {
                ItemActionFeedback.Show(actor, "容器已经装满了。");
            }
            else if (Data.Amount > 0 && !SameLiquid(Data.LiquidId, target.Liquid.Id))
            {
                ItemActionFeedback.Show(actor, "不同液体不能直接混装，请先倒空容器。");
            }
            else
            {
                int moved = AddLiquid(target.Liquid.Id, Capacity - Data.Amount);
                if (moved > 0)
                    ItemActionFeedback.Show(actor, $"已装入{target.Liquid.DisplayName}。");
            }
            return;
        }

        OpenRequested?.Invoke(this, actor);
    }

    /// <summary>世界中的液体容器通过交互打开同一面板。</summary>
    public void OnInteractStart(Item actor) { if (CanOperate(actor)) OpenRequested?.Invoke(this, actor); }

    /// <summary>交互取消不改变容器内容。</summary>
    public void OnInteractCancel(Item actor) { }

    /// <summary>所有面板动作都重新验证权威、归属与距离。</summary>
    public bool CanOperate(Item actor)
    {
        if (!GameNetwork.HasStateAuthority || actor == null || actor.DestructionHandled || item == null || item.DestructionHandled ||
            !(actor.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp)?.Hp > 0f))
            return false;
        return item.InHand ? item.Owner == actor :
            item.Owner == null && WorldTopologyRuntime.ShortestDelta(actor.transform.position, item.transform.position).sqrMagnitude <= reach * reach;
    }

    /// <summary>只有液体定义声明为可饮用时才消耗一份；恢复量由液体而不是容器决定。</summary>
    public bool Drink(Item actor)
    {
        LiquidDefinition liquid = CurrentLiquid;
        if (!CanOperate(actor) || liquid == null || !liquid.Drinkable || liquid.HydrationPerServing <= 0f || Data.Amount < 1)
            return false;

        Mod_Food food = actor.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);
        if (food == null || food.DrinkWater(liquid.HydrationPerServing, item) <= 0f)
            return false;

        RemoveLiquidInternal(1);
        Commit();
        return true;
    }

    /// <summary>玩家明确倒空当前容器。</summary>
    public void Empty(Item actor)
    {
        if (!CanOperate(actor)) return;
        ClearContentsInternal();
        Commit();
    }

    /// <summary>向另一只通用液体容器部分转移；不同液体禁止自动混合。</summary>
    public bool TransferTo(Mod_WaterVessel target, Item actor)
    {
        if (target == this || target == null || !CanOperate(actor) || !target.CanOperate(actor) || Data.Amount == 0 ||
            (target.Data.Amount > 0 && !SameLiquid(target.Data.LiquidId, Data.LiquidId)))
            return false;

        int moved = Math.Min(Data.Amount, target.Capacity - target.Data.Amount);
        if (moved <= 0) return false;
        target.AddLiquidInternal(Data.LiquidId, moved);
        RemoveLiquidInternal(moved);
        Commit();
        target.Commit();
        return true;
    }

    #endregion

    #region 通用液体 API

    /// <summary>玩法和 MOD 可向容器加入任意已注册液体；返回实际加入份数。</summary>
    public int AddLiquid(string liquidId, int amount)
    {
        if (!GameNetwork.HasStateAuthority || amount <= 0)
            return 0;
        ResolveLiquidDefinition(liquidId, true);
        if (Data.Amount > 0 && !SameLiquid(Data.LiquidId, liquidId))
            return 0;

        int moved = Math.Min(amount, Capacity - Data.Amount);
        if (moved <= 0)
            return 0;
        AddLiquidInternal(liquidId, moved);
        Commit();
        return moved;
    }

    /// <summary>玩法和 MOD 从容器移除液体；返回实际移除份数。</summary>
    public int RemoveLiquid(int amount)
    {
        if (!GameNetwork.HasStateAuthority || amount <= 0 || Data.Amount <= 0)
            return 0;
        int removed = Math.Min(amount, Data.Amount);
        RemoveLiquidInternal(removed);
        Commit();
        return removed;
    }

    /// <summary>玩法和 MOD 清空容器，不生成额外物品。</summary>
    public bool ClearContents()
    {
        if (!GameNetwork.HasStateAuthority || Data.Amount <= 0)
            return false;
        ClearContentsInternal();
        Commit();
        return true;
    }

    /// <summary>库存中的模块没有运行时组件时读取容器状态，供炉体等系统处理。</summary>
    public static bool TryRead(ItemData itemData, out Ex_ModData_MemoryPackable storage, out LiquidContainerState state)
    {
        storage = null;
        state = null;
        if (itemData?.ModuleDataDic == null) return false;
        foreach (var pair in itemData.ModuleDataDic)
        {
            if (pair.Value.ID != ModuleId || pair.Value is not Ex_ModData_MemoryPackable data)
                continue;

            storage = data;
            state = new LiquidContainerState();
            data.ReadData(ref state);
            state ??= new LiquidContainerState();
            Validate(state, ResolveConfiguredCapacity(itemData, pair.Key));
            return true;
        }
        return false;
    }

    /// <summary>
    /// 根据容器快照与液体定义解析库存/快捷栏图标。液体可声明专用 visualState；
    /// 容器没有该状态图时统一回退到 filled，因此新增 MOD 液体不需要修改 UI 代码。
    /// </summary>
    public static bool TryResolvePresentationSprite(ItemData itemData, out Sprite sprite)
    {
        sprite = null;
        if (itemData == null || GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(itemData.IDName, out RuntimeItemDefinition definition) ||
            !TryRead(itemData, out _, out LiquidContainerState state))
        {
            return false;
        }

        string stateName = state.Amount == 0
            ? "empty"
            : ResolveLiquidDefinition(state.LiquidId, false)?.VisualState;

        if (!string.IsNullOrWhiteSpace(stateName) &&
            definition.TryGetVisualStateSprite(stateName, out sprite))
        {
            return true;
        }

        if (state.Amount > 0 && definition.TryGetVisualStateSprite("filled", out sprite))
            return true;

        sprite = definition.Sprite;
        return sprite != null;
    }

    /// <summary>库存中的模块没有运行时组件，容量从正式物品定义的模块参数读取。</summary>
    private static int ResolveConfiguredCapacity(ItemData itemData, string stableModuleName)
    {
        if (GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(itemData.IDName, out RuntimeItemDefinition definition) ||
            !definition.TryGetModuleParameters(stableModuleName, out string json) ||
            string.IsNullOrWhiteSpace(json))
            return DefaultCapacity;

        JObject parameters = JObject.Parse(json);
        return parameters.Value<int?>(nameof(capacity)) ?? DefaultCapacity;
    }

    private static LiquidDefinition ResolveLiquidDefinition(string liquidId, bool throwOnMissing)
    {
        LiquidDefinition definition = GameRes.ExistingInstance?.GetLiquidDefinition(liquidId);
        if (definition == null && throwOnMissing)
            throw new InvalidOperationException($"液体定义不存在：{liquidId}");
        return definition;
    }

    private static bool SameLiquid(string left, string right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private void AddLiquidInternal(string liquidId, int amount)
    {
        if (Data.Amount == 0)
            Data.LiquidId = liquidId.Trim();
        Data.Amount += amount;
        Data.ProcessingSeconds = 0f;
    }

    private void RemoveLiquidInternal(int amount)
    {
        Data.Amount -= amount;
        Data.ProcessingSeconds = 0f;
        if (Data.Amount <= 0)
        {
            Data.Amount = 0;
            Data.LiquidId = null;
        }
    }

    private void ClearContentsInternal()
    {
        Data.LiquidId = null;
        Data.Amount = 0;
        Data.ProcessingSeconds = 0f;
    }

    /// <summary>状态提交后刷新持久化数据、网络状态和表现订阅。</summary>
    private void Commit()
    {
        Validate(Data, capacity);
        Save();
        RefreshVisual();
        RefreshContainingInventoryPresentation();
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        Changed?.Invoke();
    }

    /// <summary>模块数据原地变化时通知真实所属库存，否则快捷栏仍会保留变更前的图标。</summary>
    private void RefreshContainingInventoryPresentation()
    {
        if (item?.Owner == null || item.itemData == null ||
            !InventoryContextResolver.TryResolveContainingInventory(item.Owner, item.itemData, out Inventory inventory))
        {
            return;
        }

        inventory.Data?.NotifyItemStateChanged(item.itemData);
    }

    /// <summary>同一容器物品按液体定义选择状态 Sprite；未知专用状态时回退到 filled，再回退外壳默认图。</summary>
    private void RefreshVisual()
    {
        if (item?.Sprite == null || item.itemData == null)
            return;

        if (TryResolvePresentationSprite(item.itemData, out Sprite sprite))
            item.Sprite.sprite = sprite;
    }

    /// <summary>高亮与实际装液共用这一目标解析入口，保证显示格与操作格严格一致。</summary>
    private bool TryResolveCurrentWorldLiquidTarget(Item actor, out WorldLiquidSourceTarget target)
    {
        target = default;
        if (actor == null || item == null || !item.InHand || item.Owner != actor || !CanOperate(actor))
            return false;

        Mod_Building building = item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building);
        if (building != null && building.IsPlacementModeActive)
            return false;

        GameController controller = actor.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        if (controller == null || controller.IsGameplayInputLocked ||
            (!controller.IsUsingMobile && controller.IsPointerOverUI()))
        {
            return false;
        }

        if (!WorldLiquidSourceResolver.TryResolve(controller.GetMouseWorldPosition(), out target))
            return false;

        return FarmlandSystem.IsWithinReach(actor.transform.position, target.WorldCell, reach);
    }

    private void ReleaseTargetOutline()
    {
        if (targetOutline == null)
            return;

        Destroy(targetOutline.gameObject);
        targetOutline = null;
    }

    #endregion
}
