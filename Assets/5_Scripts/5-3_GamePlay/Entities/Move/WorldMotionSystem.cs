using System.Collections.Generic;
using UnityEngine;

/// <summary>可推动物的玩法契约：显式占地、速度请求与来源，不依赖碰撞冲量或 Transform 父子关系。</summary>
public interface IWorldPushTarget
{
    #region 推动契约
    Component MotionComponent { get; }
    Vector2 MotionPosition { get; }
    Vector2 PushHalfExtents { get; }
    bool CanReceivePush(Mover source);
    Vector2 RequestPush(Mover source, Vector2 velocity, float deltaTime);
    Vector2 MotionVelocity { get; }
    #endregion
}

/// <summary>实体模块声明是否实际接触水面；飞行等离地阶段不接受表层水流。</summary>
public interface IWaterCurrentExposure
{
    bool ReceivesWaterCurrent { get; }
}

/// <summary>
/// 游戏推动与环境带动的公共入口。主动速度用于动画/体力，水流与承载速度只改变世界位移。
/// 推动物使用作者定义的矩形占地做连续扫掠；不创建碰撞体、不施加物理冲量、不触发区块生成。
/// 来源注册可扩展到其它载具；承载链通过 ICarrierMotionSource 继续传到乘员，禁止乘员反推自身载具。
/// </summary>
public static class WorldMotionSystem
{
    #region 来源注册与环境
    private static readonly HashSet<IWorldPushTarget> PushTargets = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => PushTargets.Clear();

    public static void Register(IWorldPushTarget target) => PushTargets.Add(target);
    public static void Unregister(IWorldPushTarget target) => PushTargets.Remove(target);

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

    #region 主动接触推动
    /// <summary>推动取推动者的当前环境移速，和乘船自身的划行限速是两套独立配置。</summary>
    public static Vector2 CalculatePushVelocity(Vector2 currentMoveVelocity, bool sourceInWater, float landMultiplier)
        => currentMoveVelocity * (sourceInWater ? 1f : landMultiplier);

    /// <summary>只把主动朝向物体的移动提交为推动；限制向内速度并保留沿船边滑动的切向速度。</summary>
    public static Vector2 ResolveContactVelocity(Mover actor, Vector2 position,
        Vector2 drivenVelocity, Vector2 totalVelocity, float deltaTime)
    {
        if (deltaTime <= 0f || actor == null) return totalVelocity;
        foreach (IWorldPushTarget target in PushTargets)
        {
            Component component = target.MotionComponent;
            if (component == null || !IsActiveSource(component) ||
                component.gameObject.scene != actor.item.gameObject.scene ||
                ReferenceEquals(target, actor.CarrierSource)) continue;

            Vector2 offset = WorldTopologyRuntime.ShortestDelta(target.MotionPosition, position);
            Vector2 extent = target.PushHalfExtents + Vector2.one * actor.pushContactRadius;
            // 预留一次物理步的接触距离，避免高速输入在两帧之间越过薄船沿。
            Vector2 travel = totalVelocity * deltaTime;
            if (!TrySweepBox(offset, travel, extent, out Vector2 outwardNormal, out float fraction)) continue;
            float inwardSpeed = -Vector2.Dot(totalVelocity, outwardNormal);
            if (inwardSpeed <= 0f) continue;

            Vector2 carriedVelocity = target.MotionVelocity;
            if (Vector2.Dot(drivenVelocity, outwardNormal) < -0.001f && target.CanReceivePush(actor))
                carriedVelocity = target.RequestPush(actor, drivenVelocity, deltaTime);
            float gapSpeed = inwardSpeed * fraction;
            float allowedInward = Mathf.Max(0f, -Vector2.Dot(carriedVelocity, outwardNormal)) + gapSpeed;
            if (inwardSpeed > allowedInward)
                totalVelocity += outwardNormal * (inwardSpeed - allowedInward);
        }
        return ResolveStaticContactVelocity(actor, position, totalVelocity, deltaTime);
    }

