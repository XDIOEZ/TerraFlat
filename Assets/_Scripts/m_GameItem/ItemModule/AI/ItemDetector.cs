using NaughtyAttributes;
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

    [ShowNativeProperty, Tooltip("当前状态")]
    public int CurrentItemCount => CurrentItemsInArea.Count;

    [Tooltip("string为tag,Item列表为Value的字典")]
    public Dictionary<string, List<Item>> Type_Tag_Item_Dict = new Dictionary<string, List<Item>>();
    #endregion

    #region 属性和字段
    // IDebug接口实现
    [ShowNativeProperty]
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