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


public partial class Mod_Grow : Module, IInteractable, IPlantableCrop
{
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.25f;

    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }

    [SerializeField]
    public GrowData Data = new GrowData();

    [Header("各生长阶段的最大生命值比例")]
    [Tooltip("以成熟阶段最大生命值为基准；列表顺序与生长阶段一致。")]
    [SerializeField]
    private List<float> growState_MaxHealthRatios = new List<float>() { 0.25f, 0.5f, 0.75f, 1f };

    [Header("成熟阶段最大生命值")]
    [SerializeField, Min(1f)]
    private float matureMaxHealth = 200f;

    public enum GrowState
    {
        幼苗,
        生长,
        发育,
        成熟,
    }

    // 缓存 DamageReceiver 组件
    private DamageReceiver cachedDamageReceiver;
    private int lastAppliedHealthStage = -1;

    public override void Awake()
    {
        if (_Data.ID == "")
            _Data.ID = ModText.Grow;
    }

    public override void Load()
    {
        // 从 ModData 反序列化
        ReadGrowthDataWithMigration();
        LoadAuthoritativeCropState();

        // 确保 cachedDamageReceiver，如果前面没成功，这里再尝试一次
        if (cachedDamageReceiver == null && item != null)
        {
            cachedDamageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        }
        lastAppliedHealthStage = -1;

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

    void AdjustByEnvironment(EnvironmentLayers layers, Vector2Int localPos)
    {
        InitializeNaturalEnvironment(layers, localPos);
    }





   /// <summary>
/// 合并视觉与生命值的一次性更新。
/// 保证阶段变更时先更新权威阶段，再同步缩放与最大生命值。
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
                ApplyStageHealth(true);
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
        UpdateAuthoritativeGrowth(deltaTime);
    }

private void ApplyStageHealth(bool force = false)
{
    if (item == null || Data == null)
        return;

    if (cachedDamageReceiver == null)
        cachedDamageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);

    if (cachedDamageReceiver == null ||
        growState_MaxHealthRatios == null ||
        growState_MaxHealthRatios.Count == 0)
    {
        return;
    }

    int stageIndex = Mathf.Clamp(
        (int)Data.growState,
        0,
        growState_MaxHealthRatios.Count - 1);

    if (!force && lastAppliedHealthStage == stageIndex)
        return;

    float healthRatio = cachedDamageReceiver.MaxHp > 0f
        ? Mathf.Clamp01(cachedDamageReceiver.Hp / cachedDamageReceiver.MaxHp)
        : 1f;
    float stageRatio = Mathf.Max(0.01f, growState_MaxHealthRatios[stageIndex]);
    float targetMaxHealth = Mathf.Max(1f, matureMaxHealth * stageRatio);

    cachedDamageReceiver.MaxHp = targetMaxHealth;
    cachedDamageReceiver.Hp = targetMaxHealth * healthRatio;
    cachedDamageReceiver.DataUpdate?.Invoke();
    lastAppliedHealthStage = stageIndex;
}

public void OnValidate()
{
    // 保证阈值、缩放等列表长度合理（可选容错）
    if (Data.growState_Value == null) Data.growState_Value = new List<float>() { 0, 20, 50, 100 };
    if (Data.growState_Scale == null) Data.growState_Scale = new List<float>() { 0.1f, 0.2f, 0.6f, 1f };
    if (growState_MaxHealthRatios == null || growState_MaxHealthRatios.Count == 0)
        growState_MaxHealthRatios = new List<float>() { 0.25f, 0.5f, 0.75f, 1f };
    matureMaxHealth = Mathf.Max(1f, matureMaxHealth);
    ValidateAuthoritativeCropConfig();
}
}
