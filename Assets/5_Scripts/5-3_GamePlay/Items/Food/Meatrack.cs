using System;
using System.Collections.Generic;
using Force.DeepCloner;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
[HideReferenceObjectPicker]
/// <summary>
/// 晾肉架单条风干规则：定义匹配条件、耗时、成功率与产出。
/// </summary>
public class MeatrackDryingRule
{
    [TableColumnWidth(100, Resizable = false)]
    [LabelText("规则名")]
    [Tooltip("规则名，便于在Inspector中区分")]
    public string RuleName = "完整肉块";

    [PropertySpace(SpaceBefore = 3)]
    [BoxGroup("输入条件")]
    [TableColumnWidth(220)]
    [LabelText("输入物品ID（任意命中）")]
    [ListDrawerSettings(DraggableItems = true, ShowFoldout = true, DefaultExpandedState = false, ShowPaging = true, NumberOfItemsPerPage = 4)]
    [Tooltip("输入物品ID列表，任意一个命中即可")]
    public List<string> InputItemIds = new List<string>();

    [BoxGroup("输入条件")]
    [TableColumnWidth(220)]
    [LabelText("输入Tag（任意命中）")]
    [ListDrawerSettings(DraggableItems = true, ShowFoldout = true, DefaultExpandedState = false, ShowPaging = true, NumberOfItemsPerPage = 4)]
    [Tooltip("输入Tag列表，任意一个命中即可")]
    public List<string> InputTags = new List<string>();

    [PropertySpace(SpaceBefore = 3)]
    [BoxGroup("输出与耗时")]
    [TableColumnWidth(120, Resizable = false)]
    [LabelText("输出物品ID")]
    [Required("输出物品ID不能为空")]
    [Tooltip("风干后输出物品ID（对应ItemData.IDName）")]
    public string OutputItemId = "Meat_Cooked";

    [BoxGroup("输出与耗时")]
    [TableColumnWidth(90, Resizable = false)]
    [Min(0.01f)]
    [LabelText("基础风干时长")]
    [SuffixLabel("秒", true)]
    [Tooltip("基础风干时长（秒）")]
    public float RequiredDryingSeconds = 120f;

    [BoxGroup("输出与耗时")]
    [TableColumnWidth(90, Resizable = false)]
    [Range(0f, 1f)]
    [LabelText("风干成功率")]
    [ProgressBar(0f, 1f)]
    [Tooltip("风干成功率，完整大肉块推荐0.5，肉条推荐1")]
    public float SuccessRate = 1f;

    [BoxGroup("输出与耗时")]
    [TableColumnWidth(90, Resizable = false)]
    [Min(0f)]
    [LabelText("食材风干倍率")]
    [SuffixLabel("x", true)]
    [Tooltip("该类型食材自身的风干速度倍率")]
    public float DrySpeedMultiplier = 1f;

    [BoxGroup("显示")]
    [TableColumnWidth(80, Resizable = false)]
    [LabelText("展示Sprite")]
    [PreviewField(50, ObjectFieldAlignment.Left)]
    [Tooltip("槽位物品展示Sprite，留空则尝试自动读取对应Prefab的Sprite")]
    public Sprite DisplaySprite;

    [BoxGroup("显示")]
    [TableColumnWidth(80, Resizable = false)]
    [LabelText("熏制覆盖Sprite")]
    [PreviewField(50, ObjectFieldAlignment.Left)]
    [Tooltip("熏制状态覆盖Sprite，留空则使用默认熏制Sprite或物品Sprite")]
    public Sprite SmokedStateSprite;
}

[Serializable]
[MemoryPackable]
/// <summary>
/// 晾肉架运行时存档数据：库存快照 + 每个槽位累计风干时间。
/// </summary>
public partial class MeatrackSaveData
{
    public Inventory_Data RackInventoryData;
    public List<float> SlotElapsedSeconds = new List<float>();
}

/// <summary>
/// 晾肉架模块：负责槽位交互、风干进度推进、热源加速与挂架显示刷新。
/// </summary>
public class Meatrack : MachineAuthoringModule
{

#region 常量

    private const string ModName = "晾肉架模块";
    private const string InventoryName = "晾肉架";
    private const float MinDryingSeconds = 0.01f;

#endregion

#region 配置字段

    [TabGroup("检查器", "高级")]
    [LabelText("模块存档数据")]
    public Ex_ModData_MemoryPackable ModSaveData = new Ex_ModData_MemoryPackable(); // 模块存档数据
    public override ModuleData _Data { get => ModSaveData; set => ModSaveData = (Ex_ModData_MemoryPackable)value; }

