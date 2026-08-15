using Sirenix.OdinInspector;
using System.Collections.Generic;
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
    #endregion

    #region 当前状态
    [SerializeField, BoxGroup("当前状态")]
    private List<Item> currentItemsInArea = new List<Item>(); // 当前区域内的物品列表

    [Tooltip("当前状态")]
    public int CurrentItemCount => CurrentItemsInArea.Count; // 当前区域内物品数量

    [Tooltip("string为tag,Item列表为Value的字典")]
    [ShowInInspector]
    public Dictionary<string, List<Item>> Type_Tag_Item_Dict = new Dictionary<string, List<Item>>(); // 标签与物品列表的映射字典

    private readonly HashSet<Item> _currentItemSet = new HashSet<Item>();
    private readonly HashSet<Item> _previousItemSet = new HashSet<Item>();
    private readonly List<Item> _emptyItems = new List<Item>(0);
    private long _requestedVersion;
    private long _appliedVersion;

    public long RequestedVersion => _requestedVersion;
    public long AppliedVersion => _appliedVersion;
    #endregion

    #region 属性和字段
    public bool DebugMode { get; set; } = false; // 是否启用调试模式

    public List<Item> CurrentItemsInArea // 当前区域内的物品列表属性
    {
        get => currentItemsInArea;
        set => currentItemsInArea = value;
    }

    public float DetectionRadius // 检测半径属性
    {
        get => detectionRadius;
        set => detectionRadius = value;
    }

    /// <summary>
    /// 获取该检测器对指定目标使用的最终感知半径。
    /// 观察者的基础检测半径与目标自身的被感知倍率共同生效。
    /// </summary>
    public float GetEffectiveDetectionRadius(Item target)
    {
        return CalculateEffectiveDetectionRadius(
            DetectionRadius,
            target != null ? target.GetPerceptionRadiusMultiplier() : 1f);
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
        for (int i = 0; i < CurrentItemsInArea.Count; i++)
        {
            Item previousItem = CurrentItemsInArea[i];
            if (previousItem != null)
                _previousItemSet.Add(previousItem);
        }

        CurrentItemsInArea.Clear();
        _currentItemSet.Clear();
        if (detectedItems != null)
        {
            for (int i = 0; i < detectedItems.Count; i++)
            {
                Item detectedItem = detectedItems[i];
                if (detectedItem != null && _currentItemSet.Add(detectedItem))
                    CurrentItemsInArea.Add(detectedItem);
            }
        }

        foreach (List<Item> taggedItems in Type_Tag_Item_Dict.Values)
            taggedItems.Clear();

        // 重建标签映射字典
        for (int itemIndex = 0; itemIndex < CurrentItemsInArea.Count; itemIndex++)
        {
            Item detectedItem = CurrentItemsInArea[itemIndex];
            List<string> tags = detectedItem.itemData.Tags;
            for (int tagIndex = 0; tagIndex < tags.Count; tagIndex++)
            {
                string tag = tags[tagIndex];
                // 如果标签不存在，创建新的列表
                if (!Type_Tag_Item_Dict.TryGetValue(tag, out List<Item> taggedItems))
                {
                    taggedItems = new List<Item>(4);
                    Type_Tag_Item_Dict[tag] = taggedItems;
                }
                // 将物品添加到对应标签的列表中
                taggedItems.Add(detectedItem);
            }
        }

        // 检查物品变化
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

    public Item FindClosestItemByTags(List<string> tags, Vector3 origin, bool includeUnityPlayerTag = false)
    {
        Item closestItem = null;
        float closestDistanceSqr = float.MaxValue;

        for (int i = 0; i < CurrentItemsInArea.Count; i++)
        {
            Item detectedItem = CurrentItemsInArea[i];
            if (detectedItem == null || detectedItem.itemData == null)
                continue;

            bool matches = HasAnyTag(detectedItem.itemData.Tags, tags);
            if (!matches && includeUnityPlayerTag)
                matches = detectedItem.CompareTag("Player");
            if (!matches)
                continue;

            float distanceSqr = WorldTopologyRuntime.SqrDistance(origin, detectedItem.transform.position);
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
        Gizmos.DrawWireSphere(transform.position, DetectionRadius);

        if (Selection.Contains(gameObject))
        {
            Gizmos.color = transparentRed;
            Gizmos.DrawWireSphere(transform.position, DetectionRadius);
        }
    }
#endif

    /// <summary>
    /// 加载模块数据
    /// </summary>
    public override void Load()
    {
        // 可以在需要时实现
    }

    /// <summary>
    /// 保存模块数据
    /// </summary>
    public override void Save()
    {
        // 可以在需要时实现
    }

    private void OnValidate()
    {
        _Data.ID = ModText.Detector;
    }
    #endregion
}
