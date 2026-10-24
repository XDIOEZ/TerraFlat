// 仅替代 Unity、Item 工厂和存档容器边界；索引、快照保留及卸载事务直接编译生产文件。
namespace UnityEngine
{
    public readonly record struct Vector3(float x, float y, float z = 0);
    public sealed class Transform { public Vector3 position; }
    public sealed class GameObject { public string scene = "surface"; }
    public static class Debug
    {
        public static readonly List<string> Messages = new();
        public static void LogError(object value) => Messages.Add(value.ToString());
        public static void LogException(Exception value) => Messages.Add(value.Message);
    }
}
namespace FlatWorld.WorldModel
{
    public readonly record struct Int2(int X, int Y);
    public readonly record struct WorldAddress(string DimensionId, Int2 ChunkOrigin);
}
namespace FlatWorld.Networking { public static class GameNetwork { public static bool HasStateAuthority = true; } }
namespace FlatWorld.Spaceflight
{
    public sealed class SpaceSession
    {
        public static SpaceSession Current = new();
        public readonly HashSet<int> Pieces = new();
        public bool Owns(int id) => Pieces.Contains(id);
    }
}
public static class GameplayWorldRegistry { public static string CurrentScene = "surface"; }
public sealed class ItemData
{
    public int Guid;
    public int Hp = 83;
    public int InventoryCount = 17;
    public string ModuleState = "door-open";
    public bool Building = true;
    public ItemData Clone() => (ItemData)MemberwiseClone();
}
public sealed class Item
{
    public ItemData itemData = new();
    public readonly UnityEngine.GameObject gameObject = new();
    public readonly UnityEngine.Transform transform = new();
    public bool DestructionHandled, IsInPool, InHand, FailSave, ThrowAfterDespawn;
    public object PersistenceOwner;
    public Action<Item> OnItemDestroy;
    public Action<Item> OnSaved;
    public void Save()
    {
        if (FailSave) throw new InvalidOperationException("injected snapshot failure");
        OnSaved?.Invoke(this);
    }
}
internal sealed class ItemReferenceComparer : IEqualityComparer<Item>
{
    internal static readonly ItemReferenceComparer Instance = new();
    public bool Equals(Item x, Item y) => ReferenceEquals(x, y);
    public int GetHashCode(Item item) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item);
}
public sealed class ItemMgr
{
    public static ItemMgr Instance;
    public static ItemMgr GetInstance() => Instance;
    public readonly Dictionary<int, Item> Items = new();
    public int DespawnCalls;
    public void DespawnItem(Item item, bool saveData)
    {
        if (saveData) throw new Exception("streaming must not save twice");
        DespawnCalls++;
        Items.Remove(item.itemData.Guid);
        item.DestructionHandled = true;
        item.OnItemDestroy?.Invoke(item);
        item.IsInPool = true;
        if (item.ThrowAfterDespawn) throw new InvalidOperationException("injected teardown observer failure");
    }
}
public partial class ChunkMgr
{
    public static ChunkMgr ExistingInstance;
    public bool IsWorldModelRuntimeActive = true;
    public FlatWorld.WorldModel.WorldAddress ResolveWorldAddress(UnityEngine.Vector3 position) =>
        new("surface", new((int)Math.Floor(position.x / 16) * 16, (int)Math.Floor(position.y / 16) * 16));
}
public partial class SaveDataMgr
{
    public object SaveData = new();
    private readonly HashSet<string> runtimeBuildingDirtyChunks = new();
    private readonly Dictionary<string, FlatWorld.WorldModel.WorldAddress> runtimeBuildingDirtyAddresses = new();
    private readonly HashSet<string> restoredRuntimeBuildingChunks = new();
    public readonly Dictionary<string, List<ItemData>> Records = new();
    private static string ResolveRuntimePlanetName() => GameplayWorldRegistry.CurrentScene;
    private static string BuildChunkKey(string planet, string chunk) => planet + ":" + chunk;
    private static string ToChunkName(FlatWorld.WorldModel.WorldAddress address) => $"{address.ChunkOrigin.X},{address.ChunkOrigin.Y}";
    private static bool IsRuntimeBuildingData(ItemData data) => data?.Building == true;
    private static bool IsRuntimeBuildingItem(Item item, out object building)
    {
        building = null;
        return item != null && item.PersistenceOwner == null && !item.InHand && item.itemData.Building;
    }
    private static ItemData CloneItemData(ItemData data) => data.Clone();
    public static string Key(FlatWorld.WorldModel.WorldAddress address) => BuildChunkKey(ResolveRuntimePlanetName(), ToChunkName(address));
    public void Record(Item item)
    {
        var address = ChunkMgr.ExistingInstance.ResolveWorldAddress(item.transform.position);
        string key = Key(address);
        ChunkMgr.ExistingInstance.RegisterRuntimeBuilding(item);
        MarkRuntimeBuildingResident(key, item.itemData.Guid);
        runtimeBuildingDirtyChunks.Add(key);
        runtimeBuildingDirtyAddresses[key] = address;
    }
    private void MergeRuntimeBuildingItems(string planet, FlatWorld.WorldModel.WorldAddress address, List<ItemData> saved)
    {
        string key = BuildChunkKey(planet, ToChunkName(address));
        if (!Records.TryGetValue(key, out var existing)) Records.Add(key, existing = new());
        RemoveReplacedRuntimeBuildingSnapshots(key, existing, saved);
        existing.AddRange(saved);
    }
    public void AutoSave(FlatWorld.WorldModel.WorldAddress address, params ItemData[] live)
        => MergeRuntimeBuildingItems(ResolveRuntimePlanetName(), address, live.ToList());
    public void MarkRestored(FlatWorld.WorldModel.WorldAddress address)
        => restoredRuntimeBuildingChunks.Add($"{Key(address)}\u001f{address.DimensionId}");
    public bool IsRestored(FlatWorld.WorldModel.WorldAddress address)
        => restoredRuntimeBuildingChunks.Contains($"{Key(address)}\u001f{address.DimensionId}");
    public bool IsDirty(FlatWorld.WorldModel.WorldAddress address) => runtimeBuildingDirtyChunks.Contains(Key(address));
    public void RemoveRecord(FlatWorld.WorldModel.WorldAddress address, int guid)
    {
        string key = Key(address);
        MarkRuntimeBuildingResident(key, guid);
        if (Records.TryGetValue(key, out var saved)) saved.RemoveAll(item => item.Guid == guid);
    }
}