    /// <summary>静态 Entity 阻挡可在每个物理步重复约束，不重复提交载具推动请求。</summary>
    public static Vector2 ResolveStaticContactVelocity(Mover actor, Vector2 position,
        Vector2 velocity, float deltaTime)
    {
        if (actor == null || deltaTime <= 0f || velocity.sqrMagnitude <= 0f) return velocity;
        velocity = ResolveMechanicalContactVelocity(actor, position, velocity, deltaTime);
        return FlatWorld.NaturalEntities.NaturalEntityEcsService.ResolveContactVelocity(position,
            velocity, actor.pushContactRadius, deltaTime);
    }

    /// <summary>纯数据机械按视觉底座矩形阻挡角色，保留滑边运动而不创建逐建筑碰撞体。</summary>
    private static Vector2 ResolveMechanicalContactVelocity(
        Mover actor, Vector2 position, Vector2 velocity, float deltaTime)
    {
        if (actor == null || velocity.sqrMagnitude <= 0f || deltaTime <= 0f) return velocity;
        Vector2 travel = velocity * deltaTime;
        // 视觉阻挡框留在所属格内，先用格索引圈出候选节点，避免逐节点遍历。
        float margin = .5f + actor.pushContactRadius;
        int minX = Mathf.FloorToInt(Mathf.Min(position.x, position.x + travel.x) - margin);
        int maxX = Mathf.FloorToInt(Mathf.Max(position.x, position.x + travel.x) + margin);
        int minY = Mathf.FloorToInt(Mathf.Min(position.y, position.y + travel.y) - margin);
        int maxY = Mathf.FloorToInt(Mathf.Max(position.y, position.y + travel.y) + margin);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            MachineEntity node = MachineWorld.GetAtCurrentWorld(new Vector2Int(x, y), 0);
            if (node?.Definition.BlocksMovement != true) continue;
            MachineCollisionBounds.ResolveWorldBox(node, out Vector2 boxCenter,
                out Vector2 boxHalfExtents);
            Vector2 origin = WorldTopologyRuntime.ShortestDelta(
                boxCenter, position);
            if (!TrySweepBox(origin, velocity * deltaTime,
                    boxHalfExtents + Vector2.one * actor.pushContactRadius,
                    out Vector2 normal, out float fraction)) continue;
            float inwardSpeed = -Vector2.Dot(velocity, normal);
            if (inwardSpeed > 0f)
                velocity += normal * (inwardSpeed * (1f - fraction));
        }
        return velocity;
    }

    /// <summary>点扫掠膨胀矩形，返回入射面法线；已贴边时只约束继续向内的运动。</summary>
    public static bool TrySweepBox(Vector2 origin, Vector2 delta, Vector2 extent,
        out Vector2 normal, out float fraction)
    {
        normal = Vector2.zero;
        fraction = 0f;
        if (Mathf.Abs(origin.x) <= extent.x && Mathf.Abs(origin.y) <= extent.y)
        {
            float xGap = extent.x - Mathf.Abs(origin.x);
            float yGap = extent.y - Mathf.Abs(origin.y);
            normal = xGap < yGap
                ? new Vector2(origin.x >= 0f ? 1f : -1f, 0f)
                : new Vector2(0f, origin.y >= 0f ? 1f : -1f);
            return Vector2.Dot(delta, normal) < 0f;
        }
        float entry = 0f, exit = 1f;
        for (int axis = 0; axis < 2; axis++)
        {
            if (Mathf.Abs(delta[axis]) < 0.000001f)
            {
                if (Mathf.Abs(origin[axis]) > extent[axis]) return false;
                continue;
            }
            float near = (-extent[axis] - origin[axis]) / delta[axis];
            float far = (extent[axis] - origin[axis]) / delta[axis];
            float sign = -Mathf.Sign(delta[axis]);
            if (near > far) (near, far) = (far, near);
            if (near >= entry)
            {
                entry = near;
                normal = axis == 0 ? new Vector2(sign, 0f) : new Vector2(0f, sign);
            }
            exit = Mathf.Min(exit, far);
            if (entry > exit) return false;
        }
        fraction = Mathf.Clamp01(entry);
        return exit >= 0f && entry <= 1f && normal.sqrMagnitude > 0f;
    }

    /// <summary>接口来源可以是任意 Component，只在其宿主及 Behaviour 均启用时参与模拟。</summary>
    private static bool IsActiveSource(Component component)
        => component.gameObject.activeInHierarchy && (component is not Behaviour behaviour || behaviour.isActiveAndEnabled);
    #endregion
}
