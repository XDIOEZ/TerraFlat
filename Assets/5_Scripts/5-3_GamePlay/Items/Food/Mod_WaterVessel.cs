using System;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

/// <summary>水质独立于容器身份，海水不能通过烧开变成饮用水。</summary>
public enum VesselWaterQuality { Empty, Fresh, Drinkable, Sea }

/// <summary>一只陶罐的独立存档，默认 8 份水、每份对应 25 点饮水；处理中断保留进度。</summary>
[Serializable, MemoryPackable]
public partial class WaterVesselState
{
    public int Capacity = 8; // 最大份数。
    public int Amount; // 当前份数。
    public VesselWaterQuality Quality; // 当前水质。
    public float ProcessingSeconds; // 达温后的加工时间。
}

/// <summary>可装水、饮水、转移及倒空的复用容器；界面通过事件请求，玩法层不依赖 UI。</summary>
public sealed class Mod_WaterVessel : Module, IInteractable
{
    #region 数据与生命周期
    public const string ModuleId = "Mod_WaterVessel";
    public Ex_ModData_MemoryPackable ModData = new(); // 容器独立持久化载体。
    public WaterVesselState Data = new(); // 水量、水质与加工进度。
    public float reach = 2f; // 装水和转移距离。
    public float waterPerServing = 25f; // 每份恢复水分。
    public static event Action<Mod_WaterVessel, Item> OpenRequested; // 表现层打开容器。
    public event Action Changed; // 当前容器状态变化。
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ?? throw new ArgumentException("水容器数据类型错误。");
    }
    /// <summary>恢复容器状态并绑定统一使用入口。</summary>
    public override void Load()
    {
        ModData.ReadData(ref Data);
        Validate(Data);
        item.OnAct += Act;
    }
    /// <summary>写入水量与处理进度。</summary>
    public override void Save() => ModData.WriteData(Data);
    /// <summary>解除池化前的动作和视图订阅。</summary>
    public override void Unload()
    {
        if (item != null) item.OnAct -= Act;
        Changed = null;
    }
    /// <summary>拒绝非法容器状态，禁止从坏数据静默补水。</summary>
    public static void Validate(WaterVesselState state)
    {
        if (state.Capacity < 1 || state.Amount < 0 || state.Amount > state.Capacity ||
            !Enum.IsDefined(typeof(VesselWaterQuality), state.Quality) ||
            (state.Amount == 0) != (state.Quality == VesselWaterQuality.Empty) ||
            float.IsNaN(state.ProcessingSeconds) || float.IsInfinity(state.ProcessingSeconds) || state.ProcessingSeconds < 0f)
            throw new InvalidOperationException("陶罐水量、水质或加工状态无效。");
    }
    #endregion

    #region 操作
    /// <summary>对准水域使用时装水，否则打开该陶罐的操作面板。</summary>
    public override void Act()
    {
        if (!GameNetwork.HasStateAuthority || !item.InHand || item.Owner == null)
            return;
        Item actor = item.Owner;
        if (!CanOperate(actor)) return;
        GameController controller = actor.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        if (controller != null && ChunkMgr.ExistingInstance.TryGetRuntimeTileEffect(controller.GetMouseWorldPosition(),
            out RuntimeTerrainTileSample sample, out TileData tile, out _) && tile is TileData_Water water &&
            FarmlandSystem.IsWithinReach(actor.transform.position, sample.WorldCell, reach))
        {
            VesselWaterQuality quality = water.salt > 0.01f ? VesselWaterQuality.Sea : VesselWaterQuality.Fresh;
            if (Data.Amount > 0 && Data.Quality != quality)
                ItemActionFeedback.Show(actor, "不同水质不能混装，请先倒空陶罐。");
            else
            {
                Data.Quality = quality;
                Data.Amount = Data.Capacity;
                Data.ProcessingSeconds = 0f;
                Commit();
                ItemActionFeedback.Show(actor, quality == VesselWaterQuality.Sea ? "装好了海水，可以加热制盐。" : "装好了淡水，烧开后再喝。");
            }
            return;
        }
        OpenRequested?.Invoke(this, actor);
    }
    /// <summary>世界中的陶罐通过交互打开面板。</summary>
    public void OnInteractStart(Item actor) { if (CanOperate(actor)) OpenRequested?.Invoke(this, actor); }
    /// <summary>交互取消不改变罐内资源。</summary>
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
    /// <summary>只饮用处理好的水；水分已满时不消耗一份水。</summary>
    public bool Drink(Item actor)
    {
        if (!CanOperate(actor) || Data.Quality != VesselWaterQuality.Drinkable || Data.Amount < 1)
            return false;
        Mod_Food food = actor.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);
        if (food == null || food.DrinkWater(waterPerServing, item) <= 0f)
            return false;
        Data.Amount--;
        if (Data.Amount == 0) Data.Quality = VesselWaterQuality.Empty;
        Commit();
        return true;
    }
    /// <summary>明确倒空操作；不返还已处理水或加工进度。</summary>
    public void Empty(Item actor)
    {
        if (!CanOperate(actor)) return;
        Data.Amount = 0; Data.Quality = VesselWaterQuality.Empty; Data.ProcessingSeconds = 0f;
        Commit();
    }
    /// <summary>向另一只兼容陶罐部分转水；总份数守恒，加工进度随混装重置。</summary>
    public bool TransferTo(Mod_WaterVessel target, Item actor)
    {
        if (target == this || target == null || !CanOperate(actor) || !target.CanOperate(actor) || Data.Amount == 0 ||
            (target.Data.Amount > 0 && target.Data.Quality != Data.Quality))
            return false;
        int moved = Math.Min(Data.Amount, target.Data.Capacity - target.Data.Amount);
        if (moved <= 0) return false;
        target.Data.Quality = Data.Quality; target.Data.Amount += moved; target.Data.ProcessingSeconds = 0f;
        Data.Amount -= moved; Data.ProcessingSeconds = 0f;
        if (Data.Amount == 0) Data.Quality = VesselWaterQuality.Empty;
        Commit(); target.Commit();
        return true;
    }
    /// <summary>状态提交后刷新持久化数据和表现订阅。</summary>
    private void Commit()
    {
        Save();
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        Changed?.Invoke();
    }
    /// <summary>从库存快照读取陶罐，无需生成场景物品。</summary>
    public static bool TryRead(ItemData itemData, out Ex_ModData_MemoryPackable storage, out WaterVesselState state)
    {
        storage = null; state = null;
        if (itemData?.ModuleDataDic == null) return false;
        foreach (ModuleData module in itemData.ModuleDataDic.Values)
            if (module.ID == ModuleId && module is Ex_ModData_MemoryPackable data)
            {
                storage = data; state = new WaterVesselState(); data.ReadData(ref state); Validate(state); return true;
            }
        return false;
    }
    #endregion
}
