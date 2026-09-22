using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

/// <summary>
/// 手钻独立运行态：加工进度与钻头材质耐久一起保存。
/// 该状态属于共享“手钻模块”，因此便携载体与落地建筑互转时不会丢失制作质量。
/// </summary>
[Serializable, MemoryPackable]
public partial class HandDrillRuntimeState
{
    public const int CurrentVersion = 1;
    public int Version = CurrentVersion;
    public MechanicalProcessingState Processing = new();
    public string BitMaterialId = string.Empty;
    public float Durability;
    public float MaxDurability;
}

/// <summary>便携手钻：每次钻孔动作增加一秒工作量，默认四次完成；输入、输出和进度通过共享模块随放置、拆回与存档迁移。</summary>
public sealed class Mod_HandDrill : Module, IInteractable
{
    #region 配置与状态
    public const string ModuleId = "手钻模块";
    public const string MetalRecipeId = "core:手钻_金属锭";
    public const string DrillDurabilityTagPrefix = "DrillDurability:";
    public Ex_ModData_MemoryPackable Data = new() { ID = ModuleId };
    public float WorkPerClick = 1f; // 每次操作的工作量，秒
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public MechanicalProcessor Processor { get; private set; }
    private HandDrillRuntimeState runtimeState;
    private MechanicalPanelSession panel;
    #endregion

    #region 生命周期与交互
    public override void Load()
    {
        runtimeState = ReadRuntimeState(Data, item?.itemData);
        ApplyRuntimeDurability(item?.itemData, runtimeState);
        Processor = new MechanicalProcessor("hand_drill", runtimeState.Processing);
        item.OnAct += OnItemAct;
    }
    public override void Save()
    {
        if (Processor == null || runtimeState == null)
            return;

        runtimeState.Processing = Processor.State;
        if (item?.itemData != null)
        {
            runtimeState.Durability = Mathf.Clamp(item.itemData.Durability, 0f, item.itemData.MaxDurability);
            runtimeState.MaxDurability = Mathf.Max(1f, item.itemData.MaxDurability);
        }
        Data.WriteData(runtimeState);
    }
    public override void Unload()
    {
        if (item != null) item.OnAct -= OnItemAct;
        panel?.Dispose(); panel = null;
        Processor?.Dispose(); Processor = null;
        runtimeState = null;
    }
    private void OnItemAct()
    {
        if (item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.TryHandlePlacementAction() == true) return;
        if (item.Owner != null) OnInteractStart(item.Owner);
    }
    public void OnInteractStart(Item actor)
    {
        if (!GameNetwork.HasStateAuthority) return;
        panel ??= new MechanicalPanelSession("UI_HandDrill", item, Processor, Drill, GetStatus, GetActionLabel);
        panel.Toggle(actor);
    }
    public void OnInteractCancel(Item actor) => panel?.Close();
    public bool TryDrill(Player actor = null)
    {
        if (!GameNetwork.HasStateAuthority || Processor == null || item?.itemData == null || item.itemData.Durability <= 0f)
            return false;
        bool result = Processor.Advance(WorkPerClick, actor);
        if (result)
            item.DecreaseDurability(1);
        Save();
        return result;
    }
    private void Drill(Player actor) { TryDrill(actor); panel?.Refresh(); }
    private string GetActionLabel() => "钻孔";
    /// <summary>旧占位手钻没有建筑角色，统一转入便携载体；正式落地本体保留 HandDrill 身份。</summary>
    public static string ResolveCarrierDefinition(ItemData data)
        => data.IDName == "HandDrill" && !Mod_Building.TryReadBuildingData(data, out _, out _)
            ? "HandDrill_Summoner" : data.IDName.Trim();
    private string GetStatus() => FlatWorldLocalizationService.GetUiFormat("加工进度 {0:0}%", (Processor?.Progress01 ?? 0) * 100);
    #endregion

    #region 钻头材质质量
    /// <summary>启动时注册手钻产物规则；矿锭只需增加 DrillDurability 标签即可扩展，MOD 无需改核心配方。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterCraftingOutputRule()
    {
        CraftingOutputRules.Register("hand-drill-material-durability", new HandDrillMaterialDurabilityRule());
    }

    /// <summary>金属手钻继承实际消耗矿锭声明的耐久上限。</summary>
    private sealed class HandDrillMaterialDurabilityRule : ICraftingOutputRule
    {
        public bool Prepare(Inventory input, CraftingRecipeMatch match, IReadOnlyList<ItemData> outputs, out string error)
        {
            error = null;
            if (match?.Recipe == null || !string.Equals(match.Recipe.Id, MetalRecipeId, StringComparison.Ordinal))
                return true;

            if (!TryResolveMaterialDurability(input, match, out string materialId, out float durability))
            {
                error = $"配方 {MetalRecipeId} 使用的矿锭没有声明 {DrillDurabilityTagPrefix}<数值> 标签";
                return false;
            }

            for (int i = 0; i < outputs.Count; i++)
            {
                ItemData output = outputs[i];
                if (output == null || !string.Equals(output.IDName, "HandDrill_Summoner", StringComparison.Ordinal))
                    continue;
                if (!ApplyCraftedDurability(output, materialId, durability, out error))
                    return false;
            }
            return true;
        }

