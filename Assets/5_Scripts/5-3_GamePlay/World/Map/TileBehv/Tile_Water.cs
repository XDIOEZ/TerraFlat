using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 水体地块逻辑行为
/// 负责处理进入 / 离开水地块时的特效与 Buff 效果。
/// 作为 TileBlockBehaviour 的具体实现，通过组合到 Tile_Block 中使用。
/// </summary>
[System.Serializable]
public class Tile_Water : TileBlockBehaviour
{
    [Header("进入水体时附加的 Buff 列表")]
    public List<string> BuffInfo = new List<string>();

    public override void OnEnter(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (item == null)
            return;

        bool validItem = item != null;
        BuffManager buffManager = validItem ? item.GetComponentInChildren<BuffManager>() : null;
        TileData_Water water = tileData as TileData_Water;
        float depthValue = water != null ? Mathf.Clamp01(water.deepValue) : 0f;
        SetWaterVisualState(item, depthValue, true);

        // Buff 添加逻辑
        if (!validItem || buffManager == null || BuffInfo == null || BuffInfo.Count == 0)
            return;

        foreach (string buffId in BuffInfo)
        {
            if (string.IsNullOrWhiteSpace(buffId))
                continue;

            buffManager.AddBuff(buffId);
        }
    }

    public override void OnExit(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (item == null)
            return;
        SetWaterVisualState(item, 0f, false);

        // 移除 Buff
        BuffManager buffManager = item.GetComponentInChildren<BuffManager>();
        if (buffManager == null || BuffInfo == null)
            return;

        foreach (string buffId in BuffInfo)
        {
            if (string.IsNullOrWhiteSpace(buffId))
                continue;

            if (buffManager.HasBuff(buffId))
                buffManager.RemoveBuff(buffId);
        }
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
    }

    #endregion
}
