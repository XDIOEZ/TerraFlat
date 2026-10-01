using System;
using System.Collections.Generic;
using Force.DeepCloner;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;

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
public class Mod_Meatrack : Mod_MachineAuthoring
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

    [TabGroup("检查器", "基础配置")]
    [Min(0f)]
    [LabelText("空气暴露倍率")]
    [SuffixLabel("x", true)]
    public float AirExposureMultiplier = 4f; // 晾架只提高空气暴露，具体风干结果由物品 matter 定义决定。

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
