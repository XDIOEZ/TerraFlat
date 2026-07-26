using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Buff 生命周期、叠加、持久化和角色消费事件的统一入口。
/// </summary>
public class BuffManager : Module
{
    private const float TickInterval = 0.1f;

    [ShowInInspector]
    public Dictionary<string, BuffRunTime> BuffRunTimeData_Dic = new();

    public Ex_ModData_MemoryPackable ModData;

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }

    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => TickInterval;

    public event Action<BuffRunTime> BuffAdded;
    public event Action<BuffRunTime> BuffRemoved;
    public event Action<BuffRunTime> BuffDurationChanged;

    private readonly List<string> iterationIds = new(16);
    private readonly List<string> expiredIds = new(8);

    private Item buffReceiver;
    private Mod_Food observedFood;

    public override void Awake()
    {
        base.Awake();
        _Data.ID = ModText.BuffManager;
        buffReceiver = GetComponentInParent<Item>();

        if (buffReceiver == null)
            Debug.LogWarning("[BuffManager] 找不到父级 Item。", this);
    }

    public override void Load()
    {
        buffReceiver ??= item;
        BuffRunTimeData_Dic ??= new Dictionary<string, BuffRunTime>();

        if (ModData == null)
        {
            Debug.LogError("[BuffManager] ModData 为空，无法加载 Buff。", this);
            return;
        }

        try
        {
            ModData.ReadData(ref BuffRunTimeData_Dic);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[BuffManager] Buff 存档读取失败，已使用空状态：{exception.Message}", this);
            BuffRunTimeData_Dic = new Dictionary<string, BuffRunTime>();
        }

        BuffRunTimeData_Dic ??= new Dictionary<string, BuffRunTime>();
        InitializeBuffs();
        BindFoodEvents();
    }

    public override void Save()
    {
        if (ModData == null)
        {
            Debug.LogError("[BuffManager] ModData 为空，无法保存 Buff。", this);
            return;
        }

        ModData.WriteData(BuffRunTimeData_Dic);
    }

    private void OnDestroy()
    {
        UnbindFoodEvents();
    }

    private void InitializeBuffs()
    {
        if (BuffRunTimeData_Dic.Count == 0)
            return;

        iterationIds.Clear();
        iterationIds.AddRange(BuffRunTimeData_Dic.Keys);

        for (int i = 0; i < iterationIds.Count; i++)
        {
            string dictionaryId = iterationIds[i];
            if (!BuffRunTimeData_Dic.TryGetValue(dictionaryId, out BuffRunTime runtime) ||
                runtime == null)
            {
                BuffRunTimeData_Dic.Remove(dictionaryId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(runtime.buff_IDName))
                runtime.buff_IDName = dictionaryId;

            Item receiver = ResolveItem(runtime.receiverGuid) ?? buffReceiver;
            Item sender = ResolveItem(runtime.senderGuid);

            if (!runtime.SetBuffData(sender, receiver))
            {
                Debug.LogWarning($"[BuffManager] 已跳过缺少定义的 Buff：{runtime.buff_IDName}", this);
                BuffRunTimeData_Dic.Remove(dictionaryId);
                continue;
            }

            runtime.PrepareRestore();
            if (runtime.IsExpired)
                RemoveBuffInternal(dictionaryId, runtime, invokeStop: true);
        }
    }

    private static Item ResolveItem(int guid)
    {
        if (guid == 0 || ItemMgr.Instance == null)
            return null;

        return ItemMgr.Instance.GetItemByGuid(guid);
    }

    #region 添加与叠加

    public void AddBuff(Buff_Data buffData)
    {
        buffReceiver ??= item;
        AddBuffInternal(buffData, sender: null, receiver: buffReceiver);
    }

    public void AddBuffRuntime(Buff_Data buffData, Item receiver)
    {
        AddBuffInternal(buffData, sender: null, receiver: receiver);
    }

    public void AddBuffRuntime(Buff_Data buffData, Item sender, Item receiver)
    {
        AddBuffInternal(buffData, sender, receiver);
    }

    private bool AddBuffInternal(Buff_Data buffData, Item sender, Item receiver)
    {
        if (buffData == null)
        {
            Debug.LogWarning("[BuffManager] 不能添加空 Buff 定义。", this);
            return false;
        }

        if (receiver == null)
        {
            Debug.LogWarning($"[BuffManager] Buff {buffData.buff_ID} 缺少接收者。", this);
            return false;
        }

        string buffId = buffData.buff_ID?.Trim();
        if (string.IsNullOrEmpty(buffId))
        {
            Debug.LogError($"[BuffManager] Buff 资源 {buffData.name} 的 ID 为空。", buffData);
            return false;
        }

        BuffRunTimeData_Dic ??= new Dictionary<string, BuffRunTime>();
        if (BuffRunTimeData_Dic.TryGetValue(buffId, out BuffRunTime existing) &&
            existing != null)
        {
            return HandleBuffStack(buffData, existing);
        }

        BuffRunTime runtime = new()
        {
            buff_IDName = buffId,
            buff = buffData,
            buff_CurrentDuration = 0f,
            buff_CurrentStack = 1f
        };

        if (!runtime.SetBuffData(sender, receiver))
            return false;

        BuffRunTimeData_Dic[buffId] = runtime;
        runtime.OnBuff_Start();
        BuffAdded?.Invoke(runtime);
        return true;
    }

    private bool HandleBuffStack(Buff_Data incoming, BuffRunTime existing)
    {
        switch (incoming.buff_StackType)
        {
            case BuffStackType.DurationAdd:
                existing.ExtendDuration(Mathf.Max(0f, incoming.buff_Duration));
                BuffDurationChanged?.Invoke(existing);
                return true;

            case BuffStackType.RefreshDuration:
                existing.RefreshDuration();
                BuffDurationChanged?.Invoke(existing);
                return true;

            case BuffStackType.StackCount:
                existing.TryAddStack();
                BuffDurationChanged?.Invoke(existing);
                return true;

            case BuffStackType.Keep:
                return false;

            default:
                Debug.LogWarning($"[BuffManager] 未知叠加类型：{incoming.buff_StackType}", this);
                return false;
        }
    }

    #endregion

    #region 查询与延时

    public bool HasBuff(string buffId)
    {
        return !string.IsNullOrWhiteSpace(buffId) &&
               BuffRunTimeData_Dic != null &&
               BuffRunTimeData_Dic.ContainsKey(buffId);
    }

    public bool TryGetBuff(string buffId, out BuffRunTime runtime)
    {
        runtime = null;
        return !string.IsNullOrWhiteSpace(buffId) &&
               BuffRunTimeData_Dic != null &&
               BuffRunTimeData_Dic.TryGetValue(buffId, out runtime);
    }

    public bool TryExtendBuffDuration(string buffId, float seconds)
    {
        if (seconds <= 0f || !TryGetBuff(buffId, out BuffRunTime runtime))
            return false;

        if (runtime.ExtendDuration(seconds) <= 0f)
            return false;

        BuffDurationChanged?.Invoke(runtime);
        return true;
    }

    /// <summary>
    /// 完整喝下一份饮品后调用。每个血液流逝 Buff 使用自己的固定延时配置。
    /// </summary>
    public int ExtendBloodLossBuffsForDrink()
    {
        if (BuffRunTimeData_Dic == null || BuffRunTimeData_Dic.Count == 0)
            return 0;

        int extendedCount = 0;
        iterationIds.Clear();
        iterationIds.AddRange(BuffRunTimeData_Dic.Keys);

        for (int i = 0; i < iterationIds.Count; i++)
        {
            if (!BuffRunTimeData_Dic.TryGetValue(iterationIds[i], out BuffRunTime runtime) ||
                runtime?.buff == null ||
                runtime.buff.buff_Category != BuffCategory.BloodLoss)
            {
                continue;
            }

            float extension = Mathf.Max(0f, runtime.buff.buff_DrinkDurationExtension);
            if (extension <= 0f)
                continue;

            runtime.ExtendDuration(extension);
            BuffDurationChanged?.Invoke(runtime);
            extendedCount++;
        }

        return extendedCount;
    }

    #endregion

    #region 移除与更新

    public void RemoveBuff(string buffId)
    {
        if (string.IsNullOrWhiteSpace(buffId) ||
            BuffRunTimeData_Dic == null ||
            !BuffRunTimeData_Dic.TryGetValue(buffId, out BuffRunTime runtime))
        {
            return;
        }

        RemoveBuffInternal(buffId, runtime, invokeStop: true);
    }

    public void ClearAllBuffs()
    {
        if (BuffRunTimeData_Dic == null || BuffRunTimeData_Dic.Count == 0)
            return;

        iterationIds.Clear();
        iterationIds.AddRange(BuffRunTimeData_Dic.Keys);

        for (int i = 0; i < iterationIds.Count; i++)
        {
            string buffId = iterationIds[i];
            if (BuffRunTimeData_Dic.TryGetValue(buffId, out BuffRunTime runtime))
                RemoveBuffInternal(buffId, runtime, invokeStop: true);
        }
    }

    public override void ModUpdate(float deltaTime)
    {
        Tick(deltaTime);
    }

    public void Tick(float deltaTime)
    {
        if (BuffRunTimeData_Dic == null ||
            BuffRunTimeData_Dic.Count == 0 ||
            deltaTime <= 0f)
        {
            return;
        }

        iterationIds.Clear();
        expiredIds.Clear();
        iterationIds.AddRange(BuffRunTimeData_Dic.Keys);

        for (int i = 0; i < iterationIds.Count; i++)
        {
            string buffId = iterationIds[i];
            if (!BuffRunTimeData_Dic.TryGetValue(buffId, out BuffRunTime runtime) ||
                runtime == null ||
                runtime.Tick(deltaTime))
            {
                expiredIds.Add(buffId);
            }
        }

        for (int i = 0; i < expiredIds.Count; i++)
        {
            string buffId = expiredIds[i];
            if (BuffRunTimeData_Dic.TryGetValue(buffId, out BuffRunTime runtime))
                RemoveBuffInternal(buffId, runtime, invokeStop: true);
        }
    }

    private void RemoveBuffInternal(string buffId, BuffRunTime runtime, bool invokeStop)
    {
        if (runtime != null && invokeStop)
            runtime.OnBuff_Stop();

        BuffRunTimeData_Dic.Remove(buffId);
        if (runtime != null)
            BuffRemoved?.Invoke(runtime);
    }

    #endregion

    #region 饮水事件

    private void BindFoodEvents()
    {
        UnbindFoodEvents();
        if (item?.itemMods == null)
            return;

        observedFood = item.itemMods.GetMod_ByID(ModText.Food) as Mod_Food;
        if (observedFood != null)
            observedFood.ConsumeCompleted += OnConsumeCompleted;
    }

    private void UnbindFoodEvents()
    {
        if (observedFood != null)
            observedFood.ConsumeCompleted -= OnConsumeCompleted;

        observedFood = null;
    }

    private void OnConsumeCompleted(FoodConsumeResult result)
    {
        if (!result.IsDrink)
            return;

        ExtendBloodLossBuffsForDrink();
    }

    #endregion

    #region 调试入口

    [Button("调试：添加失血")]
    private void DebugAddBloodLoss()
    {
        DebugAddBuff(BloodLossBuffIds.BloodLoss);
    }

    [Button("调试：添加流血")]
    private void DebugAddBleeding()
    {
        DebugAddBuff(BloodLossBuffIds.Bleeding);
    }

    [Button("调试：添加出血")]
    private void DebugAddHemorrhage()
    {
        DebugAddBuff(BloodLossBuffIds.Hemorrhage);
    }

    [Button("调试：模拟完整喝水一次")]
    private void DebugDrinkOnce()
    {
        int count = ExtendBloodLossBuffsForDrink();
        Debug.Log($"[BuffManager] 模拟喝水完成，延长 {count} 个血液流逝 Buff。", this);
    }

    [Button("调试：清除全部 Buff")]
    private void DebugClearAll()
    {
        ClearAllBuffs();
    }

    private void DebugAddBuff(string buffId)
    {
        Buff_Data definition = GameRes.Instance?.GetBuffData(buffId);
        if (definition == null)
        {
            Debug.LogWarning($"[BuffManager] 调试添加失败，找不到 Buff：{buffId}", this);
            return;
        }

        AddBuff(definition);
    }

    #endregion
}

public static class BloodLossBuffIds
{
    public const string BloodLoss = "失血";
    public const string Bleeding = "流血";
    public const string Hemorrhage = "出血";
}
