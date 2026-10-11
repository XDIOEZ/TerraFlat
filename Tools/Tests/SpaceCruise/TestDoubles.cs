namespace UnityEngine
{
    #region 离线测试环境
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

#region 非测试目标依赖替身
public sealed class FluidInventoryState { public double TestMassKg; }
public static class FluidUnits { public static bool IsFinite(double value) => double.IsFinite(value); }
public readonly struct WorldAddress
{
    public const string SurfaceDimensionId = "surface";
    public WorldAddress(string planet, string dimension) { }
}
namespace FlatWorld.Spaceflight
{
    public interface IShipAtmosphereSource { }
    public static class ShipAtmosphereService
    {
        // 质量测试直接提供舱气重量，不模拟气密拓扑或流体化学。
        public static double GetGasMassKg(ShipCompartmentState room) => room.Gas.TestMassKg;
        public static void ReleaseAllGas(ShipState ship, IShipAtmosphereSource outside = null)
            => throw new NotSupportedException("离线质量测试不模拟舱气释放。");
        public static void TransferToSplit(List<ShipCompartmentState> previous, List<ShipState> ships, IShipAtmosphereSource outside)
            => throw new NotSupportedException("离线质量测试不模拟舱气分裂。");
        public static void RebuildCompartments(ShipState ship, IShipAtmosphereSource outside = null)
            => throw new NotSupportedException("离线质量测试不模拟气密拓扑。");
    }
}
#endregion
