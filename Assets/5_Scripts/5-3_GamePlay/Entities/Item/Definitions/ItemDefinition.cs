using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using FlatWorld.Localization;
using FlatWorld.WorldModel;

/// <summary>游戏本体 JSON 物品目录；文件可按玩法类别拆分。</summary>
[Serializable]
public sealed class ItemDefinitionCatalogDto
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion = 1;

    [JsonProperty("items")]
    public List<ItemDefinitionDto> Items = new();
}

/// <summary>物品配置入口清单；只加载清单中显式启用的分包。</summary>
[Serializable]
public sealed class ItemDefinitionManifestDto
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion = 1;

    [JsonProperty("packages")]
    public List<ItemDefinitionPackageDto> Packages = new();
}

/// <summary>物品定义分包；文件通常按玩法类别命名，也可声明单一外壳约束。</summary>
[Serializable]
public sealed class ItemDefinitionPackageDto
{
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("path")]
    public string Path;

    /// <summary>可选分类约束；填写后，包内所有定义解析出的 shellPrefab 必须一致。</summary>
    [JsonProperty("shellPrefab", NullValueHandling = NullValueHandling.Ignore)]
    public string ShellPrefab;

    [JsonProperty("enabled")]
    public bool Enabled = true;
}

/// <summary>单个物品定义。parent 用于复用同类物品的公共配置。</summary>
[Serializable]
public sealed class ItemDefinitionDto
{
    /// <summary>稳定业务 ID；名称或语言变化不得改变此标识。</summary>
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("abstract")]
    public bool Abstract;

    [JsonProperty("parent")]
    public string Parent;

    [JsonProperty("shellPrefab")]
    public string ShellPrefab;

    /// <summary>运行时外壳的稳定 Addressables 地址；地址与资源目录解耦。</summary>
    [JsonProperty("shellAddress", NullValueHandling = NullValueHandling.Ignore)]
    public string ShellAddress;

    /// <summary>仅供编辑器迁移/校验定位旧资源；运行时不会加载此 Prefab。</summary>
    [JsonProperty("sourcePrefab")]
    public string SourcePrefab;

    /// <summary>作者填写的默认显示名；本体使用中文，不从 ID 推导。</summary>
    [JsonProperty("gameName")]
    public string GameName;

    /// <summary>可选名称翻译键；省略时使用当前物品 ID 生成，不继承父物品的键。</summary>
    [JsonProperty("labelKey")]
    public string LabelKey;

    [JsonProperty("descriptionKey")]
    public string DescriptionKey;

    [JsonProperty("description")]
    public string Description;

    [JsonProperty("durability")]
    public float? Durability;

    [JsonProperty("maxDurability")]
    public float? MaxDurability;

    [JsonProperty("amount")]
    public float? Amount;

    [JsonProperty("volume")]
    public float? Volume;

    [JsonProperty("weight")]
    public float? Weight;

    /// <summary>作为建筑放置时每个占地格需要的最低承重；普通物品默认 0。</summary>
    [JsonProperty("requiredGroundSupport")]
    public int RequiredGroundSupport;

    [JsonProperty("stackable")]
    public bool? Stackable;

    [JsonProperty("canBePickedUp")]
    public bool? CanBePickedUp;

    /// <summary>自然生成时由地表植被图层绘制，采集后才实例化为普通物品。</summary>
    [JsonProperty("groundCover")]
    public bool GroundCover;

    /// <summary>不依赖 Collider 的确定性世界格占地；偏移相对物品根节点所在格。</summary>
    [JsonProperty("worldGridOccupancy", NullValueHandling = NullValueHandling.Ignore)]
    public WorldGridOccupancyData WorldGridOccupancy;

    /// <summary>掉落物最终落入水体时，可选转换成另一物品 ID；数量保持不变。</summary>
    [JsonProperty("waterEntryTransformItemId", NullValueHandling = NullValueHandling.Ignore)]
    public string WaterEntryTransformItemId;

    [JsonProperty("tags")]
    public List<string> Tags;

    /// <summary>ItemData 中除公共快捷字段外的其余静态模板数据。</summary>
    [JsonProperty("itemData")]
    public JObject ItemData;

    [JsonProperty("visual")]
    public ItemVisualDefinitionDto Visual;

    /// <summary>可受伤对象的声明式配置；运行时会自动复用 Module_DamageReciver。</summary>
    [JsonProperty("health", NullValueHandling = NullValueHandling.Ignore)]
    public ItemHealthDefinitionDto Health;

    /// <summary>死亡掉落所引用的全局战利品表稳定 ID。</summary>
    [JsonProperty("lootTableId", NullValueHandling = NullValueHandling.Ignore)]
    public string LootTableId;

    /// <summary>稳定模块名 -> 模块定义。稳定名会直接成为 ModuleDataDic 的键。</summary>
    [JsonProperty("modules")]
    public Dictionary<string, ItemModuleDefinitionDto> Modules = new();
}

