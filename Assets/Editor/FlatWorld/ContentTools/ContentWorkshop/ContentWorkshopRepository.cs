#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlatWorld.Editor.ContentWorkshop
{
    /// <summary>
    /// 内容工坊的数据仓库。统一读取 Manifest、解析继承、缓存图标、执行全目录校验并安全写回 JSON。
    /// 写盘前检查文件指纹，避免覆盖窗口打开后由外部工具产生的修改。
    /// </summary>
    internal sealed class ContentWorkshopRepository
    {
        #region 常量

        private const string ItemRootAssetPath = "Assets/StreamingAssets/GameConfig/Items";
        private const string ItemManifestAssetPath = ItemRootAssetPath + "/item-manifest.json";
        private const string RecipeRootAssetPath = "Assets/StreamingAssets/GameConfig/Recipes";
        private const string RecipeManifestAssetPath = RecipeRootAssetPath + "/recipe-manifest.json";
        private const string BackupRoot = "Library/FlatWorldContentWorkshop/Backups";
        private const string ItemSpriteLabel = "ItemSprite";

        private static readonly string[] KnownRecipeProperties =
        {
            "id", "displayName", "recipeType", "inputRule", "gridWidth", "gridHeight", "allowMirror",
            "temperature", "maxTemperature", "inputs", "outputs", "actions"
        };

        #endregion

        #region 字段与属性

        private readonly Dictionary<string, JObject> itemRoots =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, JObject> recipeRoots =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> itemPackagePaths =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> recipePackagePaths =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> loadedFileFingerprints =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, WorkshopItemEntry> itemsById =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, JObject> itemSourcesById =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> itemSourcePackagesById =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<WorkshopItemEntry> Items { get; private set; } = Array.Empty<WorkshopItemEntry>();
        public IReadOnlyList<WorkshopRecipeRecord> Recipes { get; private set; } = Array.Empty<WorkshopRecipeRecord>();
        public IReadOnlyList<WorkshopPackageOption> ItemPackages { get; private set; } =
            Array.Empty<WorkshopPackageOption>();
        public IReadOnlyList<WorkshopPackageOption> RecipePackages { get; private set; } =
            Array.Empty<WorkshopPackageOption>();
        public IReadOnlyList<string> KnownTags { get; private set; } = Array.Empty<string>();
        public string LastValidationSummary { get; private set; } = "尚未读取配置";

        #endregion

        #region 读取

        /// <summary>重新读取所有启用分包，并使用运行时加载器执行结构校验。</summary>
        public void Reload()
        {
            itemRoots.Clear();
            recipeRoots.Clear();
            itemPackagePaths.Clear();
            recipePackagePaths.Clear();
            loadedFileFingerprints.Clear();
            itemsById.Clear();
            itemSourcesById.Clear();
            itemSourcePackagesById.Clear();

            LoadItems();
            LoadRecipes();
            KnownTags = Items
                .SelectMany(entry => entry.Definition?.Tags ?? Enumerable.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            LastValidationSummary =
                $"已验证 {Items.Count(entry => entry.Definition?.Abstract != true)} 个可用物品、{Recipes.Count} 条配方";
        }

        public WorkshopItemEntry FindItem(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;
            itemsById.TryGetValue(id.Trim(), out WorkshopItemEntry entry);
            return entry;
        }

        public WorkshopRecipeRecord FindRecipe(string id)
        {
            return string.IsNullOrWhiteSpace(id)
                ? null
                : Recipes.FirstOrDefault(record =>
                    string.Equals(record.Definition?.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private void LoadItems()
        {
            string manifestText = ReadTrackedText(ItemManifestAssetPath);
            ItemDefinitionManifestDto manifest = ItemDefinitionCatalogLoader.DeserializeManifest(manifestText);
            ItemDefinitionCatalogLoader.ValidateManifest(manifest);

            var packageOptions = new List<WorkshopPackageOption>();
            foreach (ItemDefinitionPackageDto package in manifest.Packages ?? new List<ItemDefinitionPackageDto>())
            {
                if (package == null)
                    continue;
                string packageId = package.Id.Trim();
                string assetPath = ResolveAssetPackagePath(ItemRootAssetPath, package.Path);
                packageOptions.Add(new WorkshopPackageOption
                {
                    Id = packageId,
                    RelativePath = package.Path,
                    ShellPrefab = package.ShellPrefab,
                    Enabled = package.Enabled
                });
                if (!package.Enabled)
                    continue;

                string json = ReadTrackedText(assetPath);
                JObject root = ParseCatalogRoot(json, "items", packageId);
                itemRoots[packageId] = root;
                itemPackagePaths[packageId] = assetPath;

                foreach (JObject source in ((JArray)root["items"]).OfType<JObject>())
                {
                    string id = source.Value<string>("id")?.Trim();
                    if (string.IsNullOrWhiteSpace(id) || !itemSourcesById.TryAdd(id, source))
                        throw new InvalidDataException($"跨分包存在重复或空物品 ID：{id ?? "<空>"}");
                    itemSourcePackagesById[id] = packageId;
                }
            }

            // 与游戏启动使用同一入口，除了继承关系，还会校验分包声明的 shellPrefab 边界。
            List<ItemDefinitionDto> definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
            var entries = new List<WorkshopItemEntry>(definitions.Count);
            foreach (ItemDefinitionDto definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                    continue;
                itemSourcesById.TryGetValue(definition.Id, out JObject source);
                itemSourcePackagesById.TryGetValue(definition.Id, out string packageId);
                WorkshopItemCategory category = InferCategory(packageId, definition);
                string displayName = string.IsNullOrWhiteSpace(definition.GameName)
                    ? definition.Id
                    : definition.GameName.Trim();
                var entry = new WorkshopItemEntry
                {
                    Id = definition.Id,
                    DisplayName = displayName,
                    Description = definition.Description ?? string.Empty,
                    PackageId = packageId,
                    Category = category,
                    Definition = definition,
                    Source = source,
                    Icon = LoadItemSprite(definition),
                    SearchTerms = BuildSearchTerms(definition, displayName, packageId, category)
                };
                entries.Add(entry);
                itemsById[entry.Id] = entry;
            }

            ItemPackages = packageOptions;
            Items = entries
                .OrderBy(entry => entry.Definition?.Abstract == true)
                .ThenBy(entry => entry.Category)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void LoadRecipes()
        {
            string manifestText = ReadTrackedText(RecipeManifestAssetPath);
            RecipeManifestDto manifest = RecipeCatalogLoader.DeserializeManifest(manifestText);
            RecipeCatalogLoader.ValidateManifest(manifest);

            var packageOptions = new List<WorkshopPackageOption>();
            var records = new List<WorkshopRecipeRecord>();
            var recipeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RecipePackageDto package in manifest.Packages ?? new List<RecipePackageDto>())
            {
                if (package == null)
                    continue;
                string packageId = package.Id.Trim();
                string assetPath = ResolveAssetPackagePath(RecipeRootAssetPath, package.Path);
                packageOptions.Add(new WorkshopPackageOption
                {
                    Id = packageId,
                    RelativePath = package.Path,
                    Enabled = package.Enabled
                });
                if (!package.Enabled)
                    continue;

                string json = ReadTrackedText(assetPath);
                JObject root = ParseCatalogRoot(json, "recipes", packageId);
                RecipeCatalogDto catalog = RecipeRuntimeFactory.Deserialize(json);
                RecipeRuntimeFactory.BuildCatalog(catalog, ItemExists, out List<string> warnings);
                if (warnings.Count > 0)
                    throw new InvalidDataException($"配方分包 {packageId} 引用无效：{string.Join("；", warnings)}");

                recipeRoots[packageId] = root;
                recipePackagePaths[packageId] = assetPath;
                var sources = ((JArray)root["recipes"])
                    .OfType<JObject>()
                    .ToDictionary(source => source.Value<string>("id") ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase);
                foreach (RecipeDto definition in catalog.Recipes ?? new List<RecipeDto>())
                {
                    if (!recipeIds.Add(definition.Id))
                        throw new InvalidDataException($"跨分包存在重复配方 ID：{definition.Id}");
                    sources.TryGetValue(definition.Id, out JObject source);
                    records.Add(new WorkshopRecipeRecord
                    {
                        PackageId = packageId,
                        PackagePath = assetPath,
                        Definition = definition,
                        Source = source
                    });
                }
            }

            RecipePackages = packageOptions;
            Recipes = records
                .OrderBy(record => record.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        #endregion

        #region 配方写入

        /// <summary>保存或移动配方；全目录验证通过后才一次性覆盖涉及的分包。</summary>
        public void SaveRecipe(WorkshopRecipeDraft draft)
        {
            if (draft == null)
                throw new ArgumentNullException(nameof(draft));
            RecipeDto definition = draft.BuildDto();
            EnsureFileUnchanged(RecipeManifestAssetPath);
            if (!recipePackagePaths.ContainsKey(draft.PackageId ?? string.Empty))
                throw new InvalidOperationException($"找不到目标配方分包：{draft.PackageId}");

            ValidateRecipeDefinition(definition, draft.OriginalId);
            var proposedRoots = ReadCurrentRecipeRoots();
            if (draft.IsExisting)
            {
                if (!proposedRoots.TryGetValue(draft.OriginalPackageId, out JObject originalRoot) ||
                    !RemoveById((JArray)originalRoot["recipes"], draft.OriginalId))
                {
                    throw new InvalidOperationException(
                        $"原配方 {draft.OriginalId} 已被外部修改或删除，请重新加载内容工坊。");
                }
            }

            JObject targetRoot = proposedRoots[draft.PackageId];
            ((JArray)targetRoot["recipes"]).Add(BuildRecipeSource(definition, draft.OriginalSource));
            ValidateAllRecipes(proposedRoots);

            var writes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (draft.IsExisting)
                writes[recipePackagePaths[draft.OriginalPackageId]] =
                    proposedRoots[draft.OriginalPackageId].ToString(Formatting.Indented);
            writes[recipePackagePaths[draft.PackageId]] =
                proposedRoots[draft.PackageId].ToString(Formatting.Indented);
            WriteTrackedFiles(writes);
            Reload();
        }

        private void ValidateRecipeDefinition(RecipeDto definition, string originalId)
        {
            if (Recipes.Any(record =>
                    !string.Equals(record.Definition.Id, originalId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(record.Definition.Id, definition.Id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"配方稳定 ID 已存在：{definition.Id}");
            }

            var warnings = new List<string>();
            RecipeRuntimeFactory.Build(definition, ItemExists, warnings);
            if (warnings.Count > 0)
                throw new InvalidOperationException(string.Join("；", warnings));
        }

        private void ValidateAllRecipes(IReadOnlyDictionary<string, JObject> roots)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WorkshopPackageOption package in RecipePackages.Where(package => package.Enabled))
            {
                if (!roots.TryGetValue(package.Id, out JObject root))
                    throw new InvalidDataException($"缺少配方分包：{package.Id}");
                RecipeCatalogDto catalog = RecipeRuntimeFactory.Deserialize(root.ToString(Formatting.None));
                List<RuntimeRecipe> recipes = RecipeRuntimeFactory.BuildCatalog(catalog, ItemExists,
                    out List<string> warnings);
                if (warnings.Count > 0)
                    throw new InvalidDataException($"配方分包 {package.Id} 引用无效：{string.Join("；", warnings)}");
                foreach (RuntimeRecipe recipe in recipes)
                {
                    if (!ids.Add(recipe.Id))
                        throw new InvalidDataException($"跨分包存在重复配方 ID：{recipe.Id}");
                }
            }
        }

        #endregion

        #region 物品写入

        /// <summary>使用已验证模板创建物品差异定义，并将所选 Sprite 注册为稳定 Addressables 地址。</summary>
        public void CreateItem(WorkshopItemDraft draft, WorkshopItemTemplate template)
        {
            if (draft == null)
                throw new ArgumentNullException(nameof(draft));
            if (template == null)
                throw new ArgumentNullException(nameof(template));
            EnsureFileUnchanged(ItemManifestAssetPath);
            ValidateItemDraft(draft, template);

            string packageId = template.PackageId;
            if (!itemPackagePaths.ContainsKey(packageId))
                throw new InvalidOperationException($"找不到物品模板目标分包：{packageId}");

            string spriteAddress = BuildSpriteAddress(draft.Icon);
            JObject definition = BuildItemSource(draft, template, spriteAddress);
            var proposedRoots = ReadCurrentItemRoots();
            ((JArray)proposedRoots[packageId]["items"]).Add(definition);
            ValidateAllItems(proposedRoots);

            // Addressables 是独立资产；修改它之前再次锁定目标 JSON，尽早阻止外部编辑冲突。
            EnsureFileUnchanged(itemPackagePaths[packageId]);
            EnsureSpriteAddressable(draft.Icon, spriteAddress);
            string path = itemPackagePaths[packageId];
            WriteTrackedFiles(new Dictionary<string, string>
            {
                [path] = proposedRoots[packageId].ToString(Formatting.Indented)
            });
            Reload();
        }

        private void ValidateItemDraft(WorkshopItemDraft draft, WorkshopItemTemplate template)
        {
            if (string.IsNullOrWhiteSpace(draft.Id))
                throw new InvalidOperationException("物品稳定 ID 不能为空。");
            if (itemsById.ContainsKey(draft.Id.Trim()))
                throw new InvalidOperationException($"物品稳定 ID 已存在：{draft.Id}");
            if (string.IsNullOrWhiteSpace(draft.DisplayName))
                throw new InvalidOperationException("请填写玩家看到的物品名称。");
            if (draft.Icon == null)
                throw new InvalidOperationException("请为物品选择一个 Sprite 图标。");
            if (draft.Durability < 0f || draft.Amount <= 0f || draft.Volume < 0f)
                throw new InvalidOperationException("耐久不得为负数，初始数量必须大于 0，体积不得为负数。");
            if (draft.AddFoodAbility)
            {
                if (draft.FoodMaxEatingProgress < 1f)
                    throw new InvalidOperationException("完整进食次数必须至少为 1。");
                if (draft.FoodEnableSpoilage && string.IsNullOrWhiteSpace(draft.FoodSpoilageTargetItemId))
                    throw new InvalidOperationException("启用腐败时必须填写腐败目标物品 ID。");
                if (HasNegativeFoodValue(draft))
                    throw new InvalidOperationException("食物参数不能为负数。");
            }
            if (!itemSourcesById.ContainsKey(template.ParentId))
                throw new InvalidOperationException($"物品模板父定义不存在：{template.ParentId}");
            if (!itemSourcesById.ContainsKey(template.ReferenceItemId))
                throw new InvalidOperationException($"物品模板参考定义不存在：{template.ReferenceItemId}");
        }

        private JObject BuildItemSource(
            WorkshopItemDraft draft,
            WorkshopItemTemplate template,
            string spriteAddress)
        {
            var definition = new JObject
            {
                ["id"] = draft.Id.Trim(),
                ["parent"] = template.ParentId,
                ["gameName"] = draft.DisplayName.Trim(),
                ["description"] = draft.Description?.Trim() ?? string.Empty,
                ["durability"] = draft.Durability,
                ["maxDurability"] = draft.Durability,
                ["amount"] = draft.Amount,
                ["volume"] = draft.Volume,
                ["canBePickedUp"] = draft.CanBePickedUp,
                ["tags"] = new JArray(BuildTemplateTags(draft, template)),
                ["visual"] = new JObject
                {
                    ["spriteAddress"] = spriteAddress,
                    ["color"] = ColorToken(draft.Tint),
                    ["rendererLocalEulerAngles"] = RotationToken(draft.RotationDegrees),
                    ["flipX"] = draft.FlipX,
                    ["flipY"] = draft.FlipY
                }
            };

            JObject modules = BuildTemplateModules(draft, template);
            if (modules.HasValues)
                definition["modules"] = modules;
            return definition;
        }

        private JObject BuildTemplateModules(WorkshopItemDraft draft, WorkshopItemTemplate template)
        {
            var moduleNames = new HashSet<string>(template.DefaultModuleNames, StringComparer.OrdinalIgnoreCase);
            if (draft.AddFoodAbility)
                moduleNames.Add("food");
            if (draft.AddFuelAbility)
                moduleNames.Add("燃料模块");
            if (draft.AddCombatAbility)
            {
                moduleNames.Add("damage");
                if (template.Kind == WorkshopItemTemplateKind.Tool)
                    moduleNames.Add("animation");
            }
            if (draft.AddEquipmentAbility)
                moduleNames.Add("Module_Equipment_Store");

            var modules = new JObject();
            foreach (string moduleName in moduleNames)
            {
                JObject module = FindReferenceModule(template, moduleName);
                if (module == null)
                    throw new InvalidOperationException(
                        $"模板 {template.DisplayName} 找不到能力模块：{moduleName}");
                modules[moduleName] = module.DeepClone();
            }

            if (modules["damage"] is JObject damageModule)
            {
                JObject parameters = EnsureObject(damageModule, "parameters");
                parameters["DamageValues"] = new JObject
                {
                    ["Cutting"] = Mathf.Max(0f, draft.CuttingDamage),
                    ["Piercing"] = Mathf.Max(0f, draft.PiercingDamage),
                    ["Chopping"] = Mathf.Max(0f, draft.ChoppingDamage),
                    ["Blunt"] = Mathf.Max(0f, draft.BluntDamage)
                };
                parameters.Remove("Weakness");
                parameters.Remove("Damage");
            }

            if (draft.AddFoodAbility &&
                modules.GetValue("food", StringComparison.OrdinalIgnoreCase) is JObject foodModule)
            {
                ApplyFoodParameters(foodModule, draft);
            }
            return modules;
        }

        #region 食物模块写入

        /// <summary>将工坊食物参数写入食物基础数据和观察者状态负载。</summary>
        private static void ApplyFoodParameters(JObject foodModule, WorkshopItemDraft draft)
        {
            JObject data = EnsureObject(foodModule, "data");
            JObject foodData = EnsureObject(data, "FoodData");
            JObject nutrition = EnsureObject(foodData, "nutrition");
            nutrition["Carbohydrates"] = Mathf.Max(0f, draft.FoodCarbohydrates);
            nutrition["Max_Carbohydrates"] = Mathf.Max(0f, draft.FoodMaxCarbohydrates);
            nutrition["Fat"] = Mathf.Max(0f, draft.FoodFat);
            nutrition["Max_Fat"] = Mathf.Max(0f, draft.FoodMaxFat);
            nutrition["Protein"] = Mathf.Max(0f, draft.FoodProtein);
            nutrition["Max_Protein"] = Mathf.Max(0f, draft.FoodMaxProtein);
            nutrition["Water"] = Mathf.Max(0f, draft.FoodWater);
            nutrition["Max_Water"] = Mathf.Max(0f, draft.FoodMaxWater);
            nutrition["Vitamins"] = Mathf.Max(0f, draft.FoodVitamins);
            nutrition["Max_Vitamins"] = Mathf.Max(0f, draft.FoodMaxVitamins);
            foodData["Max_EatingProgress"] = Mathf.Max(1f, draft.FoodMaxEatingProgress);
            EnsureObject(foodData, "nutritionConsumeSpeed")["BaseValue"] =
                Mathf.Max(0f, draft.FoodNutritionConsumeSpeed);
            foodData["WaterConsumeSpeedRate"] = Mathf.Max(0f, draft.FoodWaterConsumeSpeedRate);
            foodData["nutritionConsumeRate"] = Mathf.Max(0f, draft.FoodNutritionConsumeRate);
            data.Remove("EnableSpoilage");
            data.Remove("SpoilageElapsedSeconds");
            data.Remove("SpoilageIntervalSeconds");
            data.Remove("SpoilageTargetItemID");
            JArray mechanicStates = EnsureArray(data, "MechanicStates");
            JObject spoilageState = EnsureMechanicState(mechanicStates, FoodObserverStateStore.SpoilageStateKey);
            JObject spoilageData = EnsureObject(spoilageState, "Data");
            spoilageData["EnableSpoilage"] = draft.FoodEnableSpoilage;
            spoilageData["SpoilageElapsedSeconds"] = 0f;
            spoilageData["SpoilageIntervalSeconds"] = Mathf.Max(0f, draft.FoodSpoilageIntervalSeconds);
            spoilageData["SpoilageTargetItemID"] = draft.FoodSpoilageTargetItemId?.Trim() ?? string.Empty;

            JObject consumptionState = EnsureMechanicState(mechanicStates, FoodObserverStateStore.ConsumptionStateKey);
            JObject consumptionData = EnsureObject(consumptionState, "Data");
            consumptionData["EatingProgress"] = 0f;

            JObject parameters = EnsureObject(foodModule, "parameters");
            parameters.Remove("EatingProgress");
            parameters["ConsumeKind"] = Mathf.Clamp(draft.FoodConsumeKind, 0, 1);
        }

        /// <summary>确保模块 JSON 中存在可写的对象节点。</summary>
        private static JObject EnsureObject(JObject source, string propertyName)
        {
            if (source[propertyName] is JObject existing)
                return existing;

            var created = new JObject();
            source[propertyName] = created;
            return created;
        }

        /// <summary>确保模块 JSON 中存在可写的数组节点。</summary>
        private static JArray EnsureArray(JObject source, string propertyName)
        {
            if (source[propertyName] is JArray existing)
                return existing;

            var created = new JArray();
            source[propertyName] = created;
            return created;
        }

        /// <summary>按 StateKey 查找或创建观察者状态节点。</summary>
        private static JObject EnsureMechanicState(JArray states, string stateKey)
        {
            foreach (JToken token in states)
            {
                if (token is JObject state &&
                    string.Equals(state.Value<string>("StateKey"), stateKey, StringComparison.Ordinal))
                {
                    return state;
                }
            }

            var created = new JObject
            {
                ["StateKey"] = stateKey,
                ["Data"] = new JObject()
            };
            states.Add(created);
            return created;
        }

        /// <summary>检查食物草稿中所有必须为非负数的字段。</summary>
        private static bool HasNegativeFoodValue(WorkshopItemDraft draft)
        {
            return draft.FoodCarbohydrates < 0f ||
                   draft.FoodMaxCarbohydrates < 0f ||
                   draft.FoodFat < 0f ||
                   draft.FoodMaxFat < 0f ||
                   draft.FoodProtein < 0f ||
                   draft.FoodMaxProtein < 0f ||
                   draft.FoodWater < 0f ||
                   draft.FoodMaxWater < 0f ||
                   draft.FoodVitamins < 0f ||
                   draft.FoodMaxVitamins < 0f ||
                   draft.FoodNutritionConsumeSpeed < 0f ||
                   draft.FoodWaterConsumeSpeedRate < 0f ||
                   draft.FoodNutritionConsumeRate < 0f ||
                   draft.FoodSpoilageIntervalSeconds < 0f;
        }

        #endregion

        private JObject FindReferenceModule(WorkshopItemTemplate template, string moduleName)
        {
            IEnumerable<string> candidates = new[]
            {
                template.ReferenceItemId,
                "Apple",
                "Log",
                "Axe_Stone",
                "Dagger_Stone",
                "Chestplate_Wood"
            }.Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (string itemId in candidates)
            {
                if (itemSourcesById.TryGetValue(itemId, out JObject source) &&
                    source["modules"] is JObject modules &&
                    modules.GetValue(moduleName, StringComparison.OrdinalIgnoreCase) is JObject module)
                {
                    return module;
                }
            }
            return null;
        }

        private static IEnumerable<string> BuildTemplateTags(
            WorkshopItemDraft draft,
            WorkshopItemTemplate template)
        {
            var tags = new HashSet<string>(draft.EnumerateTags(), StringComparer.OrdinalIgnoreCase);
            switch (template.Kind)
            {
                case WorkshopItemTemplateKind.Food:
                    tags.Add("Food");
                    break;
                case WorkshopItemTemplateKind.Tool:
                    tags.Add("Axe");
                    break;
                case WorkshopItemTemplateKind.Weapon:
                    tags.Add("Weapon");
                    break;
                case WorkshopItemTemplateKind.Equipment:
                    tags.Add("Equipment");
                    break;
                case WorkshopItemTemplateKind.Seed:
                    tags.Add("Seed");
                    break;
                case WorkshopItemTemplateKind.BuildingSummoner:
                    tags.Add("35");
                    break;
            }
            return tags;
        }

        private void ValidateAllItems(IReadOnlyDictionary<string, JObject> roots)
        {
            List<string> jsons = ItemPackages
                .Where(package => package.Enabled)
                .Select(package => roots.TryGetValue(package.Id, out JObject root)
                    ? root.ToString(Formatting.None)
                    : throw new InvalidDataException($"缺少物品分包：{package.Id}"))
                .ToList();
            List<ItemDefinitionDto> definitions = ItemDefinitionCatalogLoader.ResolveDefinitions(jsons);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ItemDefinitionDto definition in definitions)
            {
                if (!ids.Add(definition.Id))
                    throw new InvalidDataException($"跨分包存在重复物品 ID：{definition.Id}");
            }

            var resolvedById = definitions.ToDictionary(
                definition => definition.Id,
                StringComparer.OrdinalIgnoreCase);
            foreach (WorkshopPackageOption package in ItemPackages.Where(option => option.Enabled))
            {
                string expectedShell = package.ShellPrefab?.Trim();
                if (string.IsNullOrWhiteSpace(expectedShell))
                    continue;

                foreach (JObject source in ((JArray)roots[package.Id]["items"]).OfType<JObject>())
                {
                    string id = source.Value<string>("id")?.Trim();
                    if (!resolvedById.TryGetValue(id ?? string.Empty, out ItemDefinitionDto resolved) ||
                        !string.Equals(resolved.ShellPrefab?.Trim(), expectedShell, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"物品 {id} 解析出的 shellPrefab 与分包 {package.Id} 不一致；" +
                            $"expected={expectedShell}, actual={resolved?.ShellPrefab}");
                    }
                }
            }
        }

        #endregion

        #region 安全写盘

        private Dictionary<string, JObject> ReadCurrentRecipeRoots()
        {
            return recipePackagePaths.ToDictionary(
                pair => pair.Key,
                pair => ParseCatalogRoot(ReadText(pair.Value), "recipes", pair.Key),
                StringComparer.OrdinalIgnoreCase);
        }

        private Dictionary<string, JObject> ReadCurrentItemRoots()
        {
            return itemPackagePaths.ToDictionary(
                pair => pair.Key,
                pair => ParseCatalogRoot(ReadText(pair.Value), "items", pair.Key),
                StringComparer.OrdinalIgnoreCase);
        }

        private void WriteTrackedFiles(IReadOnlyDictionary<string, string> writes)
        {
            if (writes == null || writes.Count == 0)
                return;

            foreach (string assetPath in writes.Keys)
                EnsureFileUnchanged(assetPath);

            string backupDirectory = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                BackupRoot,
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")));
            Directory.CreateDirectory(backupDirectory);

            var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var temporaryPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (KeyValuePair<string, string> pair in writes)
                {
                    string fullPath = ToAbsolutePath(pair.Key);
                    string original = File.ReadAllText(fullPath, Encoding.UTF8);
                    originals[fullPath] = original;
                    string backupName = pair.Key
                        .Replace("Assets/", string.Empty)
                        .Replace('/', '_')
                        .Replace('\\', '_');
                    File.WriteAllText(
                        Path.Combine(backupDirectory, backupName),
                        original,
                        new UTF8Encoding(false));

                    string temporaryPath = fullPath + ".content-workshop.tmp";
                    File.WriteAllText(temporaryPath, pair.Value + Environment.NewLine, new UTF8Encoding(false));
                    temporaryPaths[fullPath] = temporaryPath;
                }

                foreach (KeyValuePair<string, string> pair in temporaryPaths)
                    File.Copy(pair.Value, pair.Key, true);
            }
            catch
            {
                foreach (KeyValuePair<string, string> original in originals)
                    File.WriteAllText(original.Key, original.Value, new UTF8Encoding(false));
                throw;
            }
            finally
            {
                foreach (string temporaryPath in temporaryPaths.Values)
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }

            foreach (string assetPath in writes.Keys)
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private void EnsureFileUnchanged(string assetPath)
        {
            string current = ReadText(assetPath);
            string fingerprint = ComputeFingerprint(current);
            if (!loadedFileFingerprints.TryGetValue(assetPath, out string loaded) ||
                !string.Equals(fingerprint, loaded, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"文件在内容工坊打开后已发生变化：{assetPath}\n请先点击“重新加载”，确认外部修改后再保存。");
            }
        }

        #endregion

        #region JSON 与资源辅助

        private bool ItemExists(string itemId)
        {
            return !string.IsNullOrWhiteSpace(itemId) && itemsById.ContainsKey(itemId.Trim());
        }

        private string ReadTrackedText(string assetPath)
        {
            string text = ReadText(assetPath);
            loadedFileFingerprints[assetPath] = ComputeFingerprint(text);
            return text;
        }

        private static string ReadText(string assetPath)
        {
            string fullPath = ToAbsolutePath(assetPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"找不到配置文件：{assetPath}", fullPath);
            return File.ReadAllText(fullPath, Encoding.UTF8);
        }

        private static JObject ParseCatalogRoot(string json, string arrayName, string sourceName)
        {
            JObject root = JObject.Parse(json);
            if (root.Value<int?>("schemaVersion") != 1)
                throw new InvalidDataException($"分包 {sourceName} 的 schemaVersion 不受支持。");
            if (root[arrayName] is not JArray)
                throw new InvalidDataException($"分包 {sourceName} 缺少 {arrayName} 数组。");
            return root;
        }

        private static string ResolveAssetPackagePath(string rootAssetPath, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new InvalidDataException($"配置分包路径无效：{relativePath}");
            string normalizedRoot = ToAbsolutePath(rootAssetPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(
                normalizedRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"配置分包路径越出目录：{relativePath}");
            return ToAssetPath(fullPath);
        }

        private static JObject BuildRecipeSource(RecipeDto definition, JObject original)
        {
            JObject serialized = JObject.FromObject(definition, JsonSerializer.Create(new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            }));
            JObject result = original == null ? new JObject() : (JObject)original.DeepClone();
            foreach (string property in KnownRecipeProperties)
            {
                if (serialized.TryGetValue(property, out JToken value))
                    result[property] = value.DeepClone();
                else
                    result.Remove(property);
            }
            return result;
        }

        private static bool RemoveById(JArray array, string id)
        {
            JObject match = array.OfType<JObject>().FirstOrDefault(item =>
                string.Equals(item.Value<string>("id"), id, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                return false;
            match.Remove();
            return true;
        }

        private static string[] BuildSearchTerms(
            ItemDefinitionDto definition,
            string displayName,
            string packageId,
            WorkshopItemCategory category)
        {
            return new[]
                {
                    definition.Id,
                    displayName,
                    definition.Description,
                    definition.ShellPrefab,
                    packageId,
                    category.ToString()
                }
                .Concat(definition.Tags ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
        }

        private static WorkshopItemCategory InferCategory(string packageId, ItemDefinitionDto definition)
        {
            string package = packageId ?? string.Empty;
            if (package.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0)
                return WorkshopItemCategory.Weapon;
            if (package.IndexOf("tool", StringComparison.OrdinalIgnoreCase) >= 0)
                return WorkshopItemCategory.Tool;
            if (package.IndexOf("equipment", StringComparison.OrdinalIgnoreCase) >= 0)
                return WorkshopItemCategory.Equipment;
            if (package.IndexOf("seed", StringComparison.OrdinalIgnoreCase) >= 0)
                return WorkshopItemCategory.Seed;
            if (package.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0)
                return WorkshopItemCategory.Building;
            if (definition.Modules?.Keys.Any(key =>
                    key.IndexOf("food", StringComparison.OrdinalIgnoreCase) >= 0 || key.Contains("食物")) == true ||
                definition.Tags?.Any(tag => string.Equals(tag, "Food", StringComparison.OrdinalIgnoreCase)) == true)
            {
                return WorkshopItemCategory.Food;
            }
            if (string.Equals(packageId, "basic_items", StringComparison.OrdinalIgnoreCase))
                return WorkshopItemCategory.Material;
            return WorkshopItemCategory.Other;
        }

        private static Sprite LoadItemSprite(ItemDefinitionDto definition)
        {
            Sprite sprite = LoadSpriteAddress(definition?.Visual?.SpriteAddress);
            if (sprite != null)
                return sprite;
            if (string.IsNullOrWhiteSpace(definition?.SourcePrefab))
                return null;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(definition.SourcePrefab);
            return prefab?.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(renderer => renderer.sprite != null)?.sprite;
        }

        private static Sprite LoadSpriteAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address) ||
                !address.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return null;

            string assetPath = address;
            string subAssetName = null;
            int openBracket = address.LastIndexOf('[');
            if (openBracket > 0 && address.EndsWith("]", StringComparison.Ordinal))
            {
                assetPath = address.Substring(0, openBracket);
                subAssetName = address.Substring(openBracket + 1, address.Length - openBracket - 2);
            }

            if (string.IsNullOrWhiteSpace(subAssetName))
                return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .FirstOrDefault(candidate => string.Equals(candidate.name, subAssetName, StringComparison.Ordinal));
        }

        private static string BuildSpriteAddress(Sprite sprite)
        {
            if (sprite == null)
                throw new ArgumentNullException(nameof(sprite));
            string assetPath = AssetDatabase.GetAssetPath(sprite);
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("所选 Sprite 必须是项目 Assets 目录内的资源。");
            if (assetPath.IndexOf('[') >= 0 || assetPath.IndexOf(']') >= 0)
                throw new InvalidOperationException($"Sprite 路径包含 Addressables 保留方括号：{assetPath}");
            Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            return ReferenceEquals(mainAsset, sprite) ? assetPath : $"{assetPath}[{sprite.name}]";
        }

        private static void EnsureSpriteAddressable(Sprite sprite, string address)
        {
            string assetPath = AssetDatabase.GetAssetPath(sprite);
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                throw new InvalidOperationException("AddressableAssetSettings 未初始化。");
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            entry.address = assetPath;
            entry.SetLabel(ItemSpriteLabel, true, true);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
            AssetDatabase.SaveAssetIfDirty(settings);

            string expected = ReferenceEquals(AssetDatabase.LoadMainAssetAtPath(assetPath), sprite)
                ? assetPath
                : $"{assetPath}[{sprite.name}]";
            if (!string.Equals(address, expected, StringComparison.Ordinal))
                throw new InvalidOperationException("Sprite 稳定地址生成结果不一致。");
        }

        private static JObject ColorToken(Color color)
        {
            return new JObject
            {
                ["r"] = color.r,
                ["g"] = color.g,
                ["b"] = color.b,
                ["a"] = color.a
            };
        }

        /// <summary>序列化物品 SpriteRenderer 的 Z 轴旋转，X/Y 保持为零。</summary>
        private static JObject RotationToken(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
                degrees = 0f;
            degrees = Mathf.Repeat(degrees + 180f, 360f) - 180f;
            return new JObject
            {
                ["x"] = 0f,
                ["y"] = 0f,
                ["z"] = degrees
            };
        }

        private static string ComputeFingerprint(string text)
        {
            using SHA256 sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty)));
        }

        private static string ToAbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
        }

        private static string ToAssetPath(string fullPath)
        {
            string projectRoot = Path.GetFullPath(Directory.GetCurrentDirectory())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalized = Path.GetFullPath(fullPath);
            if (!normalized.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"路径不属于当前 Unity 项目：{fullPath}");
            return normalized.Substring(projectRoot.Length).Replace('\\', '/');
        }

        #endregion
    }
}

#endif
