using System;
using System.Collections.Generic;
using Newtonsoft.Json;

#region Unity与场景外数据替身
namespace MemoryPack
{
    public sealed class MemoryPackableAttribute : Attribute { }
    public sealed class MemoryPackIgnoreAttribute : Attribute { }
    public static class MemoryPackSerializer
    {
        public static byte[] Serialize<T>(T value) => System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
        public static T Deserialize<T>(byte[] value) => JsonConvert.DeserializeObject<T>(System.Text.Encoding.UTF8.GetString(value));
    }
}
namespace FastCloner.Code { public sealed class FastClonerIgnoreAttribute : Attribute { } }
namespace UnityEngine
{
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Abs(float value) => Math.Abs(value);
        public static int RoundToInt(float value) => (int)Math.Round(value);
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
        public static float MoveTowards(float value, float target, float distance)
            => Math.Abs(target - value) <= distance ? target : value + Math.Sign(target - value) * distance;
    }
    public static class Debug { public static void LogException(Exception error) => throw error; }
}
public enum MatchMode { ExactItem, ByTag }
public enum RecipeType { Smelting }
public enum RecipeInputRule { 无规则合成 }
public sealed class RuntimeRecipeIngredient
{
    public MatchMode matchMode;
    public string ItemName = "", Tag = "", ExcludeTag;
    public int amount;
    public float? MinTemperature;
}
public sealed class RuntimeRecipeResult { public string ItemName; public int amount; public float durabilityMultiplier; }
public sealed class RuntimeRecipeInput
{
    public RecipeType recipeType;
    public RecipeInputRule inputOrder;
    public int GridWidth, GridHeight;
    public List<RuntimeRecipeIngredient> RowItems_List = new();
}
public sealed class RuntimeRecipeOutput { public List<RuntimeRecipeResult> results = new(); }
public sealed class RuntimeLiquidOutput { public string LiquidId; public float Amount; }
public sealed class RuntimeRecipe
{
    public string Id, DisplayName;
    public float Temperature, Temperature_Max, ProcessingSeconds;
    public int ManualWorkSteps;
    public RuntimeRecipeInput inputs = new();
    public RuntimeRecipeOutput outputs = new();
    public RuntimeLiquidOutput LiquidOutput;
}
public static class CraftedDurabilityQuality { public const float DefaultMultiplier = 1f; }
public static class Tag { public const string CombustionTinder = "Material.Tinder", CombustionFuel = "Combustion.Fuel"; }
public sealed class ItemMatterState { public bool Initialized; public float TemperatureCelsius, Moisture; }
public sealed class ItemStack
{
    public float Amount, Weight = 1f, Volume = 1f;
    public bool Stackable = true, CanBePickedUp = true;
    public float CurrentWeight => Amount * Weight;
    public float CurrentVolume => Amount * Volume;
}
public sealed class ItemData
{
    public string IDName;
    public ItemStack Stack = new();
    public List<string> Tags = new();
    public ItemMatterState MatterState;
    public bool CanStackWith(ItemData other) => other != null && Stack.Stackable && other.Stack.Stackable &&
        IDName == other.IDName && MatterState?.TemperatureCelsius == other.MatterState?.TemperatureCelsius;
}
public sealed class ItemSlot
{
    public ItemSlot(int index) { Index = index; }
    public int Index;
    public float SlotMaxVolume = 100f;
    public ItemData itemData;
    public void RefreshUI() { }
}
public sealed class Inventory_Data
{
    public const float DefaultSlotVolume = 100f;
    public List<ItemSlot> itemSlots = new();
    public uint ContentStructureRevision;
    public bool HasUnlimitedSlots;
    public Action<int> Event_RefreshUI;
    public Action<ItemSlot> Event_OnDataChanged, Event_OnBeforeDataChanged;
    public float GetCapacityLimitedAmount(ItemData item, float amount, float weight, float volume) => amount;
    public void EnsureSpareSlot() { }
}
public sealed class Inventory
{
    public Inventory_Data Data = new();
    public HashSet<int> Dragged = new();
    public bool IsSlotBeingDragged(int index) => Dragged.Contains(index);
    public void RefreshUI() { }
}
public sealed class CraftingCapabilities { }
public sealed class CraftingRecipeMatch
{
    public CraftingRecipeMatch(RuntimeRecipe recipe, bool mirror, IReadOnlyList<CraftingConsumption> consumptions)
    { Recipe = recipe; Consumptions = consumptions; }
    public RuntimeRecipe Recipe;
    public IReadOnlyList<CraftingConsumption> Consumptions;
}
public readonly struct CraftingConsumption
{
    public CraftingConsumption(int index, float amount) { SlotIndex = index; Amount = amount; }
    public int SlotIndex { get; }
    public float Amount { get; }
}
public enum CraftingFailureReason { InvalidInventory, InvalidOutput, MissingMaterials, OutputSpaceInsufficient, InventoryChanged, CommitFailed }
public sealed class CraftingResult
{
    public static CraftingResult Failed(CraftingFailureReason reason, string message, RuntimeRecipe recipe = null) => new();
}
public static class ItemInstanceDataFactory
{
    public static ItemData CloneRuntime(ItemData item) => JsonConvert.DeserializeObject<ItemData>(JsonConvert.SerializeObject(item));
}
public sealed class RuntimeItemDefinition { public IReadOnlyList<RuntimeItemReactionDefinition> Reactions = Array.Empty<RuntimeItemReactionDefinition>(); }
public sealed class GameRes
{
    public static GameRes ExistingInstance = new();
    public Dictionary<string, RuntimeItemDefinition> Definitions = new();
    public bool TryGetItemDefinition(string id, out RuntimeItemDefinition definition) => Definitions.TryGetValue(id, out definition);
    public Dictionary<string, LiquidDefinition> Liquids = new();
    public LiquidDefinition GetLiquidDefinition(string id) => Liquids.GetValueOrDefault(id);
}
public static class Mod_WaterVessel { public const float AmountEpsilon = .0001f; }
public partial class LiquidContainerState
{
    public string LiquidId;
    public float Amount, Temperature, ProcessingSeconds, RainwaterProgress;
}
public enum LiquidHeatProcessMode { Transform, ConsumeServing }
public sealed class LiquidHeatProcess
{
    public LiquidHeatProcessMode Mode;
    public float MinimumTemperature, Seconds;
    public string ResultLiquidId, OutputItemId;
    public int ConsumeAmount, OutputAmount;
}
public sealed class LiquidDefinition { public float LitersPerServing = .25f; public LiquidHeatProcess HeatProcess; }
public sealed class FluidDefinition { public double LiquidHeatCapacityJPerMolKelvin; }
public sealed class FluidCatalog
{
    public static FluidCatalog Default = new();
    public bool TryFromLiquidId(string id, out FluidDefinition definition) { definition = null; return false; }
}
public static class FluidUnits { public static decimal ServingsToMol(FluidDefinition fluid, decimal servings) => throw new NotSupportedException(); }
public static class WorldLiquidVolumeConversion { public static float GetContainerCapacityLiters(int capacity) => throw new NotSupportedException(); }
public static class TemperatureMgr { public const float DefaultAmbientTemperature = 20f; }
#endregion
