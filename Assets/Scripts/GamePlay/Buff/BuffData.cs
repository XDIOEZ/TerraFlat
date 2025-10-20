using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "新Buff数据", menuName = "Buff/新建BuffData")]
public class Buff_Data : ScriptableObject
{
    //Buff的ID
    public string buff_ID;
    //Buff的名称
    public string buff_Name; 
    //Buff的类型
    public string buff_Type; 
    //Buff的描述
    public string buff_Description;

    //Buff的持续时间
    public float buff_Duration = 5f;
    //buff的执行间隔
    public float buff_Interval = 0f;
    //buff的最大叠加数
    public int buff_MaxStack = 1;
    //buff的叠加类型
    public BuffStackType buff_StackType;

    [InlineEditor]
    public BuffAction buff_Behavior_Start;
    [InlineEditor]
    public BuffAction buff_Behavior_Update;
    [InlineEditor]
    public BuffAction buff_Behavior_Stop;
    
    /// <summary>
    /// 创建当前Buff_Data的深拷贝副本
    /// </summary>
    /// <returns>深拷贝的Buff_Data实例</returns>
    public Buff_Data Clone()
    {
        // 创建新的Buff_Data实例
        Buff_Data clonedData = Instantiate(this);
        
        // 深拷贝BuffAction字段
        if (this.buff_Behavior_Start != null)
        {
            clonedData.buff_Behavior_Start = Instantiate(buff_Behavior_Start);
        }
        
        if (this.buff_Behavior_Update != null)
        {
            clonedData.buff_Behavior_Update = Instantiate(buff_Behavior_Update);
        }
        
        if (this.buff_Behavior_Stop != null)
        {
            clonedData.buff_Behavior_Stop = Instantiate(buff_Behavior_Stop);
        }
        
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