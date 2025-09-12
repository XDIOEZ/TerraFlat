using MemoryPack;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class GrowData
{
    [Header("当前生长阶段")]
    public Mod_Grow.GrowState growState = Mod_Grow.GrowState.幼苗;

    [Header("各阶段的进度阈值 (0-100)")]
    public List<float> growState_Value = new List<float>() { 0, 20, 50, 100 };

    [Header("各阶段的缩放比例")]
    public List<float> growState_Scale = new List<float>() { 0.1f, 0.2f, 0.6f, 1f };

    [Header("当前生长进度 (0-100)")]
    public float GrowProgress = 0;

    [Header("最大生长进度")]
    public float MaxGrowProgress = 100;

    [Header("生长速度 (每秒增加多少进度点数)")]
    public float GrowSpeed = 5f;
}


public class Mod_Grow : Module
{
    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }

    [SerializeField]
    public GrowData Data = new GrowData();

    public enum GrowState
    {
        幼苗,
        生长,
        发育,
        成熟,
    }

    public override void Awake()
    {
        if (_Data.ID == "")
            _Data.ID = ModText.Grow;
    }

    public override void Load()
    {
        // 从 ModData 反序列化
        ModData.ReadData(ref Data);
        UpdateVisual();
    }

    public override void Save()
    {
        // 存到 ModData
        ModData.WriteData(Data);
    }

    public override void Action(float deltaTime)
    {
        if (Data.growState == GrowState.成熟) return; // 已成熟则不再生长

        // 增加生长进度
        Data.GrowProgress += Data.GrowSpeed * deltaTime;

        // 更新表现（缩放）
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        for (int i = 0; i < Data.growState_Value.Count; i++)
        {
            if (Data.GrowProgress >= Data.growState_Value[i])
            {
                Data.growState = (GrowState)i;
                item.transform.localScale = new Vector3(Data.growState_Scale[i], Data.growState_Scale[i], 1);
            }
        }
    }
}
