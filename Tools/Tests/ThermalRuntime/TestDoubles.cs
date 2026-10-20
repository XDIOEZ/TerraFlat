using UnityEngine;

#region 燃烧环境替身
public static class ItemEnvironmentSources
{
    public static bool CombustionSupported = true;
    public static bool CanSustainCombustion(Item item) => CombustionSupported;
    public static bool CanSustainCombustion(string worldKey, Vector2 position) => CombustionSupported;
}
#endregion

#region Unity 与检查器替身

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
    }

    public class Object
    {
        public static void Destroy(object value) { }
    }

    public sealed class GameObject
    {
        public bool activeInHierarchy = true;
    }

    public sealed class Transform
    {
        public Vector2 position;
    }

    public sealed class SpriteRenderer
    {
        public Transform transform = new();
    }

    public sealed class ParticleSystem
    {
        public Transform transform = new();
        public GameObject gameObject = new();
        public bool isPlaying;
        public T GetComponent<T>() where T : new() => new();
        public void Play(bool children) => isPlaying = true;
        public void Stop(bool children, ParticleSystemStopBehavior behavior) => isPlaying = false;
    }

    public enum ParticleSystemStopBehavior { StopEmittingAndClear }

    public sealed class ParticleSystemRenderer
    {
        public int sortingLayerID, sortingOrder;
    }

    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
        public static float Min(float left, float right) => Math.Min(left, right);
        public static float Max(float left, float right) => Math.Max(left, right);
        public static int Max(int left, int right) => Math.Max(left, right);
        public static float Clamp(float value, float min, float max) => Math.Min(Math.Max(value, min), max);
        public static float Clamp01(float value) => Clamp(value, 0f, 1f);
        public static int FloorToInt(float value) => (int)Math.Floor(value);
        public static int CeilToInt(float value) => (int)Math.Ceiling(value);
        public static int RoundToInt(float value) => (int)Math.Round(value);
        public static float MoveTowards(float current, float target, float maxDelta)
            => Math.Abs(target - current) <= maxDelta ? target : current + Math.Sign(target - current) * maxDelta;
        public static bool Approximately(float left, float right)
            => Abs(right - left) < Max(0.000001f * Max(Abs(left), Abs(right)), float.Epsilon * 8f);
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
    }
}

namespace Sirenix.OdinInspector
{
    public sealed class ShowInInspectorAttribute : Attribute { }
    public sealed class ReadOnlyAttribute : Attribute { }
    public sealed class LabelTextAttribute(string text) : Attribute
    {
        public string Text { get; } = text;
    }
}

#endregion

#region 环境与角色数据替身

public class SingletonAutoMono<T>
{
    public static T Instance;
}

public partial class TemperatureMgr
{
    private int fieldContextFrame;
    public int TestFieldContextFrame => fieldContextFrame;
    public static float SampleAmbient = 20f;
    public bool TryGetAmbientTemperature(Vector2 position, out float ambient)
    {
        ambient = SampleAmbient;
        return true;
    }
}

public sealed class Mod_Temperature
{
    public sealed class TemperatureData
    {
        public float CurrentTemperature = 36.5f;
        public float AmbientTemperature = 20f;
        public float SafeTemperatureMin = 5f;
        public float SafeTemperatureMax = 50f;
        public float DangerTemperatureBufferSeconds = 60f;
        public float TemperatureBufferRecoveryPerSecond = 1f;
        public float RemainingTemperatureBufferSeconds = 60f;
    }
}

public sealed class PlanetData
{
    public float GlobalTemperature = 20f;
}

public sealed class SaveDataMgr
{
    public static SaveDataMgr Instance;
    public PlanetData Active_PlanetData;
}

public sealed class DimensionManager
{
    public static DimensionManager ExistingInstance;
    public DimensionDefinition ActiveDefinition;
}

public sealed class DimensionDefinition
{
    public bool SuppressWeather;
}

public static class WeatherMgr
{
    public static float CalculateWeatherTemperatureOffset(PlanetData data) => 0f;
}

public static class DayTimeSystem
{
    public static float GetSeasonTemperatureOffset(float multiplier, float globalTemperature) => 0f;
}

public sealed class WorldLiquidSettings
{
    public bool WaterContact;
    public float Temperature;
    public float ContactHeatingPerSecond;
}

public sealed class WorldLiquidSourceTarget
{
    public WorldLiquid Liquid = new();
}

public sealed class WorldLiquid
{
    public WorldLiquidSettings WorldWater;
}

