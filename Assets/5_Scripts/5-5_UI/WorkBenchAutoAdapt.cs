using UnityEngine;
using System.Collections.Generic;
using Sirenix.OdinInspector;

/// <summary>
/// 工作台UI自动适配脚本
/// 通过查找UI_Content组件来定位Slot的父对象并统计Slot数量
/// 但调整大小的是当前脚本挂载的对象
/// </summary>
public class WorkBenchAutoAdapt : MonoBehaviour
{
    [Header("UI设置")]
    [Tooltip("可选：直接指定Slot的父对象，为空时会自动查找带有UI_Content组件的对象")]
    public Transform slotParentTransform;
    private RectTransform rectTransform;
    private const string SLOT_NAME_PREFIX = "UI_Slot";
    private const float SLOT_SIZE = 100f;
    private const float BORDER_SIZE = 100f; // 左右各50的边框

    private void Awake()
    {
        // 获取当前脚本挂载对象的RectTransform组件
        rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            Debug.LogError("WorkBenchAutoAdapt: 脚本必须挂载在带有RectTransform组件的对象上！");
        }

        // 如果没有指定slotParentTransform，则尝试查找带有UI_Content组件的对象
        if (slotParentTransform == null)
        {
            FindSlotParentTransform();
        }
    }

    private void Start()
    {
        // 初始调整一次大小
        AdjustSizeToFitSlots();
    }

    /// <summary>
    /// 查找带有UI_Content组件的对象作为Slot的父对象
    /// </summary>
    private void FindSlotParentTransform()
    {
        // 尝试在场景中查找带有UI_Content组件的对象
        UI_Content contentComponent = GetComponentInChildren<UI_Content>(true);
            slotParentTransform = contentComponent.transform;
    }

    /// <summary>
    /// 当子物体变化时自动调整大小
    /// </summary>
    private void OnTransformChildrenChanged()
    {
        AdjustSizeToFitSlots();
    }

   private void AdjustSizeToFitSlots()
{
    if (rectTransform == null)
        return;

    // 如果没有找到Slot的父对象，尝试再次查找
    if (slotParentTransform == null)
    {
        FindSlotParentTransform();
        if (slotParentTransform == null)
            return;
    }

    // 统计Slot父对象中所有子物体数量（不再检查名称前缀）
    int slotCount = slotParentTransform.childCount;

    // 计算网格大小
    int gridSize = Mathf.CeilToInt(Mathf.Sqrt(slotCount));
    
    // 计算UI大小
    float size = gridSize * SLOT_SIZE + BORDER_SIZE;
    
    // 应用新的大小到当前脚本挂载的对象
    rectTransform.sizeDelta = new Vector2(size, size);
    
    Debug.Log($"WorkBenchAutoAdapt: 检测到 {slotCount} 个子对象，调整当前对象大小为 {size}x{size}");
}

    /// <summary>
    /// 手动强制调整大小（用于编辑器中测试）
    /// </summary>
    [Button(ButtonSizes.Medium)]
    public void ForceAdjustSize()
    {
        // 如果没有找到Slot的父对象，再次尝试查找
        if (slotParentTransform == null)
        {
            FindSlotParentTransform();
        }
        AdjustSizeToFitSlots();
    }
}
