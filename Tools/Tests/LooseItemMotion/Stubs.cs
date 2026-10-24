using Newtonsoft.Json.Linq;

// 只替代Unity基础数学/载荷，运动算法、传送带路径和采样夹具均链接生产文件。
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
        public static Vector2 right => new(1, 0);
        public static Vector2 up => new(0, 1);
        public static Vector2 down => new(0, -1);
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float b) => new(a.x * b, a.y * b);
        public static Vector2 operator /(Vector2 a, float b) => new(a.x / b, a.y / b);
    }
    public static class Mathf
    {
        public const float PI = MathF.PI;
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Clamp(float a, float min, float max) => Math.Clamp(a, min, max);
        public static float Abs(float a) => MathF.Abs(a);
        public static float Atan2(float a, float b) => MathF.Atan2(a, b);
        public static float Sin(float a) => MathF.Sin(a);
        public static float Cos(float a) => MathF.Cos(a);
        public static float Repeat(float t, float length) => Clamp(t - MathF.Floor(t / length) * length, 0, length);
        public static float MoveTowards(float current, float target, float delta)
            => Abs(target - current) <= delta ? target : current + MathF.Sign(target - current) * delta;
    }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType value) { } }
}

public sealed class ItemData { public ItemStack Stack; }
public sealed class ItemStack { public float CurrentWeight; }

public static partial class GameplayTestSystemProbe
{
    public static double Number(JObject args, string name, double fallback) => args[name]?.Value<double>() ?? fallback;
    public static bool Flag(JObject args, string name, bool fallback) => args[name]?.Value<bool>() ?? fallback;
    public static JObject RunMotion(JObject args) => LooseItemMotionValues(args);
    public static JObject RunSources() => LooseItemSourceValues(new JObject());
}
