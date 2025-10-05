using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public class ItemDetector : Module
{
    #region 检测参数
    [SerializeField, BoxGroup("检测参数")]
    private float detectionRadius = 10f;

    [SerializeField, BoxGroup("检测参数")]
    public LayerMask itemLayer;
    #endregion

    #region 当前状态
    [SerializeField, BoxGroup("当前状态")]
    private List<Item> currentItemsInArea = new List<Item>();

    [Tooltip("当前状态")]
    public int CurrentItemCount => CurrentItemsInArea.Count;

    [Tooltip("string为tag,Item列表为Value的字典")]
    [ShowInInspector]
    public Dictionary<string, List<Item>> Type_Tag_Item_Dict = new Dictionary<string, List<Item>>();
    #endregion

    #region 属性和字段
    public bool DebugMode { get; set; }
    
    public List<Item> CurrentItemsInArea 
    { 
        get => currentItemsInArea; 
        set => currentItemsInArea = value; 
    }
    
    public float DetectionRadius 
    { 
        get => detectionRadius; 
        set => detectionRadius = value; 
    }
    
    public Ex_ModData_MemoryPackable ModData;
    
    public override ModuleData _Data 
    { 
        get => ModData; 
        set => ModData = (Ex_ModData_MemoryPackable)value; 
    }
    #endregion

    #region 公共方法
    [Button("强制更新检测器")]
    public void Update_Detector()
    {
        if (DebugMode)
            Debug.Log($"<color=yellow>=== 开始检测（位置：{transform.position}，半径：{DetectionRadius}）===</color>");

        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, DetectionRadius, itemLayer);

        List<Item> currentItems = hitColliders
            .Select(col => col.GetComponent<Item>())
            .Where(item => item != null && item != this.item) // ✅ 排除自己
            .Distinct()
            .ToList();

        Type_Tag_Item_Dict.Clear();
        CurrentItemsInArea.Clear();

        foreach (var item in currentItems)
        {
            foreach (var tag in item.itemData.Tags.TypeTag.values)
            {
                // 如果标签不存在，创建新的列表
                if (!Type_Tag_Item_Dict.ContainsKey(tag))
                {
                    Type_Tag_Item_Dict[tag] = new List<Item>();
                }
                // 将物品添加到对应标签的列表中
                Type_Tag_Item_Dict[tag].Add(item);
            }
        }

        CheckItemEntries(currentItems);
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
        return new List<Item>();
    }
    
    /// <summary>
    /// 根据多个标签获取物品列表（并集）
    /// </summary>
    /// <param name="tags">要查询的标签列表</param>
    /// <returns>具有任一指定标签的物品列表</returns>
    public List<Item> GetItemsByTags(List<string> tags)
    {
        HashSet<Item> result = new HashSet<Item>();
        
        foreach (string tag in tags)
        {
            if (Type_Tag_Item_Dict.TryGetValue(tag, out List<Item> items))
            {
                foreach (Item item in items)
                {
                    result.Add(item);
                }
            }
        }
        
        return new List<Item>(result);
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
    List<Item> items = GetItemsByIdNamesFast(itemIds);
    return items.Count > 0 ? items[0] : null;
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
            
        // 获取第一个标签的物品列表作为基础
        List<Item> result = GetItemsByTag(tags[0]);
        
        // 对于后续标签，只保留同时存在于所有标签中的物品
        for (int i = 1; i < tags.Count; i++)
        {
            List<Item> currentTagItems = GetItemsByTag(tags[i]);
            result = result.Intersect(currentTagItems).ToList();
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

    // 创建ID名称的HashSet以提高查找效率
    HashSet<string> idSet = new HashSet<string>(itemIds);
    List<Item> result = new List<Item>();
    
    // 遍历所有当前检测到的物品
    foreach (Item item in CurrentItemsInArea)
    {
        // 使用HashSet快速检查是否存在
        if (idSet.Contains(item.itemData.IDName))
        {
            result.Add(item);
        }
    }
    
    return result;
}
    #endregion

    #region 私有方法
    private void CheckItemEntries(List<Item> currentItems)
    {
        if (DebugMode)
            Debug.Log($"<color=green>=== 检测物品变化 ===</color>");

        foreach (var item in currentItems)
        {
            if (!CurrentItemsInArea.Contains(item))
            {
                if (DebugMode)
                    Debug.Log($"<color=lime>进入区域：{item.name}（ID：{item.GetInstanceID()}）</color>");
                OnItemEnter(item);
            }
        }

        foreach (var item in CurrentItemsInArea.ToList())
        {
            if (!currentItems.Contains(item))
            {
                if (DebugMode)
                    Debug.Log($"<color=orange>离开区域：{item.name}（ID：{item.GetInstanceID()}）</color>");
                OnItemExit(item);
            }
        }

        CurrentItemsInArea = currentItems;

        if (DebugMode)
            Debug.Log($"<color=blue>当前区域物品总数：{CurrentItemCount}</color>");
    }

    private void OnItemEnter(Item item)
    {
        if (DebugMode)
            Debug.Log($"<color=green>处理进入事件：{item.name}</color>");
    }

    private void OnItemExit(Item item)
    {
        if (DebugMode)
            Debug.Log($"<color=orange>处理离开事件：{item.name}</color>");
    }
    #endregion

    #region Unity回调方法
#if UNITY_EDITOR
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

    public override void Load()
    {
        // throw new System.NotImplementedException();
    }

    public override void Save()
    {
        //throw new System.NotImplementedException();
    }
    #endregion
}