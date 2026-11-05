#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class GameRes
{
    #region 构建期模块原型目录

    /// <summary>仅在编辑态用隔离原型目录执行正式模块装配，不发布服务或创建玩法实例。</summary>
    public static void BakeBuiltInModuleParameters(IEnumerable<GameObject> prefabs,
        IEnumerable<ItemDefinitionDto> itemDefinitions, IEnumerable<ItemDefinitionDto> actorDefinitions,
        IReadOnlyDictionary<string, GameObject> shells)
    {
        using var probability = DiscreteProbabilityConfiguration.UseBuiltInConfigurationForBake();
        WithBuiltInModulePrototypeCatalog(prefabs, resources =>
        {
            foreach (RuntimeLootTable table in LootTableCatalogLoader.LoadBuiltIn()) resources.RegisterLootTable(table);
            var prototypes = new ItemModulePrototypeCache();
            foreach (ItemDefinitionDto dto in itemDefinitions.Concat(actorDefinitions))
            {
                if (dto.Abstract) continue;
                string id = dto.ShellPrefab?.Trim();
                GameObject shell = shells.TryGetValue(id ?? string.Empty, out GameObject explicitShell)
                    ? explicitShell : resources.GetPrefab(id, false);
                if (shell == null) throw new InvalidOperationException($"物品 {dto.Id} 烘焙时找不到外壳：{id}");
                ItemDefinitionCatalogLoader.BakeDefinitionModuleParameters(resources, dto, shell, prototypes);
            }
            return true;
        });
    }

    /// <summary>编辑期调用者共用正式模块工厂，隔离原型仅在回调期间有效。</summary>
    public static TResult WithBuiltInModulePrototypeCatalog<TResult>(IEnumerable<GameObject> prefabs,
        Func<GameRes, TResult> inspect)
    {
        var owner = new GameObject("Built-in configuration bake prototypes") { hideFlags = HideFlags.HideAndDontSave };
        owner.SetActive(false);
        GameRes resources = owner.AddComponent<GameRes>();
        var assets = new ResourceAssetScope();
        resources.resourceAssets = assets;
        try
        {
            GameObject[] sources = prefabs.Where(prefab => prefab != null).Distinct().ToArray();
            foreach (GameObject prefab in sources) resources.RegisterPrefabAlias(prefab.name, prefab);
            var aliases = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            foreach (GameObject prefab in sources) resources.CollectPrefabAliases(prefab, aliases);
            resources.RegisterUniqueModuleAliases(aliases);
            resources.EnsureIndustrialRuntimeTemplates();
            foreach (GameObject prefab in resources.AllPrefabs.Values.Distinct())
                if (resources.resourceAssets.ContainsRuntimeTemplate(prefab))
                {
                    prefab.hideFlags = HideFlags.HideAndDontSave;
                    prefab.transform.SetParent(owner.transform, false);
                }
            return inspect(resources);
        }
        finally
        {
            try { assets.Dispose(); }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
    }

    #endregion
}
#endif
