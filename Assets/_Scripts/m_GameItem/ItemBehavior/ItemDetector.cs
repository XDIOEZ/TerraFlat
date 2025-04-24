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
    [SerializeField, BoxGroup("������")]
    private float detectionRadius = 10f;

    [SerializeField, BoxGroup("������")]
    public LayerMask itemLayer;

    [ShowNonSerializedField, BoxGroup("��ǰ״̬")]
    private List<Item> currentItemsInArea = new List<Item>();

    [ShowNativeProperty, Tooltip("��ǰ״̬")]
    public int CurrentItemCount => CurrentItemsInArea.Count;

    [Tooltip("stringΪtag,ItemΪValue���ֵ�")]
    public Dictionary<string, Item> Tag_Item_Dict = new Dictionary<string, Item>();

    // IDebug�ӿ�ʵ��
    [ShowNativeProperty]
    public bool DebugMode { get; set; }
    public List<Item> CurrentItemsInArea { get => currentItemsInArea; set => currentItemsInArea = value; }
    public float DetectionRadius { get => detectionRadius; set => detectionRadius = value; }

    [Button("ǿ�Ƹ��¼����")]
    public void Update_Detector()
    {
        if (DebugMode)
            Debug.Log($"<color=yellow>=== ��ʼ��⣨λ�ã�{transform.position}���뾶��{DetectionRadius}��===</color>");

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
            Debug.Log($"<color=green>=== �����Ʒ�仯 ===</color>");

        foreach (var item in currentItems)
        {
            if (!CurrentItemsInArea.Contains(item))
            {
                if (DebugMode)
                    Debug.Log($"<color=lime>��������{item.name}��ID��{item.GetInstanceID()}��</color>");
                OnItemEnter(item);
            }
        }

        foreach (var item in CurrentItemsInArea.ToList())
        {
            if (!currentItems.Contains(item))
            {
                if (DebugMode)
                    Debug.Log($"<color=orange>�뿪����{item.name}��ID��{item.GetInstanceID()}��</color>");
                OnItemExit(item);
            }
        }

        CurrentItemsInArea = currentItems;

        if (DebugMode)
            Debug.Log($"<color=blue>��ǰ������Ʒ������{CurrentItemCount}</color>");
    }

    private void OnItemEnter(Item item)
    {
        if (DebugMode)
            Debug.Log($"<color=green>��������¼���{item.name}</color>");
    }

    private void OnItemExit(Item item)
    {
        if (DebugMode)
            Debug.Log($"<color=orange>�����뿪�¼���{item.name}</color>");
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Color transparentYellow = new Color(1f, 0.92f, 0.016f, 0.4f); // �����Ļ�
        Color transparentRed = new Color(1f, 0f, 0f, 0.6f);           // ����

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