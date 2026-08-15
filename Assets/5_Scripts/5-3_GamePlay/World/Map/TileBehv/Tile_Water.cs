using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 水体地块逻辑行为
/// 负责水体表现、配置型环境 Buff，以及向角色提供无状态的喝水动作定义。
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
    [Tooltip("每次饮水恢复的水分。")]
    [Min(0f)] public float waterGainPerTick = 125f;
    [Tooltip("脏淡水每次饮水触发感染的概率。")]
    [Range(0f, 1f)] public float dirtyWaterInfectionChance = 0.2f;

    [Header("水体环境效果")]
    [Tooltip("进入水体后的移动速度倍率；由环境实例维护，不进入 Buff 系统。")]
    [Min(0.01f)] public float moveSpeedMultiplier = 0.5f;

    public override void OnEnter(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (item == null)
            return;

        bool validItem = item != null;
        BuffManager buffManager = validItem ? item.GetComponentInChildren<BuffManager>() : null;
        TileData_Water water = tileData as TileData_Water;
        float depthValue = water != null ? Mathf.Clamp01(water.deepValue) : 0f;
        SetWaterVisualState(item, depthValue, true);

        // 配置型 Buff 与环境动作相互独立；没有 BuffManager 的角色仍可获得动作定义。
        if (validItem && buffManager != null && BuffInfo != null)
        {
            foreach (string buffId in BuffInfo)
            {
                if (string.IsNullOrWhiteSpace(buffId))
                    continue;

                buffManager.AddBuff(buffId);
            }
        }

        ProvideWaterActions(item, water, receiver);
        ProvideWaterEffects(receiver);
    }

    public override void OnExit(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (item == null)
            return;
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

    public override void OnUpdate(Item item, TileData tileData, Map map, TileEffectReceiver receiver, float deltaTime)
    {
        if (item == null || !(tileData is TileData_Water water))
            return;

        // 持续刷新目标深度，允许区块运行时更新水深时平滑跟随。
        SetWaterVisualState(item, Mathf.Clamp01(water.deepValue), true);
    }

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

        WaterEnvironmentKind waterKind = water.salt > SaltWaterThreshold
            ? WaterEnvironmentKind.Salt
            : IsCleanFlowingFreshWater(item.transform.position)
                ? WaterEnvironmentKind.CleanFresh
                : WaterEnvironmentKind.DirtyFresh;
        runner.SetAvailableActions(new DrinkWaterActionDefinition(
            waterKind,
            drinkHoldSeconds,
            drinkTickSeconds,
            waterGainPerTick,
            dirtyWaterInfectionChance));
    }

    private static bool IsCleanFlowingFreshWater(Vector2 worldPosition)
    {
        ChunkMgr manager = ChunkMgr.Instance;
        if (manager == null ||
            !manager.TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample))
        {
            return false;
        }

        return sample.Terrain.TryGetEnvironmentValue(
                   "riverKind", sample.LocalCell.x, sample.LocalCell.y, out float riverKind) &&
               Mathf.RoundToInt(riverKind) == (int)HydrologyWaterKind.River;
    }

    /// <summary>进入水体时创建角色独享的减速实例；清 Buff 不会影响该环境效果。</summary>
    private void ProvideWaterEffects(TileEffectReceiver receiver)
    {
        receiver?.EnvironmentInteractions.SetAvailableEffects(
            new MoveSpeedEnvironmentEffectDefinition(moveSpeedMultiplier));
    }

    #endregion
}
