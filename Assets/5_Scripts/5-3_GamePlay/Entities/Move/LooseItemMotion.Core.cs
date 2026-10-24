using System;
using UnityEngine;

/// <summary>推动来源只读物品所处世界与状态，不直接写位置。</summary>
public readonly struct LooseItemMotionContext
{
    #region 推动上下文
    public readonly string WorldKey;
    public readonly ItemData Item;
    public readonly Vector2 Position;
    public readonly bool InWater;
    public float Mass => Item?.Stack?.CurrentWeight ?? 0f;

    public LooseItemMotionContext(string worldKey, ItemData item, Vector2 position, bool inWater)
    { WorldKey = worldKey; Item = item; Position = position; InWater = inWater; }
    #endregion
}

/// <summary>水流、传送带和外力共用的散物运动内核；来源采样与位置提交分离。</summary>
public static partial class LooseItemMotion
{
    #region 可组合的推动来源
    private sealed class Registration : IDisposable
    {
        internal readonly string Id;
        internal readonly Func<LooseItemMotionContext, Vector2> Sample;
        internal bool Active = true;
        internal Registration(string id, Func<LooseItemMotionContext, Vector2> sample)
        { Id = id; Sample = sample; }
        public void Dispose()
        {
            if (!Active) return;
            Active = false;
            int index = Array.IndexOf(sources, this);
            if (index < 0) return;
            var next = new Registration[sources.Length - 1];
            Array.Copy(sources, 0, next, 0, index);
            Array.Copy(sources, index + 1, next, index, next.Length - index);
            sources = next;
        }
    }

    private static Registration[] sources = Array.Empty<Registration>();
    public static bool HasVelocitySources => sources.Length != 0;

    /// <summary>返回世界单位/秒的附加速度；来源自行按世界、位置和质量筛选，租约释放即撤销。</summary>
    public static IDisposable RegisterVelocitySource(string id, Func<LooseItemMotionContext, Vector2> sample)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("推动来源必须有稳定标识。", nameof(id));
        if (sample == null) throw new ArgumentNullException(nameof(sample));
        foreach (Registration source in sources)
            if (source.Id == id) throw new InvalidOperationException("重复的散物推动来源：" + id);
        var registration = new Registration(id, sample);
        var next = new Registration[sources.Length + 1];
        Array.Copy(sources, next, sources.Length);
        next[next.Length - 1] = registration;
        sources = next;
        return registration;
    }

    internal static Vector2 SampleAdditionalVelocity(in LooseItemMotionContext context)
    {
        Vector2 velocity = Vector2.zero;
        // 注册时复制，采样时无分配；来源在回调中撤销也不会破坏当前枚举。
        Registration[] snapshot = sources;
        foreach (Registration source in snapshot)
        {
            if (!source.Active) continue;
            Vector2 value = source.Sample(context);
            if (Finite(value) && Finite(velocity + value)) velocity += value;
        }
        return velocity;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSources()
    {
        foreach (Registration source in sources) source.Active = false;
        sources = Array.Empty<Registration>();
    }
    #endregion

    #region 统一积分与阻挡
    internal interface IField
    {
        bool WaterOnly { get; }
        Vector2 CarrierVelocity(Vector2 position, float seconds);
        Vector2 ExternalVelocity(Vector2 position);
        bool IsWater(Vector2 position);
        Vector2 Move(Vector2 position, Vector2 displacement);
        Vector2 Delta(Vector2 from, Vector2 to);
    }

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);

    /// <summary>所有来源先合成再移动；短步重采样路径，反向外力不会被传送带最后一次写位置覆盖。</summary>
    internal static Vector2 Integrate<T>(Vector2 position, float seconds, ref T field) where T : struct, IField
    {
        if (!Finite(position) || !Finite(seconds) || seconds <= 0f) return position;
        float remaining = Mathf.Min(seconds, 1f);
        for (int segment = 0; segment < 128 && remaining > .000001f; segment++)
        {
            float step = remaining;
            Vector2 external = field.ExternalVelocity(position);
            Vector2 carrier = field.CarrierVelocity(position, step);
            if (!Finite(external)) external = Vector2.zero;
            if (!Finite(carrier)) carrier = Vector2.zero;
            float speedBound = carrier.magnitude + external.magnitude;
            if (!Finite(speedBound)) break;
            if (speedBound * step > .1f)
            {
                step = .1f / speedBound;
                carrier = field.CarrierVelocity(position, step);
                if (!Finite(carrier)) carrier = Vector2.zero;
            }
            Vector2 displacement = (carrier + external) * step;
            if (!Finite(displacement)) break;
            if (displacement.sqrMagnitude <= .000000000001f) break;
            // 岸边只撤销水流自身的推动，风或新外力仍可把物品推出水面。
            if (field.WaterOnly && !field.IsWater(position + displacement))
                displacement = external * step;
            if (displacement.sqrMagnitude <= .000000000001f) break;
            Vector2 destination = field.Move(position, displacement);
            if (!Finite(destination) || field.Delta(position, destination).sqrMagnitude <= .000000000001f) break;
            position = destination;
            remaining -= step;
            if (field.WaterOnly && !field.IsWater(position)) break;
        }
        return position;
    }

    #endregion
}
