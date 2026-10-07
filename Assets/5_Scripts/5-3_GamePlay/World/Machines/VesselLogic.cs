using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using Newtonsoft.Json;
using System.Globalization;
using UnityEngine;

/// <summary>陶罐与木桶是同一种容器领域；液体和可选固体库存随机器保存，手持继续复用共享模块载荷。</summary>
public class VesselLogic : MachineLogic, ILiquidVessel, IVesselContents
{
    #region 容器状态
    public LiquidContainerState Data { get; private set; }
    public int Capacity { get; }
    public float Reach { get; }
    public LiquidDefinition CurrentLiquid => GameRes.ExistingInstance?.GetLiquidDefinition(Data.LiquidId);
    public ItemData ItemData => Entity.Snapshot;
    public Item Item => null;
    public MachineEntity Machine => Entity;
    public IVesselContents ContentsSource => Contents == null ? null : this;
    public Inventory Contents { get; }
    public override GameObject PanelPrefab => GameRes.Instance.GetPrefab("UI_WaterVessel");
    private readonly VesselContentsState contentsState;
    private float reactionElapsed;
    private bool reactionDirty = true;
    private bool committing;
    private float nextRemotePour;

    public VesselLogic(MachineEntity entity) : base(entity)
    {
        var config = entity.Definition.Content.Find<Mod_WaterVessel>() ?? throw new InvalidOperationException("容器缺少液体配置。");
        var source = (Mod_WaterVessel)config.Authoring;
        Capacity = config.Value("capacity", source.capacity);
        Reach = config.Value("reach", source.reach);
        Data = MachineModuleState.Read<LiquidContainerState>(entity.Snapshot, Mod_WaterVessel.ModuleId) ?? new LiquidContainerState();
        Mod_WaterVessel.NormalizeStoredAmount(Data);
        Mod_WaterVessel.Validate(Data, Capacity);
        if (entity.Definition.Content.Has<Mod_VesselContents>())
        {
            Inventory_Data contentsData = MachineInventory.NewData("木桶内物品", 6);
            contentsState = MachineModuleState.Read<VesselContentsState>(entity.Snapshot, Mod_VesselContents.ModuleId)
                ?? new VesselContentsState { Items = InventoryInstanceSnapshot.Capture(contentsData) };
            Contents = new Mod_VesselContents.VesselInventory { Data = contentsData };
            RestoreContents(contentsState.Items);
            Contents.Data.SetUnlimitedSlots(false);
            Contents.InitData();
            Track(Contents);
        }
    }

    public bool CanOperate(Item actor)
        => MachineWorld.Contains(Entity) && actor != null && !actor.DestructionHandled &&
           actor.gameObject.scene.name == MachineWorld.WorldKey &&
           actor.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp > 0f &&
           WorldTopologyRuntime.ShortestDelta(actor.transform.position, Entity.Position).sqrMagnitude <= Reach * Reach;

    public bool Drink(Item actor) => GameNetwork.HasStateAuthority ? LiquidVesselOperations.Drink(this, actor)
        : CanOperate(actor) && MachineWorld.RequestOperation(Entity, "vessel.drink", "", actor as Player);
    public float PourToGround(Item actor, float amount)
    {
        if (GameNetwork.HasStateAuthority) return LiquidVesselOperations.PourToGround(this, actor, amount);
        if (!CanOperate(actor) || !MachineDefinition.Positive(amount) || Time.unscaledTime < nextRemotePour) return 0f;
        nextRemotePour = Time.unscaledTime + .1f;
        float value = Mathf.Min(amount, Data.Amount);
        return MachineWorld.RequestOperation(Entity, "vessel.pour", value.ToString(CultureInfo.InvariantCulture), actor as Player) ? value : 0f;
    }
    public bool TransferFromInventoryItem(ItemData source, Item actor, float maximumItemAmount = float.PositiveInfinity)
    {
        if (GameNetwork.HasStateAuthority) return LiquidVesselOperations.TransferFromInventory(this, source, actor, maximumItemAmount);
        if (!CanOperate(actor) || actor is not Player player || source == null || float.IsNaN(maximumItemAmount) || maximumItemAmount <= 0f ||
            !InventoryContextResolver.TryResolveContainingInventory(actor, source, out Inventory inventory) ||
            !MachineInventoryCommands.TryAddress(player, inventory, out var address)) return false;
        int slot = inventory.Data.itemSlots.FindIndex(value => ReferenceEquals(value?.itemData, source));
        if (slot < 0) return false;
        return MachineWorld.RequestOperation(Entity, "vessel.fill", JsonConvert.SerializeObject(new VesselFillRequest
        {
            Source = address, Slot = slot, Guid = source.Guid, ItemId = source.IDName,
            Maximum = Mathf.Min(1000000000f, maximumItemAmount)
        }), player);
    }
    public float AddLiquid(string id, float amount) => LiquidVesselOperations.Add(this, id, amount);
    public float RemoveLiquid(float amount) => LiquidVesselOperations.Remove(this, amount);