public static class WorldLiquidSourceResolver
{
    public static WorldLiquidSettings Settings;
    public static bool TryResolve(Vector2 position, out WorldLiquidSourceTarget source)
    {
        source = Settings == null ? null : new WorldLiquidSourceTarget { Liquid = new WorldLiquid { WorldWater = Settings } };
        return source != null;
    }
}

#endregion

#region 物品目录与状态替身

public sealed class ItemData
{
    public string IDName;
    public ItemMatterState MatterState;
    public ItemStack Stack;
    public int Guid;
    public bool inHand;
    public object transform;
}

public sealed class ItemMatterState
{
    public bool Initialized;
    public float TemperatureCelsius = 20f;
    public float Moisture;
    public bool IsBurning;
    public float CombustionElapsedSeconds;
}

public sealed class ItemStack
{
    public float Amount;
    public bool CanBePickedUp;
}

public sealed class RuntimeItemDefinition
{
    public RuntimeItemMatterDefinition Matter;
    public List<RuntimeItemReactionDefinition> Reactions = new();
    public bool UsesLightweightWorldDrop;
}

public sealed class GameRes
{
    public static GameRes ExistingInstance;
    public Dictionary<string, RuntimeItemDefinition> Definitions = new(StringComparer.Ordinal);
    public bool TryGetItemDefinition(string id, out RuntimeItemDefinition definition)
        => Definitions.TryGetValue(id, out definition);
    public ItemData CreateItemData(string id) => new() { IDName = id, Stack = new ItemStack() };
}

public static class Mod_Fuel
{
    public static bool TryResolveItemData(ItemData item, out FuelData data)
    {
        data = null;
        return false;
    }
}

public sealed class FuelData
{
    public Vector2 Fuel;
}

public static class Tag
{
    public const string CombustionTinder = "CombustionTinder";
    public const string CombustionFuel = "CombustionFuel";
}

namespace FastCloner
{
    public static class FastCloner
    {
        public static T DeepClone<T>(T value) => value;
    }
}

#endregion

#region 完整物品调度依赖替身

public sealed class Item
{
    public ItemData itemData;
    public bool IsInitialized = true;
    public bool DestructionHandled, IsInPool, InHand;
    public GameObject gameObject = new();
    public object Owner;
    public uint RuntimeGeneration = 1;
    public Transform transform = new();
    public ItemMods itemMods = new();
    public SpriteRenderer Sprite;
    public T GetComponentInChildren<T>(bool inactive) where T : class => null;
}

public sealed class ItemMods
{
    public T GetMod_ByID<T>(string id) where T : class => null;
}

public sealed class ItemReferenceComparer : IEqualityComparer<Item>
{
    public static readonly ItemReferenceComparer Instance = new();
    public bool Equals(Item left, Item right) => ReferenceEquals(left, right);
    public int GetHashCode(Item value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
}

public partial class ItemMgr
{
    private readonly List<Item> RuntimeItems = new();
    private Transform transform = new();
    public void TestRegister(Item item)
    {
        RuntimeItems.Add(item);
        TrackWorldItemMatter(item);
    }
    public void TestTick(float seconds) => TickWorldItemMatter(seconds);
    public double TestClock => worldMatterTime;
    public double TestSettledTime(Item item) => worldMatterEntries[item].LastTime;
    private bool IsRuntimeItemRegistered(Item item) => RuntimeItems.Contains(item);
    private void DespawnItem(Item item, bool saveData) => RuntimeItems.Remove(item);
}

public static class RuntimeAiEntityUtility
{
    public static bool IsAiEntity(Item item) => false;
}

public static class Mod_Droping
{
    public static bool IsDropInProgress(Item item) => false;
}

public sealed class Mod_TileEffectReceiver { }

public sealed class Mod_Projectile
{
    public bool HasActiveWorldAttachment;
}

public sealed class Mod_Combustion
{
    public const string ModuleId = "燃烧";
    public bool IsBurning, IsActivelyBurning, Enabled, IsRuntimeLoaded;
    public void Extinguish() => IsBurning = false;
    public void Ignite() => IsBurning = true;
}

public sealed class Mod_CombustionVisual
{
    public const string ModuleId = "燃烧表现";
    public bool Enabled, IsRuntimeLoaded;
}

public static class WorldItemCombustionVisual
{
    public static ParticleSystem Create(Transform transform) => new();
}

public static class WorldSortingManager
{
    public static void ReadExternalRendererKey(SpriteRenderer renderer, out int layer, out int order)
        => (layer, order) = (0, 0);
}

namespace FlatWorld.Networking
{
    public static class GameNetwork
    {
        public static bool HasStateAuthority = true;
    }

