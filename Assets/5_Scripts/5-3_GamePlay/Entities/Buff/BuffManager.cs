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
    private const string LegacyModuleId = "Buff模块";

    [ShowInInspector]
    public Dictionary<string, BuffInstance> ActiveBuffs =
        new(StringComparer.OrdinalIgnoreCase);

    public Ex_ModData_MemoryPackable ModData;

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }

    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => TickInterval;
    public override string CanonicalModuleId => ModText.BuffManager;

    public override bool MatchesPersistedId(string persistedId)
    {
        return base.MatchesPersistedId(persistedId) ||
               string.Equals(persistedId?.Trim(), LegacyModuleId, StringComparison.OrdinalIgnoreCase);
    }

    public event Action<BuffInstance> BuffAdded;
    public event Action<BuffInstance> BuffRemoved;
    public event Action<BuffInstance> BuffDurationChanged;

    /// <summary>限时 Buff 的显示秒数变化时触发，供只读表现层按需刷新倒计时。</summary>
    public event Action<BuffInstance> BuffCountdownChanged;

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
        if (ModData == null)
        {
            Debug.LogError("[BuffManager] ModData 为空，无法加载 Buff。", this);
            return;
        }

        try
        {
            var saveData = new BuffManagerSaveData();
            ModData.ReadData(ref saveData);
            ActiveBuffs = new Dictionary<string, BuffInstance>(StringComparer.OrdinalIgnoreCase);

            foreach (BuffInstance runtime in saveData?.Buffs ?? new List<BuffInstance>())
            {
                if (runtime == null || string.IsNullOrWhiteSpace(runtime.DefinitionId))
                    continue;

                if (!ActiveBuffs.TryAdd(runtime.DefinitionId, runtime))
                {
                    throw new InvalidOperationException(
                        $"Buff 存档包含重复定义 ID：{runtime.DefinitionId}");
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"[BuffManager] Buff 存档读取失败，已使用空状态：{exception.Message}", this);
            ActiveBuffs = new Dictionary<string, BuffInstance>(StringComparer.OrdinalIgnoreCase);
        }

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

        var runtimes = new List<BuffInstance>(ActiveBuffs.Values);
        runtimes.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(
            left?.DefinitionId,
            right?.DefinitionId));
        ModData.WriteData(new BuffManagerSaveData { Buffs = runtimes });
    }

    private void OnDestroy()
    {
        UnbindFoodEvents();
    }

    private void InitializeBuffs()
    {
        if (ActiveBuffs.Count == 0)
            return;

        iterationIds.Clear();
        iterationIds.AddRange(ActiveBuffs.Keys);

        for (int i = 0; i < iterationIds.Count; i++)
        {
            string dictionaryId = iterationIds[i];
            if (!ActiveBuffs.TryGetValue(dictionaryId, out BuffInstance runtime) ||
                runtime == null)
            {
                ActiveBuffs.Remove(dictionaryId);
                continue;
            }

            if (!runtime.Restore(buffReceiver))
            {
                Debug.LogWarning($"[BuffManager] 已跳过无效 Buff：{runtime.DefinitionId}", this);
                ActiveBuffs.Remove(dictionaryId);
                continue;
            }

            if (runtime.IsExpired)
                RemoveBuffInternal(dictionaryId, runtime, invokeStop: true);
        }
    }

    #region 添加与叠加

    public bool AddBuff(string buffId)
    {
        buffReceiver ??= item;
        if (string.IsNullOrWhiteSpace(buffId))
        {
            Debug.LogWarning("[BuffManager] 不能添加空 Buff ID。", this);
            return false;
        }

        BuffDefinition definition = GameRes.Instance?.GetBuffDefinition(buffId.Trim());
        if (definition == null)
        {
            Debug.LogWarning($"[BuffManager] 找不到 Buff JSON 定义：{buffId}", this);
            return false;
        }

        if (buffReceiver == null)
        {
            Debug.LogWarning($"[BuffManager] Buff {definition.Id} 缺少接收者。", this);
            return false;
        }

        string definitionId = definition.Id;
        if (ActiveBuffs.TryGetValue(definitionId, out BuffInstance existing) &&
            existing != null)
        {
            return HandleBuffStack(definition, existing);
        }

        var runtime = new BuffInstance();
        if (!runtime.Initialize(definition, buffReceiver))
            return false;

        ActiveBuffs[definitionId] = runtime;
        runtime.Start();
        BuffAdded?.Invoke(runtime);
        return true;
    }

    private bool HandleBuffStack(BuffDefinition incoming, BuffInstance existing)
    {
        switch (incoming.StackMode)
        {
            case BuffStackMode.ExtendDuration:
                existing.ExtendDuration(Mathf.Max(0f, incoming.DurationSeconds ?? 0f));
                BuffDurationChanged?.Invoke(existing);
                return true;

            case BuffStackMode.RefreshDuration:
                existing.RefreshDuration();
                BuffDurationChanged?.Invoke(existing);
                return true;

            case BuffStackMode.Ignore:
                return true;

            default:
                Debug.LogWarning($"[BuffManager] 未知叠加模式：{incoming.StackMode}", this);
                return false;
        }
    }

    #endregion

    #region 查询与延时

    public bool HasBuff(string buffId)
    {
        return !string.IsNullOrWhiteSpace(buffId) &&
               ActiveBuffs.ContainsKey(buffId);
    }

    public bool TryGetBuff(string buffId, out BuffInstance runtime)
    {
        runtime = null;
        return !string.IsNullOrWhiteSpace(buffId) &&
               ActiveBuffs.TryGetValue(buffId, out runtime);
    }

    public bool TryExtendBuffDuration(string buffId, float seconds)
    {
        if (seconds <= 0f || !TryGetBuff(buffId, out BuffInstance runtime))
            return false;

        if (!runtime.ExtendDuration(seconds))
            return false;

        BuffDurationChanged?.Invoke(runtime);
        return true;
    }

    /// <summary>
    /// 覆盖一个限时 Buff 的剩余时间。
    /// 永久 Buff 不支持覆盖，以免运行时调试操作改变 JSON 定义的永久语义。
    /// </summary>
    public bool TrySetBuffDuration(string buffId, float seconds)
    {
        if (!TryGetBuff(buffId, out BuffInstance runtime) ||
            !runtime.TrySetRemainingDuration(seconds))
        {
            return false;
        }

        BuffDurationChanged?.Invoke(runtime);
        return true;
    }

    /// <summary>
    /// 完整喝下一份饮品后调用。每个血液流逝 Buff 使用自己的固定延时配置。
    /// </summary>
    public int ExtendBloodLossBuffsForDrink()
    {
        if (ActiveBuffs.Count == 0)
            return 0;

        int extendedCount = 0;
        iterationIds.Clear();
        iterationIds.AddRange(ActiveBuffs.Keys);

        for (int i = 0; i < iterationIds.Count; i++)
        {
            if (!ActiveBuffs.TryGetValue(iterationIds[i], out BuffInstance runtime) ||
                runtime?.Definition == null ||
                runtime.Definition.Category != BuffCategory.BloodLoss)
            {
                continue;
            }

            float extension = Mathf.Max(0f, runtime.Definition.DrinkDurationExtensionSeconds);
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
            !ActiveBuffs.TryGetValue(buffId, out BuffInstance runtime))
        {
            return;
        }

        RemoveBuffInternal(buffId, runtime, invokeStop: true);
    }

    public void ClearAllBuffs()
    {
        if (ActiveBuffs.Count == 0)
            return;

        iterationIds.Clear();
        iterationIds.AddRange(ActiveBuffs.Keys);

        for (int i = 0; i < iterationIds.Count; i++)
        {
            string buffId = iterationIds[i];
            if (ActiveBuffs.TryGetValue(buffId, out BuffInstance runtime))
                RemoveBuffInternal(buffId, runtime, invokeStop: true);
        }
    }

    public override void ModUpdate(float deltaTime)
    {
        Tick(deltaTime);
    }

    public void Tick(float deltaTime)
    {
        if (ActiveBuffs.Count == 0 || deltaTime <= 0f)
        {
            return;
        }

        iterationIds.Clear();
        expiredIds.Clear();
        iterationIds.AddRange(ActiveBuffs.Keys);
        Action<BuffInstance> countdownChanged = BuffCountdownChanged;

        for (int i = 0; i < iterationIds.Count; i++)
        {
            string buffId = iterationIds[i];
            if (!ActiveBuffs.TryGetValue(buffId, out BuffInstance runtime) || runtime == null)
            {
                expiredIds.Add(buffId);
                continue;
            }

            int previousDisplaySeconds = countdownChanged != null
                ? GetCountdownDisplaySeconds(runtime)
                : -1;
            if (runtime.Tick(deltaTime))
            {
                expiredIds.Add(buffId);
                continue;
            }

            if (countdownChanged != null)
            {
                int currentDisplaySeconds = GetCountdownDisplaySeconds(runtime);
                if (currentDisplaySeconds != previousDisplaySeconds)
                    countdownChanged.Invoke(runtime);
            }
        }

        for (int i = 0; i < expiredIds.Count; i++)
        {
            string buffId = expiredIds[i];
            if (ActiveBuffs.TryGetValue(buffId, out BuffInstance runtime))
                RemoveBuffInternal(buffId, runtime, invokeStop: true);
        }
    }

    private void RemoveBuffInternal(string buffId, BuffInstance runtime, bool invokeStop)
    {
        if (runtime != null && invokeStop)
            runtime.Stop();

        ActiveBuffs.Remove(buffId);
        if (runtime != null)
            BuffRemoved?.Invoke(runtime);
    }

    /// <summary>把连续时长压缩为 HUD 实际显示的整秒值，永久 Buff 不参与倒计时事件。</summary>
    private static int GetCountdownDisplaySeconds(BuffInstance runtime)
    {
        if (runtime?.Definition == null || runtime.Definition.IsPermanent)
            return -1;

        return Mathf.CeilToInt(Mathf.Max(0f, runtime.RemainingDurationSeconds));
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
        AddBuff(buffId);
    }

    #endregion
}

public static class BloodLossBuffIds
{
    public const string BloodLoss = "失血";
    public const string Bleeding = "流血";
    public const string Hemorrhage = "出血";
}

public static class BurningBuffIds
{
    public const string Burning = "燃烧";
}

/// <summary>感染类 Buff 的稳定 ID，供玩法、表现和测试统一引用。</summary>
public static class InfectionBuffIds
{
    public const string Infection = "感染";
}

/// <summary>淡水环境能力 Buff 的稳定 ID；只表示玩家当前可以饮水，不直接修改营养值。</summary>
public static class FreshWaterBuffIds
{
    public const string Clean = "位于干净的淡水中";
    public const string Dirty = "位于脏的淡水中";
}
