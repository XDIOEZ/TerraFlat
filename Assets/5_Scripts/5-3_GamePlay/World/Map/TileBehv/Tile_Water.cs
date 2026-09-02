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
    [Tooltip("脏淡水每次饮水触发感染的概率。")]
    [Range(0f, 1f)] public float dirtyWaterInfectionChance = 0.2f;

    [Header("水体环境效果")]
    [Tooltip("水深为 0 时的移动速度倍率；默认浅水仅轻微减速。")]
    [Range(0.01f, 1f)] public float shallowMoveSpeedMultiplier = 0.85f;
    [Tooltip("水深为 1 时的移动速度倍率；由环境实例维护，不进入 Buff 系统。")]
    [Min(0.01f)] public float moveSpeedMultiplier = 0.5f;
    [Tooltip("首次进入一片连续水域时固定降低的体温。")]
    [Min(0f)] public float entryTemperatureDrop = 10f;
    [Tooltip("入水降温不能把角色体温压到低于该值。")]
    [Min(0f)] public float entryTemperatureFloor = 10f;

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
        SetWaterTemperatureState(item, !edgeInteractionOnly);
        if (edgeInteractionOnly)
        {
            // 对象池复用时也要清掉上一轮真实入水留下的目标状态。
            SetWaterVisualState(item, 0f, false);
        }
        else
            SetWaterVisualState(item, depthValue, true);

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
        if (!edgeInteractionOnly)
            ProvideWaterEffects(receiver, depthValue);
    }

    /// <summary>离开水格时撤销水体状态、环境 Buff、动作与被动效果。</summary>
    public override void OnExit(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (item == null)
            return;
        SetWaterTemperatureState(item, false);
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

        // 持续刷新目标深度，允许区块运行时更新水深时同步跟随视觉和移速。
        float depthValue = Mathf.Clamp01(water.deepValue);
        SetWaterVisualState(item, depthValue, true);
        ProvideWaterEffects(receiver, depthValue);
    }

    #region Temperature State

    /// <summary>把真实入水状态和本水体的降温参数交给角色体温模块。</summary>
    private void SetWaterTemperatureState(Item item, bool inWater)
    {
        Mod_Temperature temperature = item?.itemMods?.GetMod_ByID<Mod_Temperature>(
            ModText.Temperature);
        temperature?.SetWaterExposure(
            inWater,
            entryTemperatureDrop,
            entryTemperatureFloor);
    }

    #endregion

    #region Visual State

    /// <summary>只向通用渲染效果模块发送水体状态，不在地块逻辑中直接写材质参数。</summary>
    private static void SetWaterVisualState(Item item, float depth, bool inWater)
    {
        WaterImmersionRenderEffect effect = item.GetComponentInChildren<WaterImmersionRenderEffect>(true);
        if (effect != null)
            effect.SetWaterState(depth, inWater);

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

        // 当前所有非盐水地表水都视为污水，包含河流；饮用后沿用现有感染概率。
        WaterEnvironmentKind waterKind = water.salt > SaltWaterThreshold
            ? WaterEnvironmentKind.Salt
            : WaterEnvironmentKind.DirtyFresh;
        float resolvedWaterGain = waterKind == WaterEnvironmentKind.Salt
            ? saltWaterGainPerTick
            : waterGainPerTick;
        runner.SetAvailableActions(new DrinkWaterActionDefinition(
            waterKind,
            drinkHoldSeconds,
            drinkTickSeconds,
            resolvedWaterGain,
            dirtyWaterInfectionChance));
    }

    /// <summary>根据水深计算并应用角色独享的减速实例；清 Buff 不会影响该环境效果。</summary>
    private void ProvideWaterEffects(TileEffectReceiver receiver, float depthValue)
    {
        EnvironmentInteractionRunner runner = receiver?.EnvironmentInteractions;
        if (runner == null)
            return;

        float shallowMultiplier = Mathf.Clamp(shallowMoveSpeedMultiplier, 0.01f, 1f);
        float deepMultiplier = Mathf.Clamp(moveSpeedMultiplier, 0.01f, shallowMultiplier);
        float resolvedMultiplier = Mathf.Lerp(
            shallowMultiplier,
            deepMultiplier,
            Mathf.Clamp01(depthValue));

        // 水深动态变化时只更新已有实例，避免每帧清空并重建其他环境效果。
        if (runner.TryUpdateMoveSpeedMultiplier(resolvedMultiplier))
            return;

        runner.SetAvailableEffects(
            new MoveSpeedEnvironmentEffectDefinition(resolvedMultiplier));
    }

    #endregion
}
