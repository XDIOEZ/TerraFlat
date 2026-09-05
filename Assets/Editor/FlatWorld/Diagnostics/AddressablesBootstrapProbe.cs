#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace FlatWorld.Editor.Diagnostics
{
    /// <summary>
    /// Addressables 编辑器目录诊断：检查标签条目数与实际可解析的位置数。
    /// 只创建独立的 Fast Mode 目录视图并读取已导入资源，不实例化游戏实体；
    /// Play 会话的静态状态统一由 Unity Domain Reload 重建。
    /// </summary>
    internal static class AddressablesBootstrapProbe
    {
        #region 常量

        private const string LogPrefix = "[AddressablesBootstrap]";

        #endregion

        #region 静态目录诊断

        /// <summary>只检查编辑器目录及标签解析，不加载 Prefab、不进入 Play Mode。</summary>
        [MenuItem("FlatWorld/诊断/检查 Addressables 目录")]
        private static void InspectEditorCatalog()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                throw new InvalidOperationException("Addressables 默认 Settings 资源不存在。");

            Debug.Log($"{LogPrefix} 播放配置：optionsEnabled={EditorSettings.enterPlayModeOptionsEnabled}, " +
                      $"options={EditorSettings.enterPlayModeOptions}");

            Type locatorType = typeof(AddressableAssetSettings).Assembly.GetType(
                "UnityEditor.AddressableAssets.Settings.AddressableAssetSettingsLocator", true);
            IResourceLocator locator = (IResourceLocator)Activator.CreateInstance(locatorType, new object[] { settings });
            try
            {
                foreach (string label in new[] { "Prefab", "ItemPrefab", "TileBlock", "ItemSprite", "TileBase", "InventoryInit", "Skill" })
                {
                    Type assetType = label == "Prefab" || label == "ItemPrefab" ? typeof(GameObject) : null;
                    bool found = locator.Locate(label, assetType, out IList<IResourceLocation> locations);
                    int entryCount = settings.groups.Where(group => group != null)
                        .Sum(group => group.entries.Count(entry => entry.labels.Contains(label)));
                    Debug.Log($"{LogPrefix} 静态目录：label={label}, entries={entryCount}, " +
                              $"locations={(found ? locations.Count : 0)}, builder={settings.ActivePlayModeDataBuilder?.Name}");
                }
                InspectContentReferences(locator);
            }
            finally
            {
                MethodInfo callback = locatorType.GetMethod("Settings_OnModification", BindingFlags.Instance | BindingFlags.NonPublic);
                settings.OnModification -= (Action<AddressableAssetSettings, AddressableAssetSettings.ModificationEvent, object>)
                    Delegate.CreateDelegate(typeof(Action<AddressableAssetSettings, AddressableAssetSettings.ModificationEvent, object>), locator, callback);
            }
        }

        /// <summary>静态目录检查只在编辑状态运行，避免干扰播放中的配置快照。</summary>
        [MenuItem("FlatWorld/诊断/检查 Addressables 目录", true)]
        private static bool CanInspectEditorCatalog() => !EditorApplication.isPlayingOrWillChangePlaymode;

        #endregion

        #region 内容引用预检

        /// <summary>检查当前 Manifest 所需的地址、模块和 Tile 接线，不启动资源会话或游戏。</summary>
        private static void InspectContentReferences(IResourceLocator locator)
        {
            var errors = new List<string>();
            List<ItemDefinitionDto> definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
            List<ItemDefinitionDto> actors = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions();
            HashSet<string> excluded = ItemDefinitionCatalogLoader.GetRedundantBuiltInPrefabPaths(definitions);
            List<IResourceLocation> Locations(string label, Type type) => locator.Locate(label, type, out IList<IResourceLocation> found)
                ? found.GroupBy(location => location.InternalId).Select(group => group.First()).ToList() : new();
            var actorPaths = new HashSet<string>(Locations(ActorDefinitionCatalogLoader.ShellAddressableLabel, typeof(GameObject))
                .Select(location => location.InternalId), StringComparer.Ordinal);
            var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            foreach (IResourceLocation location in Locations("Prefab", typeof(GameObject)).Concat(Locations("ItemPrefab", typeof(GameObject))))
            {
                if (excluded.Contains(location.PrimaryKey) || actorPaths.Contains(location.InternalId)) continue;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(location.InternalId);
                if (prefab == null) { errors.Add($"Prefab 无法读取：{location.PrimaryKey}"); continue; }
                if (prefabs.TryGetValue(prefab.name, out GameObject existing) && existing != prefab)
                    errors.Add($"Prefab 主名称冲突：{prefab.name} -> {AssetDatabase.GetAssetPath(existing)} / {location.InternalId}");
                else prefabs[prefab.name] = prefab;
            }
            foreach (GameObject prefab in prefabs.Values.ToArray())
            {
                string itemId = prefab.GetComponent<Item>()?.itemData?.IDName;
                if (string.IsNullOrWhiteSpace(itemId)) continue;
                if (prefabs.TryGetValue(itemId, out GameObject existing) && existing != prefab)
                    errors.Add($"Prefab 物品别名冲突：{itemId} -> {existing.name} / {prefab.name}");
                else prefabs[itemId] = prefab;
            }
            var moduleAliases = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            foreach (GameObject prefab in prefabs.Values.Distinct().Where(prefab => prefab.GetComponent<Item>() == null))
                foreach (Module module in prefab.GetComponentsInChildren<Module>(true))
                    foreach (string id in new[] { module.CanonicalModuleId, module._Data?.ID })
                    {
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        if (!moduleAliases.TryGetValue(id, out GameObject previous)) moduleAliases[id] = prefab;
                        else if (previous != prefab) moduleAliases[id] = null;
                    }
            foreach (var pair in moduleAliases)
                if (pair.Value != null && !prefabs.ContainsKey(pair.Key)) prefabs.Add(pair.Key, pair.Value);

            foreach (var group in new[] { ("TileBase", typeof(UnityEngine.Tilemaps.TileBase)),
                ("InventoryInit", typeof(Inventoryinit)), ("Skill", typeof(BaseSkill)) })
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                List<IResourceLocation> locations = Locations(group.Item1, group.Item2);
                if (locations.Count == 0) errors.Add($"必需标签 {group.Item1} 未解析到 {group.Item2.Name}");
                foreach (IResourceLocation location in locations)
                {
                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(location.InternalId, group.Item2);
                    if (asset == null) errors.Add($"{group.Item1} 资源无法读取：{location.InternalId}");
                    else if (!names.Add(asset.name)) errors.Add($"{group.Item1} 主键重复：{asset.name}");
                }
            }
            void CheckAddress(string source, string address, Type type)
            {
                if (string.IsNullOrWhiteSpace(address)) return;
                string mainAddress = address.Contains('[') ? address.Substring(0, address.IndexOf('[')) : address;
                if (!locator.Locate(mainAddress, type, out IList<IResourceLocation> found) || found.Count == 0)
                    errors.Add($"{source} -> {type.Name} 地址未注册或类型不符：{address}");
                if (type == typeof(Sprite) && address.StartsWith("Assets/", StringComparison.Ordinal) &&
                    !ItemDefinitionCatalogLoader.TryLoadEditorSprite(address, out _, out string error)) errors.Add(source + " -> " + error);
            }
            var tileRequests = new Dictionary<string, string>(StringComparer.Ordinal);
            var itemIds = new HashSet<string>(definitions.Concat(actors).Where(definition => !definition.Abstract).Select(definition => definition.Id));
            foreach (ItemDefinitionDto definition in definitions.Concat(actors).Where(definition => !definition.Abstract))
            {
                if (string.IsNullOrWhiteSpace(definition.ShellAddress) && !prefabs.ContainsKey(definition.ShellPrefab))
                    errors.Add($"物品 {definition.Id} -> 外壳 {definition.ShellPrefab} 不在 Prefab 计划中");
                CheckAddress(definition.Id, definition.ShellAddress, typeof(GameObject));
                CheckAddress(definition.Id, definition.Visual?.SpriteAddress, typeof(Sprite));
                CheckAddress(definition.Id, definition.Visual?.MaterialAddress, typeof(Material));
                CheckAddress(definition.Id, definition.Visual?.AnimatorControllerAddress, typeof(RuntimeAnimatorController));
                GameObject shell = null;
                if (!string.IsNullOrWhiteSpace(definition.ShellAddress) && locator.Locate(definition.ShellAddress, typeof(GameObject), out var shellLocations))
                    shell = AssetDatabase.LoadAssetAtPath<GameObject>(shellLocations[0].InternalId);
                else prefabs.TryGetValue(definition.ShellPrefab, out shell);
                if (shell != null && prefabs.TryGetValue(definition.Id, out GameObject aliased) && aliased != shell)
                    errors.Add($"物品 {definition.Id} 的外壳 {shell.name} 与已有 Prefab 别名 {aliased.name} 冲突");
                foreach (var module in definition.Modules)
                {
                    prefabs.TryGetValue(module.Value.Prefab, out GameObject prefab);
                    Module prototype = ItemDefinitionCatalogLoader.FindModulePrototype(shell, prefab, module.Value.Prefab);
                    if (prototype == null) errors.Add($"物品 {definition.Id} -> 模块 {module.Key} -> Prefab {module.Value.Prefab} 缺失");
                    else
                    {
                        try { ModuleJsonConfigurator.Validate(prototype, definition.Id, module.Key,
                            module.Value.Id, module.Value.Parameters?.ToString()); }
                        catch (Exception exception) { errors.Add(exception.ToString()); }
                    }
                    string tileId = null;
                    if (module.Value.Id == ModText.Building || module.Value.Prefab == "Module_Building")
                    {
                        string bitData = module.Value.Data?.Value<string>("BitData");
                        if (string.IsNullOrWhiteSpace(bitData)) errors.Add($"建筑 {definition.Id} 缺少 BitData");
                        else
                        {
                            var state = Newtonsoft.Json.JsonConvert.DeserializeObject<Mod_Building.Building_Data>(bitData);
                            tileId = state.TileBlockId;
                            if (string.IsNullOrWhiteSpace(tileId) && !itemIds.Contains(state.BuildingPrefabId))
                                errors.Add($"建筑 {definition.Id} -> 本体 {state.BuildingPrefabId} 未注册");
                            if (!itemIds.Contains(state.SummonerPrefabId)) errors.Add($"建筑 {definition.Id} -> 召唤器 {state.SummonerPrefabId} 未注册");
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(tileId)) tileRequests[definition.Id] = tileId;
                }
            }
            var blocks = new Dictionary<string, Tile_Block>(StringComparer.Ordinal);
            foreach (IResourceLocation location in Locations("TileBlock", typeof(Tile_Block)))
            {
                Tile_Block block = AssetDatabase.LoadAssetAtPath<Tile_Block>(location.InternalId);
                if (block == null) { errors.Add($"TileBlock 无法读取：{location.InternalId}"); continue; }
                if (!blocks.TryAdd(block.name, block)) errors.Add($"TileBlock 名称冲突：{block.name}");
            }
            BuildingResourceCatalogValidator.ValidateTiles(tileRequests, blocks, errors);
            if (errors.Count > 0) Debug.LogError($"{LogPrefix} 内容预检发现 {errors.Count} 个问题：\n" + string.Join("\n", errors));
            else Debug.Log($"{LogPrefix} 内容预检通过：{definitions.Count(definition => !definition.Abstract)} 个物品，{actors.Count(definition => !definition.Abstract)} 个 Actor，" +
                $"{prefabs.Count} 个 Prefab 名称/别名，{tileRequests.Count} 个 Tile 建筑，地址与引用完整。");
        }

        #endregion

    }
}

#endif