[Serializable]
public sealed class ItemVisualDefinitionDto
{
    [JsonProperty("rendererPath")]
    public string RendererPath;

    [JsonProperty("spriteAddress")]
    public string SpriteAddress;

    /// <summary>按稳定状态名声明额外 Sprite；例如作物的 seedling/growing/mature。</summary>
    [JsonProperty("spriteStates", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, string> SpriteStates;

    /// <summary>液体容器 Sprite 开口内腔的归一化矩形；液面像素由容器模块按 LiquidDefinition 主色重绘。</summary>
    [JsonProperty("liquidSurface", NullValueHandling = NullValueHandling.Ignore)]
    public LiquidSurfaceDefinition LiquidSurface;

    /// <summary>世界 SpriteRenderer 使用的共享材质 Addressable 地址。</summary>
    [JsonProperty("materialAddress", NullValueHandling = NullValueHandling.Ignore)]
    public string MaterialAddress;

    /// <summary>MOD AssetBundle 名；与 spriteAsset 配合覆盖本体 Sprite。</summary>
    [JsonProperty("spriteBundle", NullValueHandling = NullValueHandling.Ignore)]
    public string SpriteBundle;

    /// <summary>MOD AssetBundle 内的 Sprite 资源名。</summary>
    [JsonProperty("spriteAsset", NullValueHandling = NullValueHandling.Ignore)]
    public string SpriteAsset;

    [JsonProperty("animatorPath", NullValueHandling = NullValueHandling.Ignore)]
    public string AnimatorPath;

    [JsonProperty("animatorControllerAddress", NullValueHandling = NullValueHandling.Ignore)]
    public string AnimatorControllerAddress;

    /// <summary>绑定 AnimatorController 后默认播放的状态名；Actor 用它代替 Sprite 子资源地址。</summary>
    [JsonProperty("animationState", NullValueHandling = NullValueHandling.Ignore)]
    public string AnimationState;

    /// <summary>MOD AssetBundle 名；与 animatorControllerAsset 配合覆盖动画控制器。</summary>
    [JsonProperty("animatorControllerBundle", NullValueHandling = NullValueHandling.Ignore)]
    public string AnimatorControllerBundle;

    /// <summary>MOD AssetBundle 内的 RuntimeAnimatorController 资源名。</summary>
    [JsonProperty("animatorControllerAsset", NullValueHandling = NullValueHandling.Ignore)]
    public string AnimatorControllerAsset;

    [JsonProperty("rendererLocalPosition")]
    public Vector3? RendererLocalPosition;

    [JsonProperty("rendererLocalEulerAngles")]
    public Vector3? RendererLocalEulerAngles;

    [JsonProperty("rendererLocalScale")]
    public Vector3? RendererLocalScale;

    [JsonProperty("color")]
    public Color? Color;

    [JsonProperty("flipX")]
    public bool? FlipX;

    [JsonProperty("flipY")]
    public bool? FlipY;

    [JsonProperty("sortingLayerName")]
    public string SortingLayerName;

    [JsonProperty("sortingOrder")]
    public int? SortingOrder;

    /// <summary>世界阴影的可选视觉配置；未声明时沿用实体默认规则。</summary>
    [JsonProperty("shadows", NullValueHandling = NullValueHandling.Ignore)]
    public ItemShadowVisualDefinitionDto Shadows;

    [JsonProperty("collider")]
    public ItemColliderDefinitionDto Collider;
}

/// <summary>程序化液面的 0～1 Sprite UV 开口范围与源像素亮度上限；亮度默认 165，容器可按内腔配色覆盖以保护外沿。</summary>
[Serializable]
public sealed class LiquidSurfaceDefinition
{
    [JsonProperty("bounds", Required = Required.Always)]
    public Rect Bounds;

    [JsonProperty("maxSourceChannel")]
    public int MaxSourceChannel = 165; // 内腔源像素的最大亮度，由贴图自身配色决定。

    /// <summary>校验开口矩形，防止 UI 液面绘制到物品图标外。</summary>
    public void Validate(string itemId)
    {
        if (float.IsNaN(Bounds.x) || float.IsInfinity(Bounds.x) ||
            float.IsNaN(Bounds.y) || float.IsInfinity(Bounds.y) ||
            float.IsNaN(Bounds.width) || float.IsInfinity(Bounds.width) ||
            float.IsNaN(Bounds.height) || float.IsInfinity(Bounds.height) ||
            Bounds.width <= 0f || Bounds.height <= 0f ||
            Bounds.xMin < 0f || Bounds.yMin < 0f || Bounds.xMax > 1f || Bounds.yMax > 1f ||
            MaxSourceChannel < 1 || MaxSourceChannel > 255)
        {
            throw new InvalidDataException($"物品 {itemId} 的 liquidSurface 必须声明 0～1 范围内的非空矩形与 1～255 的源像素亮度上限");
        }
    }
}

/// <summary>
/// 物品世界阴影的静态视觉参数：落地点使用物品根节点的局部坐标，
/// 椭圆底座阴影宽度以世界格为单位；只填写落地点时仍可单独校正太阳投影。
/// </summary>
[Serializable]
public sealed class ItemShadowVisualDefinitionDto
{
    #region 落地点与底座尺寸

    /// <summary>椭圆底座阴影宽度；未填写时不额外注册底座阴影。</summary>
    [JsonProperty("contactWidth", NullValueHandling = NullValueHandling.Ignore)]
    public float? ContactWidth;

    /// <summary>底座和太阳投影共用的物品局部落地点。</summary>
    [JsonProperty("footLocalPosition", NullValueHandling = NullValueHandling.Ignore)]
    public Vector2? FootLocalPosition;

    /// <summary>脚点相对可见底边的最小内缩；未填写时使用全局阴影默认值。</summary>
    [JsonProperty("footOverlap", NullValueHandling = NullValueHandling.Ignore)]
    public float? FootOverlap;

    #endregion
}

[Serializable]
public sealed class ItemColliderDefinitionDto
{
    [JsonProperty("path")]
    public string Path;

    [JsonProperty("type")]
    public string Type;

    [JsonProperty("enabled")]
    public bool? Enabled;

    [JsonProperty("isTrigger")]
    public bool? IsTrigger;

    [JsonProperty("offset")]
    public Vector2? Offset;

    [JsonProperty("size")]
    public Vector2? Size;

    [JsonProperty("radius")]
    public float? Radius;

    [JsonProperty("edgeRadius")]
    public float? EdgeRadius;

    [JsonProperty("direction")]
    public int? Direction;

    [JsonProperty("points")]
    public List<Vector2> Points;
}

[Serializable]
public sealed class ItemHealthDefinitionDto
{
    [JsonProperty("hasHp")]
    public bool HasHp = true;

    [JsonProperty("hp")]
    public float Hp = 100f;

    [JsonProperty("maxHp")]
    public float MaxHp = 100f;

    [JsonProperty("defense")]
    public ItemDefenseDefinitionDto Defense = new();

    /// <summary>DamageReceiver 模块节点相对 Item 根节点的位置。</summary>
    [JsonProperty("moduleLocalPosition", NullValueHandling = NullValueHandling.Ignore)]
    public Vector3? ModuleLocalPosition;

    /// <summary>DamageReceiver 模块自己的受击 Collider，不与物品交互 Collider 混用。</summary>
    [JsonProperty("collider", NullValueHandling = NullValueHandling.Ignore)]
    public ItemColliderDefinitionDto Collider;
}

[Serializable]
public sealed class ItemDefenseDefinitionDto
{
    [JsonProperty("cutting")] public float Cutting;
    [JsonProperty("piercing")] public float Piercing;
    [JsonProperty("chopping")] public float Chopping;
    [JsonProperty("blunt")] public float Blunt;
}

[Serializable]
public sealed class ItemModuleDefinitionDto
{
    /// <summary>模块 Prefab ID；外壳已内置该模块时也使用同一个稳定 ID。</summary>
    [JsonProperty("prefab")]
    public string Prefab;

    /// <summary>玩法侧模块 ID；可与用于实例化的 Prefab 地址不同。</summary>
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("enabled")]
    public bool? Enabled;

    /// <summary>模块持久化数据；会写入克隆后的具体 ModuleData 子类。</summary>
    [JsonProperty("data")]
    public JObject Data;

    /// <summary>直接映射到 Module 的可序列化字段，支持 $transform 特殊块。</summary>
    [JsonProperty("parameters")]
    public JObject Parameters;
}

/// <summary>校验并解析后的不可变运行时物品定义。</summary>
public sealed class RuntimeItemDefinition
{
    private readonly ItemData templateData;
    private readonly Dictionary<string, string> moduleParameters;
    private readonly Dictionary<string, string> modulePrefabIds;
    private readonly Dictionary<string, Sprite> visualStateSprites;

