using UnityEngine;

/// <summary>只积分风纹相位，风向与强度沿用天气系统的平滑结果。</summary>
public sealed class WindStreakPresentation
{
    #region 风纹漂移

    public const float PatternRepeatCells = 128f;

    public Vector2 Phase { get; private set; }
    public Vector2 Direction { get; private set; }
    public Vector4 Parameters { get; private set; }
    public bool IsVisible { get; private set; }

    public void Step(Vector2 wind, Vector2 cameraDelta, float deltaTime,
        WorldRenderingConfig.RenderingWindStreaks config)
    {
        if (!float.IsFinite(wind.x) || !float.IsFinite(wind.y) ||
            !float.IsFinite(cameraDelta.x) || !float.IsFinite(cameraDelta.y) ||
            config == null || !config.TryResolve(wind.magnitude, out Vector4 parameters))
        {
            Reset();
            return;
        }

        Direction = wind.normalized;
        Parameters = parameters;
        IsVisible = true;
        float seconds = float.IsFinite(deltaTime) ? Mathf.Max(0f, deltaTime) : 0f;
        Vector2 across = new Vector2(-Direction.y, Direction.x);
        // 相机位移抵消屏幕粘连，有限周期与 Shader 的散列周期一致，长时间运行也不丢精度。
        Phase = new Vector2(
            Mathf.Repeat(Phase.x + (Vector2.Dot(cameraDelta, Direction) - parameters.x * seconds) / config.cellSize.x,
                PatternRepeatCells),
            Mathf.Repeat(Phase.y + Vector2.Dot(cameraDelta, across) / config.cellSize.y, PatternRepeatCells));
    }

    public void Reset()
    {
        Phase = Direction = Vector2.zero;
        Parameters = Vector4.zero;
        IsVisible = false;
    }

    #endregion
}
