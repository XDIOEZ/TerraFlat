using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WorldSortingProfile = WorldRenderingConfig.WorldSortingProfile;

/// <summary>
/// 世界 Sprite 排序配置中心。类别与排序轴从统一的世界渲染 JSON 读取，
/// 保存 Unity Sorting Layer 和 Order；本组件声明动态类别并启用 Y 轴透明排序。
/// 玩家、生物、落地建筑在 Item 注册完成后接入；批量掉落物读取同一世界物品排序键。
/// MOD 可注册新类别和成员。
/// 地形 BRG 固定处于 Default，地表表现位于 Shadow；动态实体类别必须配置在两者之后。
/// </summary>
public sealed class WorldSortingManager : SingletonMono<WorldSortingManager>
{
    #region 配置
    private const string PlayerCategory = "player";
    public const string CreatureCategory = "creature"; // ECS 与旧生物共用类别。
    public const string BuildingCategory = "building"; // 建筑与机械动态视觉共用类别。
    public const string WorldItemCategory = "world-item"; // 普通 Item 与 ECS 掉落物共用类别。
    public const string GroundShadowCategory = "ground-shadow"; // 太阳与接触阴影共用地表排序键。
    public const string GroundMarkCategory = "ground-mark"; // 裂纹、脚印与耕地渐显。
    public const string GroundSplashCategory = "ground-splash"; // 雨滴与物品入水水花。
    public const string GroundPreviewCategory = "ground-preview"; // 建造与播种预览。
    public const string WorldEffectCategory = "world-effect"; // 脱离主体的世界特效。
    private static readonly string[] requiredCategories =
    {
        PlayerCategory, CreatureCategory, BuildingCategory, WorldItemCategory,
        GroundShadowCategory, GroundMarkCategory, GroundSplashCategory,
        GroundPreviewCategory, WorldEffectCategory
    };

    private readonly Dictionary<string, WorldSortingProfile> profiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> dynamicCategorySet = new(StringComparer.Ordinal);
    #endregion

    #region 生命周期
    /// <summary>读取类别配置并建立世界排序轴。</summary>
    protected override void Awake()
    {
        base.Awake();
        LoadProfiles();
        ConfigureDynamicAxis();
    }

    /// <summary>监听新实体，并纳入已比管理器更早注册的实体。</summary>
    private void OnEnable()
    {
        ItemMgr.RuntimeItemRegistered += RegisterItem;
        ItemMgr itemManager = ItemMgr.GetInstance();
        if (itemManager == null) return;
        foreach (Item item in itemManager.WorldRunTimeItems.Values)
            RegisterItem(item);
    }

    /// <summary>取消事件订阅。</summary>
    private void OnDisable()
    {
        ItemMgr.RuntimeItemRegistered -= RegisterItem;
    }
    #endregion

    #region 类别配置
    /// <summary>加载统一渲染 JSON 中的排序类别；缺失、重复或无效的 Unity 层直接报错。</summary>
    private void LoadProfiles()
    {
        profiles.Clear();
        dynamicCategorySet.Clear();
        foreach (string category in WorldRenderingConfigCatalog.Default.sorting.dynamicCategories)
            if (!string.IsNullOrWhiteSpace(category)) dynamicCategorySet.Add(category);

        foreach (WorldSortingProfile profile in WorldRenderingConfigCatalog.Default.sorting.profiles)
            RegisterProfile(profile);

        foreach (string category in requiredCategories)
            if (!profiles.ContainsKey(category))
                throw new InvalidOperationException($"世界渲染配置缺少排序类别 {category}。");
        ValidateDynamicProfiles();
        ValidateWorldDomainProfiles();
    }

    /// <summary>注册 MOD 新类别或替换现有类别配置；JSON 只定义静态排序键。</summary>
    public void RegisterProfile(string json)
    {
        WorldSortingProfile profile = JsonUtility.FromJson<WorldSortingProfile>(json);
        RegisterProfile(profile);
    }

