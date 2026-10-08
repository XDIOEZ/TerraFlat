using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>本体与 MOD 在全部继承和 Patch 完成后共用严格端口校验。</summary>
public static class ContainerPortConfigurationValidator
{
    #region 最终目录校验
    public static List<ContainerPortConfiguration> Read(JToken token)
    {
        if (token == null) return new();
        if (token.Type != JTokenType.Array) throw new InvalidDataException("ContainerPorts 必须为数组。");
        return token.ToObject<List<ContainerPortConfiguration>>(JsonSerializer.Create(new JsonSerializerSettings
        { MissingMemberHandling = MissingMemberHandling.Error })) ?? new();
    }
    public static void ValidateDefinitions(IEnumerable<RuntimeItemDefinition> definitions, Func<string, LiquidDefinition> liquid)
    {
        var all = new Dictionary<string, RuntimeItemDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions) all[definition.Id] = definition;
        foreach (RuntimeItemDefinition definition in all.Values)
        {
            var ports = new HashSet<string>(StringComparer.Ordinal);
            foreach (RuntimeItemModuleDefinition module in definition.ModuleDefinitions)
            {
                if (!module.Enabled) continue;
                JObject parameters = string.IsNullOrWhiteSpace(module.ParametersJson) ? new() : JObject.Parse(module.ParametersJson);
                definition.TryGetModuleAssembly(module.StableName, out var assembly);
                Module prototype = assembly?.ModulePrefab != null
                    ? Array.Find(assembly.ModulePrefab.GetComponentsInChildren<Module>(true), assembly.MatchesImplementation)
                    : assembly != null && definition.ShellPrefab != null ? assembly.ResolveEmbeddedModule(definition.ShellPrefab.transform) : null;
                Mod_Inventory inventoryModule = prototype as Mod_Inventory;
                Mod_WaterVessel liquidModule = prototype as Mod_WaterVessel;
                if (module.ModuleId == Mod_WaterVessel.ModuleId)
                {
                    JToken declaredCapacity = parameters.GetValue(nameof(Mod_WaterVessel.capacity), StringComparison.OrdinalIgnoreCase);
                    int capacity = declaredCapacity != null ? declaredCapacity.Value<int>() : liquidModule?.capacity ?? Mod_WaterVessel.DefaultCapacity;
                    if (capacity < 1) throw new InvalidDataException($"{definition.Id} 的液体容器容量必须大于零。");
                }
                JToken declared = parameters.GetValue(nameof(Mod_Inventory.ContainerPorts), StringComparison.OrdinalIgnoreCase);
                var configurations = declared != null ? Read(declared) : inventoryModule != null ? inventoryModule.ContainerPorts :
                    liquidModule != null ? liquidModule.ContainerPorts : new List<ContainerPortConfiguration>();
                foreach (ContainerPortConfiguration configuration in configurations ?? new())
                {
                    Validate(configuration, definition.Id, ports, id => all.ContainsKey(id), liquid);
                    foreach (string tag in configuration.ItemTags ?? Array.Empty<string>())
                    {
                        bool found = false;
                        foreach (var candidate in all.Values) if (candidate.HasTag(tag)) { found = true; break; }
                        if (!found) throw new InvalidDataException($"{definition.Id}/{configuration.Id} 引用未知物品标签：{tag}");
                    }
                    if (configuration.Type == "core:world_liquid") throw new InvalidDataException($"{definition.Id}/{configuration.Id} 世界液体口由世界查询创建，不能绑到物品库存。");
                    if (configuration.Type == "core:inventory")
                    {
                        if (string.IsNullOrWhiteSpace(configuration.InventoryId)) throw new InvalidDataException($"{definition.Id}/{configuration.Id} 缺少稳定 InventoryId。");
                        bool exists = false;
                        int? slotCount = null;
                        var inventoryArray = parameters.GetValue(nameof(Mod_Inventory.InventoryInstances), StringComparison.OrdinalIgnoreCase) as JArray;
                        foreach (JToken inventory in inventoryArray ?? new JArray())
                            if (inventory["Data"]?["Name"]?.Value<string>() == configuration.InventoryId)
                            { exists = true; slotCount = (inventory["Data"]?["itemSlots"] as JArray)?.Count; }
                        if (!exists && inventoryModule != null)
                            for (int i = 0; i < inventoryModule.InventoryInstances.Count; i++)
                                if (Mod_Inventory.GetInventoryKey(inventoryModule.InventoryInstances[i], i) == configuration.InventoryId)
                                { exists = true; slotCount = inventoryModule.InventoryInstances[i].Data?.itemSlots?.Count; }
                        if (!exists) throw new InvalidDataException($"{definition.Id}/{configuration.Id} 库存引用不存在：{configuration.InventoryId}");
                        foreach (int slot in configuration.SlotIndices ?? Array.Empty<int>())
                            if (slotCount.HasValue && slot >= slotCount.Value) throw new InvalidDataException($"{definition.Id}/{configuration.Id} 的槽位 {slot} 超出库存布局。");
                    }
                    if (configuration.Type == "core:liquid_vessel" && module.ModuleId != Mod_WaterVessel.ModuleId)
                        throw new InvalidDataException($"{definition.Id}/{configuration.Id} 液体端口必须配置在液体容器模块。");
                }
            }
        }
    }
    private static void Validate(ContainerPortConfiguration config, string item, HashSet<string> ids,
        Func<string, bool> itemExists, Func<string, LiquidDefinition> liquid)
    {
        if (config == null || string.IsNullOrWhiteSpace(config.Id) || config.Id.Contains("/") ||
            config.Id.Trim() != config.Id || !ids.Add(config.Id)) throw new InvalidDataException($"{item} 的端口 ID 为空或重复。");
        if (config.Version != ContainerTransferService.ContractVersion) throw new InvalidDataException($"{item}/{config.Id} 使用不支持的端口配置版本：{config.Version}");
        if ((int)config.Direction < 1 || (int)config.Direction > 3 || (int)config.Access < 1 || (int)config.Access > 15 ||
            !float.IsFinite(config.Reach) || config.Reach <= 0f) throw new InvalidDataException($"{item}/{config.Id} 的方向、访问类型或距离无效。");
        ContainerPortFactoryRegistry.ValidateConfiguration(config);
        foreach (string id in config.ItemIds ?? Array.Empty<string>()) if (string.IsNullOrWhiteSpace(id) || !itemExists(id)) throw new InvalidDataException($"{item}/{config.Id} 引用未知物品：{id}");
        foreach (string id in config.LiquidIds ?? Array.Empty<string>()) if (string.IsNullOrWhiteSpace(id) || liquid(id) == null) throw new InvalidDataException($"{item}/{config.Id} 引用未知液体：{id}");
        var slots = new HashSet<int>();
        foreach (int slot in config.SlotIndices ?? Array.Empty<int>()) if (slot < 0 || !slots.Add(slot)) throw new InvalidDataException($"{item}/{config.Id} 包含无效或重复槽位。");
    }
    #endregion
}
