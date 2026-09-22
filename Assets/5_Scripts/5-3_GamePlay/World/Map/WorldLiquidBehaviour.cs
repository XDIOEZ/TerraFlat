using UnityEngine;

/// <summary>
/// 世界液体共享行为。负责液体表现、潮湿、体温、游泳与饮用，角色状态始终由接收器持有。
/// 由 LiquidDefinition 驱动并直接读取 Liquid 采样，深度范围为 0～1，不参与 Ground Behaviour 或地块存档。
/// </summary>
[System.Serializable]
public class WorldLiquidBehaviour
{
    #region 定义配置
    public WorldLiquidBehaviour() { }
    /// <summary>每种液体只构建一次共享行为，禁止把角色状态放入液体目录。</summary>
    public WorldLiquidBehaviour(WorldLiquidSettings settings)
    {
        shallowMoveSpeedMultiplier = settings.ShallowMoveSpeedMultiplier;
        moveSpeedMultiplier = settings.DeepMoveSpeedMultiplier;
        entryTemperatureFloor = settings.EntryTemperatureFloor;
        entryTemperatureTransitionSeconds = settings.EntryTemperatureTransitionSeconds;
        drinkHoldSeconds = settings.DrinkHoldSeconds;
        drinkTickSeconds = settings.DrinkTickSeconds;
    }
    [Header("水体环境动作")]
    [Tooltip("长按交互键达到该时长后开始饮水。")]
    [Min(0f)] public float drinkHoldSeconds = 1f;
    [Tooltip("持续饮水的结算间隔。")]
    [Min(0.05f)] public float drinkTickSeconds = 1f;

    [Header("水体环境效果")]
    [Tooltip("有效淹没为 0 时的移动速度倍率；角色实际减速按当前有效淹没高度插值。")]
    [Range(0.01f, 1f)] public float shallowMoveSpeedMultiplier = 0.5f;
    [Tooltip("有效淹没为 1 时的移动速度倍率；最深水体最多降低 80% 移速，由环境实例维护，不进入 Buff 系统。")]
    [Min(0.01f)] public float moveSpeedMultiplier = 0.2f;
    [Tooltip("入水降温不能把角色体温压到低于该值。")]
    [Min(0f)] public float entryTemperatureFloor = 10f;
    [Tooltip("首次入水降温平滑过渡到目标体温所需的时间。")]
    [Min(0.1f)] public float entryTemperatureTransitionSeconds = 5f;
    #endregion

    #region 液体接触生命周期
    /// <summary>进入液体时启用真实水体状态、动作与被动效果。</summary>
    public virtual void OnEnter(Item item, WorldLiquidSourceTarget source, TileEffectReceiver receiver)
    {
        if (item == null)
            return;

        BuffManager buffManager = item.GetComponentInChildren<BuffManager>();
        float depthValue = Mathf.Clamp01(source.Sample.LiquidDepth);
        bool edgeInteractionOnly = receiver != null && receiver.IsActiveTileEdgeInteractionOnly;
        buffManager?.SetWaterStackExposure(!edgeInteractionOnly && depthValue > 0f);
        if (edgeInteractionOnly)
        {
            SetWaterTemperatureState(item, false, 0f);
            // 对象池复用时也要清掉上一轮真实入水留下的目标状态。
            SetWaterVisualState(item, 0f, false);
        }
        else
        {
            float effectiveImmersion = receiver != null
                ? receiver.EnterWaterSurvival(item, depthValue)
                : depthValue;
            SetWaterTemperatureState(item, true, effectiveImmersion);
            SetWaterVisualState(item, effectiveImmersion, true);
            ProvideWaterEffects(receiver, effectiveImmersion);
        }

        ProvideWaterActions(item, source.Liquid, receiver);
    }

    /// <summary>离开液体时撤销水体状态、动作与被动效果。</summary>
    public virtual void OnExit(Item item, WorldLiquidSourceTarget source, TileEffectReceiver receiver)
    {
        if (item == null)
            return;
        SetWaterTemperatureState(item, false, 0f);
        receiver?.ExitWaterSurvival(item);
        SetWaterVisualState(item, 0f, false);

        BuffManager buffManager = item.GetComponentInChildren<BuffManager>();
        buffManager?.SetWaterStackExposure(false);

        receiver?.EnvironmentInteractions.ClearAvailableActions();
        receiver?.EnvironmentInteractions.ClearAvailableEffects();
    }