    private void RegisterProfile(WorldSortingProfile profile)
    {
        if (profile == null || string.IsNullOrWhiteSpace(profile.category) ||
            string.IsNullOrWhiteSpace(profile.sortingLayer))
            throw new InvalidOperationException("世界排序 JSON 必须声明 category 和 sortingLayer。");

        bool layerExists = false;
        foreach (SortingLayer layer in SortingLayer.layers)
            if (layer.name == profile.sortingLayer) { layerExists = true; break; }
        if (!layerExists)
            throw new InvalidOperationException($"世界排序类别 {profile.category} 引用了不存在的层 {profile.sortingLayer}。");

        profiles[profile.category] = profile;
        ValidateDynamicProfiles();
        if (HasRequiredProfiles()) ValidateWorldDomainProfiles();
    }

    /// <summary>动态类别共用排序键，并位于 Default 地形与 Shadow 地表表现之后。</summary>
    private void ValidateDynamicProfiles()
    {
        WorldSortingProfile first = null;
        int terrainLayerValue = SortingLayer.GetLayerValueFromName(
            WorldRenderingConfigCatalog.Default.sorting.terrainSortingLayer);
        int groundEffectLayerValue = profiles.TryGetValue(GroundShadowCategory, out WorldSortingProfile ground)
            ? SortingLayer.GetLayerValueFromName(ground.sortingLayer) : terrainLayerValue;
        foreach (string category in dynamicCategorySet)
        {
            if (!profiles.TryGetValue(category, out WorldSortingProfile current)) continue;
            if (SortingLayer.GetLayerValueFromName(current.sortingLayer) <= groundEffectLayerValue)
                throw new InvalidOperationException($"动态类别 {category} 必须位于地表表现 Shadow 排序层前方。");
            if (first == null) { first = current; continue; }
            if (current.sortingLayer != first.sortingLayer || current.order != first.order)
                throw new InvalidOperationException("所有动态排序类别必须使用同一 Sorting Layer 和 Order。");
        }
    }

    /// <summary>约束地形、地表表现、动态实体与世界特效的全局先后关系。</summary>
    private void ValidateWorldDomainProfiles()
    {
        int terrain = SortingLayer.GetLayerValueFromName(
            WorldRenderingConfigCatalog.Default.sorting.terrainSortingLayer);
        GetSortingKey(GroundShadowCategory, out int shadowLayerId, out int shadowOrder);
        int ground = SortingLayer.GetLayerValueFromID(shadowLayerId);
        if (ground <= terrain)
            throw new InvalidOperationException("地表表现排序层必须位于 Default 地形之后。");

        string[] groundCategories = { GroundMarkCategory, GroundSplashCategory, GroundPreviewCategory };
        foreach (string category in groundCategories)
        {
            GetSortingKey(category, out int layerId, out int order);
            if (layerId != shadowLayerId || order <= shadowOrder)
                throw new InvalidOperationException($"地表表现类别 {category} 必须与阴影同层且顺序更高。");
        }
        GetSortingKey(PlayerCategory, out int playerLayerId, out _);
        int player = SortingLayer.GetLayerValueFromID(playerLayerId);
        if (player <= ground)
            throw new InvalidOperationException("动态实体排序层必须位于地表表现之后。");
        GetSortingKey(WorldEffectCategory, out int effectLayerId, out _);
        if (SortingLayer.GetLayerValueFromID(effectLayerId) <= player)
            throw new InvalidOperationException("世界特效排序层必须位于动态实体之后。");
    }

    /// <summary>判断正式类别配置是否已经全部载入，再验证跨类别关系。</summary>
    private bool HasRequiredProfiles()
    {
        foreach (string category in requiredCategories)
            if (!profiles.ContainsKey(category)) return false;
        return true;
    }

    /// <summary>同步全局透明轴；URP 2D Renderer 资源也配置为相同的 Y 轴。</summary>
    private static void ConfigureDynamicAxis()
    {
        GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
        GraphicsSettings.transparencySortAxis = WorldRenderingConfigCatalog.Default.sorting.transparencySortAxis;
    }
    #endregion

    #region 成员注册
    /// <summary>向批量表现与 MOD 提供类别的唯一图层和顺序来源。</summary>
    public void GetSortingKey(string category, out int sortingLayerId, out int sortingOrder)
    {
        if (string.IsNullOrWhiteSpace(category) || !profiles.TryGetValue(category, out WorldSortingProfile profile))
            throw new InvalidOperationException($"世界排序类别 {category} 未注册。");

        sortingLayerId = SortingLayer.NameToID(profile.sortingLayer);
        sortingOrder = profile.order;
    }