        private static bool TryResolveMaterialDurability(
            Inventory input,
            CraftingRecipeMatch match,
            out string materialId,
            out float durability)
        {
            materialId = string.Empty;
            durability = 0f;
            if (input?.Data?.itemSlots == null)
                return false;

            foreach (CraftingConsumption consumption in match.Consumptions)
            {
                if (consumption.SlotIndex < 0 || consumption.SlotIndex >= input.Data.itemSlots.Count)
                    continue;
                ItemData material = input.Data.itemSlots[consumption.SlotIndex]?.itemData;
                if (material?.Tags == null)
                    continue;

                foreach (string tag in material.Tags)
                {
                    if (string.IsNullOrWhiteSpace(tag) ||
                        !tag.StartsWith(DrillDurabilityTagPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string value = tag.Substring(DrillDurabilityTagPrefix.Length);
                    if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out durability) ||
                        durability <= 0f)
                    {
                        return false;
                    }

                    materialId = material.IDName?.Trim() ?? string.Empty;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>制作时把实例耐久同时写入共享模块，确保放置、拆回和存档重建后仍保留钻头质量。</summary>
    private static bool ApplyCraftedDurability(
        ItemData output,
        string materialId,
        float durability,
        out string error)
    {
        error = null;
        if (output == null || durability <= 0f)
        {
            error = "手钻产物或钻头耐久无效";
            return false;
        }

        if (!TryGetRuntimeModuleData(output, out Ex_ModData_MemoryPackable moduleData))
        {
            error = $"手钻产物 {output.IDName} 缺少 {ModuleId}";
            return false;
        }

        HandDrillRuntimeState state = ReadRuntimeState(moduleData, output);
        state.BitMaterialId = materialId ?? string.Empty;
        state.Durability = durability;
        state.MaxDurability = durability;
        output.Durability = durability;
        output.MaxDurability = durability;
        moduleData.WriteData(state);
        return true;
    }

    /// <summary>ItemDefinition 重建静态字段后，从共享模块恢复手钻实例自己的材质耐久。</summary>
    public static void RestoreRuntimeDurability(ItemData itemData)
    {
        if (!TryGetRuntimeModuleData(itemData, out Ex_ModData_MemoryPackable moduleData))
            return;

        HandDrillRuntimeState state = ReadRuntimeState(moduleData, itemData);
        ApplyRuntimeDurability(itemData, state);
    }

    /// <summary>读取当前运行态，并兼容旧版只序列化 MechanicalProcessingState 的载荷。</summary>
    private static HandDrillRuntimeState ReadRuntimeState(
        Ex_ModData_MemoryPackable moduleData,
        ItemData itemData)
    {
        if (moduleData?.BitData != null && moduleData.BitData.Length > 0)
        {
            try
            {
                HandDrillRuntimeState current = moduleData.GetData<HandDrillRuntimeState>();
                if (current?.Version == HandDrillRuntimeState.CurrentVersion && current.Processing != null)
                    return SanitizeRuntimeState(current, itemData);
            }
            catch
            {
                // 旧版手钻载荷不是该结构，继续按旧 MechanicalProcessingState 尝试迁移。
            }

            try
            {
                MechanicalProcessingState legacy = moduleData.GetData<MechanicalProcessingState>();
                HandDrillRuntimeState migrated = CreateDefaultRuntimeState(itemData);
                migrated.Processing = legacy ?? new MechanicalProcessingState();
                return migrated;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("手钻模块状态无法按当前或旧格式读取。", exception);
            }
        }

        return CreateDefaultRuntimeState(itemData);
    }

    private static HandDrillRuntimeState CreateDefaultRuntimeState(ItemData itemData)
    {
        float maxDurability = Mathf.Max(1f, itemData?.MaxDurability ?? 1f);
        float durability = Mathf.Clamp(itemData?.Durability ?? maxDurability, 0f, maxDurability);
        return new HandDrillRuntimeState
        {
            Durability = durability,
            MaxDurability = maxDurability
        };
    }

    private static HandDrillRuntimeState SanitizeRuntimeState(HandDrillRuntimeState state, ItemData itemData)
    {
        state.Processing ??= new MechanicalProcessingState();
        if (state.MaxDurability <= 0f)
        {
            float maxDurability = Mathf.Max(1f, itemData?.MaxDurability ?? 1f);
            state.MaxDurability = maxDurability;
            state.Durability = Mathf.Clamp(itemData?.Durability ?? maxDurability, 0f, maxDurability);
        }
        else
        {
            state.Durability = Mathf.Clamp(state.Durability, 0f, state.MaxDurability);
        }

        return state;
    }

    private static bool TryGetRuntimeModuleData(ItemData itemData, out Ex_ModData_MemoryPackable result)
    {
        result = null;
        if (itemData?.ModuleDataDic == null)
            return false;

        foreach (ModuleData candidate in itemData.ModuleDataDic.Values)
        {
            if (candidate is not Ex_ModData_MemoryPackable binary ||
                !string.Equals(candidate.ID, ModuleId, StringComparison.Ordinal))
            {
                continue;
            }

            if (result != null)
                throw new InvalidOperationException($"物品 {itemData.IDName} 含有重复的 {ModuleId}。");
            result = binary;
        }

        return result != null;
    }

    private static void ApplyRuntimeDurability(ItemData itemData, HandDrillRuntimeState state)
    {
        if (itemData == null || state == null)
            return;

        itemData.MaxDurability = Mathf.Max(1f, state.MaxDurability);
        itemData.Durability = Mathf.Clamp(state.Durability, 0f, itemData.MaxDurability);
    }

    #endregion
}
