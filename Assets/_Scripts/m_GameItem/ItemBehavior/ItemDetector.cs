using NaughtyAttributes;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;


#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public class ItemDetector : MonoBehaviour, IDebug, IDetector
{
    [SerializeField, BoxGroup("检测参数")]
    private float detectionRadius = 10f;

    [SerializeField, BoxGroup("检测参数")]
    public LayerMask itemLayer;

    [ShowNonSerializedField, BoxGroup("当前状态")]
    private List<Item> currentItemsInArea = new List<Item>();

    [ShowNativeProperty, Tooltip("当前状态")]
    public int CurrentItemCount => CurrentItemsInArea.Count;

    [Tooltip("string为tag,Item为Value的字典")]
    public Dictionary<string, Item> Tag_Item_Dict = new Dictionary<string, Item>();

    // IDebug接口实现
    [ShowNativeProperty]
    public bool DebugMode { get; set; }
    public List<Item> CurrentItemsInArea { get => currentItemsInArea; set => currentItemsInArea = value; }
    public float DetectionRadius { get => detectionRadius; set => detectionRadius = value; }

    [Button("强制更新检测器")]
    public void Update_Detector()
    {
        if (DebugMode)
            Debug.Log($"<color=yellow>=== 开始检测（位置：{transform.position}，半径：{DetectionRadius}）===</color>");

        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, DetectionRadius, itemLayer);

        List<Item> currentItems = hitColliders
            .Select(col =>
            {
                Item item = col.GetComponent<Item>();
                return item;
            })
            .Where(item => item != null)
            .Distinct()
            .ToList();

        Tag_Item_Dict.Clear();

        foreach (var item in currentItems)
        {
            foreach (var tag in item.Item_Data.ItemTags.Item_TypeTag)
            {
                Tag_Item_Dict[tag] = item;
            }
        }

        CheckItemEntries(currentItems);
    }

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




}

public interface IDetector
{
    void Update_Detector();

    public List<Item> CurrentItemsInArea { get; set; }

    public float DetectionRadius { get; set; }
}

public interface IDebug
{
    public bool DebugMode { get; set; }
}