    /// <summary>供未进入 Play Mode 的编辑器工具读取正式类别资源。</summary>
    public static void GetResourceSortingKey(string category, out int sortingLayerId, out int sortingOrder)
    {
        WorldSortingProfile profile = null;
        foreach (WorldSortingProfile candidate in WorldRenderingConfigCatalog.Default.sorting.profiles)
            if (candidate != null && candidate.category == category) { profile = candidate; break; }
        if (profile == null || string.IsNullOrWhiteSpace(profile.sortingLayer))
            throw new InvalidOperationException($"世界渲染配置缺少排序类别 {category}。");
        sortingLayerId = SortingLayer.NameToID(profile.sortingLayer);
        sortingOrder = profile.order;
    }

    /// <summary>把独立地表表现或特效 Renderer 接入类别配置。</summary>
    public void ApplyRenderer(Renderer renderer, string category, int orderOffset = 0)
    {
        if (renderer == null)
            throw new ArgumentNullException(nameof(renderer));
        GetSortingKey(category, out int layerId, out int order);
        renderer.sortingLayerID = layerId;
        renderer.sortingOrder = checked(order + orderOffset);
    }

    /// <summary>离体代理读取源对象最外层有效排序组，避免继承子 Sprite 的原始层级。</summary>
    public static void ReadExternalRendererKey(Renderer source, out int layerId, out int order)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        layerId = source.sortingLayerID;
        order = source.sortingOrder;
        for (Transform current = source.transform; current != null; current = current.parent)
        {
            SortingGroup group = current.GetComponent<SortingGroup>();
            if (group == null || !group.enabled) continue;
            layerId = group.sortingLayerID;
            order = group.sortingOrder;
        }
    }

    /// <summary>接口入口：Prefab/MOD 的组件可自行注册到类别表。</summary>
    public void Register(WorldSortingMember member)
    {
        if (member == null || !member.IsWorldVisible || string.IsNullOrWhiteSpace(member.Category)) return;
        GetSortingKey(member.Category, out int sortingLayerId, out int sortingOrder);
        member.Apply(sortingLayerId, sortingOrder,
            dynamicCategorySet.Contains(member.Category));
    }

    /// <summary>从实体类型选类别并绑定主体；不修改阴影、特效或 Tilemap 的独立排序。</summary>
    private void RegisterItem(Item item)
    {
        if (item == null || item is Map) return;

        WorldSortingMember member = item.GetComponent<WorldSortingMember>();
        string category = member != null && !string.IsNullOrWhiteSpace(member.Category)
            ? member.Category : ResolveCategory(item);

        SpriteRenderer renderer = member != null && member.TargetRenderer != null
            ? member.TargetRenderer : ResolveMainRenderer(item);
        if (renderer == null && category == WorldItemCategory) return;
        if (renderer == null)
        {
            Debug.LogError($"[WorldSorting] {item.name} 缺少主体 SpriteRenderer。", item);
            return;
        }

        if (member == null)
            member = item.gameObject.AddComponent<WorldSortingMember>();
        member.Bind(category, renderer, item);
        BuildingDepthMeshBridge.Register(item);
    }

    /// <summary>优先使用 Item 的主体引用，尚未赋值时选非 Canvas 的有效世界精灵。</summary>
    private static SpriteRenderer ResolveMainRenderer(Item item)
    {
        if (item.Sprite != null) return item.Sprite;
        foreach (SpriteRenderer candidate in item.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (candidate.sprite != null && candidate.GetComponentInParent<Canvas>() == null)
                return candidate;
        }
        return null;
    }

    /// <summary>类别判断集中在一个具名入口，供后续实体类型或 MOD 替换。</summary>
    private static string ResolveCategory(Item item)
    {
        if (item is Player) return PlayerCategory;
        if (RuntimeAiEntityUtility.IsAiEntity(item)) return CreatureCategory;
        if (item.GetComponentInChildren<Mod_Building>(true) != null) return BuildingCategory;
        return WorldItemCategory;
    }
    #endregion

}
