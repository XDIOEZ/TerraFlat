using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>资源 Ready 前检查机械配方及落地设施定义，不创建实体、运行逻辑或面板。</summary>
public sealed class MachineResourceCatalogValidator : IIncrementalResourceCatalogValidator
{
    #region 目录校验
    public string Id => "mechanical";
    public void Validate(GameRes resources, List<string> errors)
    {
        IEnumerator routine = ValidateAsync(resources, errors);
        try { while (routine.MoveNext()) { } }
        finally { (routine as IDisposable)?.Dispose(); }
    }

    /// <summary>机械内容编译逐定义让出预算，运行时启动与编辑器诊断共用检查逻辑。</summary>
    public IEnumerator ValidateAsync(GameRes resources, List<string> errors)
    {
        foreach (var process in MachineCatalog.Processes)
        {
            if (!resources.ItemDefinitions.ContainsKey(process.Input)) errors.Add("机械加工输入未注册：" + process.Input);
            foreach (var output in process.Outputs)
                if (!resources.ItemDefinitions.ContainsKey(output.ItemName)) errors.Add("机械加工产物未注册：" + output.ItemName);
        }
        foreach (string id in new[] { "Module_HandDrill", "Module_ManualProcessor", "Module_MechanicalNode", "UI_HandDrill", "UI_Mechanical" })
            if (resources.GetPrefab(id, false) == null) errors.Add("机械资源未注册：" + id);

        foreach (var pair in resources.ItemDefinitions)
        {
            if (resources.ShouldYieldResourceWork()) yield return null;
            if (!pair.Value.HasModule(ModText.Building)) continue;
            try
            {
                ItemData data = pair.Value.CreateItemData();
                if (!Mod_Building.TryReadBuildingData(data, out _, out var building) ||
                    building.Role != BuildingRole.PlacedBuilding) continue;
                MachineDefinition definition = MachineCatalog.Get(pair.Key);
                if (definition == null) continue;
                definition.Validate();
                try { MachineCombatBridge.ValidateHealth(pair.Value.Health); }
                catch (InvalidOperationException error) { errors.Add("机器受击配置无效：" + pair.Key + "，" + error.Message); }
                if (!string.IsNullOrWhiteSpace(definition.LogicId) && !MachineLogicRegistry.IsRegistered(definition.LogicId))
                    errors.Add("机器领域工厂未注册：" + pair.Key + " -> " + definition.LogicId);
                ValidateAuthoring(pair.Key, new MachineContent(pair.Value, resources), errors);
            }
            catch (Exception error)
            {
                errors.Add("机器内容编译失败：" + pair.Key + "，" + error.Message);
            }
        }
    }

    /// <summary>配置模板保留在资源目录，缺少库存或 UI 必须在放置前报错。</summary>
    private static void ValidateAuthoring(string id, MachineContent content, List<string> errors)
    {
        if (content.Find<Mod_MakeTable>()?.Authoring is Mod_MakeTable workbench)
        {
            ValidateInventory(id, "工作台输入", workbench.inputInventory, errors, 5);
            ValidateInventory(id, "工作台输出", workbench.outputInventory, errors, 2);
            ValidatePanel(id, workbench.InventoryPanel_Prefab, errors);
        }
        if (content.Find<Mod_Furnace>()?.Authoring is Mod_Furnace furnace)
        {
            ValidateInventory(id, "熔炉输入", furnace.InputInventory, errors);
            ValidateInventory(id, "熔炉输出", furnace.OutputInventory, errors);
            ValidateInventory(id, "熔炉燃料", furnace.FuelInventory, errors);
            if (!content.Has<Mod_Fuel>()) errors.Add("炉体缺少燃料配置：" + id);
            ValidatePanel(id, furnace.UI_Prefab, errors);
            ValidateFurnacePanelBindings(id, furnace, errors);
        }
        if (content.Find<Mod_Inventory>()?.Authoring is Mod_Inventory storage)
        {
            if (storage.InventoryInstances == null || storage.InventoryInstances.Count == 0)
                errors.Add("储物设施缺少库存配置：" + id);
            else foreach (Inventory inventory in storage.InventoryInstances)
            {
                ValidateInventory(id, "储物", inventory, errors);
                ValidatePanel(id, inventory?.InventoryPanel_Prefab, errors);
            }
        }
    }

    private static void ValidateInventory(string id, string role, Inventory inventory, List<string> errors, int exactCount = 0)
    {
        var slots = inventory?.Data?.itemSlots;
        if (slots == null || slots.Count == 0 || exactCount > 0 && slots.Count != exactCount ||
            slots != null && slots.Exists(slot => slot == null))
            errors.Add("机器库存模板无效：" + id + " / " + role);
    }

    private static void ValidatePanel(string id, GameObject panel, List<string> errors)
    {
        if (panel == null || panel.GetComponentInChildren<BasePanel>(true) == null)
            errors.Add("机器正式面板缺失：" + id);
    }

    /// <summary>炉体面板槽位必须和库存模板一一对应，避免交互时才因缺少槽位抛异常。</summary>
    private static void ValidateFurnacePanelBindings(string id, Mod_Furnace furnace, List<string> errors)
    {
        GameObject panel = furnace?.UI_Prefab;
        if (panel == null) return;
        ValidatePanelSlots(id, panel, "输入", furnace.InputInventory, errors);
        ValidatePanelSlots(id, panel, "输出", furnace.OutputInventory, errors);
        ValidatePanelSlots(id, panel, "燃料", furnace.FuelInventory, errors);

        bool hasAction = false;
        foreach (Button button in panel.GetComponentsInChildren<Button>(true))
        {
            if (button != null && button.gameObject.name == "合成按钮") { hasAction = true; break; }
        }
        if (!hasAction) errors.Add("炉体面板缺少合成按钮：" + id);
    }

    private static void ValidatePanelSlots(string id, GameObject panel, string prefix, Inventory inventory, List<string> errors)
    {
        int count = inventory?.Data?.itemSlots?.Count ?? 0;
        ItemSlot_UI[] slots = panel.GetComponentsInChildren<ItemSlot_UI>(true);
        for (int i = 1; i <= count; i++)
        {
            string underscore = prefix + "_" + i;
            string spaced = prefix + " " + i;
            bool found = false;
            foreach (ItemSlot_UI slot in slots)
            {
                string name = slot != null ? slot.gameObject.name : null;
                if (name == underscore || name == spaced) { found = true; break; }
            }
            if (!found) errors.Add($"炉体面板槽位缺失：{id} / {underscore}");
        }
    }
    #endregion
}
