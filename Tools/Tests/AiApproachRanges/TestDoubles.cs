using UnityEngine;

// 仅替代 Unity 类型和世界服务；觅食、游动、计时方法由 verify.py 逐字取自生产文件。
namespace UnityEngine
{
    public struct Vector2(float x, float y)
    {
        public float x = x, y = y;
        public static Vector2 zero => default;
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float b) => new(a.x * b, a.y * b);
        public static Vector2 ClampMagnitude(Vector2 a, float maximum)
            => a.magnitude > maximum && a.magnitude > 0f ? a * (maximum / a.magnitude) : a;
        public static implicit operator Vector3(Vector2 a) => new(a.x, a.y, 0f);
    }
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z;
        public static implicit operator Vector2(Vector3 a) => new(a.x, a.y);
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
    }
    public sealed class Rigidbody2D { public Vector2 position, velocity; }
    public sealed class Transform { public Vector3 position; }
}

public sealed class Item
{
    public Transform transform = new();
    public ItemData itemData = new();
}
public sealed class ItemData { public int Guid = 42; }
public sealed class ItemMgr
{
    public static ItemMgr Instance = new();
    public void NotifyRuntimeItemMoved(Item item) { }
}
public struct DroppedItemHandle
{
    public int Id;
    public Item Legacy;
    public bool IsValid => Id != 0;
}
public static class DroppedItemService
{
    public static Vector2 Position;
    public static bool Available;
    public static bool TryGetPickablePosition(DroppedItemHandle target, out Vector2 position)
    { position = Position; return Available && target.IsValid; }
}
public static class AquaticHabitat
{
    public static bool Wet = true, Traversable = true;
    public static bool CanSwimAt(Vector2 position) => Wet;
    public static bool CanTraverse(Vector2 origin, Vector2 destination) => Traversable;
}
public static class WorldTopologyRuntime
{
    public static float Period;
    public static Vector2 ShortestDelta(Vector2 origin, Vector2 target)
    {
        Vector2 delta = target - origin;
        if (Period > 0f) delta.x -= MathF.Round(delta.x / Period) * Period;
        return delta;
    }
    public static float SqrDistance(Vector2 origin, Vector2 target) => ShortestDelta(origin, target).sqrMagnitude;
    public static Vector2 NormalizePosition(Vector2 position)
    {
        if (Period > 0f) position.x = (position.x % Period + Period) % Period;
        return position;
    }
}
public sealed class Mod_Food(Item owner)
{
    public sealed class Nutrition { public float Rate = .5f; public float GetFoodRate() => Rate; }
    public sealed class FoodData { public Nutrition nutrition = new(); }
    public FoodData Data = new();
    public int Eaten;
    public bool TryEatDroppedFood(DroppedItemHandle target, string tag, float range)
    {
        if (!DroppedItemService.TryGetPickablePosition(target, out Vector2 position) ||
            WorldTopologyRuntime.SqrDistance(owner.transform.position, position) > range * range) return false;
        Eaten++;
        DroppedItemService.Available = false;
        return true;
    }
}
public static class Mod_FishingRod
{
    public static int Notifications;
    public static void NotifyBaitEaten(DroppedItemHandle target, FishHarness fish, int guid) => Notifications++;
}

public sealed partial class FishHarness
{
    public Item item = new();
    public Rigidbody2D body = new();
    public Mod_Food food;
    public DroppedItemHandle target = new() { Id = 1 };
    public string targetTag = "Food";
    public float swimSpeed, forageRadius, eatRange, eatSeconds, forageStopDistance, eatElapsed;
    public float eatAvailableFoodThreshold, activeForageThreshold;

    public FishHarness(System.Text.Json.JsonElement config)
    {
        food = new Mod_Food(item);
        swimSpeed = config.GetProperty("swimSpeed").GetSingle();
        forageRadius = config.GetProperty("forageRadius").GetSingle();
        eatRange = config.GetProperty("eatRange").GetSingle();
        eatSeconds = config.GetProperty("eatSeconds").GetSingle();
        forageStopDistance = config.GetProperty("forageStopDistance").GetSingle();
        eatAvailableFoodThreshold = config.GetProperty("eatAvailableFoodThreshold").GetSingle();
        activeForageThreshold = config.GetProperty("activeForageThreshold").GetSingle();
        DroppedItemService.Available = AquaticHabitat.Wet = AquaticHabitat.Traversable = true;
        WorldTopologyRuntime.Period = 0f;
        Mod_FishingRod.Notifications = 0;
    }
    public void Tick(float deltaTime) => TickForage(deltaTime);
}
