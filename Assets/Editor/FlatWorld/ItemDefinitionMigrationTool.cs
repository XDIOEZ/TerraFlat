using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 把同构物品 Prefab 批量导出为 JSON 定义。目标根目录和共享组均为显式白名单，
/// 可重复执行；旧 Prefab 只作为编辑器迁移源，运行时不会读取 sourcePrefab。
/// </summary>
public static class ItemDefinitionMigrationTool
{
    private const string CatalogPath = "Assets/StreamingAssets/GameConfig/Items/items.json";
    private const string RequestPath = "Temp/FlatWorldItemDefinitionMigration.request";
    private const string ItemSpriteLabel = "ItemSprite";

    private static readonly HashSet<string> PreservedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Knife_Base", "Dagger_Stone", "Dagger_Copper", "Dagger_Bone", "Knife_Flint"
    };

    private static readonly Dictionary<string, string> PreservedSourcePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dagger_Stone"] = "Assets/2_Prefabs/Weapon/Weapon/Dagger_Stone.prefab",
        ["Dagger_Copper"] = "Assets/2_Prefabs/Weapon/Weapon/Dagger_Copper.prefab",
        ["Dagger_Bone"] = "Assets/2_Prefabs/Weapon/Weapon/Dagger_Bone.prefab",
        ["Knife_Flint"] = "Assets/2_Prefabs/Weapon/Weapon/Knife_Flint.prefab"
    };

    private static readonly MigrationGroup[] Groups =
    {
        new(
            "BasicItem_Base",
            "Assets/2_Prefabs/Item/Bone.prefab",
            new[]
            {
                "Assets/2_Prefabs/Item/Bone.prefab",
                "Assets/2_Prefabs/Item/CharredMatter.prefab",
                "Assets/2_Prefabs/Item/Earth.prefab",
                "Assets/2_Prefabs/Item/Leaf.prefab",
                "Assets/2_Prefabs/Item/Leather.prefab",
                "Assets/2_Prefabs/Item/Plank.prefab",
                "Assets/2_Prefabs/Item/RawHide.prefab",
                "Assets/2_Prefabs/Item/Rope.prefab",
                "Assets/2_Prefabs/Item/Twine.prefab",
                "Assets/2_Prefabs/Mineral/Ingot/Ingot_Bronze.prefab",
                "Assets/2_Prefabs/Mineral/Ingot/Ingot_Copper.prefab",
                "Assets/2_Prefabs/Mineral/Ingot/Ingot_RawIron.prefab",
                "Assets/2_Prefabs/Mineral/Ingot/Ingot_Steel.prefab",
                "Assets/2_Prefabs/Mineral/Ingot/Ingot_Tin.prefab",
                "Assets/2_Prefabs/Mineral/Ingot/Ingot_WroughtIron.prefab",
                "Assets/2_Prefabs/Mineral/Ore/Ore_Coal.prefab",
                "Assets/2_Prefabs/Mineral/Ore/Ore_Copper.prefab",
                "Assets/2_Prefabs/Mineral/Ore/Ore_Flint.prefab",
                "Assets/2_Prefabs/Mineral/Ore/Ore_Iron.prefab",
                "Assets/2_Prefabs/Mineral/Ore/Ore_MagicalStone.prefab",
                "Assets/2_Prefabs/Mineral/Ore/Ore_Tin.prefab",
                "Assets/2_Prefabs/Food/Apple.prefab",
                "Assets/2_Prefabs/Food/Berry.prefab",
                "Assets/2_Prefabs/Food/Coconut_Addle.prefab",
                "Assets/2_Prefabs/Food/Coconut_Green.prefab",
                "Assets/2_Prefabs/Food/Coconut_Half.prefab",
                "Assets/2_Prefabs/Food/Coconut_Nude.prefab",
                "Assets/2_Prefabs/Food/Coconut_Shell.prefab",
                "Assets/2_Prefabs/Food/Coconut_Water.prefab",
                "Assets/2_Prefabs/Food/Coconut_WaterSalt.prefab",
                "Assets/2_Prefabs/Food/CoconutMeat.prefab",
                "Assets/2_Prefabs/Food/Egg.prefab",
                "Assets/2_Prefabs/Food/Egg_Cooked.prefab",
                "Assets/2_Prefabs/Food/Fat.prefab",
                "Assets/2_Prefabs/Food/Meat.prefab",
                "Assets/2_Prefabs/Food/Meat_Cooked.prefab",
                "Assets/2_Prefabs/Food/Meat_Dehydrate.prefab",
                "Assets/2_Prefabs/Food/Meat_Rotten.prefab",
                "Assets/2_Prefabs/Food/Tea.prefab"
            }),
        new(
            "WoodTool_Base",
            "Assets/2_Prefabs/Item/Stick_Wood.prefab",
            new[]
            {
                "Assets/2_Prefabs/Item/Stick_Wood.prefab",
                "Assets/2_Prefabs/Item/Log.prefab"
            }),
        new(
            "Axe_Base",
            "Assets/2_Prefabs/Weapon/Axe/Axe_Stone.prefab",
            new[]
            {
                "Assets/2_Prefabs/Weapon/Axe/Axe_Stone.prefab",
                "Assets/2_Prefabs/Weapon/Axe/Axe_Flint.prefab",
                "Assets/2_Prefabs/Weapon/Axe/Axe_Copper.prefab",
                "Assets/2_Prefabs/Weapon/Axe/Axe_Bronze.prefab",
                "Assets/2_Prefabs/Weapon/Axe/Axe_RawIron.prefab",
                "Assets/2_Prefabs/Weapon/Axe/Axe_Iron.prefab"
            }),
        new(
            "Pickaxe_Base",
            "Assets/2_Prefabs/Weapon/Pixkaxe/Pickaxe_Stone.prefab",
            new[]
            {
                "Assets/2_Prefabs/Weapon/Pixkaxe/Pickaxe_Stone.prefab",
                "Assets/2_Prefabs/Weapon/Pixkaxe/Pickaxe_Copper.prefab",
                "Assets/2_Prefabs/Weapon/Pixkaxe/Pickaxe_Bronze.prefab",
                "Assets/2_Prefabs/Weapon/Pixkaxe/Pickaxe_RawIron.prefab",
                "Assets/2_Prefabs/Weapon/Pixkaxe/Pickaxe_Iron.prefab"
            }),
        new(
            "Spear_Base",
            "Assets/2_Prefabs/Weapon/Weapon/Spear_Stone.prefab",
            new[]
            {
                "Assets/2_Prefabs/Weapon/Weapon/Spear_Stone.prefab",
                "Assets/2_Prefabs/Weapon/Weapon/Spear_Copper.prefab",
                "Assets/2_Prefabs/Weapon/Weapon/Spear_Iron.prefab",
                "Assets/2_Prefabs/Weapon/Weapon/Spear_Stone_Animation.prefab"
            }),
        new(
            "Seed_Base",
            "Assets/2_Prefabs/Seed/Seed_Apple.prefab",
            new[] { "Assets/2_Prefabs/Seed/Seed_Apple.prefab" })
    };

    private static bool requestHookInstalled;

    [InitializeOnLoadMethod]
    private static void InstallRequestHook()
    {
        if (requestHookInstalled)
            return;
        requestHookInstalled = true;
        EditorApplication.update += PollMigrationRequest;
    }

    [MenuItem("FlatWorld/物品JSON迁移/预览共享外壳迁移")]
    public static void Preview()
    {
        MigrationPreview preview = BuildPreview();
        Debug.Log($"[ItemDefinitionMigration] 预览：{preview.ItemCount} 个物品 -> " +
                  $"{preview.ShellCount} 个共享外壳；可移出运行时加载 {preview.RedundantPrefabCount} 个 Prefab。\n" +
                  string.Join("\n", preview.GroupLines));
    }

    [MenuItem("FlatWorld/物品JSON迁移/执行全部迁移 %#j")]
    public static void ExportAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出 PlayMode 再执行物品 JSON 迁移。运行时状态不会被迁移器修改。");

        JObject existingRoot = File.Exists(CatalogPath)
            ? JObject.Parse(File.ReadAllText(CatalogPath, Encoding.UTF8))
            : new JObject();
        JArray output = new();
        PreserveManualDefinitions(existingRoot, output);

        var concreteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JObject preserved in output.OfType<JObject>())
        {
            if (preserved.Value<bool?>("abstract") != true)
                concreteIds.Add(preserved.Value<string>("id") ?? string.Empty);
        }

        foreach (MigrationGroup group in Groups)
            ExportGroup(group, output, concreteIds);

        JObject root = new()
        {
            ["schemaVersion"] = ItemDefinitionCatalogLoader.SupportedSchemaVersion,
            ["items"] = output
        };
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath) ?? "Assets/StreamingAssets");
        File.WriteAllText(CatalogPath, root.ToString(Formatting.Indented), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(CatalogPath, ImportAssetOptions.ForceUpdate);
        RebuildRuntimePrefabAddressables();
        AssetDatabase.SaveAssets();

        ValidateCatalogInternal(root, true);
        MigrationPreview preview = BuildPreview();
        Debug.Log($"[ItemDefinitionMigration] 已完成：{preview.ItemCount} 个物品共用 {preview.ShellCount} 个外壳，" +
                  $"{preview.RedundantPrefabCount} 个旧物品 Prefab 不再是运行时依赖。JSON={CatalogPath}");
    }

    [MenuItem("FlatWorld/物品JSON迁移/校验当前目录")]
    public static void ValidateCatalog()
    {
        if (!File.Exists(CatalogPath))
            throw new FileNotFoundException("找不到物品 JSON", CatalogPath);
        ValidateCatalogInternal(JObject.Parse(File.ReadAllText(CatalogPath, Encoding.UTF8)), true);
    }

    private static void PollMigrationRequest()
    {
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        File.Delete(RequestPath);
        try
        {
            ExportAll();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void PreserveManualDefinitions(JObject existingRoot, JArray output)
    {
        if (existingRoot["items"] is not JArray items)
            return;

        foreach (JObject source in items.OfType<JObject>())
        {
            string id = source.Value<string>("id");
            if (!PreservedIds.Contains(id ?? string.Empty))
                continue;

            JObject copy = (JObject)source.DeepClone();
            if (PreservedSourcePaths.TryGetValue(id, out string sourcePath))
                copy["sourcePrefab"] = sourcePath;
            output.Add(copy);
        }
    }

    private static void ExportGroup(MigrationGroup group, JArray output, HashSet<string> concreteIds)
    {
        GameObject shell = LoadPrefab(group.ShellPath);
        Item shellItem = RequireItem(shell, group.ShellPath);
        SpriteRenderer shellRenderer = RequireRenderer(shell, group.ShellPath);
        Collider2D shellCollider = FindItemCollider(shellItem);

        output.Add(new JObject
        {
            ["id"] = group.BaseId,
            ["abstract"] = true,
            ["shellPrefab"] = shell.name,
            ["visual"] = new JObject
            {
                ["rendererPath"] = GetRelativePath(shell.transform, shellRenderer.transform)
            }
        });

        foreach (string sourcePath in group.SourcePaths)
        {
            GameObject source = LoadPrefab(sourcePath);
            Item sourceItem = RequireItem(source, sourcePath);
            if (sourceItem.itemData.GetType() != shellItem.itemData.GetType())
            {
                throw new InvalidDataException(
                    $"{sourcePath} 的 ItemData={sourceItem.itemData.GetType().Name}，与外壳 {group.ShellPath} 不一致");
            }

            string id = sourceItem.itemData.IDName?.Trim();
            if (string.IsNullOrWhiteSpace(id) || !concreteIds.Add(id))
                throw new InvalidDataException($"迁移物品 ID 为空或重复：{id}（{sourcePath}）");

            JObject definition = BuildDefinition(group, sourcePath, sourceItem, shell, shellRenderer, shellCollider);
            output.Add(definition);
        }
    }

    private static JObject BuildDefinition(
        MigrationGroup group,
        string sourcePath,
        Item sourceItem,
        GameObject shell,
        SpriteRenderer shellRenderer,
        Collider2D shellCollider)
    {
        ItemData data = sourceItem.itemData;
        SpriteRenderer sourceRenderer = RequireRenderer(sourceItem.gameObject, sourcePath);
        string spriteAddress = EnsureSpriteAddressable(sourceRenderer.sprite, sourcePath);
        JObject visual = BuildVisual(sourceItem, sourceRenderer, shell, shellRenderer, shellCollider, spriteAddress);
        JObject itemData = SerializeFields(data);
        RemoveProperties(
            itemData,
            "IDName", "GameName", "Description", "Durability", "MaxDurability", "Tags", "Stack", "Guid", "ModuleDataDic");

        JObject modules = new();
        var moduleNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Module module in sourceItem.GetComponentsInChildren<Module>(true))
        {
            if (module?._Data == null)
                continue;

            string moduleId = ResolveModuleId(module);
            string modulePrefab = ResolveModulePrefab(module, moduleId);
            ValidateModuleAvailable(shell, module, moduleId, modulePrefab, sourcePath);
            string stableName = ResolveStableModuleName(module, moduleId, moduleNameCounts);
            JObject moduleData = SerializeFields(module._Data);
            RemoveProperties(
                moduleData,
                "Name", "ID", "isRunning", "RuntimeOwnerItemData", "RuntimeOwnerInventoryData",
                "RuntimeOwnerSlot", "RuntimeOwnerSlotIndex");

            JObject moduleDefinition = new()
            {
                ["prefab"] = modulePrefab,
                ["id"] = moduleId,
                ["enabled"] = module._Data.isRunning
            };
            if (moduleData.HasValues)
                moduleDefinition["data"] = moduleData;

            JObject parameters = SerializeModuleParameters(module);
            if (parameters.HasValues)
                moduleDefinition["parameters"] = parameters;
            modules[stableName] = moduleDefinition;
        }

        JObject definition = new()
        {
            ["id"] = data.IDName,
            ["parent"] = group.BaseId,
            ["sourcePrefab"] = sourcePath,
            ["gameName"] = data.GameName,
            ["description"] = data.Description,
            ["durability"] = data.Durability,
            ["maxDurability"] = data.MaxDurability,
            ["amount"] = data.Stack?.Amount ?? 1f,
            ["volume"] = data.Stack?.Volume ?? 0f,
            ["canBePickedUp"] = data.Stack?.CanBePickedUp ?? true,
            ["tags"] = data.Tags != null ? JArray.FromObject(data.Tags) : new JArray(),
            ["visual"] = visual,
            ["modules"] = modules
        };
        if (itemData.HasValues)
            definition["itemData"] = itemData;
        return definition;
    }

    private static JObject BuildVisual(
        Item sourceItem,
        SpriteRenderer sourceRenderer,
        GameObject shell,
        SpriteRenderer shellRenderer,
        Collider2D shellCollider,
        string spriteAddress)
    {
        JObject visual = new()
        {
            ["spriteAddress"] = spriteAddress,
            ["rendererLocalPosition"] = Vector3Token(sourceRenderer.transform.localPosition),
            ["rendererLocalEulerAngles"] = Vector3Token(sourceRenderer.transform.localEulerAngles),
            ["rendererLocalScale"] = Vector3Token(sourceRenderer.transform.localScale),
            ["color"] = ColorToken(sourceRenderer.color),
            ["flipX"] = sourceRenderer.flipX,
            ["flipY"] = sourceRenderer.flipY,
            ["sortingLayerName"] = sourceRenderer.sortingLayerName,
            ["sortingOrder"] = sourceRenderer.sortingOrder
        };

        Collider2D sourceCollider = FindItemCollider(sourceItem);
        if (sourceCollider != null || shellCollider != null)
        {
            if (sourceCollider == null || shellCollider == null || sourceCollider.GetType() != shellCollider.GetType())
            {
                throw new InvalidDataException(
                    $"物品 {sourceItem.itemData.IDName} 与外壳 {shell.name} 的主碰撞体类型不一致");
            }
            visual["collider"] = SerializeCollider(
                sourceCollider,
                GetRelativePath(shell.transform, shellCollider.transform));
        }

        return visual;
    }

    private static JObject SerializeModuleParameters(Module module)
    {
        JObject parameters = new();
        for (Type type = module.GetType(); type != null && type != typeof(Module); type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!IsUnitySerializedField(field) || IsIgnoredField(field) ||
                    typeof(ModuleData).IsAssignableFrom(field.FieldType) ||
                    typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType) ||
                    typeof(Delegate).IsAssignableFrom(field.FieldType) ||
                    typeof(UnityEventBase).IsAssignableFrom(field.FieldType) ||
                    field.FieldType.FullName?.Contains("UltEvent", StringComparison.Ordinal) == true)
                {
                    continue;
                }

                if (TrySerializeValue(field.GetValue(module), field.FieldType, new HashSet<object>(ReferenceComparer.Instance), 0,
                        out JToken token))
                {
                    parameters[field.Name] = token;
                }
            }
        }

        parameters["$transform"] = new JObject
        {
            ["localPosition"] = Vector3Token(module.transform.localPosition),
            ["localEulerAngles"] = Vector3Token(module.transform.localEulerAngles),
            ["localScale"] = Vector3Token(module.transform.localScale)
        };
        Collider2D collider = module.GetComponent<Collider2D>();
        if (collider != null)
            parameters["$collider2D"] = SerializeCollider(collider, null);
        return parameters;
    }

    private static JObject SerializeFields(object value)
    {
        if (value == null)
            return new JObject();

        JObject result = new();
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        for (Type type = value.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!IsUnitySerializedField(field) || IsIgnoredField(field))
                    continue;
                if (TrySerializeValue(field.GetValue(value), field.FieldType, visited, 0, out JToken token))
                    result[field.Name] = token;
            }
        }
        return result;
    }

    private static bool TrySerializeValue(
        object value,
        Type declaredType,
        HashSet<object> visited,
        int depth,
        out JToken token)
    {
        token = null;
        if (depth > 10 || typeof(UnityEngine.Object).IsAssignableFrom(declaredType) ||
            typeof(Delegate).IsAssignableFrom(declaredType) || typeof(UnityEventBase).IsAssignableFrom(declaredType) ||
            declaredType.FullName?.Contains("UltEvent", StringComparison.Ordinal) == true)
        {
            return false;
        }

        if (value == null)
        {
            token = JValue.CreateNull();
            return true;
        }

        Type type = value.GetType();
        if (type.IsEnum)
        {
            token = new JValue(Convert.ToInt32(value));
            return true;
        }
        if (type == typeof(string) || type == typeof(char) || type == typeof(bool) || type.IsPrimitive ||
            type == typeof(decimal))
        {
            token = JToken.FromObject(value);
            return true;
        }
        if (value is Vector2 vector2) { token = Vector2Token(vector2); return true; }
        if (value is Vector3 vector3) { token = Vector3Token(vector3); return true; }
        if (value is Vector2Int vector2Int)
        {
            token = new JObject { ["x"] = vector2Int.x, ["y"] = vector2Int.y };
            return true;
        }
        if (value is Vector3Int vector3Int)
        {
            token = new JObject { ["x"] = vector3Int.x, ["y"] = vector3Int.y, ["z"] = vector3Int.z };
            return true;
        }
        if (value is Vector4 vector4)
        {
            token = new JObject { ["x"] = vector4.x, ["y"] = vector4.y, ["z"] = vector4.z, ["w"] = vector4.w };
            return true;
        }
        if (value is Quaternion quaternion)
        {
            token = new JObject { ["x"] = quaternion.x, ["y"] = quaternion.y, ["z"] = quaternion.z, ["w"] = quaternion.w };
            return true;
        }
        if (value is Color color) { token = ColorToken(color); return true; }
        if (value is LayerMask layerMask) { token = new JValue(layerMask.value); return true; }

        bool trackReference = !type.IsValueType;
        if (trackReference && !visited.Add(value))
            return false;
        try
        {
            if (value is IDictionary dictionary)
            {
                JObject obj = new();
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is not string key ||
                        !TrySerializeValue(entry.Value, entry.Value?.GetType() ?? typeof(object), visited, depth + 1,
                            out JToken child))
                        continue;
                    obj[key] = child;
                }
                token = obj;
                return true;
            }
            if (value is IEnumerable enumerable)
            {
                JArray array = new();
                foreach (object element in enumerable)
                {
                    if (TrySerializeValue(element, element?.GetType() ?? typeof(object), visited, depth + 1,
                            out JToken child))
                        array.Add(child);
                }
                token = array;
                return true;
            }

            JObject result = new();
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!IsUnitySerializedField(field) || IsIgnoredField(field))
                        continue;
                    if (TrySerializeValue(field.GetValue(value), field.FieldType, visited, depth + 1, out JToken child))
                        result[field.Name] = child;
                }
            }
            token = result;
            return true;
        }
        finally
        {
            if (trackReference)
                visited.Remove(value);
        }
    }

    private static bool IsUnitySerializedField(FieldInfo field)
    {
        return !field.IsStatic && !field.IsInitOnly && !field.IsNotSerialized &&
               (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null ||
                field.GetCustomAttribute<SerializeReference>() != null);
    }

    private static bool IsIgnoredField(FieldInfo field)
    {
        if (field.Name.StartsWith("Runtime", StringComparison.Ordinal) ||
            field.GetCustomAttribute<JsonIgnoreAttribute>() != null)
            return true;

        return field.GetCustomAttributes(true).Any(attribute =>
            attribute.GetType().Name is "MemoryPackIgnoreAttribute" or "FastClonerIgnoreAttribute");
    }

    private static JObject SerializeCollider(Collider2D collider, string path)
    {
        JObject data = new()
        {
            ["type"] = collider.GetType().Name,
            ["enabled"] = collider.enabled,
            ["isTrigger"] = collider.isTrigger,
            ["offset"] = Vector2Token(collider.offset)
        };
        if (path != null)
            data["path"] = path;

        switch (collider)
        {
            case BoxCollider2D box:
                data["size"] = Vector2Token(box.size);
                break;
            case CircleCollider2D circle:
                data["radius"] = circle.radius;
                break;
            case CapsuleCollider2D capsule:
                data["size"] = Vector2Token(capsule.size);
                data["direction"] = (int)capsule.direction;
                break;
            case PolygonCollider2D polygon:
                data["points"] = JArray.FromObject(polygon.GetPath(0));
                break;
        }
        return data;
    }

    private static string ResolveModuleId(Module module)
    {
        if (!string.IsNullOrWhiteSpace(module._Data?.ID))
            return module._Data.ID.Trim();

        return module.GetType().Name;
    }

    private static string ResolveModulePrefab(Module module, string fallbackId)
    {
        UnityEngine.Object original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(module.gameObject);
        string assetPath = original != null ? AssetDatabase.GetAssetPath(original) : null;
        if (!string.IsNullOrWhiteSpace(assetPath) &&
            assetPath.StartsWith("Assets/2_Prefabs/Module/", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(assetPath);

        return module.GetType().Name;
    }

    private static string ResolveStableModuleName(
        Module module,
        string moduleId,
        Dictionary<string, int> nameCounts)
    {
        string baseName = module switch
        {
            Mod_Weapon_AnimationAction => "animation",
            Mod_Damage => "damage",
            Mod_Food => "food",
            _ => moduleId
        };
        nameCounts.TryGetValue(baseName, out int count);
        nameCounts[baseName] = ++count;
        return count == 1 ? baseName : $"{baseName}_{count}";
    }

    private static void ValidateModuleAvailable(
        GameObject shell,
        Module source,
        string moduleId,
        string modulePrefab,
        string sourcePath)
    {
        bool inShell = shell.GetComponentsInChildren<Module>(true).Any(candidate =>
            candidate.GetType() == source.GetType() ||
            string.Equals(candidate._Data?.ID, moduleId, StringComparison.OrdinalIgnoreCase));
        if (inShell)
            return;

        bool prefabExists = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/2_Prefabs/Module" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Any(path => string.Equals(
                Path.GetFileNameWithoutExtension(path), modulePrefab, StringComparison.OrdinalIgnoreCase));
        if (!prefabExists)
            throw new InvalidDataException(
                $"{sourcePath} 的模块 {moduleId} 既不在共享外壳中，也没有独立 Module Prefab：{modulePrefab}");
    }

    private static string EnsureSpriteAddressable(Sprite sprite, string sourcePath)
    {
        if (sprite == null)
            throw new InvalidDataException($"{sourcePath} 缺少 Sprite");
        string assetPath = AssetDatabase.GetAssetPath(sprite);
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(guid))
            throw new InvalidDataException($"{sourcePath} 的 Sprite 不是项目资源：{sprite.name}");

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("AddressableAssetSettings 未初始化");
        AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
        entry.address = assetPath;
        entry.SetLabel(ItemSpriteLabel, true, true);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);

        UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        return ReferenceEquals(mainAsset, sprite) ? assetPath : $"{assetPath}[{sprite.name}]";
    }

    private static void RebuildRuntimePrefabAddressables()
    {
        const string prefabRoot = "Assets/2_Prefabs";
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            throw new InvalidOperationException("AddressableAssetSettings 未初始化");

        HashSet<string> redundant = ItemDefinitionCatalogLoader.GetRedundantBuiltInPrefabPaths();
        string folderGuid = AssetDatabase.AssetPathToGUID(prefabRoot);
        if (!string.IsNullOrWhiteSpace(folderGuid))
            settings.RemoveAssetEntry(folderGuid, false);

        int included = 0;
        int excluded = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { prefabRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            if (redundant.Contains(path))
            {
                settings.RemoveAssetEntry(guid, false);
                excluded++;
                continue;
            }

            AddressableAssetEntry entry = settings.FindAssetEntry(guid);
            entry ??= settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
            entry.address = path;
            entry.SetLabel("Prefab", true, true, false);
            included++;
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true);
        Debug.Log($"[ItemDefinitionMigration] Addressables Prefab 白名单：保留 {included}，排除迁移源 {excluded}");
    }

    private static void ValidateCatalogInternal(JObject root, bool logSuccess)
    {
        List<ItemDefinitionDto> resolved = ItemDefinitionCatalogLoader.ResolveDefinitions(root.ToString(Formatting.None));
        var ids = new HashSet<string>(resolved.Where(definition => !definition.Abstract).Select(definition => definition.Id),
            StringComparer.OrdinalIgnoreCase);
        foreach (MigrationGroup group in Groups)
        {
            foreach (string sourcePath in group.SourcePaths)
            {
                Item item = RequireItem(LoadPrefab(sourcePath), sourcePath);
                if (!ids.Contains(item.itemData.IDName))
                    throw new InvalidDataException($"JSON 缺少迁移物品：{item.itemData.IDName}");
            }
        }

        if (logSuccess)
            Debug.Log($"[ItemDefinitionMigration] 校验通过：{ids.Count} 个运行时物品定义。 ");
    }

    private static MigrationPreview BuildPreview()
    {
        var lines = new List<string>();
        int itemCount = PreservedIds.Count - 1;
        int redundant = Math.Max(0, PreservedIds.Count - 2);
        var shells = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Dagger_Stone" };
        foreach (MigrationGroup group in Groups)
        {
            itemCount += group.SourcePaths.Length;
            redundant += Math.Max(0, group.SourcePaths.Length - 1);
            shells.Add(Path.GetFileNameWithoutExtension(group.ShellPath));
            lines.Add($"{group.BaseId}: {group.SourcePaths.Length} -> {Path.GetFileNameWithoutExtension(group.ShellPath)}");
        }
        return new MigrationPreview(itemCount, shells.Count, redundant, lines);
    }

    private static GameObject LoadPrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        return prefab != null ? prefab : throw new FileNotFoundException("找不到迁移 Prefab", path);
    }

    private static Item RequireItem(GameObject prefab, string path)
    {
        Item item = prefab.GetComponent<Item>();
        if (item?.itemData == null)
            throw new InvalidDataException($"迁移 Prefab 缺少 Item/itemData：{path}");
        return item;
    }

    private static SpriteRenderer RequireRenderer(GameObject prefab, string path)
    {
        SpriteRenderer renderer = prefab.GetComponentsInChildren<SpriteRenderer>(true)
            .FirstOrDefault(candidate => candidate.sprite != null) ?? prefab.GetComponentInChildren<SpriteRenderer>(true);
        return renderer != null ? renderer : throw new InvalidDataException($"迁移 Prefab 缺少 SpriteRenderer：{path}");
    }

    private static Collider2D FindItemCollider(Item item)
    {
        return item.GetComponentsInChildren<Collider2D>(true)
            .FirstOrDefault(collider => collider.GetComponentInParent<Module>(true) == null);
    }

    private static string GetRelativePath(Transform root, Transform child)
    {
        if (root == child)
            return string.Empty;
        var parts = new Stack<string>();
        Transform current = child;
        while (current != null && current != root)
        {
            parts.Push(current.name);
            current = current.parent;
        }
        if (current != root)
            throw new InvalidOperationException($"{child.name} 不属于 {root.name}");
        return string.Join("/", parts);
    }

    private static void RemoveProperties(JObject target, params string[] names)
    {
        foreach (string name in names)
            target.Property(name, StringComparison.OrdinalIgnoreCase)?.Remove();
    }

    private static JObject Vector2Token(Vector2 value) => new() { ["x"] = value.x, ["y"] = value.y };
    private static JObject Vector3Token(Vector3 value) => new() { ["x"] = value.x, ["y"] = value.y, ["z"] = value.z };
    private static JObject ColorToken(Color value) =>
        new() { ["r"] = value.r, ["g"] = value.g, ["b"] = value.b, ["a"] = value.a };

    private sealed class MigrationGroup
    {
        public MigrationGroup(string baseId, string shellPath, string[] sourcePaths)
        {
            BaseId = baseId;
            ShellPath = shellPath;
            SourcePaths = sourcePaths;
        }

        public string BaseId { get; }
        public string ShellPath { get; }
        public string[] SourcePaths { get; }
    }

    private sealed class MigrationPreview
    {
        public MigrationPreview(int itemCount, int shellCount, int redundantPrefabCount, List<string> groupLines)
        {
            ItemCount = itemCount;
            ShellCount = shellCount;
            RedundantPrefabCount = redundantPrefabCount;
            GroupLines = groupLines;
        }

        public int ItemCount { get; }
        public int ShellCount { get; }
        public int RedundantPrefabCount { get; }
        public List<string> GroupLines { get; }
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
