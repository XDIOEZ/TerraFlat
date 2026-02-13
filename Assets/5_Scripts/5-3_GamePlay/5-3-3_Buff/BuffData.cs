using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "新Buff数据", menuName = "Buff/新建BuffData")]
public class Buff_Data : ScriptableObject
{
    [Tooltip("Buff的唯一标识")]
    public string buff_ID;
    [Tooltip("显示名称")]
    public string buff_Name;
    [Tooltip("分类或标签")]
    public string buff_Type;
    [Tooltip("描述文案")]
    public string buff_Description;

    [Tooltip("持续时间(秒)")]
    public float buff_Duration = 5f;
    [Tooltip("执行间隔(秒)，0为仅开始/结束")]
    public float buff_Interval = 0f;
    [Tooltip("最大叠加层数")]
    public int buff_MaxStack = 1;
    [Tooltip("叠加方式")]
    public BuffStackType buff_StackType;

    [SerializeReference]
    [Tooltip("开始时执行的行为")]
    public BuffAction buff_Behavior_Start;
    [SerializeReference]
    [Tooltip("间隔执行的行为")]
    public BuffAction buff_Behavior_Update;
    [SerializeReference]
    [Tooltip("结束时执行的行为")]
    public BuffAction buff_Behavior_Stop;

    /// <summary>
    /// 创建当前Buff_Data的深拷贝副本
    /// </summary>
    /// <returns>深拷贝的Buff_Data实例</returns>
    public Buff_Data Clone()
    {
        // 创建新的Buff_Data实例
        Buff_Data clonedData = Instantiate(this);
        return clonedData;
    }
}

public enum BuffStackType
{
    DurationAdd,       // 叠加的延长持续时间
    RefreshDuration,   // 每次刷新持续时间
    StackCount,        // 增加层数
    Keep
}