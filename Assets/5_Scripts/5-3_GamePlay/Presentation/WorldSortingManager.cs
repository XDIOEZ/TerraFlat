using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 世界 Sprite 排序配置中心。Resources/GameConfig/WorldSorting 下每个类别一份 JSON，
/// 保存 Unity Sorting Layer 和 Order；本组件声明动态类别并启用 Y 轴透明排序。
/// 玩家、生物、落地建筑在 Item 注册完成后接入，MOD 可注册新类别和成员。
/// 地形 BRG 固定处于 Default 排序域；动态实体类别必须配置在更靠前的排序层。
/// </summary>
public sealed class WorldSortingManager : SingletonMono<WorldSortingManager>
{
    #region 配置
    private const string ProfileResourcePath = "GameConfig/WorldSorting";
    private const string PlayerCategory = "player";
    private const string CreatureCategory = "creature";
    private const string BuildingCategory = "building";
    private const string WorldItemCategory = "world-item";
    private const string TerrainSortingLayer = "Default";

    [SerializeField] private string[] dynamicCategories =
    {
        PlayerCategory, CreatureCategory, BuildingCategory, WorldItemCategory
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
    /// <summary>加载每类独立 JSON；缺失、重复或无效的 Unity 层直接报错。</summary>
    private void LoadProfiles()
    {
        profiles.Clear();
        dynamicCategorySet.Clear();
        foreach (string category in dynamicCategories)
            if (!string.IsNullOrWhiteSpace(category)) dynamicCategorySet.Add(category);

        foreach (TextAsset asset in Resources.LoadAll<TextAsset>(ProfileResourcePath))
            RegisterProfile(asset.text);

        foreach (string category in dynamicCategorySet)
            if (!profiles.ContainsKey(category))
                Debug.LogError($"[WorldSorting] 动态类别 {category} 缺少独立 JSON 配置。", this);

        ValidateDynamicProfiles();
    }

    /// <summary>注册 MOD 新类别或替换现有类别配置；JSON 只定义静态排序键。</summary>
    public void RegisterProfile(string json)
    {
        WorldSortingProfile profile = JsonUtility.FromJson<WorldSortingProfile>(json);
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
    }

    /// <summary>动态类别共用排序键，并位于地形 BRG 的 Default 层前方。</summary>
    private void ValidateDynamicProfiles()
    {
        WorldSortingProfile first = null;
        int terrainLayerValue = SortingLayer.GetLayerValueFromName(TerrainSortingLayer);
        foreach (string category in dynamicCategorySet)
        {
            if (!profiles.TryGetValue(category, out WorldSortingProfile current)) continue;
            if (SortingLayer.GetLayerValueFromName(current.sortingLayer) <= terrainLayerValue)
                throw new InvalidOperationException($"动态类别 {category} 必须位于地形 BRG 的 Default 排序层前方。");
            if (first == null) { first = current; continue; }
            if (current.sortingLayer != first.sortingLayer || current.order != first.order)
                throw new InvalidOperationException("所有动态排序类别必须使用同一 Sorting Layer 和 Order。");
        }
    }

    /// <summary>同步全局透明轴；URP 2D Renderer 资源也配置为相同的 Y 轴。</summary>
    private static void ConfigureDynamicAxis()
    {
        GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
        GraphicsSettings.transparencySortAxis = Vector3.up;
    }
    #endregion

    #region 成员注册
    /// <summary>接口入口：Prefab/MOD 的组件可自行注册到类别表。</summary>
    public void Register(WorldSortingMember member)
    {
        if (member == null || !member.IsWorldVisible || string.IsNullOrWhiteSpace(member.Category)) return;
        if (!profiles.TryGetValue(member.Category, out WorldSortingProfile profile))
        {
            Debug.LogError($"[WorldSorting] 缺少类别 {member.Category} 的 JSON。", member);
            return;
        }

        member.Apply(SortingLayer.NameToID(profile.sortingLayer), profile.order,
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

    #region JSON 数据
    /// <summary>单一类别的静态排序键；动态规则只在管理器中声明。</summary>
    [Serializable]
    private sealed class WorldSortingProfile
    {
        public string category;
        public string sortingLayer;
        public int order;
    }
    #endregion
}
