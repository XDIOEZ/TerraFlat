using MemoryPack;
using System;
using System.Collections.Generic;
using UnityEngine;
using static DamageReceiver;

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

    // 缓存DamageReceiver组件
    private DamageReceiver cachedDamageReceiver;

[System.Serializable]
public class LootEntryCollection
{
    public List<LootEntry> lootEntries = new List<LootEntry>();
}

[Header("各生长阶段的战利品")]
public List<LootEntryCollection> stageLoots = new List<LootEntryCollection>();

    public override void Awake()
    {
        if (_Data.ID == "")
            _Data.ID = ModText.Grow;
    }

    public override void Load()
    {
        // 从 ModData 反序列化
        ModData.ReadData(ref Data);

        // 确保 cachedDamageReceiver，如果前面没成功，这里再尝试一次
        if (cachedDamageReceiver == null && item != null)
        {
            cachedDamageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        }

        // 根据当前生长阶段直接更新视觉（缩放），仅视觉不添加战利品
        if (item != null && Data.growState_Scale != null && Data.growState_Scale.Count > 0)
        {
            // 将枚举转为索引并保护越界
            int idx = Mathf.Clamp((int)Data.growState, 0, Data.growState_Scale.Count - 1);

            float scale = Data.growState_Scale[idx];

            // 额外容错：如果 scale 非法（NaN/<=0），使用默认 1
            if (float.IsNaN(scale) || scale <= 0f)
                scale = 1f;

            item.transform.localScale = new Vector3(scale, scale, 1f);
        }
    }






   /// <summary>
/// 合并视觉与行为的一次性更新（当进度改变且希望同时更新视觉与行为时使用）。
/// 保证阶段变更时：先更新 Data.growState -> 更新视觉 -> 添加战利品（若未添加过）。
/// </summary>
private void UpdateVisualAndBehavior()
{
    // 从后往前查找当前应该处于的阶段，确保找到最高的符合条件的阶段
    for (int i = Data.growState_Value.Count - 1; i >= 0; i--)
    {
        if (Data.GrowProgress >= Data.growState_Value[i])
        {
            // 如果阶段发生变化
            if ((int)Data.growState != i)
            {
                // 更新生长阶段
                Data.growState = (GrowState)i;

                // 更新视觉表现（缩放），保护索引越界
                float scale = (i < Data.growState_Scale.Count) ? Data.growState_Scale[i] : 1f;
                item.transform.localScale = new Vector3(scale, scale, 1);

                // 执行阶段相关的行为（添加战利品），避免重复添加
                if (cachedDamageReceiver != null && stageLoots != null && stageLoots.Count > i)
                {
                    // 添加该阶段的所有战利品
                    foreach (LootEntry lootEntry in stageLoots[i-1].lootEntries)
                    {
                        if (lootEntry != null && !cachedDamageReceiver.Data.LootTable.Contains(lootEntry))
                        {
                            cachedDamageReceiver.Data.LootTable.Add(lootEntry);
                        }
                    }
                    Debug.Log($"植物进入{Data.growState}阶段，添加{stageLoots[i].lootEntries.Count}个战利品");
                }
            }
            break;
        }
    }
}

    public override void Save()
    {
        // 存到 ModData
        ModData.WriteData(Data);
    }

    public override void ModUpdate(float deltaTime)
    {
        if (Data.growState == GrowState.成熟) return; // 已成熟则不再生长

        // 增加生长进度
        Data.GrowProgress += Data.GrowSpeed * deltaTime;

        // 限制最大值
        if (Data.GrowProgress > Data.MaxGrowProgress)
            Data.GrowProgress = Data.MaxGrowProgress;
        if (Data.GrowProgress < 0)
            Data.GrowProgress = 0;

        // 同时更新视觉与行为（只在阶段变化时触发添加战利品）
        UpdateVisualAndBehavior();
    }

public void OnValidate()
{
    // 确保每个 LootEntry 的 PrefabName 等字段被更新
    if (stageLoots != null)
    {
        foreach (var lootList in stageLoots)
        {
            if (lootList != null)
            {
                foreach (var loot in lootList.lootEntries)
                {
                    if (loot != null)
                    {
                        loot.OnValidate();
                    }
                }
            }
        }
    }

    // 保证阈值、缩放等列表长度合理（可选容错）
    if (Data.growState_Value == null) Data.growState_Value = new List<float>() { 0, 20, 50, 100 };
    if (Data.growState_Scale == null) Data.growState_Scale = new List<float>() { 0.1f, 0.2f, 0.6f, 1f };
}
}
