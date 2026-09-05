using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>独立系统的资源引用校验契约；只读目录，把问题加入报告，不生成游戏实体。</summary>
public interface IResourceCatalogValidator
{
    string Id { get; }
    void Validate(GameRes resources, List<string> errors);
}

/// <summary>资源校验注册表；本体与 MOD 合并后共用同一组校验，任何错误都会阻止 Ready。</summary>
public static class ResourceCatalogValidation
{
    #region 校验器注册

    private static readonly SortedDictionary<string, IResourceCatalogValidator> validators = new(StringComparer.Ordinal)
    {
        ["items"] = new ItemResourceCatalogValidator(),
        ["buildings"] = new BuildingResourceCatalogValidator()
    };

    /// <summary>按稳定 ID 接入独立系统校验；替换既有校验必须显式声明。</summary>
    public static void Register(IResourceCatalogValidator validator, bool replace = false)
    {
        if (validator == null || string.IsNullOrWhiteSpace(validator.Id)) throw new ArgumentException("资源校验器缺少 ID。");
        if (!replace && validators.ContainsKey(validator.Id)) throw new InvalidOperationException($"资源校验器重复：{validator.Id}");
        validators[validator.Id] = validator;
    }

    /// <summary>收集各系统的全部问题，保留校验器 ID 与资源引用链。</summary>
    public static void Validate(GameRes resources)
    {
        var errors = new List<string>();
        foreach (IResourceCatalogValidator validator in validators.Values.ToArray())
        {
            try { validator.Validate(resources, errors); }
            catch (Exception exception) { errors.Add($"[{validator.Id}] {exception}"); }
        }
        if (errors.Count > 0) throw new InvalidDataException($"资源引用校验发现 {errors.Count} 个问题：\n" + string.Join("\n", errors));
    }

    #endregion
}

/// <summary>物品外观、模块参数及战利品引用校验；覆盖本体定义与 MOD 定义。</summary>
internal sealed class ItemResourceCatalogValidator : IResourceCatalogValidator
{
    public string Id => "items";

    public void Validate(GameRes resources, List<string> errors)
    {
        foreach (RuntimeItemDefinition definition in resources.ItemDefinitions.Values)
        {
            try
            {
                if (definition.ShellPrefab == null) throw new InvalidDataException("缺少外壳");
                if (!string.IsNullOrWhiteSpace(definition.RendererPath))
                {
                    Transform renderer = definition.ShellPrefab.transform.Find(definition.RendererPath);
                    if (renderer == null || renderer.GetComponent<SpriteRenderer>() == null)
                        errors.Add($"物品 {definition.Id} -> rendererPath {definition.RendererPath} 缺少 SpriteRenderer");
                }
                ItemData data = definition.CreateItemData();
                foreach (KeyValuePair<string, ModuleData> pair in data.ModuleDataDic)
                {
                    string prefabId = definition.GetModulePrefabId(pair.Key, pair.Value.ID);
                    GameObject prefab = resources.GetPrefab(prefabId, false);
                    Module prototype = ItemDefinitionCatalogLoader.FindModulePrototype(definition.ShellPrefab, prefab, prefabId);
                    if (prototype == null)
                    {
                        errors.Add($"物品 {definition.Id} -> 模块 {pair.Key} -> Prefab {prefabId} 缺失");
                        continue;
                    }
                    if (definition.TryGetModuleParameters(pair.Key, out string parameters))
                        ModuleJsonConfigurator.Validate(prototype, definition.Id, pair.Key, pair.Value.ID, parameters);
                }
            }
            catch (Exception exception) { errors.Add($"物品 {definition.Id}：{exception.Message}"); }
        }
        foreach (RuntimeLootTable table in resources.LootTables.Values)
            foreach (RuntimeLootTableEntry entry in table.Entries)
                if (!resources.ItemDefinitions.ContainsKey(entry.ItemId))
                    errors.Add($"战利品表 {table.Id} -> 物品 {entry.ItemId} 未注册");
    }
}
