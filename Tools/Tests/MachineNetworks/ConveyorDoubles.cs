using UnityEngine;

namespace UnityEngine
{
    #region 输送带离线数学与贴图元数据
    public readonly record struct Vector2(float x, float y)
    {
        public static Vector2 up => new(0, 1);
        public static Vector2 down => new(0, -1);
        public static Vector2 right => new(1, 0);
        public float magnitude => MathF.Sqrt(x * x + y * y);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float b) => new(a.x * b, a.y * b);
        public static Vector2 operator /(Vector2 a, float b) => new(a.x / b, a.y / b);
    }
    public readonly partial record struct Vector3
    {
        public static Vector3 right => new(1, 0, 0);
        public static implicit operator Vector3(Vector2 value) => new(value.x, value.y, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a) => new(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator /(Vector3 a, float b) => new(a.x / b, a.y / b, a.z / b);
        public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
    }
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w)
        { this.x = x; this.y = y; this.z = z; this.w = w; }
    }
    public readonly record struct Rect(float x, float y, float width, float height)
    {
        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
        public Vector2 size => new(width, height);
    }
    public sealed class Texture2D { public int width, height; }
    public sealed class Sprite
    {
        public Rect rect, textureRect;
        public Vector2 pivot;
        public float pixelsPerUnit;
        public Texture2D texture;
    }
    public static partial class Mathf
    {
        public const float PI = MathF.PI;
        public static float Atan2(float y, float x) => MathF.Atan2(y, x);
        public static float Sin(float value) => MathF.Sin(value);
        public static float Cos(float value) => MathF.Cos(value);
        public static float Ceil(float value) => MathF.Ceiling(value);
        public static float MoveTowards(float current, float target, float step)
            => MathF.Abs(target - current) <= step ? target : current + MathF.Sign(target - current) * step;
    }
    #endregion
}

// 离线不加载游戏资源，几何检查直接提供真实素材的切片尺寸、PPU 与 Pivot。
public sealed class GameRes
{
    public static GameRes ExistingInstance => null;
    public bool TryGetItemDefinition(string id, out RuntimeItemDefinition definition)
    { definition = null; return false; }
}
public sealed class RuntimeItemDefinition
{
    public bool TryGetVisualStateSprite(string state, out Sprite sprite)
    { sprite = null; return false; }
}
