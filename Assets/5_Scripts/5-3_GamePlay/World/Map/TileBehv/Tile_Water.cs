using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 水体地块逻辑行为
/// 负责水体表现、配置型环境 Buff、入水降温，以及向角色提供无状态的喝水动作定义。
/// 作为 TileBlockBehaviour 的具体实现，通过组合到 Tile_Block 中使用。
/// </summary>
[System.Serializable]
public class Tile_Water : TileBlockBehaviour
{
    /// <summary>水体盐度高于此值时视为盐水；海水运行时数据使用 80 作为盐度。</summary>
    private const float SaltWaterThreshold = 0.01f;

    [Header("进入水体时附加的 Buff 列表")]
    public List<string> BuffInfo = new List<string>();

    [Header("水体环境动作")]
    [Tooltip("长按交互键达到该时长后开始饮水。")]
    [Min(0f)] public float drinkHoldSeconds = 1f;
    [Tooltip("持续饮水的结算间隔。")]
    [Min(0.05f)] public float drinkTickSeconds = 1f;
    [Tooltip("每次饮用淡水恢复的水分。")]
    [Min(0f)] public float waterGainPerTick = 12.5f;
    [Tooltip("每次饮用海水恢复的水分；海水还会同时附加脱水 Buff。")]
    [Min(0f)] public float saltWaterGainPerTick = 10f;

    [Header("水体环境效果")]
    [Tooltip("有效淹没为 0 时的移动速度倍率；角色实际减速按当前有效淹没高度插值。")]
    [Range(0.01f, 1f)] public float shallowMoveSpeedMultiplier = 0.5f;
    [Tooltip("有效淹没为 1 时的移动速度倍率；最深水体最多降低 80% 移速，由环境实例维护，不进入 Buff 系统。")]
    [Min(0.01f)] public float moveSpeedMultiplier = 0.2f;
    [Tooltip("入水降温不能把角色体温压到低于该值。")]
    [Min(0f)] public float entryTemperatureFloor = 10f;
    [Tooltip("首次入水降温平滑过渡到目标体温所需的时间。")]
    [Min(0.1f)] public float entryTemperatureTransitionSeconds = 5f;

    /// <summary>进入水格时启用真实水体状态、环境 Buff、动作与被动效果。</summary>
    public override void OnEnter(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (item == null)
            return;

        bool validItem = item != null;
        BuffManager buffManager = validItem ? item.GetComponentInChildren<BuffManager>() : null;
        TileData_Water water = tileData as TileData_Water;
        float depthValue = water != null ? Mathf.Clamp01(water.deepValue) : 0f;
        bool edgeInteractionOnly = receiver != null && receiver.IsActiveTileEdgeInteractionOnly;
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

        // 配置型 Buff 与环境动作相互独立；没有 BuffManager 的角色仍可获得动作定义。
        if (!edgeInteractionOnly && validItem && buffManager != null && BuffInfo != null)
        {
            foreach (string buffId in BuffInfo)
            {
                if (string.IsNullOrWhiteSpace(buffId))
                    continue;

                buffManager.AddBuff(buffId);
            }
        }

        ProvideWaterActions(item, water, receiver);
    }

    /// <summary>离开水格时撤销水体状态、环境 Buff、动作与被动效果。</summary>
    public override void OnExit(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (item == null)
            return;
        SetWaterTemperatureState(item, false, 0f);
        receiver?.ExitWaterSurvival(item);
        SetWaterVisualState(item, 0f, false);

        // 移除 Buff
        BuffManager buffManager = item.GetComponentInChildren<BuffManager>();
        if (buffManager != null && BuffInfo != null)
        {
            foreach (string buffId in BuffInfo)
            {
                if (string.IsNullOrWhiteSpace(buffId))
                    continue;

                if (buffManager.HasBuff(buffId))
                    buffManager.RemoveBuff(buffId);
            }
        }


        receiver?.EnvironmentInteractions.ClearAvailableActions();
        receiver?.EnvironmentInteractions.ClearAvailableEffects();
    }

    /// <summary>持续同步真实水格的水深与移动速度影响。</summary>
    public override void OnUpdate(Item item, TileData tileData, Map map, TileEffectReceiver receiver, float deltaTime)
    {
        if (item == null || !(tileData is TileData_Water water))
            return;

        // 邻接水格只用于保留边缘交互，不得把沙地角色染成浸没状态或施加水下减速。
        if (receiver != null && receiver.IsActiveTileEdgeInteractionOnly)
            return;

        // 漂浮结算直接以地块真实水深为准；水深不超过 0.3 时保持浅水状态，不进入漂浮维持。
        float depthValue = Mathf.Clamp01(water.deepValue);
        float effectiveImmersion = receiver != null
            ? receiver.UpdateWaterSurvival(item, depthValue, deltaTime)
            : depthValue;
        SetWaterTemperatureState(item, true, effectiveImmersion);
        SetWaterVisualState(item, effectiveImmersion, true);
        ProvideWaterEffects(receiver, effectiveImmersion);
    }

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
    private void ProvideWaterActions(Item item, TileData_Water water,
        TileEffectReceiver receiver)
    {
        EnvironmentInteractionRunner runner = receiver?.EnvironmentInteractions;
        if (runner == null)
            return;

        runner.ClearAvailableActions();
        if (item == null || water == null)
            return;

        GameRes gameRes = GameRes.ExistingInstance;
        if (gameRes == null ||
            string.IsNullOrWhiteSpace(water.LiquidId) ||
            !gameRes.TryGetLiquidDefinition(water.LiquidId, out LiquidDefinition liquid) ||
            !liquid.Drinkable)
        {
            return;
        }

        // 水体只决定当前环境种类与饮用节奏；感染、脱水等饮用后果统一读取 LiquidDefinition。
        WaterEnvironmentKind waterKind = water.salt > SaltWaterThreshold
            ? WaterEnvironmentKind.Salt
            : WaterEnvironmentKind.DirtyFresh;
        float resolvedWaterGain = waterKind == WaterEnvironmentKind.Salt
            ? saltWaterGainPerTick
            : waterGainPerTick;
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
