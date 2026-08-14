#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Editor.ContentWorkshop
{
    /// <summary>
    /// 内容工坊主页面。编辑器只显示创作者能理解的业务概念，稳定 ID 与 JSON 路径放在高级区域。
    /// </summary>
    internal enum ContentWorkshopPage
    {
        Crafting,
        Heating,
        Items
    }

    /// <summary>物品图鉴分类；分类只影响编辑器检索，不写回运行时配置。</summary>
    internal enum WorkshopItemCategory
    {
        All,
        Material,
        Food,
        Tool,
        Weapon,
        Equipment,
        Seed,
        Building,
        Other
    }

    /// <summary>UGC 道具模板；每个模板绑定一个项目内已验证的抽象外壳与参考物品。</summary>
    internal enum WorkshopItemTemplateKind
    {
        Material,
        Food,
        Tool,
        Weapon,
        Equipment,
        Seed,
        BuildingSummoner
    }

    /// <summary>热加工预设；当前运行时统一落为 smelting，并通过温度区分加工强度。</summary>
    internal enum WorkshopHeatingPreset
    {
        Cooking,
        Charcoal,
        Smelting,
        Alloy
    }

    /// <summary>Manifest 中的可写分包入口。</summary>
    internal sealed class WorkshopPackageOption
    {
        public string Id;
        public string RelativePath;
        public string ShellPrefab;
        public bool Enabled;

        public string DisplayName => string.IsNullOrWhiteSpace(Id) ? RelativePath : Id;
    }

    /// <summary>图鉴条目；同时保留继承解析后的预览数据与原始差异节点。</summary>
    internal sealed class WorkshopItemEntry
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string PackageId;
        public WorkshopItemCategory Category;
        public ItemDefinitionDto Definition;
        public JObject Source;
        public Sprite Icon;
        public string[] SearchTerms = Array.Empty<string>();

        public bool Matches(string search, WorkshopItemCategory category)
        {
            if (category != WorkshopItemCategory.All && Category != category)
                return false;
            if (string.IsNullOrWhiteSpace(search))
                return true;

            string needle = search.Trim();
            return SearchTerms.Any(term =>
                !string.IsNullOrWhiteSpace(term) &&
                term.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    /// <summary>已有配方索引；Source 用于保存时保留未来版本的未知字段。</summary>
    internal sealed class WorkshopRecipeRecord
    {
        public string PackageId;
        public string PackagePath;
        public RecipeDto Definition;
        public JObject Source;

        public string DisplayName => string.IsNullOrWhiteSpace(Definition?.DisplayName)
            ? Definition?.Id ?? "<无名称配方>"
            : Definition.DisplayName;
    }

    /// <summary>单个可视化材料槽；物品引用与标签引用互斥。</summary>
    [Serializable]
    internal sealed class WorkshopIngredientDraft
    {
        public string ItemId;
        public string Tag;
        public int Amount = 1;

        public bool IsTag => !string.IsNullOrWhiteSpace(Tag);
        public bool IsEmpty => string.IsNullOrWhiteSpace(ItemId) && string.IsNullOrWhiteSpace(Tag);

        public WorkshopIngredientDraft Clone()
        {
            return new WorkshopIngredientDraft
            {
                ItemId = ItemId,
                Tag = Tag,
                Amount = Amount
            };
        }

        public void SetItem(string itemId)
        {
            ItemId = itemId?.Trim();
            Tag = null;
            Amount = Mathf.Max(1, Amount);
        }

        public void SetTag(string tag)
        {
            Tag = tag?.Trim();
            ItemId = null;
            Amount = Mathf.Max(1, Amount);
        }

        public void Clear()
        {
            ItemId = null;
            Tag = null;
            Amount = 1;
        }
    }

    /// <summary>可视化配方产物。</summary>
    [Serializable]
    internal sealed class WorkshopOutputDraft
    {
        public string ItemId;
        public int Amount = 1;

        public WorkshopOutputDraft Clone()
        {
            return new WorkshopOutputDraft
            {
                ItemId = ItemId,
                Amount = Amount
            };
        }
    }

    /// <summary>
    /// 配方编辑草稿。始终使用 3×3 创作画布，保存时再按规则裁剪成运行时 DTO 所需尺寸。
    /// </summary>
    [Serializable]
    internal sealed class WorkshopRecipeDraft
    {
        public const int CanvasWidth = 3;
        public const int CanvasHeight = 3;

        public string OriginalId;
        public string OriginalPackageId;
        public JObject OriginalSource;
        public string Id;
        public string DisplayName;
        public string PackageId;
        public bool IsHeating;
        public bool Ordered = true;
        public bool AllowMirror = true;
        public bool AutoTrim = true;
        public float Temperature;
        public float MaxTemperature = 2000f;
        public int OriginalGridWidth = CanvasWidth;
        public int OriginalGridHeight = CanvasHeight;
        public WorkshopHeatingPreset HeatingPreset = WorkshopHeatingPreset.Cooking;
        public readonly WorkshopIngredientDraft[] Ingredients =
            Enumerable.Range(0, CanvasWidth * CanvasHeight)
                .Select(_ => new WorkshopIngredientDraft())
                .ToArray();
        public readonly List<WorkshopOutputDraft> Outputs = new();
        public readonly List<RecipeActionDto> Actions = new();

        public bool IsExisting => !string.IsNullOrWhiteSpace(OriginalId);

        public static WorkshopRecipeDraft CreateNew(bool heating, string packageId)
        {
            var draft = new WorkshopRecipeDraft
            {
                IsHeating = heating,
                PackageId = packageId,
                Ordered = !heating,
                AllowMirror = !heating,
                AutoTrim = true,
                Id = WorkshopIdUtility.CreateTimestampId(heating ? "heating" : "recipe")
            };
            draft.Outputs.Add(new WorkshopOutputDraft());
            if (heating)
                draft.ApplyHeatingPreset(WorkshopHeatingPreset.Cooking);
            return draft;
        }

        public static WorkshopRecipeDraft FromRecord(WorkshopRecipeRecord record)
        {
            if (record?.Definition == null)
                throw new ArgumentNullException(nameof(record));

            RecipeDto source = record.Definition;
            if (source.GridWidth > CanvasWidth || source.GridHeight > CanvasHeight)
            {
                throw new InvalidOperationException(
                    $"配方 {source.Id} 使用 {source.GridWidth}×{source.GridHeight} 网格，" +
                    $"超过内容工坊当前支持的 {CanvasWidth}×{CanvasHeight} 上限。为避免截断，已拒绝载入。");
            }

            var draft = new WorkshopRecipeDraft
            {
                OriginalId = source.Id,
                OriginalPackageId = record.PackageId,
                OriginalSource = record.Source == null ? null : (JObject)record.Source.DeepClone(),
                Id = source.Id,
                DisplayName = source.DisplayName,
                PackageId = record.PackageId,
                IsHeating = string.Equals(source.RecipeType, "smelting", StringComparison.OrdinalIgnoreCase),
                Ordered = string.Equals(source.InputRule, "ordered", StringComparison.OrdinalIgnoreCase),
                AllowMirror = source.AllowMirror,
                AutoTrim = false,
                Temperature = source.Temperature,
                MaxTemperature = source.MaxTemperature,
                OriginalGridWidth = Mathf.Clamp(source.GridWidth, 1, CanvasWidth),
                OriginalGridHeight = Mathf.Clamp(source.GridHeight, 1, CanvasHeight)
            };

            foreach (RecipeIngredientDto input in source.Inputs ?? new List<RecipeIngredientDto>())
            {
                if (input == null || source.GridWidth <= 0)
                    continue;
                int row = input.Slot / source.GridWidth;
                int column = input.Slot % source.GridWidth;
                if (row < 0 || row >= CanvasHeight || column < 0 || column >= CanvasWidth)
                    continue;

                WorkshopIngredientDraft slot = draft.Ingredients[row * CanvasWidth + column];
                slot.Amount = Mathf.Max(1, input.Amount);
                if (string.Equals(input.Match, "tag", StringComparison.OrdinalIgnoreCase))
                    slot.SetTag(input.Tag);
                else
                    slot.SetItem(input.ItemId);
            }

            foreach (RecipeOutputDto output in source.Outputs ?? new List<RecipeOutputDto>())
            {
                if (output == null)
                    continue;
                draft.Outputs.Add(new WorkshopOutputDraft
                {
                    ItemId = output.ItemId,
                    Amount = Mathf.Max(1, output.Amount)
                });
            }
            if (draft.Outputs.Count == 0)
                draft.Outputs.Add(new WorkshopOutputDraft());

            foreach (RecipeActionDto action in source.Actions ?? new List<RecipeActionDto>())
            {
                if (action != null)
                    draft.Actions.Add(action);
            }

            draft.HeatingPreset = GuessHeatingPreset(record.PackageId, source.Temperature);
            return draft;
        }

        public void ApplyHeatingPreset(WorkshopHeatingPreset preset)
        {
            HeatingPreset = preset;
            IsHeating = true;
            Ordered = preset == WorkshopHeatingPreset.Alloy;
            AllowMirror = false;
            switch (preset)
            {
                case WorkshopHeatingPreset.Cooking:
                    Temperature = 150f;
                    MaxTemperature = 260f;
                    break;
                case WorkshopHeatingPreset.Charcoal:
                    Temperature = 500f;
                    MaxTemperature = 900f;
                    break;
                case WorkshopHeatingPreset.Smelting:
                    Temperature = 1000f;
                    MaxTemperature = 1600f;
                    break;
                case WorkshopHeatingPreset.Alloy:
                    Temperature = 1200f;
                    MaxTemperature = 2000f;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }

        public RecipeDto BuildDto()
        {
            if (string.IsNullOrWhiteSpace(Id))
                throw new InvalidOperationException("配方稳定 ID 不能为空。");
            if (string.IsNullOrWhiteSpace(DisplayName))
                throw new InvalidOperationException("请填写玩家看到的配方名称。");
            if (Outputs.Count == 0 || Outputs.Any(output => string.IsNullOrWhiteSpace(output.ItemId)))
                throw new InvalidOperationException("配方至少需要一个有效产物。");
            if (Ingredients.All(ingredient => ingredient.IsEmpty))
                throw new InvalidOperationException("请至少放入一种材料。");

            var dto = new RecipeDto
            {
                Id = Id.Trim(),
                DisplayName = DisplayName.Trim(),
                RecipeType = IsHeating ? "smelting" : "crafting",
                InputRule = Ordered ? "ordered" : "unordered",
                AllowMirror = Ordered && AllowMirror,
                Temperature = IsHeating ? Mathf.Max(0f, Temperature) : 0f,
                MaxTemperature = IsHeating ? Mathf.Max(Temperature, MaxTemperature) : 2000f,
                Outputs = Outputs.Select(output => new RecipeOutputDto
                {
                    ItemId = output.ItemId.Trim(),
                    Amount = Mathf.Max(1, output.Amount)
                }).ToList(),
                Actions = Actions.ToList()
            };

            BuildInputs(dto);
            return dto;
        }

        private void BuildInputs(RecipeDto dto)
        {
            if (!AutoTrim)
            {
                dto.GridWidth = IsExisting
                    ? Mathf.Clamp(OriginalGridWidth, 1, CanvasWidth)
                    : CanvasWidth;
                dto.GridHeight = IsExisting
                    ? Mathf.Clamp(OriginalGridHeight, 1, CanvasHeight)
                    : CanvasHeight;
                CopyCanvasRegion(dto, 0, 0, dto.GridWidth, dto.GridHeight);
                return;
            }

            if (!Ordered)
            {
                List<WorkshopIngredientDraft> compact = Ingredients
                    .Where(ingredient => !ingredient.IsEmpty)
                    .Select(ingredient => ingredient.Clone())
                    .ToList();
                dto.GridWidth = Mathf.Min(CanvasWidth, compact.Count);
                dto.GridHeight = Mathf.CeilToInt((float)compact.Count / dto.GridWidth);
                for (int index = 0; index < compact.Count; index++)
                    dto.Inputs.Add(ToDto(compact[index], index));
                return;
            }

            int minRow = CanvasHeight;
            int maxRow = -1;
            int minColumn = CanvasWidth;
            int maxColumn = -1;
            for (int index = 0; index < Ingredients.Length; index++)
            {
                if (Ingredients[index].IsEmpty)
                    continue;
                int row = index / CanvasWidth;
                int column = index % CanvasWidth;
                minRow = Mathf.Min(minRow, row);
                maxRow = Mathf.Max(maxRow, row);
                minColumn = Mathf.Min(minColumn, column);
                maxColumn = Mathf.Max(maxColumn, column);
            }

            dto.GridWidth = maxColumn - minColumn + 1;
            dto.GridHeight = maxRow - minRow + 1;
            CopyCanvasRegion(dto, minRow, minColumn, dto.GridWidth, dto.GridHeight);
        }

        private void CopyCanvasRegion(RecipeDto dto, int startRow, int startColumn, int width, int height)
        {
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    WorkshopIngredientDraft ingredient =
                        Ingredients[(startRow + row) * CanvasWidth + startColumn + column];
                    if (ingredient.IsEmpty)
                        continue;
                    dto.Inputs.Add(ToDto(ingredient, row * width + column));
                }
            }
        }

        private static RecipeIngredientDto ToDto(WorkshopIngredientDraft ingredient, int slot)
        {
            return new RecipeIngredientDto
            {
                Slot = slot,
                Match = ingredient.IsTag ? "tag" : "exact_item",
                ItemId = ingredient.IsTag ? null : ingredient.ItemId?.Trim(),
                Tag = ingredient.IsTag ? ingredient.Tag?.Trim() : null,
                Amount = Mathf.Max(1, ingredient.Amount)
            };
        }

        private static WorkshopHeatingPreset GuessHeatingPreset(string packageId, float temperature)
        {
            if (packageId?.StartsWith("cooking/", StringComparison.OrdinalIgnoreCase) == true)
                return WorkshopHeatingPreset.Cooking;
            if (packageId?.EndsWith("alloys", StringComparison.OrdinalIgnoreCase) == true)
                return WorkshopHeatingPreset.Alloy;
            return temperature < 800f ? WorkshopHeatingPreset.Charcoal : WorkshopHeatingPreset.Smelting;
        }
    }

    /// <summary>模板定义；模块从 ReferenceItemId 的原始定义复制，避免手工拼写模块与 Prefab ID。</summary>
    internal sealed class WorkshopItemTemplate
    {
        public WorkshopItemTemplate(
            WorkshopItemTemplateKind kind,
            string displayName,
            string description,
            string packageId,
            string parentId,
            string referenceItemId,
            params string[] defaultModuleNames)
        {
            Kind = kind;
            DisplayName = displayName;
            Description = description;
            PackageId = packageId;
            ParentId = parentId;
            ReferenceItemId = referenceItemId;
            DefaultModuleNames = defaultModuleNames ?? Array.Empty<string>();
        }

        public WorkshopItemTemplateKind Kind { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string PackageId { get; }
        public string ParentId { get; }
        public string ReferenceItemId { get; }
        public string[] DefaultModuleNames { get; }
    }

    /// <summary>模板化物品草稿；高级字段自动生成，用户主要配置外观和游戏语义。</summary>
    [Serializable]
    internal sealed class WorkshopItemDraft
    {
        public WorkshopItemTemplateKind TemplateKind = WorkshopItemTemplateKind.Material;
        public string Id = WorkshopIdUtility.CreateTimestampId("item");
        public string DisplayName;
        public string Description;
        public Sprite Icon;
        public float Durability = 1f;
        public float Amount = 1f;
        public float Volume = 1f;
        public bool CanBePickedUp = true;
        public Color Tint = Color.white;
        /// <summary>SpriteRenderer 绕 Z 轴的本地旋转角度，单位为度。</summary>
        public float RotationDegrees;
        public bool FlipX;
        public bool FlipY;
        public string Tags = string.Empty;
        public bool AddFoodAbility;
        public bool AddFuelAbility;
        public bool AddCombatAbility;
        public bool AddEquipmentAbility;
        public float CuttingDamage;
        public float PiercingDamage;
        public float ChoppingDamage;
        public float BluntDamage = 5f;

        #region 食物参数

        /// <summary>食物提供的碳水化合物数值。</summary>
        public float FoodCarbohydrates = 40f;
        /// <summary>食物提供的碳水化合物容量上限。</summary>
        public float FoodMaxCarbohydrates = 40f;
        /// <summary>食物提供的脂肪数值。</summary>
        public float FoodFat = 1f;
        /// <summary>食物提供的脂肪容量上限。</summary>
        public float FoodMaxFat = 1f;
        /// <summary>食物提供的蛋白质数值。</summary>
        public float FoodProtein = 1f;
        /// <summary>食物提供的蛋白质容量上限。</summary>
        public float FoodMaxProtein = 1f;
        /// <summary>食物提供的水分数值。</summary>
        public float FoodWater = 55f;
        /// <summary>食物提供的水分容量上限。</summary>
        public float FoodMaxWater = 55f;
        /// <summary>食物提供的维生素数值。</summary>
        public float FoodVitamins = 20f;
        /// <summary>食物提供的维生素容量上限。</summary>
        public float FoodMaxVitamins = 20f;
        /// <summary>完整吃掉一份食物所需的进食次数。</summary>
        public float FoodMaxEatingProgress = 3f;
        /// <summary>营养自然消耗速度。</summary>
        public float FoodNutritionConsumeSpeed;
        /// <summary>水分自然消耗倍率。</summary>
        public float FoodWaterConsumeSpeedRate;
        /// <summary>总体营养消耗倍率。</summary>
        public float FoodNutritionConsumeRate;
        /// <summary>是否启用食物腐败。</summary>
        public bool FoodEnableSpoilage = true;
        /// <summary>食物腐败触发间隔，单位为秒。</summary>
        public float FoodSpoilageIntervalSeconds = 1800f;
        /// <summary>食物腐败后替换成的物品 ID。</summary>
        public string FoodSpoilageTargetItemId = "Meat_Rotten";
        /// <summary>食用方式：0 为固体，1 为饮品。</summary>
        public int FoodConsumeKind;

        #endregion

        public void ResetForTemplate(WorkshopItemTemplate template)
        {
            TemplateKind = template.Kind;
            DisplayName = string.Empty;
            Description = string.Empty;
            Icon = null;
            Durability = template.Kind is WorkshopItemTemplateKind.Tool or WorkshopItemTemplateKind.Weapon
                ? 100f
                : 1f;
            Amount = 1f;
            Volume = template.Kind switch
            {
                WorkshopItemTemplateKind.Tool => 2f,
                WorkshopItemTemplateKind.Weapon => 2f,
                WorkshopItemTemplateKind.Equipment => 10f,
                WorkshopItemTemplateKind.BuildingSummoner => 10f,
                _ => 1f
            };
            CanBePickedUp = true;
            Tint = Color.white;
            RotationDegrees = 0f;
            FlipX = false;
            FlipY = false;
            Tags = string.Empty;
            AddFoodAbility = template.Kind == WorkshopItemTemplateKind.Food;
            AddFuelAbility = false;
            AddCombatAbility = template.Kind is WorkshopItemTemplateKind.Tool or WorkshopItemTemplateKind.Weapon;
            AddEquipmentAbility = template.Kind == WorkshopItemTemplateKind.Equipment;
            CuttingDamage = 0f;
            PiercingDamage = 0f;
            ChoppingDamage = template.Kind == WorkshopItemTemplateKind.Weapon ? 10f : 5f;
            BluntDamage = 0f;

            FoodCarbohydrates = 40f;
            FoodMaxCarbohydrates = 40f;
            FoodFat = 1f;
            FoodMaxFat = 1f;
            FoodProtein = 1f;
            FoodMaxProtein = 1f;
            FoodWater = 55f;
            FoodMaxWater = 55f;
            FoodVitamins = 20f;
            FoodMaxVitamins = 20f;
            FoodMaxEatingProgress = 3f;
            FoodNutritionConsumeSpeed = 0f;
            FoodWaterConsumeSpeedRate = 0f;
            FoodNutritionConsumeRate = 0f;
            FoodEnableSpoilage = true;
            FoodSpoilageIntervalSeconds = 1800f;
            FoodSpoilageTargetItemId = "Meat_Rotten";
            FoodConsumeKind = 0;
            Id = WorkshopIdUtility.CreateTimestampId("item");
        }

        public IEnumerable<string> EnumerateTags()
        {
            return (Tags ?? string.Empty)
                .Split(new[] { ',', '，', ';', '；', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>稳定 ID 生成辅助；中文名称不参与主键，避免重命名或编码差异破坏引用。</summary>
    internal static class WorkshopIdUtility
    {
        public static string CreateTimestampId(string prefix)
        {
            string safePrefix = ToSlug(prefix);
            return $"ugc:{safePrefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
        }

        public static string ToSlug(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "content";

            var builder = new StringBuilder(value.Length);
            foreach (char character in value.Normalize(NormalizationForm.FormD))
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;
                if (character <= 127 && char.IsLetterOrDigit(character))
                    builder.Append(char.ToLowerInvariant(character));
                else if ((character == '-' || character == '_' || char.IsWhiteSpace(character)) &&
                         builder.Length > 0 && builder[builder.Length - 1] != '-')
                    builder.Append('-');
            }

            string result = builder.ToString().Trim('-');
            return string.IsNullOrWhiteSpace(result) ? "content" : result;
        }
    }
}

#endif