    public string Id { get; }
    public string ShellPrefabId { get; }
    public GameObject ShellPrefab { get; }
    public ItemVisualDefinitionDto Visual { get; }
    public ItemHealthDefinitionDto Health { get; }
    public string LootTableId { get; }
    public string WaterEntryTransformItemId { get; }
    /// <summary>当前物品定义的建筑放置承重门槛，不随实例存档覆盖。</summary>
    public int RequiredGroundSupport { get; }
    public string RendererPath => Visual?.RendererPath;
    public Sprite Sprite { get; }
    public Material Material { get; }
    public RuntimeAnimatorController AnimatorController { get; }
    public bool IsActor { get; }

    /// <summary>由当前内容编译的共享根级感知几何；非 Actor 通过旧对象 Bridge 感知。</summary>
    internal FlatWorld.Geometry.PerceptionShape2D[] ActorPerceptionShapes { get; }
    public IReadOnlyList<FlatWorld.Geometry.PerceptionShape2D> PerceptionShapes => ActorPerceptionShapes; // 数据后端只读共享几何。

    /// <summary>该定义的自然生成点是否使用无 Item、无碰撞体的植被图层。</summary>
    public bool IsGroundCover { get; }

    /// <summary>纯整数世界格偏移；不读取或保留 Unity Collider/Transform。</summary>
    public IReadOnlyList<GridCellOffset> WorldGridOccupancy { get; }