    public override bool Execute(string operation, string argument, Player actor)
    {
        if (!GameNetwork.HasStateAuthority || !CanOperate(actor)) return false;
        if (operation == "vessel.drink") return Drink(actor);
        if (operation == "vessel.pour") return float.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out float amount) &&
            MachineDefinition.Positive(amount) && PourToGround(actor, Mathf.Min(amount, Capacity)) > 0f;
        if (operation != "vessel.fill") return false;
        VesselFillRequest request = JsonConvert.DeserializeObject<VesselFillRequest>(argument);
        if (request == null || !MachineDefinition.Positive(request.Maximum) || request.Maximum > 1000000000f ||
            request.ItemId == null || request.ItemId.Length > 128 || (request.Source.PlayerInventory?.Length ?? 0) > 128) return false;
        if (request.Source.MachineId != 0)
        {
            var sourceMachine = MachineWorld.GetById(request.Source.MachineId);
            float range = actor.GetComponentInChildren<Mod_InteractSender>()?.maxInteractDistance ?? Mod_InteractSender.DefaultMaxInteractDistance;
            if (sourceMachine == null || WorldTopologyRuntime.Distance(actor.transform.position, sourceMachine.Position) > range) return false;
        }
        Inventory inventory = MachineInventoryCommands.Resolve(actor, request.Source);
        ItemData source = inventory?.Data?.itemSlots != null && (uint)request.Slot < (uint)inventory.Data.itemSlots.Count
            ? inventory.Data.itemSlots[request.Slot].itemData : null;
        return source != null && source.Guid == request.Guid && source.IDName == request.ItemId &&
            LiquidVesselOperations.TransferFromInventory(this, source, actor, request.Maximum);
    }

    public void CommitVessel()
    {
        Mod_WaterVessel.NormalizeStoredAmount(Data);
        Mod_WaterVessel.Validate(Data, Capacity);
        Capture();
        reactionDirty = true;
        NotifyChanged(true);
    }

    public override void Capture()
    {
        MachineModuleState.Write(Entity.Snapshot, Mod_WaterVessel.ModuleId, Data);
        if (contentsState != null)
        {
            contentsState.Items = InventoryInstanceSnapshot.Capture(Contents.Data);
            MachineModuleState.Write(Entity.Snapshot, Mod_VesselContents.ModuleId, contentsState);
        }
    }

    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        Data = MachineModuleState.Read<LiquidContainerState>(snapshot, Mod_WaterVessel.ModuleId)
            ?? throw new InvalidOperationException("服务器容器状态缺失。");
        if (Contents != null)
        {
            var incoming = MachineModuleState.Read<VesselContentsState>(snapshot, Mod_VesselContents.ModuleId);
            RestoreContents(incoming?.Items);
            Contents.RefreshUI();
        }
        NotifyRemoteChanged();
        return true;
    }

    private void RestoreContents(InventoryInstanceSnapshot snapshot)
    {
        if (snapshot == null || snapshot.SlotCount != 6)
            throw new InvalidOperationException("木桶固体库存必须为六格。");
        // 机器保留自己的六格布局，网络与存档只合并物品实例状态。
        snapshot.RestoreTo(Contents.Data,
            data => data.SharedConfiguration == null
                ? ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, data) : data);
    }

    protected override void OnInventoryChanged(ItemSlot slot)
    {
        if (committing) return;
        reactionDirty = true;
        base.OnInventoryChanged(slot);
    }
    #endregion

    #region 容器投料反应
    public override void Tick(float seconds)
    {
        if (!reactionDirty || Contents == null) return;
        reactionElapsed += seconds;
        if (reactionElapsed < 1.1f || MachineInventory.IsBeingDragged(Contents)) return;
        reactionElapsed = 0f;
        reactionDirty = false;
        TryReact();
    }

    /// <summary>整份原料与液体身份在同一事务中提交，失败恢复库存与液体，不能只扣一半盐。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual bool TryReact()
    {
        if (!GameNetwork.HasStateAuthority || CurrentLiquid == null || Contents == null) return false;
        foreach (LiquidIngredientReaction reaction in CurrentLiquid.IngredientReactions)
        {
            if (reaction == null || reaction.Amount < 1 || Data.LiquidId == reaction.ResultLiquidId ||
                reaction.RequireFullContainer && Data.Amount < Capacity - Mod_WaterVessel.AmountEpsilon ||
                GameRes.Instance.GetLiquidDefinition(reaction.ResultLiquidId) == null) continue;
            var consumed = new List<CraftingConsumption>();
            int needed = reaction.Amount;
            for (int i = 0; i < Contents.Data.itemSlots.Count && needed > 0; i++)
            {
                ItemData item = Contents.Data.itemSlots[i].itemData;
                if (item?.IDName != reaction.ItemId) continue;
                int amount = Mathf.Min(needed, Mathf.FloorToInt(item.Stack.Amount));
                if (amount > 0) { consumed.Add(new CraftingConsumption(i, amount)); needed -= amount; }
            }
            if (needed > 0) continue;
            var recipe = new RuntimeRecipe { Id = "machine.vessel." + reaction.ResultLiquidId };
            var match = new CraftingRecipeMatch(recipe, false, consumed);
            string previous = Data.LiquidId;
            float previousProgress = Data.ProcessingSeconds;
            committing = true;
            CraftingResult result;
            try
            {
                result = CraftingService.ApplyInputEffect(Contents, match,
                    () => { Data.LiquidId = reaction.ResultLiquidId; Data.ProcessingSeconds = 0f; },
                    () => { Data.LiquidId = previous; Data.ProcessingSeconds = previousProgress; });
            }
            finally { committing = false; }
            if (!result.Success) return false;
            CommitVessel();
            return true;
        }
        return false;
    }
    #endregion
}

public sealed class VesselFillRequest
{
    public MachineInventoryAddress Source;
    public int Slot;
    public int Guid;
    public string ItemId;
    public float Maximum;
}
