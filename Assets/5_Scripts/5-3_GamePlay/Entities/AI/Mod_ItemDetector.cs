using Sirenix.OdinInspector;
using System.Collections.Generic;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public class Mod_ItemDetector : Module
{
    #region 检测参数
    [SerializeField, BoxGroup("检测参数")]
    public float detectionRadius = 10f; // 检测半径

    [SerializeField, BoxGroup("检测参数")]
    public LayerMask itemLayer; // 物品所在的层级

    [SerializeField, BoxGroup("检测参数"), Tooltip("开启后，动态建筑占地、墙体和岩石等阻挡格会遮挡感知视线。")]
    public bool wallsBlockPerception = true; // 建筑与固定墙体是否遮挡感知
    #endregion

    #region 当前状态
    [SerializeField, BoxGroup("当前状态")]
    private List<Item> currentItemsInArea = new List<Item>(); // 当前区域内的物品列表

    [Tooltip("当前状态")]
    public int CurrentItemCount => CurrentItemsInArea.Count; // 当前区域内物品数量

    [Tooltip("string为tag,Item列表为Value的字典")]
    [ShowInInspector]
    public Dictionary<string, List<Item>> Type_Tag_Item_Dict
    {
        get { EnsureCachedTargetsMatchLogicalWorld(); return _typeTagItems; }
    }

    private readonly Dictionary<string, List<Item>> _typeTagItems = new Dictionary<string, List<Item>>();
    private readonly Dictionary<Item, ulong> _detectedWorldRevisions = new Dictionary<Item, ulong>();
    private readonly HashSet<Item> _currentItemSet = new HashSet<Item>();
    private readonly HashSet<Item> _previousItemSet = new HashSet<Item>();
    private readonly List<Item> _emptyItems = new List<Item>(0);
    private readonly List<Item> _rangeFilteredItems = new();
    private long _requestedVersion;
    private long _appliedVersion;
    private ulong _validatedOwnershipRevision = ulong.MaxValue;
    private string _validatedWorldKey;
    private int _validatedWorldIdentity;
    private ulong _validatedDetectorWorldRevision;
    private readonly GridLineOfSightQuery _lineOfSightQuery = new();

    public long RequestedVersion => _requestedVersion;
    public long AppliedVersion => _appliedVersion;
    #endregion

    #region 属性和字段
    public bool DebugMode { get; set; } = false; // 是否启用调试模式

    public List<Item> CurrentItemsInArea // 当前区域内的物品列表属性
    {
        get { EnsureCachedTargetsMatchLogicalWorld(); return currentItemsInArea; }
        set
        {
            currentItemsInArea = value ?? new List<Item>();
            _currentItemSet.Clear();
            _detectedWorldRevisions.Clear();
            for (int i = 0; i < currentItemsInArea.Count; i++)
            {
                Item target = currentItemsInArea[i];
                if (target != null)
                {
                    _currentItemSet.Add(target);
                    _detectedWorldRevisions[target] = GameplayWorldRegistry.GetItemWorldRevision(target);
                }
            }
            _validatedWorldIdentity = GetDetectorLogicalWorldIdentity();
            _validatedDetectorWorldRevision = item != null ? GameplayWorldRegistry.GetItemWorldRevision(item) : 0UL;
            _validatedWorldKey = GetDetectorLogicalWorldKey();
            _validatedOwnershipRevision = ulong.MaxValue;
        }
    }

    public float DetectionRadius // 检测半径属性
    {
        get => detectionRadius;
        set => detectionRadius = value;
    }

    public float MaximumDetectionRadius => item != null ? item.MaximumPerceptionRadius : float.PositiveInfinity;

    /// <summary>按当前感知上限与墙体遮挡检查目标是否可见。</summary>
    public bool HasLineOfSight(Item target)
    {
        if (target == null)
            return false;
        if (!wallsBlockPerception)
            return IsWithinPerceptionRadiusLimit(target);

        using GridLineOfSightQuery query = BeginLineOfSightQuery();
        return HasLineOfSight(target, query);
    }

    /// <summary>
    /// 获取该检测器对指定目标使用的最终感知半径。
    /// 基础检测半径乘目标倍率后，再应用外部来源的统一上限。
    /// </summary>
    public float GetEffectiveDetectionRadius(Item target)
    {
        return Mathf.Min(MaximumDetectionRadius, CalculateEffectiveDetectionRadius(
            DetectionRadius,
            target != null ? target.GetPerceptionRadiusMultiplier() : 1f));
    }

    /// <summary>按指定目标计算任意基础感知距离的最终范围，供 AI 状态阈值复用。</summary>
    public static float CalculateEffectiveDetectionRadius(float baseDetectionRadius, Item target)
    {
        return CalculateEffectiveDetectionRadius(
            baseDetectionRadius,
            target != null ? target.GetPerceptionRadiusMultiplier() : 1f);
    }

    /// <summary>按目标倍率修正观察者的基础感知半径。</summary>
    public static float CalculateEffectiveDetectionRadius(
        float baseDetectionRadius,
        float targetPerceptionRadiusMultiplier)
    {
        float safeRadius = float.IsNaN(baseDetectionRadius) || float.IsInfinity(baseDetectionRadius)
            ? 0f
            : Mathf.Max(0f, baseDetectionRadius);
        float safeMultiplier = float.IsNaN(targetPerceptionRadiusMultiplier) ||
                               float.IsInfinity(targetPerceptionRadiusMultiplier)
            ? 1f
            : Mathf.Max(0f, targetPerceptionRadiusMultiplier);
        return safeRadius * safeMultiplier;
    }

    public Ex_ModData_MemoryPackable ModData; // 模块数据

    public override ModuleData _Data // 重写的模块数据属性
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }
    #endregion

    #region 公共方法
    [Button("强制更新检测器")]
    /// <summary>
    /// 强制更新检测器，重新扫描当前区域内的物品
    /// </summary>
    public void Update_Detector()
    {
        RequestDetectorUpdate();
    }

    public long RequestDetectorUpdate()
    {
        _requestedVersion++;

        ItemMgr itemManager = ItemMgr.GetInstance();
        if (itemManager == null)
        {
            ApplyDetectorResults(_requestedVersion, _emptyItems);
            return _requestedVersion;
        }

        itemManager.QueueDetectorQuery(this, _requestedVersion);
        return _requestedVersion;
    }

    public bool IsRequestApplied(long requestVersion)
    {
        return requestVersion > 0 && _appliedVersion >= requestVersion;
    }

    internal void ApplyDetectorResults(long requestVersion, List<Item> detectedItems)
    {
        if (requestVersion != _requestedVersion)
            return;

        if (DebugMode)
            Debug.Log($"<color=yellow>=== 应用检测结果（位置：{transform.position}，半径：{DetectionRadius}）===</color>");

        _previousItemSet.Clear();
        // 进入/离开回调只输出调试日志，普通感知不再构造旧目标差集。
        if (DebugMode)
        {
            for (int i = 0; i < CurrentItemsInArea.Count; i++)
            {
                Item previousItem = CurrentItemsInArea[i];
                if (previousItem != null)
                    _previousItemSet.Add(previousItem);
            }
        }

        currentItemsInArea.Clear();
        _currentItemSet.Clear();
        _detectedWorldRevisions.Clear();
        int detectorWorld = GetDetectorLogicalWorldIdentity();
        if (detectedItems != null)
        {
            for (int i = 0; i < detectedItems.Count; i++)
            {
                Item detectedItem = detectedItems[i];
                if (detectorWorld != 0 && detectedItem != null && GameplayWorldRegistry.GetItemWorldIdentity(detectedItem) == detectorWorld &&
                    _currentItemSet.Add(detectedItem))
                {
                    currentItemsInArea.Add(detectedItem);
                    _detectedWorldRevisions[detectedItem] = GameplayWorldRegistry.GetItemWorldRevision(detectedItem);
                }
            }
        }

        foreach (List<Item> taggedItems in _typeTagItems.Values)
            taggedItems.Clear();

        // 重建标签映射字典
        for (int itemIndex = 0; itemIndex < currentItemsInArea.Count; itemIndex++)
        {
            Item detectedItem = currentItemsInArea[itemIndex];
            List<string> tags = detectedItem.itemData.Tags;
            for (int tagIndex = 0; tagIndex < tags.Count; tagIndex++)
            {
                string tag = tags[tagIndex];
                // 如果标签不存在，创建新的列表
                if (!_typeTagItems.TryGetValue(tag, out List<Item> taggedItems))
                {
                    taggedItems = new List<Item>(4);
                    _typeTagItems[tag] = taggedItems;
                }
                // 将物品添加到对应标签的列表中
                taggedItems.Add(detectedItem);
            }
        }

        // 仅 Debug 模式对比进入/离开变化，正常模式只保留正式目标和标签结果。
        _validatedWorldKey = GetDetectorLogicalWorldKey();
        _validatedOwnershipRevision = GameplayWorldRegistry.GetWorldItemOwnershipRevision(_validatedWorldKey);
        _validatedWorldIdentity = detectorWorld;
        _validatedDetectorWorldRevision = item != null ? GameplayWorldRegistry.GetItemWorldRevision(item) : 0UL;
        if (DebugMode)
            CheckItemEntries();
        _appliedVersion = requestVersion;
    }

    /// <summary>
    /// 根据标签获取物品列表
    /// </summary>
    /// <param name="tag">要查询的标签</param>
    /// <returns>具有指定标签的物品列表，如果标签不存在则返回空列表</returns>
    public List<Item> GetItemsByTag(string tag)
    {
        if (Type_Tag_Item_Dict.TryGetValue(tag, out List<Item> items))
        {
            return items;
        }
        return _emptyItems;
    }

    /// <summary>
    /// 根据多个标签获取物品列表（并集）
    /// </summary>
    /// <param name="tags">要查询的标签列表</param>
    /// <returns>具有任一指定标签的物品列表</returns>
    public List<Item> GetItemsByTags(List<string> tags)
    {
        List<Item> result = new List<Item>();
        if (tags == null)
            return result;

        for (int tagIndex = 0; tagIndex < tags.Count; tagIndex++)
        {
            string tag = tags[tagIndex];
            if (Type_Tag_Item_Dict.TryGetValue(tag, out List<Item> items))
            {
                for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
                {
                    Item detectedItem = items[itemIndex];
                    if (detectedItem != null && !result.Contains(detectedItem))
                        result.Add(detectedItem);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 根据标签获取第一个物品
    /// </summary>
    /// <param name="tag">要查询的标签</param>
    /// <returns>具有指定标签的第一个物品，如果没有则返回null</returns>
    public Item GetFirstItemByTag(string tag)
    {
        List<Item> items = GetItemsByTag(tag);
        return items.Count > 0 ? items[0] : null;
    }

    /// <summary>
    /// 根据物品ID名称列表获取第一个物品
    /// </summary>
    /// <param name="itemIds">要查询的物品ID名称列表</param>
    /// <returns>匹配指定ID名称的第一个物品，如果没有则返回null</returns>
    public Item GetFirstItemByIdNamesFast(List<string> itemIds)
    {
        if (itemIds == null || itemIds.Count == 0)
            return null;

        for (int itemIndex = 0; itemIndex < CurrentItemsInArea.Count; itemIndex++)
        {
            Item detectedItem = CurrentItemsInArea[itemIndex];
            if (detectedItem == null || detectedItem.itemData == null)
                continue;

            for (int idIndex = 0; idIndex < itemIds.Count; idIndex++)
            {
                if (detectedItem.itemData.IDName == itemIds[idIndex])
                    return detectedItem;
            }
        }

        return null;
    }

    /// <summary>
    /// 根据多个标签获取物品列表（交集）
    /// </summary>
    /// <param name="tags">要查询的标签列表</param>
    /// <returns>同时具有所有指定标签的物品列表</returns>
    public List<Item> GetItemsByTagsIntersection(List<string> tags)
    {
        if (tags == null || tags.Count == 0)
            return new List<Item>();

        List<Item> result = new List<Item>();
        for (int itemIndex = 0; itemIndex < CurrentItemsInArea.Count; itemIndex++)
        {
            Item detectedItem = CurrentItemsInArea[itemIndex];
            if (detectedItem == null || detectedItem.itemData?.Tags == null)
                continue;

            bool containsAllTags = true;
            for (int tagIndex = 0; tagIndex < tags.Count; tagIndex++)
            {
                if (!detectedItem.itemData.Tags.Contains(tags[tagIndex]))
                {
                    containsAllTags = false;
                    break;
                }
            }

            if (containsAllTags)
                result.Add(detectedItem);
        }

        return result;
    }

    /// <summary>
    /// 根据物品ID名称列表获取物品（高性能版本）
    /// </summary>
    /// <param name="itemIds">要查询的物品ID名称列表</param>
    /// <returns>匹配指定ID名称的物品列表</returns>
    public List<Item> GetItemsByIdNamesFast(List<string> itemIds)
    {
        if (itemIds == null || itemIds.Count == 0)
            return new List<Item>();

        List<Item> result = new List<Item>();

        // 遍历所有当前检测到的物品
        for (int itemIndex = 0; itemIndex < CurrentItemsInArea.Count; itemIndex++)
        {
            Item detectedItem = CurrentItemsInArea[itemIndex];
            if (detectedItem == null || detectedItem.itemData == null)
                continue;

            for (int idIndex = 0; idIndex < itemIds.Count; idIndex++)
            {
                if (detectedItem.itemData.IDName != itemIds[idIndex])
                    continue;

                result.Add(detectedItem);
                break;
            }
        }

        return result;
    }

    /// <summary>按物品标签寻找最近目标；启用玩家选项时同时识别 Player 实体和 ItemData 标签。</summary>
    public Item FindClosestItemByTags(List<string> tags, Vector3 origin, bool includePlayerEntities = false)
        => FindClosestItemByTagsInternal(tags, origin, includePlayerEntities, default, resolveActiveTopology: true);

    /// <summary>本轮筛选复用世界拓扑，避免逐候选重新读取星球边界。</summary>
    internal Item FindClosestItemByTags(List<string> tags, Vector3 origin, bool includePlayerEntities,
        in WorldTopologyDomain topology)
        => FindClosestItemByTagsInternal(tags, origin, includePlayerEntities, topology, resolveActiveTopology: false);

    private Item FindClosestItemByTagsInternal(List<string> tags, Vector3 origin,
        bool includePlayerEntities, WorldTopologyDomain topology, bool resolveActiveTopology)
    {
        Item closestItem = null;
        float closestDistanceSqr = float.MaxValue;
        float2 originPosition = new(origin.x, origin.y);

        for (int i = 0; i < CurrentItemsInArea.Count; i++)
        {
            Item detectedItem = CurrentItemsInArea[i];
            if (detectedItem == null)
                continue;

            bool matches = detectedItem.itemData != null && HasAnyTag(detectedItem.itemData.Tags, tags);
            if (!matches && includePlayerEntities)
                matches = detectedItem is Player ||
                          detectedItem.itemData?.Tags?.ContainsTag(Tag.Player) == true;
            if (!matches)
                continue;

            if (resolveActiveTopology)
            {
                topology = WorldTopologyRuntime.GetActiveDomain();
                resolveActiveTopology = false;
            }
            Vector3 targetPosition = detectedItem.transform.position;
            float distanceSqr = topology.SqrDistance(originPosition, new float2(targetPosition.x, targetPosition.y));
            if (distanceSqr >= closestDistanceSqr)
                continue;

            closestDistanceSqr = distanceSqr;
            closestItem = detectedItem;
        }

        return closestItem;
    }

    private static bool HasAnyTag(List<string> itemTags, List<string> targetTags)
    {
        if (itemTags == null || targetTags == null)
            return false;

        for (int targetIndex = 0; targetIndex < targetTags.Count; targetIndex++)
        {
            string targetTag = targetTags[targetIndex];
            for (int itemTagIndex = 0; itemTagIndex < itemTags.Count; itemTagIndex++)
            {
                if (itemTags[itemTagIndex] == targetTag)
                    return true;
            }
        }

        return false;
    }

    #endregion

    #region 视线遮挡

    // 只有拥有同一个有效逻辑世界的目标才能进入检测结果。
    private int GetDetectorLogicalWorldIdentity()
        => item != null ? GameplayWorldRegistry.GetItemWorldIdentity(item) : GameplayWorldRegistry.ExecutionWorldIdentity;

    private string GetDetectorLogicalWorldKey()
        => item != null ? GameplayWorldRegistry.GetItemWorldKey(item) : GameplayWorldRegistry.ExecutionWorldKey;

    internal bool IsTargetInDetectorLogicalWorld(Item target)
    {
        int worldIdentity = GetDetectorLogicalWorldIdentity();
        return worldIdentity != 0 && target != null && GameplayWorldRegistry.GetItemWorldIdentity(target) == worldIdentity;
    }

    // 归属发生变化时原位清除旧目标及标签，避免改变外部持有的列表引用。
    private void EnsureCachedTargetsMatchLogicalWorld()
    {
        string worldKey = GetDetectorLogicalWorldKey();
        ulong revision = GameplayWorldRegistry.GetWorldItemOwnershipRevision(worldKey);
        if (_validatedOwnershipRevision == revision && _validatedWorldKey == worldKey) return;
        int worldIdentity = GetDetectorLogicalWorldIdentity();
        ulong detectorRevision = item != null ? GameplayWorldRegistry.GetItemWorldRevision(item) : 0UL;
        bool detectorChanged = _validatedWorldIdentity != worldIdentity || _validatedDetectorWorldRevision != detectorRevision;
        _validatedOwnershipRevision = revision;
        _validatedWorldKey = worldKey;
        _validatedWorldIdentity = worldIdentity;
        _validatedDetectorWorldRevision = detectorRevision;
        bool changed = false;
        for (int i = currentItemsInArea.Count - 1; i >= 0; i--)
        {
            Item target = currentItemsInArea[i];
            if (!detectorChanged && target != null && worldIdentity != 0 &&
                GameplayWorldRegistry.TryGetItemWorldOwnership(target, out var owner) && owner.WorldIdentity == worldIdentity &&
                _detectedWorldRevisions.TryGetValue(target, out ulong targetRevision) &&
                owner.Revision == targetRevision) continue;
            currentItemsInArea.RemoveAt(i);
            if (!ReferenceEquals(target, null)) { _currentItemSet.Remove(target); _detectedWorldRevisions.Remove(target); }
            changed = true;
        }
        if (!changed) return;
        foreach (List<Item> targets in _typeTagItems.Values)
        {
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                Item target = targets[i];
                if (target == null || !_currentItemSet.Contains(target)) targets.RemoveAt(i);
            }
        }
    }

    // 已锁定目标和旧检测结果也受通用上限约束，关闭墙体遮挡不会绕过它。
    internal bool IsWithinPerceptionRadiusLimit(Item target)
    {
        if (!IsTargetInDetectorLogicalWorld(target)) return false;
        float radius = MaximumDetectionRadius;
        if (float.IsPositiveInfinity(radius)) return true;
        ItemMgr manager = ItemMgr.GetExistingInstance();
        if (manager != null && manager.TryIsWithinDataPerceptionRange(transform.position, target, radius, out bool within))
            return within;
        return WorldTopologyRuntime.SqrDistance(transform.position, target.transform.position) <= radius * radius;
    }

    private void HandlePerceptionRadiusLimitChanged()
    {
        _rangeFilteredItems.Clear();
        foreach (Item target in CurrentItemsInArea)
            if (IsWithinPerceptionRadiusLimit(target)) _rangeFilteredItems.Add(target);
        ApplyDetectorResults(++_requestedVersion, _rangeFilteredItems);
        _rangeFilteredItems.Clear();
        RequestDetectorUpdate();
    }

    /// <summary>本轮冻结真实观察者位置与世界上下文，候选射线复用寻址，结束后清空引用。</summary>
    internal GridLineOfSightQuery BeginLineOfSightQuery()
    {
        if (!wallsBlockPerception)
            return null;
        Vector2 observerPosition = transform.position;
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager == null)
        {
            _lineOfSightQuery.Begin(null, default, null, 1, 1, observerPosition);
        }
        else
        {
            chunkManager.BeginLineOfSightQuery(_lineOfSightQuery, observerPosition);
        }
        return _lineOfSightQuery;
    }

    internal bool HasLineOfSight(Item target, GridLineOfSightQuery query)
        => target != null && IsWithinPerceptionRadiusLimit(target) && (!wallsBlockPerception ||
                              query != null && query.HasClearLineTo(target.transform.position));

    #endregion

    #region 私有方法
    /// <summary>
    /// 检查物品进入和离开的变化
    /// </summary>
    private void CheckItemEntries()
    {
        if (DebugMode)
            Debug.Log($"<color=green>=== 检测物品变化（当前区域内：{CurrentItemsInArea.Count}个，上次检测：{_previousItemSet.Count}个） ===</color>");

        // 检查新进入的物品
        for (int i = 0; i < CurrentItemsInArea.Count; i++)
        {
            Item detectedItem = CurrentItemsInArea[i];
            if (!_previousItemSet.Contains(detectedItem))
            {
                if (DebugMode)
                    Debug.Log($"<color=lime>进入区域：{detectedItem.name}（ID：{detectedItem.GetInstanceID()}，物品ID：{detectedItem.itemData.IDName}）</color>");
                OnItemEnter(detectedItem);
            }
        }

        // 检查离开的物品
        foreach (Item previousItem in _previousItemSet)
        {
            if (!_currentItemSet.Contains(previousItem))
            {
                if (DebugMode)
                    Debug.Log($"<color=orange>离开区域：{previousItem.name}（ID：{previousItem.GetInstanceID()}，物品ID：{previousItem.itemData.IDName}）</color>");
                OnItemExit(previousItem);
            }
        }

        if (DebugMode)
            Debug.Log($"<color=blue>当前区域物品总数：{CurrentItemCount}</color>");
    }

    /// <summary>
    /// 处理物品进入事件
    /// </summary>
    /// <param name="item">进入的物品</param>
    private void OnItemEnter(Item item)
    {
        if (DebugMode)
            Debug.Log($"<color=green>处理进入事件：{item.name}（物品ID：{item.itemData.IDName}）</color>");
    }

    /// <summary>
    /// 处理物品离开事件
    /// </summary>
    /// <param name="item">离开的物品</param>
    private void OnItemExit(Item item)
    {
        if (DebugMode)
            Debug.Log($"<color=orange>处理离开事件：{item.name}（物品ID：{item.itemData.IDName}）</color>");
    }
    #endregion

    #region Unity回调方法
#if UNITY_EDITOR
    /// <summary>
    /// 绘制场景中的检测范围 gizmo
    /// </summary>
    private void OnDrawGizmos()
    {
        Color transparentYellow = new Color(1f, 0.92f, 0.016f, 0.4f); // 更淡的黄
        Color transparentRed = new Color(1f, 0f, 0f, 0.6f);           // 淡红

        Gizmos.color = transparentYellow;
        Gizmos.DrawWireSphere(transform.position, GetEffectiveDetectionRadius(null));

        if (Selection.Contains(gameObject))
        {
            Gizmos.color = transparentRed;
            Gizmos.DrawWireSphere(transform.position, GetEffectiveDetectionRadius(null));
        }
    }
#endif

    /// <summary>
    /// 加载模块数据
    /// </summary>
    protected override void OnLoad()
    {
        if (item != null)
        {
            item.PerceptionRadiusLimitChanged -= HandlePerceptionRadiusLimitChanged;
            item.PerceptionRadiusLimitChanged += HandlePerceptionRadiusLimitChanged;
        }
    }

    /// <summary>
    /// 保存模块数据
    /// </summary>
    protected override void OnSave()
    {
        // 可以在需要时实现
    }

    protected override void OnUnload()
    {
        if (item != null) item.PerceptionRadiusLimitChanged -= HandlePerceptionRadiusLimitChanged;
        currentItemsInArea.Clear();
        _currentItemSet.Clear();
        _detectedWorldRevisions.Clear();
        foreach (List<Item> targets in _typeTagItems.Values) targets.Clear();
        _validatedOwnershipRevision = ulong.MaxValue;
        _rangeFilteredItems.Clear();
        _lineOfSightQuery.Dispose();
    }

    private void OnValidate()
    {
        // 新增组件时数据尚未绑定，延后到模块装配设置能力身份。
        if (_Data != null) _Data.ID = ModText.Detector;
    }
    #endregion
}