    /// <summary>名称在 String Table 中的稳定 key。</summary>
    public string LabelKey { get; }

    /// <summary>说明在 String Table 中的稳定 key。</summary>
    public string DescriptionKey { get; }

    /// <summary>定义中的默认名称，独立于业务 ID 和当前语言，供文本绑定使用。</summary>
    public string SourceDisplayName => templateData.GameName;

    /// <summary>按当前定义的语义标签匹配世界数据资源，无需实例化 Item。</summary>
    public bool HasTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || templateData.Tags == null)
            return false;
        for (int index = 0; index < templateData.Tags.Count; index++)
            if (string.Equals(templateData.Tags[index], tag, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>按名称键查询当前语言；界面统一使用此属性，不读取存档里的 GameName。</summary>
    public string DisplayName => FlatWorldLocalizationService.Get(LabelKey, SourceDisplayName);

    /// <summary>按当前语言返回物品说明；没有表时回退到 JSON description。</summary>
    public string Description => FlatWorldLocalizationService.Get(DescriptionKey, templateData?.Description ?? string.Empty);

    public RuntimeItemDefinition(
        string id,
        string shellPrefabId,
        GameObject shellPrefab,
        ItemData itemData,
        ItemVisualDefinitionDto visual,
        ItemHealthDefinitionDto health,
        string lootTableId,
        string waterEntryTransformItemId,
        Sprite sprite,
        Dictionary<string, string> parameters,
        Dictionary<string, string> prefabIds,
        string labelKey,
        string descriptionKey,
        RuntimeAnimatorController animatorController = null,
        bool isActor = false,
        Material material = null,
        Dictionary<string, Sprite> stateSprites = null,
        bool isGroundCover = false,
        IReadOnlyList<GridCellOffset> worldGridOccupancy = null,
        int requiredGroundSupport = 0)
    {
        Id = id;
        ShellPrefabId = shellPrefabId;
        ShellPrefab = shellPrefab;
        templateData = itemData;
        Visual = visual;
        Health = health;
        LootTableId = lootTableId;
        WaterEntryTransformItemId = string.IsNullOrWhiteSpace(waterEntryTransformItemId)
            ? null
            : waterEntryTransformItemId.Trim();
        RequiredGroundSupport = requiredGroundSupport;
        Sprite = sprite;
        Material = material;
        AnimatorController = animatorController;
        IsActor = isActor;
        ActorPerceptionShapes = isActor ? ActorPerceptionShapeCompiler.Compile(shellPrefab, visual?.Collider) : null;
        IsGroundCover = isGroundCover;
        var occupancyCells = worldGridOccupancy == null
            ? Array.Empty<GridCellOffset>()
            : new GridCellOffset[worldGridOccupancy.Count];
        if (worldGridOccupancy != null)
        {
            for (int i = 0; i < worldGridOccupancy.Count; i++)
                occupancyCells[i] = worldGridOccupancy[i];
        }
        WorldGridOccupancy = Array.AsReadOnly(occupancyCells);
        LabelKey = string.IsNullOrWhiteSpace(labelKey)
            ? FlatWorldLocalizationService.GetItemLabelKey(id)
            : labelKey.Trim();
        DescriptionKey = string.IsNullOrWhiteSpace(descriptionKey)
            ? FlatWorldLocalizationService.GetItemDescriptionKey(id)
            : descriptionKey.Trim();
        moduleParameters = parameters ?? new Dictionary<string, string>(StringComparer.Ordinal);
        modulePrefabIds = prefabIds ?? new Dictionary<string, string>(StringComparer.Ordinal);
        visualStateSprites = stateSprites ?? new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>按状态名读取已由资源目录统一持有的额外 Sprite。</summary>
    public bool TryGetVisualStateSprite(string stateName, out Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(stateName))
        {
            sprite = null;
            return false;
        }

        return visualStateSprites.TryGetValue(stateName.Trim(), out sprite) && sprite != null;
    }

    public ItemData CreateItemData()
    {
        return FastCloner.FastCloner.DeepClone(templateData);
    }

    #region 模块配置计划

    // 同一定义中的 JSON 与模块类型固定；配置计划随资源定义一起释放。
    private readonly Dictionary<(string Name, Type ModuleType), ModuleJsonConfigurator.PreparedParameters>
        preparedModuleParameters = new();

    /// <summary>一次资源定义内复用模块参数计划；资源重载会换用新的定义实例。</summary>
    public void ApplyModuleConfiguration(Module module, string moduleName, string moduleId)
    {
        if (module == null || !TryGetModuleParameters(moduleName, out string json) ||
            string.IsNullOrWhiteSpace(json))
            return;

        var key = (moduleName ?? string.Empty, module.GetType());
        if (!preparedModuleParameters.TryGetValue(key, out ModuleJsonConfigurator.PreparedParameters prepared))
        {
            prepared = ModuleJsonConfigurator.Prepare(module, Id, moduleName, moduleId, json);
            preparedModuleParameters.Add(key, prepared);
        }
        prepared.Apply(module);
    }

    #endregion

    public bool TryGetModuleParameters(string stableModuleName, out string json)
    {
        return moduleParameters.TryGetValue(stableModuleName ?? string.Empty, out json);
    }

    public string GetModulePrefabId(string stableModuleName, string fallbackId)
    {
        return modulePrefabIds.TryGetValue(stableModuleName ?? string.Empty, out string prefabId) &&
               !string.IsNullOrWhiteSpace(prefabId)
            ? prefabId
            : fallbackId;
    }
}