    [TabGroup("检查器", "高级")]
    [LabelText("晾肉架库存")]
    public Inventory RackInventory = new Inventory(); // 晾肉架库存（固定3槽）

    [TabGroup("检查器", "基础配置")]
    [LabelText("交互面板预制体")]
    public GameObject InventoryPanelPrefab; // 交互面板预制体

    [TabGroup("检查器", "基础配置")]
    [ReadOnly]
    [LabelText("槽位数量（固定）")]
    public int SlotCount = 3; // 晾肉槽位数量

    [TabGroup("检查器", "风干规则")]
    [InfoBox("规则从上到下依次匹配，命中第一条后停止匹配。")]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 240, MaxScrollViewHeight = 520)]
    [LabelText("风干规则表")]
    public List<MeatrackDryingRule> DryingRules = new List<MeatrackDryingRule>(); // 食材风干规则表

    [TabGroup("检查器", "热源加速")]
    [Min(0.1f)]
    [LabelText("热源检测半径")]
    [SuffixLabel("格", true)]
    public float HeatSourceRadius = 8f; // 热源检测半径（格）

    [TabGroup("检查器", "热源加速")]
    [Min(0.05f)]
    [LabelText("热源扫描间隔")]
    [SuffixLabel("秒", true)]
    public float HeatSourceScanInterval = 0.5f; // 热源扫描间隔（秒）

    [TabGroup("检查器", "热源加速")]
    [Min(0f)]
    [LabelText("每个热源倍率增量")]
    [SuffixLabel("x", true)]
    public float HeatSourceSpeedBoostPerSource = 1f; // 每个热源提升倍率，1=+100%

    [TabGroup("检查器", "热源加速")]
    [LabelText("热源检测层")]
    public LayerMask HeatSourceLayerMask = ~0; // 热源扫描层

    [TabGroup("检查器", "热源加速")]
    [LabelText("热源物品ID")]
    [ListDrawerSettings(DraggableItems = true, ShowFoldout = true, DefaultExpandedState = true, ShowPaging = false)]
    public List<string> HeatSourceItemIds = new List<string> { "Bonfire", "Smelter" }; // 直接视作热源的物品ID

    [TabGroup("检查器", "热源加速")]
    [LabelText("热源模块ID")]
    [ListDrawerSettings(DraggableItems = true, ShowFoldout = true, DefaultExpandedState = true, ShowPaging = false)]
    public List<string> HeatSourceModuleIds = new List<string> { "熔炼模块", "熔炉模块" }; // 带这些模块ID的物品也视作热源

    [TabGroup("检查器", "挂架显示")]
    [LabelText("槽位物品渲染器")]
    public List<SpriteRenderer> SlotItemRenderers = new List<SpriteRenderer>(); // 每个槽位的物品显示

    [TabGroup("检查器", "挂架显示")]
    [LabelText("槽位熏制渲染器")]
    public List<SpriteRenderer> SlotSmokeStateRenderers = new List<SpriteRenderer>(); // 每个槽位的熏制状态显示

    [TabGroup("检查器", "挂架显示")]
    [LabelText("默认熏制Sprite")]
    public Sprite DefaultSmokeStateSprite; // 默认熏制状态Sprite

    [TabGroup("检查器", "挂架显示")]
    [LabelText("初始熏制色")]
    public Color SmokeStartColor = new Color(1f, 1f, 1f, 0f); // 初始熏制色

    [TabGroup("检查器", "挂架显示")]
    [LabelText("完成熏制色")]
    public Color SmokeDoneColor = new Color(0.45f, 0.3f, 0.2f, 0.9f); // 完成熏制色

    [TabGroup("检查器", "挂架显示")]
    [LabelText("显示锚点偏移")]
    public Vector3 VisualAnchorOffset = new Vector3(0f, 0.8f, 0f); // 显示锚点偏移

    [TabGroup("检查器", "挂架显示")]
    [Min(0.05f)]
    [LabelText("槽位显示间距")]
    [SuffixLabel("格", true)]
    public float VisualSlotSpacing = 0.45f; // 槽位显示间距

    [TabGroup("检查器", "挂架显示")]
    [LabelText("物品层级")]
    public int ItemSpriteSortingOrder = 10; // 物品显示层级

    [TabGroup("检查器", "挂架显示")]
    [LabelText("熏制层级")]
    public int SmokeSpriteSortingOrder = 11; // 熏制显示层级

#endregion

}
