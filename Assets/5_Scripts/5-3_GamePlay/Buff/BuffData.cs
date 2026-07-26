using UnityEngine;

[CreateAssetMenu(fileName = "新Buff数据", menuName = "Buff/新建BuffData")]
public class Buff_Data : ScriptableObject
{
    [Tooltip("Buff 的稳定唯一标识。存档和运行时查询均使用此值。")]
    public string buff_ID;

    [Tooltip("显示名称")]
    public string buff_Name;

    [Tooltip("旧版分类文本，保留给现有 UI 和资源使用。")]
    public string buff_Type;

    [Tooltip("结构化分类，供玩法逻辑可靠筛选。")]
    public BuffCategory buff_Category = BuffCategory.General;

    [Tooltip("描述文案")]
    public string buff_Description;

    [Min(0f)]
    [Tooltip("基础持续时间（秒）")]
    public float buff_Duration = 5f;

    [Min(0f)]
    [Tooltip("周期行为执行间隔（秒）；0 表示不执行周期行为。")]
    public float buff_Interval;

    [Min(1)]
    [Tooltip("最大叠加层数")]
    public int buff_MaxStack = 1;

    [Tooltip("重复获得同 ID Buff 时的处理方式")]
    public BuffStackType buff_StackType;

    [Tooltip("读档时 Start 行为的恢复策略。仅运行时修正未写入模块存档时才选择 ReapplyStart。")]
    public BuffLoadBehavior buff_LoadBehavior = BuffLoadBehavior.AssumeApplied;

    [Min(0f)]
    [Tooltip("完整喝下一份饮品时，为该 Buff 增加的持续时间（秒）。")]
    public float buff_DrinkDurationExtension;

    [SerializeReference]
    [Tooltip("开始时执行的行为")]
    public BuffAction buff_Behavior_Start;

    [SerializeReference]
    [Tooltip("按间隔执行的行为")]
    public BuffAction buff_Behavior_Update;

    [SerializeReference]
    [Tooltip("结束时执行的行为")]
    public BuffAction buff_Behavior_Stop;

    /// <summary>
    /// 为兼容旧调用保留。BuffManager 将定义视为只读资源，不再为每次添加克隆对象。
    /// </summary>
    public Buff_Data Clone()
    {
        return Instantiate(this);
    }

    private void OnValidate()
    {
        buff_Duration = Mathf.Max(0f, buff_Duration);
        buff_Interval = Mathf.Max(0f, buff_Interval);
        buff_MaxStack = Mathf.Max(1, buff_MaxStack);
        buff_DrinkDurationExtension = Mathf.Max(0f, buff_DrinkDurationExtension);
    }
}

public enum BuffStackType
{
    DurationAdd,
    RefreshDuration,
    StackCount,
    Keep
}

public enum BuffCategory
{
    General = 0,
    BloodLoss = 1
}

public enum BuffLoadBehavior
{
    AssumeApplied = 0,
    ReapplyStart = 1
}