    /// <summary>持续同步真实水格的水深与移动速度影响。</summary>
    public virtual void OnUpdate(Item item, WorldLiquidSourceTarget source, TileEffectReceiver receiver, float deltaTime)
    {
        if (item == null)
            return;

        // 邻接水格只用于保留边缘交互，不得把沙地角色染成浸没状态或施加水下减速。
        if (receiver != null && receiver.IsActiveTileEdgeInteractionOnly)
            return;

        // 漂浮结算直接以 Liquid 真实液深为准；液深不超过 0.3 时不进入漂浮维持。
        float depthValue = Mathf.Clamp01(source.Sample.LiquidDepth);
        BuffManager buffManager = item.itemMods?.GetMod_ByID<BuffManager>(ModText.BuffManager);
        buffManager?.SetWaterStackExposure(depthValue > 0f);
        buffManager?.AdvanceWaterWetness(depthValue, deltaTime);
        float effectiveImmersion = receiver != null
            ? receiver.UpdateWaterSurvival(item, depthValue, deltaTime)
            : depthValue;
        SetWaterTemperatureState(item, true, effectiveImmersion);
        SetWaterVisualState(item, effectiveImmersion, true);
        ProvideWaterEffects(receiver, effectiveImmersion);
    }
    #endregion

    #region Temperature State

    /// <summary>把真实入水状态和本水体的降温参数交给角色体温模块。</summary>
    private void SetWaterTemperatureState(Item item, bool inWater, float effectiveImmersion)
    {
        Mod_Temperature temperature = item?.itemMods?.GetMod_ByID<Mod_Temperature>(
            ModText.Temperature);
        temperature?.SetWaterExposure(
            inWater,
            WaterEnvironmentRules.ResolveCoolingDrop(effectiveImmersion),
            entryTemperatureFloor,
            entryTemperatureTransitionSeconds);
    }

    #endregion

    #region Visual State

    /// <summary>角色水体遮罩使用玩法有效淹没高度；深水有体力漂浮时保持 0.3，体力耗尽后随下沉进度升高。</summary>
    private static void SetWaterVisualState(Item item, float immersionLevel, bool inWater)
    {
        WaterImmersionRenderEffect effect = item.GetComponentInChildren<WaterImmersionRenderEffect>(true);
        if (effect != null)
            effect.SetActorImmersionState(immersionLevel, inWater);

        // 水下看不到脚底阴影，阴影状态与水体视觉状态保持同一入口更新。
        ActorShadowManager.GetInstance()?.SetActorInWater(item, inWater);
    }

    #endregion

    #region 环境动作提供

    /// <summary>水体只提供无角色状态的动作定义；角色侧运行器在按键时创建独立实例。</summary>
    private void ProvideWaterActions(Item item, LiquidDefinition liquid,
        TileEffectReceiver receiver)
    {
        EnvironmentInteractionRunner runner = receiver?.EnvironmentInteractions;
        if (runner == null)
            return;

        runner.ClearAvailableActions();
        if (item == null || liquid == null || !liquid.Drinkable)
            return;

        // 水体只决定当前环境种类与饮用节奏；感染、脱水等饮用后果统一读取 LiquidDefinition。
        WaterEnvironmentKind waterKind = liquid.Id == LiquidIds.SeaWater
            ? WaterEnvironmentKind.Salt
            : WaterEnvironmentKind.DirtyFresh;
        float resolvedWaterGain = liquid.HydrationPerServing;
        runner.SetAvailableActions(new DrinkWaterActionDefinition(
            liquid,
            waterKind,
            drinkHoldSeconds,
            drinkTickSeconds,
            resolvedWaterGain));
    }

    /// <summary>根据当前有效淹没高度计算减速；漂浮时固定按 0.3，体力耗尽后随下沉程度继续增加。</summary>
    private void ProvideWaterEffects(TileEffectReceiver receiver, float immersionLevel)
    {
        EnvironmentInteractionRunner runner = receiver?.EnvironmentInteractions;
        if (runner == null)
            return;

        float shallowMultiplier = Mathf.Clamp(shallowMoveSpeedMultiplier, 0.01f, 1f);
        float deepMultiplier = Mathf.Clamp(moveSpeedMultiplier, 0.01f, shallowMultiplier);
        float resolvedMultiplier = Mathf.Lerp(
            shallowMultiplier,
            deepMultiplier,
            Mathf.Clamp01(immersionLevel));

        // 水深动态变化时只更新已有实例，避免每帧清空并重建其他环境效果。
        if (runner.TryUpdateMoveSpeedMultiplier(resolvedMultiplier))
            return;

        runner.SetAvailableEffects(
            new MoveSpeedEnvironmentEffectDefinition(resolvedMultiplier));
    }

    #endregion
}
