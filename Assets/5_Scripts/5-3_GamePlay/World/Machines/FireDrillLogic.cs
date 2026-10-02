using System;
using System.Runtime.CompilerServices;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

[Serializable, MemoryPackable]
public partial class FireDrillRuntimeState
{
    #region 取火状态
    public Inventory_Data Input;
    public Inventory_Data Output;
    #endregion
}

/// <summary>钻木器只提供摩擦热量，材料自身的物质状态决定产物。</summary>
public class FireDrillLogic : MachineLogic
{
    #region 摩擦取火
    public const string ModuleId = "钻木取火模块";
    public MaterialHeatingProcessor Heater { get; }
    public FireDrillRuntimeState State { get; }
    public override GameObject PanelPrefab { get; }
    public override float TickInterval => .1f;
    public override float Progress01 => Heater.Progress01;
    public override string ActionLabel => "摩擦";
    public override string Status => Heater.Status;
    public override bool CanAct => Heater.CanRub;

    public FireDrillLogic(MachineEntity entity) : base(entity)
    {
        var config = entity.Definition.Content.Find<Mod_FireDrill>() ?? throw new InvalidOperationException("取火器缺少配置。");
        var source = (Mod_FireDrill)config.Authoring;
        State = MachineModuleState.Read<FireDrillRuntimeState>(entity.Snapshot, ModuleId) ?? new FireDrillRuntimeState();
        Inventory input = Track(MachineInventory.Create(source.InputInventory, State.Input, "输入", 1));
        Inventory output = Track(MachineInventory.Create(source.OutputInventory, State.Output, "输出", 1));
        State.Input = input.Data;
        State.Output = output.Data;
        PanelPrefab = source.UI_Prefab != null ? source.UI_Prefab : GameRes.Instance.GetPrefab("UI_FireDrill");
        Heater = new MaterialHeatingProcessor(input, output)
        {
            SourceTemperature = config.Value("HeatSourceTemperature", source.HeatSourceTemperature),
            TemperaturePerClick = config.Value("TemperaturePerClick", source.TemperaturePerClick)
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool Rub(Player actor)
    {
        if (!GameNetwork.HasStateAuthority || !Heater.CanRub) return false;
        bool completed = Heater.Rub(GetAmbientTemperature(), actor);
        NotifyChanged();
        return completed;
    }

    public override bool Execute(string operation, string argument, Player actor)
    {
        if (operation == "begin-interaction") return true;
        if (operation != "work") return false;
        Rub(actor); return true;
    }

    public override void Tick(float seconds)
    {
        if (Heater.Tick(GetAmbientTemperature(), seconds)) NotifyChanged();
    }

    private float GetAmbientTemperature()
    {
        TemperatureMgr.Instance.TryGetAmbientTemperature(Entity.Position, out float temperature);
        return temperature;
    }

    public override void Capture()
    {
        MachineModuleState.Write(Entity.Snapshot, ModuleId, State);
    }
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        var incoming = MachineModuleState.Read<FireDrillRuntimeState>(snapshot, ModuleId);
        if (incoming == null) return false;
        MachineInventory.ApplySnapshot(Heater.Input, incoming.Input);
        MachineInventory.ApplySnapshot(Heater.Output, incoming.Output);
        NotifyRemoteChanged();
        return true;
    }
    #endregion
}

/// <summary>手持与落地共用材料加热流程，不保存配方、点击进度或材料白名单。</summary>
public sealed class MaterialHeatingProcessor
{
    #region 材料热状态
    public Inventory Input { get; }
    public Inventory Output { get; }
    public float SourceTemperature = 150f;
    public float TemperaturePerClick = 10f;
    private Player lastActor;
    private bool isCommitting;
    private ItemData Material => Input.Data.GetItemSlot(0)?.itemData;
    public bool CanRub => !isCommitting && Material?.Stack?.Amount > 0f &&
        !MachineInventory.IsBeingDragged(Input) && !MachineInventory.IsBeingDragged(Output);

    public MaterialHeatingProcessor(Inventory input, Inventory output)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public float Progress01
    {
        get
        {
            ItemData material = Material;
            if (material?.MatterState?.Initialized != true) return 0f;
            float target = ItemMatterRuntime.GetHeatingTransition(material)?.MinTemperature ?? SourceTemperature;
            return Mathf.InverseLerp(TemperatureMgr.DefaultAmbientTemperature, target, material.MatterState.TemperatureCelsius);
        }
    }

    public string Status
    {
        get
        {
            ItemData material = Material;
            if (material == null) return $"放入材料，点击摩擦升温 · 供热上限 {SourceTemperature:0}°C";
            string current = material.MatterState?.Initialized == true
                ? $"{material.MatterState.TemperatureCelsius:0.#}°C" : "待测温";
            RuntimeItemMatterTransition transition = ItemMatterRuntime.GetHeatingTransition(material);
            string target = transition == null ? "" : $" / 转化 {transition.MinTemperature:0}°C";
            return $"材料 {current}{target} · 供热上限 {SourceTemperature:0}°C";
        }
    }

    public ItemData PreviewOutput()
    {
        ItemData material = Material;
        RuntimeItemMatterTransition transition = ItemMatterRuntime.GetMatchedTransition(material, false)
            ?? ItemMatterRuntime.GetHeatingTransition(material);
        if (material?.Stack == null || transition == null) return null;
        ItemData preview = GameRes.Instance.CreateItemData(transition.OutputItemId);
        preview.Stack.Amount = Mathf.RoundToInt(material.Stack.Amount * transition.OutputAmountMultiplier);
        return preview;
    }
    #endregion

    #region 供热与物质转化
    public bool Rub(float ambientTemperature, Player actor)
    {
        if (!CanRub) return false;
        lastActor = actor;
        ItemData material = Material;
        if (ItemMatterRuntime.AddHeat(material, ambientTemperature, SourceTemperature, TemperaturePerClick))
            Input.Data.NotifyItemStateChanged(material);
        return TryTransform();
    }

    public bool Tick(float ambientTemperature, float seconds)
    {
        if (seconds <= 0f || isCommitting) return false;
        bool changed = TryTransform();
        changed |= AdvanceInventory(Input, ambientTemperature, seconds);
        changed |= AdvanceInventory(Output, ambientTemperature, seconds);
        return TryTransform() || changed;
    }

    private static bool AdvanceInventory(Inventory inventory, float ambientTemperature, float seconds)
    {
        bool changed = false;
        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            ItemData data = inventory.Data.itemSlots[i]?.itemData;
            if (data == null || inventory.IsSlotBeingDragged(i)) continue;
            if (!ItemMatterRuntime.Advance(data, ambientTemperature, 1f, seconds)) continue;
            inventory.Data.NotifyItemStateChanged(data);
            changed = true;
        }
        return changed;
    }

    private bool TryTransform()
    {
        if (isCommitting || ItemMatterRuntime.GetMatchedTransition(Material, false) == null) return false;
        isCommitting = true;
        try
        {
            CraftingResult result = ItemMatterRuntime.TransformSolidSlot(Input, Output, 0, "matter.heating");
            if (!result.Success) return false;
            foreach (ItemData product in result.Outputs)
                if (product.Tags?.Contains(Tag.CombustionTinder) == true)
                    GameplayProgressEvents.PublishFireSeedCreated(lastActor, product.IDName, product.Stack.Amount);
            return true;
        }
        finally { isCommitting = false; }
    }
    #endregion
}
