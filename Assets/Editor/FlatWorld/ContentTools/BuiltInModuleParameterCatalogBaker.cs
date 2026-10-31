using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>构建时按正式 Prefab 标签和外壳地址选择真实模块原型。</summary>
public static class BuiltInModuleParameterCatalogBaker
{
    #region 模块参数烘焙

    [BuiltInContentBake]
    public static void BakeBuiltInConfiguration()
    {
        List<ItemDefinitionDto> items = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
        List<ItemDefinitionDto> actors = ActorDefinitionCatalogLoader.LoadBuiltInDefinitionsForBake();
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) throw new InvalidOperationException("缺少 Addressables 资源设置。");
        HashSet<string> excluded = ItemDefinitionCatalogLoader.GetRedundantBuiltInPrefabPaths(items);
        var entries = settings.groups.Where(group => group != null).SelectMany(group => group.entries).ToArray();
        var generic = entries.Where(entry => (entry.labels.Contains("Prefab") || entry.labels.Contains("ItemPrefab")) &&
            !entry.labels.Contains(ActorDefinitionCatalogLoader.ShellAddressableLabel) && !excluded.Contains(entry.AssetPath))
            .Select(entry => AssetDatabase.LoadAssetAtPath<GameObject>(entry.AssetPath)).Where(value => value != null).ToList();
        var shells = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto dto in items.Concat(actors))
        {
            if (dto.Abstract || string.IsNullOrWhiteSpace(dto.ShellAddress)) continue;
            string address = dto.ShellAddress.Trim();
            var entry = entries.FirstOrDefault(value => value.address == address);
            string path = entry?.AssetPath ?? address;
            GameObject shell = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (shell == null) throw new InvalidOperationException($"物品 {dto.Id} 外壳地址无效：{address}");
            shells[dto.ShellPrefab.Trim()] = shell;
        }
        GameRes.BakeBuiltInModuleParameters(generic, items, actors, shells);
    }

    #endregion
}
