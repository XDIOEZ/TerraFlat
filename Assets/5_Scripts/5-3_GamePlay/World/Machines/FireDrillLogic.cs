using System;
using System.Runtime.CompilerServices;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Localization;
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
    public override string ActionLabel => FlatWorldLocalizationService.GetUiText("摩擦");
    public override string Status => Heater.Status;
    public override bool CanAct => Heater.CanRub && Entity.Snapshot.Durability > 0f;

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
        if (!GameNetwork.HasStateAuthority || !CanAct) return false;
        bool completed = Heater.Rub(GetAmbientTemperature(), actor, out bool worked);
        if (worked) Entity.Snapshot.Durability = Mathf.Max(0f, Entity.Snapshot.Durability - 1f);
        NotifyChanged();
        if (worked && Entity.Snapshot.Durability <= 0f && !MachineWorld.TryDestroyWornMachine(Entity, 0f, out string reason))
            Debug.LogError("[取火工具损毁] " + reason);
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
        return MachineWorld.SampleAmbientTemperature(Entity);
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
            if (material == null) return FlatWorldLocalizationService.GetUiFormat("放入材料，点击摩擦升温 · 供热上限 {0:0}°C", SourceTemperature);
            string current = material.MatterState?.Initialized == true
                ? $"{material.MatterState.TemperatureCelsius:0.#}°C" : FlatWorldLocalizationService.GetUiText("待测温");
            RuntimeItemMatterTransition transition = ItemMatterRuntime.GetHeatingTransition(material);
            string target = transition == null ? "" : FlatWorldLocalizationService.GetUiFormat(" / 转化 {0:0}°C", transition.MinTemperature);
            return FlatWorldLocalizationService.GetUiFormat("材料 {0}{1} · 供热上限 {2:0}°C", current, target, SourceTemperature);
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
    public bool Rub(float ambientTemperature, Player actor) => Rub(ambientTemperature, actor, out _);

    public bool Rub(float ambientTemperature, Player actor, out bool worked)
    {
        worked = false;
        if (!CanRub) return false;
        lastActor = actor;
        ItemData material = Material;
        worked = ItemMatterRuntime.AddHeat(material, ambientTemperature, SourceTemperature, TemperaturePerClick);
        if (worked)
            Input.Data.NotifyItemStateChanged(material);
        bool transformed = TryTransform();
        worked |= transformed;
        return transformed;
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
