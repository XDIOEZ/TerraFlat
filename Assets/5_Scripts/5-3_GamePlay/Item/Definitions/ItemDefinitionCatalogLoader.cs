using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>从 StreamingAssets 加载本体 ItemDefinition，并绑定少量外壳 Prefab。</summary>
public static class ItemDefinitionCatalogLoader
{
    public const int SupportedSchemaVersion = 1;
    public const string RelativeCatalogPath = "GameConfig/Items/items.json";

    public static string BuiltInCatalogPath =>
        Path.Combine(Application.streamingAssetsPath, RelativeCatalogPath);

    private static readonly JsonSerializerSettings TemplateSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    public static int LoadBuiltIn(GameRes gameRes)
    {
        if (gameRes == null)
            throw new ArgumentNullException(nameof(gameRes));
        if (!File.Exists(BuiltInCatalogPath))
            throw new FileNotFoundException($"找不到物品 JSON：{BuiltInCatalogPath}", BuiltInCatalogPath);

        List<ItemDefinitionDto> dtos = ResolveDefinitions(File.ReadAllText(BuiltInCatalogPath));
        var definitions = new List<RuntimeItemDefinition>(dtos.Count);

        // 先完整构建，避免失败时只注册了一半目录。
        foreach (ItemDefinitionDto dto in dtos)
        {
            if (!dto.Abstract)
                definitions.Add(BuildRuntimeDefinition(gameRes, dto));
        }

        foreach (RuntimeItemDefinition definition in definitions)
            gameRes.RegisterItemDefinition(definition);

        Debug.Log($"[ItemDefinitionCatalog] 已从 JSON 加载 {definitions.Count} 个物品：{BuiltInCatalogPath}");
        return definitions.Count;
    }

    public static IEnumerator LoadBuiltInAsync(
        GameRes gameRes,
        Action<int> completed,
        Action<Exception> failed,
        Action<float> progress = null)
    {
        List<ItemDefinitionDto> dtos;
        try
        {
            if (gameRes == null)
                throw new ArgumentNullException(nameof(gameRes));
            if (!File.Exists(BuiltInCatalogPath))
                throw new FileNotFoundException($"找不到物品 JSON：{BuiltInCatalogPath}", BuiltInCatalogPath);
            dtos = ResolveDefinitions(File.ReadAllText(BuiltInCatalogPath));
        }
        catch (Exception exception)
        {
            failed?.Invoke(exception);
            yield break;
        }

        string[] addresses = dtos
            .Where(dto => !dto.Abstract && !string.IsNullOrWhiteSpace(dto.Visual?.SpriteAddress))
            .Select(dto => dto.Visual.SpriteAddress.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var handles = new Dictionary<string, AsyncOperationHandle<Sprite>>(StringComparer.OrdinalIgnoreCase);
        foreach (string address in addresses)
            handles[address] = Addressables.LoadAssetAsync<Sprite>(address);

        while (handles.Values.Any(handle => !handle.IsDone))
        {
            int done = handles.Values.Count(handle => handle.IsDone);
            progress?.Invoke(addresses.Length == 0 ? 0.5f : 0.5f * done / addresses.Length);
            yield return null;
        }

        var sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, AsyncOperationHandle<Sprite>> pair in handles)
        {
            if (pair.Value.Status != AsyncOperationStatus.Succeeded || pair.Value.Result == null)
            {
                failed?.Invoke(new InvalidDataException($"找不到物品 Sprite Addressable：{pair.Key}"));
                yield break;
            }
            sprites[pair.Key] = pair.Value.Result;
        }

        var definitions = new List<RuntimeItemDefinition>(dtos.Count);
        try
        {
            foreach (ItemDefinitionDto dto in dtos)
            {
                if (!dto.Abstract)
                    definitions.Add(BuildRuntimeDefinition(gameRes, dto, sprites));
            }
            foreach (RuntimeItemDefinition definition in definitions)
                gameRes.RegisterItemDefinition(definition);
        }
        catch (Exception exception)
        {
            failed?.Invoke(exception);
            yield break;
        }

        progress?.Invoke(1f);
        Debug.Log($"[ItemDefinitionCatalog] 已从 JSON 异步加载 {definitions.Count} 个物品：{BuiltInCatalogPath}");
        completed?.Invoke(definitions.Count);
    }

