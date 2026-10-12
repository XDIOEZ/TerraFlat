namespace UnityEngine
{
    #region Unity 数值类型替身
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
    public class TextAsset { public string text; }
    public static class Resources { public static T Load<T>(string path) where T : class => null; }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : System.Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }
    #endregion
}

#region 非接触目标的依赖替身
public sealed class FluidInventoryState { }
public static class FluidUnits { public static bool IsFinite(double value) => double.IsFinite(value); }
public readonly struct WorldAddress
{
    public const string SurfaceDimensionId = "surface";
    public WorldAddress(string planet, string dimension) { }
}
public sealed class TestStack { public float Amount = 1f; }
public class ItemData
{
    public readonly TestStack Stack = new();
    public double TestMassKg = 2d;
}
public sealed class Data_Player : ItemData { }
public class Item
{
    public object Owner;
    public bool InHand;
    public ItemData itemData;
}
public sealed class Player : Item { public Data_Player Data; }
public sealed class ItemMgr
{
    public static ItemMgr Instance;
    public readonly Dictionary<string, Player> Player_DIC = new(StringComparer.Ordinal);
}
public partial class GameSaveData
{
    public readonly Dictionary<string, Data_Player> PlayerData_Dict = new(StringComparer.Ordinal);
}
namespace FlatWorld.Networking
{
    public static class GameNetwork { public static bool HasStateAuthority = true; }
}
#endregion

namespace FlatWorld.Spaceflight
{
    #region 非接触入口显式拒绝
    public sealed class ShipPartConfiguration { }
    public static class ShipStructureService
    {
        public static bool HasConsole(ShipState ship)
            => throw new NotSupportedException("接触测试不模拟控制台对接确认。");
    }
    public static class ShipLandingGuidance
    {
        public static SurfaceContact CreateDescentContact(BodyState body, SpaceVector2 position, SpaceVector2 velocity, double time)
            => throw new NotSupportedException("接触测试不模拟降落请求。");
    }
    public sealed class SpaceLooseItemState
    {
        public string Id, Snapshot, WorldKey;
        public bool InSpace;
        public SpaceMotionState Motion = new();
        public double SurfaceHeight;
    }
    public sealed class ShipMassReader
    {
        public static int ResetCalls, ReadCalls;
        public void Reset() => ResetCalls++;
        public double ReadItem(ItemData data, Item live, int depth)
        {
            ReadCalls++;
            return data.TestMassKg;
        }
    }
    #endregion

    public sealed partial class SpaceSession
    {
        #region 会话数据与依赖观测
        public SpaceSessionState State { get; private set; } = new();
        public UniverseSimulation Universe { get; }
        private readonly GameSaveData owner = new();
        private readonly Dictionary<string, Item> looseViews = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemData> looseData = new(StringComparer.Ordinal);
        private readonly ShipMassReader contactMassReader = new();
        private readonly ShipAssemblyCache queryAssemblies = new();
        public static int DecodeCalls;
        public int TestContactRebuildCount => freeContactAssemblies.RebuildCount;
        public ShipAssemblyState TestContactAssembly(string id) => freeContactAssemblies.Find(id);

        public SpaceSession()
        {
            // 星体远离测试几何，运动与接触仍由生产领域代码执行。
            UniverseState universe = new()
            {
                Bodies = { new BodyState { BodyId = "earth", PlanetId = "Earth", RadiusMeters = 1d, PlacementDistanceMeters = 100000d } }
            };
            Universe = new UniverseSimulation(universe);
            State.Universe = universe;
        }
        public ShipState GetShip(string id)
        {
            foreach (ShipState ship in State.Ships) if (ship.ShipId == id) return ship;
            return null;
        }
        public UniverseSimulation GetUniverse(SpaceSectorId sector) => Universe;
        public bool TryGetBody(string id, out BodyState body) => Universe.TryGetBody(id, out body);
        public ShipFlightState GetFlight(string id)
        {
            foreach (ShipFlightState flight in State.Flights) if (flight.ShipId == id) return flight;
            return null;
        }
        public static ItemData Decode(string encoded)
        {
            DecodeCalls++;
            if (encoded != "contact-fixture") throw new InvalidOperationException("非候选散落物进入了解码入口。");
            return new ItemData();
        }
        public static void ResetCounters()
        {
            DecodeCalls = 0; ShipMassReader.ResetCalls = 0; ShipMassReader.ReadCalls = 0;
        }
        #endregion

        #region 非目标场景入口
        private void ApplyLineImpact(ShipState ship, double damage, string reason)
            => throw new NotSupportedException("散落物接触测试不模拟船间结构断裂。");
        public ShipAssemblyState GetConnectedAssembly(string id)
            => throw new NotSupportedException("接触测试不查询场景降落整体。");
        private bool CanBeginPoweredLanding(ShipAssemblyState assembly, double gravity)
            => throw new NotSupportedException("接触测试不消费降落燃料。");
        private void BeginSurfaceDescent(ShipAssemblyState assembly, SurfaceContact contact, bool powered = false)
            => throw new NotSupportedException("接触测试不启动场景下降。");
        private void LandAssembly(ShipAssemblyState assembly, SurfaceContact contact, bool applyImpact = true)
            => throw new NotSupportedException("接触测试不落地或切场景。");
        private double AssemblyRadius(ShipAssemblyState assembly) => 1d;
        #endregion
    }
}
