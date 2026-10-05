using UnityEngine;

/// <summary>实体模块声明是否实际接触水面；飞行等离地阶段不接受表层水流。</summary>
public interface IWaterCurrentExposure
{
    bool ReceivesWaterCurrent { get; }
}

/// <summary>
/// 游戏意图与环境速度的公共入口；接触、推动和阻挡交给 Physics2D。
/// </summary>
public static class WorldMotionSystem
{
    #region 环境速度
    /// <summary>角色和载具共用有效表面流场；河流按权威流量区分快慢，平台与静水返回零速度。</summary>
    public static Vector2 SampleWaterVelocity(Vector2 position, float speed)
    {
        if (speed <= 0f) return Vector2.zero;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null || !manager.TryGetRuntimeWaterCurrent(position, out RuntimeWaterCurrentSample current))
            return Vector2.zero;

        float strength = current.Kind == RuntimeWaterCurrentKind.River
            ? WaterEnvironmentRules.ResolveRiverStrength(current.Flow)
            : Mathf.Clamp01(current.Flow);
        return current.Direction * (speed * strength);
    }
    #endregion

    #region 主动推动意图
    /// <summary>推动取推动者的当前环境移速，和乘船自身的划行限速是两套独立配置。</summary>
    public static Vector2 CalculatePushVelocity(Vector2 currentMoveVelocity, bool sourceInWater, float landMultiplier)
        => currentMoveVelocity * (sourceInWater ? 1f : landMultiplier);

    #endregion
}
