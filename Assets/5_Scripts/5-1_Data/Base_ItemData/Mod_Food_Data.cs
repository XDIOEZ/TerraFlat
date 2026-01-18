using DG.Tweening;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;
using UltEvents;

[MemoryPackable]
[System.Serializable]
public partial class Mod_Food_Data
{
    public Nutrition nutrition = new();//营养值
    public float Max_EatingProgress = 3;//最大进度
    public bool ShowCanvas = false;//面板显示状态
    public GameValue_float nutritionConsumeSpeed = new(1f);

    public bool FeelGood = false; // ← 加在这里
                                  // 添加子对象的面板位置作为持久化数据 在实例化面板时保存面板位置 在关闭面板时恢复面板位置 在Save函数中 如果面板存在 就保存面板的位置
    public Vector2 PanelPosition = new Vector2(0, 0);

    [Tooltip("水份消耗速度倍率")]
    public float WaterConsumeSpeedRate = 1f;
     [Tooltip("营养消耗倍率")]
    public float nutritionConsumeRate = 1f;
    [Tooltip("模块观察者的持久化状态bit流")]
    public byte[] ObserverState;
}