    public static class ItemNetworkStateSerialization
    {
        public static int Notifications;
        public static void NotifyRuntimeStateChanged(Item item) => Notifications++;
    }
}

#endregion

#region 轻量掉落物调度依赖替身

namespace FlatWorld.DroppedItems
{
    public struct LightweightDroppedBody
    {
        public int Id, WaterKind;
        public Vector2 Position;
        public float Amount;
    }

    public sealed class LightweightDroppedItemSimulation
    {
        private readonly Dictionary<int, LightweightDroppedBody> bodies = new();
        public IEnumerable<int> Ids => bodies.Keys;
        public bool Contains(int id) => bodies.ContainsKey(id);
        public LightweightDroppedBody Get(int id) => bodies[id];
        public void Set(LightweightDroppedBody body) => bodies[body.Id] = body;
        public void Remove(int id) => bodies.Remove(id);
        public bool TryGetFlight(int id, out object flight)
        {
            flight = null;
            return false;
        }
    }
}

internal sealed partial class DroppedItemRuntime
{
    private readonly string matterWorldKey = "probe:thermal";
    private readonly FlatWorld.DroppedItems.LightweightDroppedItemSimulation simulation = new();
    private readonly Dictionary<int, ItemData> payloads = new();
    private readonly Dictionary<int, object> visuals = new();
    private readonly DroppedItemPresentation presentation = new();
    public void TestAdd(int id, ItemData data)
    {
        payloads.Add(id, data);
        simulation.Set(new FlatWorld.DroppedItems.LightweightDroppedBody { Id = id, Amount = data.Stack.Amount });
        lastMatterTimes.Add(id, matterTime);
    }
    public void TestTick(float seconds) => TickMatter(seconds);
    public double TestClock => matterTime;
    public double TestSettledTime(int id) => lastMatterTimes[id];
    public int TestNotifications => presentation.Notifications;
    private void Remove(int id) => simulation.Remove(id);
    private void UpdatePlacement(int id) { }
    private object ResolveVisual(ItemData item) => null;
}

internal sealed class DroppedItemPresentation
{
    public int Notifications;
    public void Changed(int id) => Notifications++;
}

#endregion

#region 配方与库存依赖替身

public enum MatchMode { ExactItem, ByTag }
public enum RecipeType { Smelting }
public enum RecipeInputRule { 无规则合成 }
public enum CraftingFailureReason { InvalidInventory, ConditionsNotMet }

public static class CraftedDurabilityQuality
{
    public const float DefaultMultiplier = 1f;
}

public sealed class RuntimeLiquidOutput
{
    public string LiquidId;
    public float Amount;
}

public sealed class RuntimeRecipe
{
    public string Id, DisplayName;
    public float Temperature, Temperature_Max, ProcessingSeconds;
    public int ManualWorkSteps;
    public RuntimeRecipeInput inputs;
    public RuntimeRecipeOutput outputs;
    public RuntimeLiquidOutput LiquidOutput;
}

public sealed class RuntimeRecipeInput
{
    public RecipeType recipeType;
    public RecipeInputRule inputOrder;
    public int GridWidth, GridHeight;
    public List<RuntimeRecipeIngredient> RowItems_List;
}

public sealed class RuntimeRecipeIngredient
{
    public MatchMode matchMode;
    public string ItemName, Tag;
    public int amount;
}

public sealed class RuntimeRecipeOutput
{
    public List<RuntimeRecipeResult> results;
}

public sealed class RuntimeRecipeResult
{
    public string ItemName;
    public int amount;
    public float durabilityMultiplier;
}

public sealed class Inventory
{
    public InventoryData Data;
}

public sealed class InventoryData
{
    public List<ItemSlot> itemSlots;
}

public sealed class ItemSlot
{
    public ItemData itemData;
}

public sealed class CraftingResult
{
    public bool Success;
    public static CraftingResult Failed(CraftingFailureReason reason, string message) => new();
}

public sealed class CraftingCapabilities { }

public sealed class CraftingRecipeMatch
{
    public List<CraftingConsumption> Consumptions;
}

public sealed class CraftingConsumption
{
    public int SlotIndex;
}

public static class CraftingRecipeMatcher
{
    public static bool TryMatchRecipe(Inventory inventory, RuntimeRecipe recipe, CraftingCapabilities capabilities,
        out CraftingRecipeMatch match)
    {
        match = null;
        return false;
    }
}

public static class CraftingService
{
    public static CraftingResult TransformSlot(Inventory input, Inventory output, int slot, string itemId,
        int amount, string processId) => new();
}

#endregion
