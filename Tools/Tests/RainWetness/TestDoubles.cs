// 仅替换引擎、资源及天气输入；Buff 定义、校验、实例、管理器和两个暴露时钟使用生产源码。
namespace UnityEngine
{
    public class MonoBehaviour
    {
        public bool isActiveAndEnabled = true;
        public Transform transform = new();
        public T GetComponentInParent<T>() where T : class => (this as Module)?.item as T;
    }
    public sealed class Transform { public Vector3 position; }
    public struct Vector3 { public float x, y, z; }
    public static class Time { public static int frameCount; }
    public static class Mathf
    {
        public const float Epsilon = float.Epsilon;
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Clamp(int v, int min, int max) => Math.Clamp(v, min, max);
        public static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
    }
    public static class Debug
    {
        public static void Log(string message, object context = null) { }
        public static void LogWarning(string message, object context = null) { }
        public static void LogError(string message, object context = null) => throw new Exception(message);
    }
}
namespace Sirenix.OdinInspector
{
    public sealed class ShowInInspectorAttribute : Attribute { }
    public sealed class ButtonAttribute : Attribute { public ButtonAttribute(string text) { } }
}
namespace MemoryPack
{
    public sealed class MemoryPackableAttribute : Attribute { }
    public sealed class MemoryPackIgnoreAttribute : Attribute { }
}
namespace FlatWorld.Networking
{
    public static class GameNetwork { public static bool HasStateAuthority = true; }
}
public enum ModuleTickMode { FixedInterval }
public class ModuleData { public string ID; }
public class Ex_ModData_MemoryPackable : ModuleData
{
    public void ReadData<T>(ref T data) { }
    public void WriteData<T>(T data) { }
}
public class Module : UnityEngine.MonoBehaviour
{
    public Item item;
    public virtual ModuleData _Data { get; set; } = new();
    public virtual ModuleTickMode TickMode => default;
    public virtual float FixedTickInterval => 0f;
    public virtual string CanonicalModuleId => "";
    public virtual bool MatchesPersistedId(string id) => false;
    public virtual void Awake() { }
    public virtual void Load() { }
    public virtual void Save() { }
    public virtual void Unload() { }
    public virtual void ModUpdate(float dt) { }
}
public static class ModText
{
    public const string Mod_BuffManager = "Mod_BuffManager", Food = "Food", Hp = "Hp";
}
public sealed class ItemData { public string IDName = "Animal"; }
public class Item : UnityEngine.MonoBehaviour
{
    public bool DestructionHandled;
    public ItemData itemData = new();
    public ItemMods itemMods = new();
}
public sealed class Player : Item { }
public sealed class ItemMods
{
    public readonly Dictionary<string, Module> Modules = new();
    public Module GetMod_ByID(string id) => Modules.GetValueOrDefault(id);
    public T GetMod_ByID<T>(string id) where T : Module => GetMod_ByID(id) as T;
}
public sealed class Mod_DamageReceiver : Module { public float Hp = 100f; }
public struct FoodConsumeResult { public bool IsDrink; }
public sealed class Mod_Food : Module
{
    public event Action<FoodConsumeResult> ConsumeCompleted;
    public void Complete(FoodConsumeResult result) => ConsumeCompleted?.Invoke(result);
}
public sealed class RuntimeItemDefinition { public bool IsActor; }
public sealed class GameRes
{
    public static GameRes Instance = new();
    public readonly Dictionary<string, BuffDefinition> Buffs = new();
    public BuffDefinition GetBuffDefinition(string id) => Buffs.GetValueOrDefault(id);
    public bool TryGetItemDefinition(string id, out RuntimeItemDefinition definition)
    {
        definition = new RuntimeItemDefinition { IsActor = id == "Animal" };
        return true;
    }
}
public sealed class WeatherMgr
{
    public static WeatherMgr ExistingInstance = new();
    public bool Raining = true, Suppressed, Snowing;
    public float Intensity = 0.65f;
    public bool IsRaining() => !Suppressed && Raining && Intensity > 0f;
    public bool IsSnowingAt(UnityEngine.Vector3 position) => Snowing || position.x < 0f;
    public float GetCurrentWeatherIntensity() => Intensity;
}
public enum BodyPartType { Head, Chest, Abdomen, Pelvis, LeftHand, RightHand, LeftLeg, RightLeg }
public static class BodyTraumaBuffEffects
{
    public const string RestoreDurability = "core:body_durability_restore", Move = "core:trauma_move",
        Attack = "core:trauma_attack", Confusion = "core:trauma_confusion", Blur = "core:trauma_blur";
}
public static class BuffEffectTypeIds
{
    public const string TrueDamage = "core:true_damage", TemperatureWarming = "core:temperature_warming",
        NightVision = "core:night_vision", MoveSpeedMultiplier = "core:move_speed_multiplier",
        FoodConsumeSpeedMultiplier = "core:food_consume_speed_multiplier", WaterConsumeSpeedMultiplier = "core:water_consume_speed_multiplier",
        TemperatureCoolingMultiplier = "core:temperature_cooling_multiplier", DamageTakenMultiplier = "core:damage_taken_multiplier",
        Heal = "core:heal", MaxHealthPercentTrueDamage = "core:max_health_percent_true_damage",
        MaxHealthPercentHeal = "core:max_health_percent_heal", NutritionChange = "core:nutrition_change";
}
public static class BuffEffectDispatcher
{
    public static readonly Dictionary<(string, BuffEffectPhase), int> Executions = new();
    public static bool TryCacheHandler(BuffEffectDefinition effect) => effect.TryCacheHandler((e, b) =>
    {
        var key = (b.DefinitionId, e.Phase);
        Executions[key] = Executions.GetValueOrDefault(key) + 1;
    });
    public static bool IsSupportedNutritionTarget(string id) =>
        new[] { "carbohydrates", "fat", "protein", "water", "vitamins" }.Contains(id?.ToLowerInvariant());
    public static void Execute(IReadOnlyList<BuffEffectDefinition> effects, BuffInstance runtime)
    {
        foreach (var effect in effects) effect.Execute(runtime);
    }
}
