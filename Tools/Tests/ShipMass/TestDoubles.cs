using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 私有生产读取器的测试入口
        public sealed class MassReaderHarness
        {
            private readonly ShipMassReader reader = new();
            public void Reset() => reader.Reset();
            public void ClearCache() => reader.ClearCache();
            public void RegisterStructuralItem(ItemData data) => reader.RegisterStructuralItem(data);
            public double ReadItem(ItemData data, Item live = null) => reader.ReadItem(data, live, 0);
            public double ReadInventory(Inventory_Data data) => reader.ReadInventory(data, 0);
            public double ReadContents(ItemData data, Item live = null, MachineEntity machine = null)
                => reader.ReadItemContents(data, live, machine, 0);
            public double ReadFluidMass(FluidInventoryState state) => reader.ReadFluidMass(state);
        }
        #endregion
    }
}

#region 可观测的非测试目标依赖替身
// 替身只提供输入状态和解码计数，质量遍历与缓存逻辑直接执行生产代码。
public static class DecodeCounters
{
    public static int Json, Binary, RootJson, ColdInventory, Rebase;
    public static void Reset() { Json = Binary = RootJson = ColdInventory = Rebase = 0; }
}

public sealed class GameRes
{
    public static GameRes ExistingInstance { get; set; } = new();
    public int ResourceReloadVersion { get; set; }
}
public static class ItemDefinitionRuntime
{
    public static void RebasePersistedData(GameRes resources, ItemData data)
    { DecodeCounters.Rebase++; data.SharedConfiguration = new object(); }
}
public class ItemData
{
    public int Guid;
    public string IDName = "test";
    public StackData Stack = new();
    public object SharedConfiguration = new();
    public string ItemSpecialData;
    public Dictionary<string, ModuleData> ModuleDataDic = new(StringComparer.Ordinal);
    public FluidInventory PortableTank;
}
public sealed class Data_Player : ItemData
{ public Dictionary<string, Inventory_Data> _inventoryData = new(); }
public sealed class StackData
{
    public float Amount = 1f;
    public double UnitWeight;
    public double CurrentWeight => Amount * UnitWeight;
}
public sealed class ItemSlot { public ItemData itemData; }
public sealed class Inventory_Data { public List<ItemSlot> itemSlots = new(); }
public sealed class Inventory { public Inventory_Data Data; }
public sealed class InventoryInstanceSnapshot
{
    public Func<Inventory_Data> Factory;
    public Inventory_Data CreateColdData() { DecodeCounters.ColdInventory++; return Factory(); }
}
public class ModuleData
{
    public string ID;
    public string ModuleId;
}
public class Ex_ModData : ModuleData
{
    public string BitData;
    public T GetData<T>() { DecodeCounters.Json++; return string.IsNullOrEmpty(BitData) ? default : JsonConvert.DeserializeObject<T>(BitData); }
    public void ReadData<T>(ref T data) => data = GetData<T>();
    public void WriteData<T>(T data) => BitData = JsonConvert.SerializeObject(data);
}
public class Ex_ModData_MemoryPackable : ModuleData
{
    public byte[] BitData;
    public T GetData<T>() => MemoryPack.MemoryPackSerializer.Deserialize<T>(BitData);
}
public sealed class Inventory_ModuleData : ModuleData
{ public Dictionary<string, Inventory_Data> Data = new(); }
public sealed class Item { public Dictionary<string, Module> Mods = new(StringComparer.Ordinal); }
public class Module { public bool Enabled = true, IsRuntimeLoaded = true; }
public sealed class Mod_Inventory : Module
{
    public List<Inventory> InventoryInstances = new();
    public Dictionary<string, Inventory> InventoryRefDic = new();
}
public sealed class Mod_VesselContents : Module
{
    public const string ModuleId = "vessel";
    public Inventory Contents;
}
public sealed class Mod_WaterVessel : Module
{
    public const string ModuleId = "water";
    public LiquidContainerState Data;
}
public sealed class Mod_Hand : Module { public Inventory HandInventory; }
public sealed class Mod_HotBar : Module { public Inventory RuntimeInventory; }
public sealed class Mod_HandCraftTable : Module { public Inventory inputInventory, outputInventory; }
public sealed class Mod_HandMade : Module { public Dictionary<string, Inventory> InventoryRefDic = new(); }
public sealed class Mod_Equipment : Module
{
    public Inventory EquipmentInventory;
    public List<EquipmentInstance> Instances = new();
    public IEnumerable<EquipmentInstance> EnumerateEquippedInstances() => Instances;
}
public class EquipmentInstance { }
public sealed class EquipmentInstance_Bag : EquipmentInstance { public ItemData EquippedItem; public Inventory_Data BagData; }
public sealed class Mod_EquipmentSaveData
{
    public InventoryInstanceSnapshot EquipmentInventoryData;
    public List<List<EquipmentInstance>> EquipmentInstances;
}
public static class ModText
{
    public const string Building = "building", Equipment_Module = "equipment", Equipment_Store = "equipment-store";
}
public static class Mod_Building
{
    public sealed class Building_Data { public string SnapshotBase64; }
    public static bool TryReadBuildingData(ItemData data, out Ex_ModData module, out Building_Data state)
    {
        foreach (ModuleData candidate in data.ModuleDataDic.Values)
        {
            if (candidate is Ex_ModData found && candidate.ID == ModText.Building)
            { module = found; state = found.GetData<Building_Data>(); return true; }
        }
        module = null; state = null; return false;
    }
}
public static class ItemNetworkStateSerialization
{
    public static bool TryDeserializeItemData(byte[] bytes, out ItemData item)
    { item = MemoryPack.MemoryPackSerializer.Deserialize<ItemData>(bytes); return item != null; }
}
public sealed class FluidInventory { public FluidInventoryState State; }
public sealed class FluidInventoryState { public List<FluidComponentState> Components = new(); }
public sealed class FluidComponentState { public string FluidId; public double GasMoles, LiquidMoles; }
public sealed class FluidDefinition
{
    public string Id, LiquidId;
    public double MolarMassKgPerMol, LitersPerServing = 1d, LiquidDensityKgPerLiter = 1d;
}
public sealed class FluidCatalog
{
    public static readonly FluidCatalog Default = new();
    public readonly Dictionary<string, FluidDefinition> Definitions = new();
    public FluidDefinition Find(string id) => Definitions[id];
    public bool TryFromLiquidId(string id, out FluidDefinition value)
    { value = Definitions.Values.FirstOrDefault(x => x.LiquidId == id); return value != null; }
}
public sealed class LiquidContainerState { public Dictionary<string, double> Composition = new(); }
public static class MixedLiquidContents { public static void Ensure(LiquidContainerState data) { } }
public static class FluidTankStorage
{
    public static bool TryGet(ItemData data, out FluidInventory inventory, out string reason)
    { inventory = data.PortableTank; reason = null; return inventory != null; }
    public static bool TryReadContents(ItemData data, out FluidInventoryState state)
    { state = data.PortableTank?.State; return state != null; }
}
public static class Mod_Spacesuit { public const string ModuleId = "spacesuit"; }
public sealed class SpacesuitState { public InventoryInstanceSnapshot OxygenTank, PropulsionTank; }
public static class Mod_FluidTank { public const string ModuleId = "fluid-tank"; }
public sealed class FluidTankState { public FluidInventoryState Contents; }
public static class Mod_Cultivator { public const string ModuleId = "cultivator"; }
public sealed class CultivatorState { public InventoryInstanceSnapshot Seeds, Harvest, Fertilizer; }
public sealed class VesselContentsState { public InventoryInstanceSnapshot Items; }
public sealed class MachineEntity { public MachineLogic Logic; public MachineProcessor Processor; }
public class MachineLogic { public List<Inventory> Inventories = new(); }
public sealed class VesselLogic : MachineLogic { public LiquidContainerState Data; }
public sealed class CultivatorLogic : MachineLogic { }
public sealed class MachineProcessor { public Inventory Input, Output; }
public sealed class MachineStorageState { public List<Inventory_Data> Inventories; }
public sealed class RecipeProcessingState { public Inventory_Data Input, Output; }
public sealed class FurnaceRuntimeState { public SmeltingState Smelting; public RecipeProcessingState Processing; }
public sealed class SmeltingState { public Dictionary<string, Inventory_Data> InvData; }
public sealed class SlotProcessingState { public Inventory_Data Inventory; }
public sealed class FluidMachineState
{
    public Dictionary<string, FluidInventoryState> Chambers;
    public Inventory_Data PortableTank, FilterMaterial, Residue;
}
namespace FlatWorld.Gameplay.Progress
{
    public static class ItemSpecialDataJsonStore
    {
        public static JObject ReadRoot(string text)
        { DecodeCounters.RootJson++; return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text); }
    }
}
namespace MemoryPack
{
    public static class MemoryPackSerializer
    {
        private static int nextId;
        private static readonly Dictionary<int, object> Payloads = new();
        public static byte[] Serialize<T>(T value)
        { int id = ++nextId; Payloads.Add(id, value); return BitConverter.GetBytes(id); }
        public static T Deserialize<T>(byte[] bytes)
        { DecodeCounters.Binary++; return (T)Payloads[BitConverter.ToInt32(bytes)]; }
    }
}
#endregion