    /// <summary>
    /// 返回已经完整迁入 JSON、且不是任何运行时外壳/模块 Prefab 的旧资源路径。
    /// GameRes 用它在 Addressables 定位阶段直接跳过冗余变体，避免先加载再覆盖。
    /// </summary>
    public static HashSet<string> GetRedundantBuiltInPrefabPaths()
    {
        if (!File.Exists(BuiltInCatalogPath))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        List<ItemDefinitionDto> definitions = ResolveDefinitions(File.ReadAllText(BuiltInCatalogPath));
        var requiredPrefabIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in definitions)
        {
            if (!string.IsNullOrWhiteSpace(definition.ShellPrefab))
                requiredPrefabIds.Add(definition.ShellPrefab.Trim());
            foreach (ItemModuleDefinitionDto module in definition.Modules?.Values ?? Enumerable.Empty<ItemModuleDefinitionDto>())
            {
                if (!string.IsNullOrWhiteSpace(module?.Prefab))
                    requiredPrefabIds.Add(module.Prefab.Trim());
            }
        }

        var redundant = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ItemDefinitionDto definition in definitions)
        {
            if (definition.Abstract || string.IsNullOrWhiteSpace(definition.SourcePrefab))
                continue;
            string path = definition.SourcePrefab.Trim().Replace('\\', '/');
            string prefabId = Path.GetFileNameWithoutExtension(path);
            if (!requiredPrefabIds.Contains(prefabId))
                redundant.Add(path);
        }
        return redundant;
    }

    public static List<ItemDefinitionDto> ResolveDefinitions(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("物品 JSON 为空");

        JObject root = JObject.Parse(json);
        int schemaVersion = root.Value<int?>("schemaVersion") ?? 0;
        if (schemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的物品 schemaVersion：{schemaVersion}");
        if (root["items"] is not JArray items)
            throw new InvalidDataException("物品 JSON 缺少 items 数组");

        var sources = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        foreach (JToken token in items)
        {
            if (token is not JObject source)
                throw new InvalidDataException("items 中包含非对象定义");
            string id = source.Value<string>("id")?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("物品定义包含空 ID");
            if (!sources.TryAdd(id, (JObject)source.DeepClone()))
                throw new InvalidDataException($"重复物品 ID：{id}");
        }

        var resolved = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        var resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in sources.Keys)
            ResolveOne(id, sources, resolved, resolving);

        return sources.Keys
            .Select(id => resolved[id].ToObject<ItemDefinitionDto>() ??
                          throw new InvalidDataException($"无法解析物品定义：{id}"))
            .ToList();
    }

    private static JObject ResolveOne(
        string id,
        Dictionary<string, JObject> sources,
        Dictionary<string, JObject> resolved,
        HashSet<string> resolving)
    {
        if (resolved.TryGetValue(id, out JObject cached))
            return cached;
        if (!resolving.Add(id))
            throw new InvalidDataException($"物品定义继承存在循环：{id}");

        JObject source = sources[id];
        JObject result = new();
        string parentId = source.Value<string>("parent")?.Trim();
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            if (!sources.ContainsKey(parentId))
                throw new InvalidDataException($"物品 {id} 找不到 parent：{parentId}");
            result = (JObject)ResolveOne(parentId, sources, resolved, resolving).DeepClone();
        }

        result.Merge(source, new JsonMergeSettings
        {
            MergeArrayHandling = MergeArrayHandling.Replace,
            MergeNullValueHandling = MergeNullValueHandling.Merge
        });
        result["id"] = id;
        result["abstract"] = source.Value<bool?>("abstract") ?? false;
        result.Remove("parent");

        resolving.Remove(id);
        resolved.Add(id, result);
        return result;
    }

    private static RuntimeItemDefinition BuildRuntimeDefinition(
        GameRes gameRes,
        ItemDefinitionDto dto,
        IReadOnlyDictionary<string, Sprite> preloadedSprites = null)
    {
        string id = dto.Id?.Trim();
        string shellId = dto.ShellPrefab?.Trim();
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("物品定义 ID 为空");
        if (string.IsNullOrWhiteSpace(shellId))
            throw new InvalidDataException($"物品 {id} 缺少 shellPrefab");

        GameObject shell = gameRes.GetPrefab(shellId, false);
        Item shellItem = shell != null ? shell.GetComponent<Item>() : null;
        if (shellItem?.itemData == null)
            throw new InvalidDataException($"物品 {id} 的外壳不是有效 Item：{shellId}");

        ItemData template = FastCloner.FastCloner.DeepClone(shellItem.itemData);
        PopulateTemplateData(dto.ItemData, template, id);
        template.IDName = id;
        template.Guid = 0;
        if (!string.IsNullOrWhiteSpace(dto.GameName)) template.GameName = dto.GameName;
        if (dto.Description != null) template.Description = dto.Description;
        if (dto.Durability.HasValue) template.Durability = dto.Durability.Value;
        if (dto.MaxDurability.HasValue) template.MaxDurability = dto.MaxDurability.Value;
        if (dto.Tags != null) template.Tags = new List<string>(dto.Tags);

        template.Stack ??= new ItemStack();
        if (dto.Amount.HasValue) template.Stack.Amount = dto.Amount.Value;
        if (dto.Volume.HasValue) template.Stack.Volume = dto.Volume.Value;
        if (dto.CanBePickedUp.HasValue) template.Stack.CanBePickedUp = dto.CanBePickedUp.Value;

        // JSON 是模块初始状态的唯一来源，不继承外壳 Prefab 上的 ModuleData。
        template.ModuleDataDic = new Dictionary<string, ModuleData>(StringComparer.Ordinal);
        var moduleParameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var modulePrefabIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ItemModuleDefinitionDto> pair in dto.Modules ?? new())
        {
            string moduleName = pair.Key?.Trim();
            ItemModuleDefinitionDto moduleDto = pair.Value;
            string moduleId = moduleDto?.Prefab?.Trim();
            if (string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(moduleId))
                throw new InvalidDataException($"物品 {id} 包含空模块名或 prefab");

            Module prototype = ResolveModulePrototype(gameRes, shell, moduleId);
            if (prototype?._Data == null)
                throw new InvalidDataException($"物品 {id} 找不到模块 Prefab/外壳模块：{moduleId}");

            ModuleData moduleData = FastCloner.FastCloner.DeepClone(prototype._Data);
            PopulateModuleData(moduleDto.Data, moduleData, id, moduleName);
            moduleData.Name = moduleName;
            moduleData.ID = string.IsNullOrWhiteSpace(moduleDto.Id)
                ? (!string.IsNullOrWhiteSpace(prototype._Data.ID) ? prototype._Data.ID : moduleId)
                : moduleDto.Id.Trim();
            if (moduleDto.Enabled.HasValue)
                moduleData.isRunning = moduleDto.Enabled.Value;
            template.ModuleDataDic.Add(moduleName, moduleData);
            moduleParameters.Add(moduleName, moduleDto.Parameters?.ToString(Formatting.None));
            modulePrefabIds.Add(moduleName, moduleId);
        }

        Sprite sprite = ResolveSprite(dto.Visual?.SpriteAddress, id, preloadedSprites);
        return new RuntimeItemDefinition(
            id,
            shellId,
            shell,
            template,
            dto.Visual,
            sprite,
            moduleParameters,
            modulePrefabIds);
    }

    private static void PopulateTemplateData(JObject data, ItemData template, string itemId)
    {
        if (data == null)
            return;

        RejectFields(data, itemId, "itemData", "IDName", "Guid", "ModuleDataDic");
        try
        {
            JsonConvert.PopulateObject(data.ToString(Formatting.None), template, TemplateSettings);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException($"物品 {itemId} 的 itemData 无法应用到 {template.GetType().Name}", exception);
        }
    }

    private static void PopulateModuleData(
        JObject data,
        ModuleData moduleData,
        string itemId,
        string moduleName)
    {
        if (data == null)
            return;

        RejectFields(
            data,
            itemId,
            $"modules.{moduleName}.data",
            "Name",
            "ID",
            "isRunning",
            "RuntimeOwnerItemData",
            "RuntimeOwnerInventoryData",
            "RuntimeOwnerSlot",
            "RuntimeOwnerSlotIndex");
        try
        {
            JsonConvert.PopulateObject(data.ToString(Formatting.None), moduleData, TemplateSettings);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                $"物品 {itemId} 的模块数据 {moduleName} 无法应用到 {moduleData.GetType().Name}",
                exception);
        }
    }

    private static void RejectFields(JObject data, string itemId, string section, params string[] names)
    {
        foreach (string name in names)
        {
            if (data.Property(name, StringComparison.OrdinalIgnoreCase) != null)
                throw new InvalidDataException($"物品 {itemId} 的 {section} 不允许覆盖保留字段：{name}");
        }
    }

    private static Module ResolveModulePrototype(GameRes gameRes, GameObject shell, string moduleId)
    {
        GameObject modulePrefab = gameRes.GetPrefab(moduleId, false);
        Module module = modulePrefab?.GetComponentInChildren<Module>(true);
        if (module != null)
            return module;

        return shell.GetComponentsInChildren<Module>(true)
            .FirstOrDefault(candidate => candidate != null && candidate.MatchesPersistedId(moduleId));
    }

    private static Sprite ResolveSprite(
        string address,
        string itemId,
        IReadOnlyDictionary<string, Sprite> preloadedSprites)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        string key = address.Trim();
        if (preloadedSprites != null)
        {
            if (preloadedSprites.TryGetValue(key, out Sprite preloaded) && preloaded != null)
                return preloaded;
            throw new InvalidDataException($"物品 {itemId} 找不到预加载 Sprite：{key}");
        }

        AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(key);
        Sprite sprite = handle.WaitForCompletion();
        if (handle.Status != AsyncOperationStatus.Succeeded || sprite == null)
            throw new InvalidDataException($"物品 {itemId} 找不到 Sprite Addressable：{address}");
        return sprite;
    }
}
