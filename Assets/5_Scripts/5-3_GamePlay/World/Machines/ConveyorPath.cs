using UnityEngine;

/// <summary>输送路径由权威相邻带格派生，直线与圆弧共用运输和表现坐标。</summary>
public readonly struct ConveyorPath
{
    #region 输送路径
    public readonly int Input, Output;
    public ConveyorPath(int input, int output) { Input = input & 3; Output = output & 3; }
    public bool Valid => Input != Output;
    public int Mask => (1 << Input) | (1 << Output);
    public int DriveMask => (~Mask) & 15;
    public bool Curved => ((Input - Output) & 1) != 0;
    public int Rotation => Mask switch { 10 => 1, 3 => 3, 12 => 1, 9 => 2, _ => 0 };
    public int CanonicalSign => Output == ((Rotation + (Curved ? 1 : 0)) & 3) ? 1 : -1;
    public float Length => Curved ? Mathf.PI * .25f : 1f;
    public static ConveyorPath Straight(int rotation) => new(rotation + 2, rotation);
    // 手动模式包含四个直线朝向和八个有方向的拐角。
    public static ConveyorPath FromManualMode(int mode)
    {
        if (mode < 1 || mode > 12) return default;
        int output = (mode - 1) & 3;
        int turn = mode <= 4 ? 2 : mode <= 8 ? 1 : 3;
        return new ConveyorPath(output + turn, output);
    }
    public int ManualMode => 1 + Output + (((Input - Output) & 3) switch { 2 => 0, 1 => 4, _ => 8 });
    public bool HasPathPort(int direction) => (Mask & (1 << direction)) != 0;
    public bool HasDrivePort(int direction) => (DriveMask & (1 << direction)) != 0;
    public bool Same(ConveyorPath other) => Input == other.Input && Output == other.Output;

    /// <summary>有多个邻带时优先保留直线，只有两条正交线路才组成拐角。</summary>
    public static ConveyorPath Select(int neighbors, int rotation)
    {
        rotation &= 3;
        int straight = (1 << rotation) | (1 << ((rotation + 2) & 3));
        int pair = (neighbors & straight) == straight ? straight
            : (neighbors & 5) == 5 ? 5 : (neighbors & 10) == 10 ? 10 : 0;
        if (pair == 0)
        {
            int first = -1, second = -1;
            for (int i = 0; i < 4; i++)
            {
                int direction = (rotation + i) & 3;
                if ((neighbors & (1 << direction)) == 0) continue;
                if (first < 0) first = direction; else { second = direction; break; }
            }
            if (first < 0) return Straight(rotation);
            pair = (1 << first) | (1 << (second < 0 ? (first + 2) & 3 : second));
        }
        int output = -1, input = -1, best = 5;
        for (int direction = 0; direction < 4; direction++)
        {
            if ((pair & (1 << direction)) == 0) continue;
            int delta = (direction - rotation + 4) & 3;
            int cost = delta == 0 ? 0 : delta == 1 ? 1 : delta == 3 ? 2 : 3;
            if (cost < best) { input = output; output = direction; best = cost; }
            else input = direction;
        }
        return new ConveyorPath(input, output);
    }

    /// <summary>按带长推进，掉落物可收拢侧向偏移，角色保留站位沿弧线移动。</summary>
    public bool TryMove(Vector2 offset, float distance, float halfWidth, out Vector2 destination, bool centerLateral = true)
    {
        Vector2 local = Rotate(offset, -Rotation);
        float progress, lateral;
        if (Curved)
        {
            Vector2 radial = local - new Vector2(-.5f, .5f);
            progress = Mathf.Clamp(Mathf.Atan2(radial.x, -radial.y), 0f, Mathf.PI * .5f) * .5f;
            lateral = radial.magnitude - .5f;
        }
        else { progress = local.x + .5f; lateral = local.y; }
        destination = offset;
        if (Mathf.Abs(lateral) > halfWidth) return false;
        progress += distance * CanonicalSign;
        if (centerLateral) lateral = Mathf.MoveTowards(lateral, 0f, Mathf.Abs(distance));
        Vector2 point, normal;
        if (!Curved) { point = new Vector2(progress - .5f, 0f); normal = Vector2.up; }
        else if (progress < 0f) { point = new Vector2(-.5f + progress, 0f); normal = Vector2.down; }
        else if (progress > Length) { point = new Vector2(0f, .5f + progress - Length); normal = Vector2.right; }
        else
        {
            float angle = progress * 2f;
            normal = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
            point = new Vector2(-.5f, .5f) + normal * .5f;
        }
        destination = Rotate(point + normal * lateral, Rotation);
        return true;
    }

    public static Vector2 Rotate(Vector2 value, int quarter) => (quarter & 3) switch
    { 1 => new Vector2(-value.y, value.x), 2 => -value, 3 => new Vector2(value.y, -value.x), _ => value };
    #endregion
}
