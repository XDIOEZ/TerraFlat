namespace UnityEngine
{
    #region 离线测试环境
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new(1f, 1f, 1f);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        // 保留 Unity 的计算属性，避免离线替身掩盖 JSON 递归展开问题。
        public Vector3 normalized => magnitude > 1e-5f ? new(x / magnitude, y / magnitude, z / magnitude) : default;
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
    public sealed class ShipPartConfiguration { }
    public interface IShipAtmosphereSource { }
    public static class ShipAtmosphereService
    {
        // 质量测试直接提供舱气重量，不模拟气密拓扑或流体化学。
        public static double GetGasMassKg(ShipCompartmentState room) => room.Gas.TestMassKg;
        public static void ReleaseAllGas(ShipState ship, IShipAtmosphereSource outside = null)
        {
            if (ship.Compartments.Count != 0) throw new NotSupportedException("离线质量测试不模拟舱气释放。");
        }
        public static void TransferToSplit(List<ShipCompartmentState> previous, List<ShipState> ships, IShipAtmosphereSource outside)
        {
            if (previous.Count != 0) throw new NotSupportedException("离线质量测试不模拟舱气分裂。");
        }
        public static void RebuildCompartments(ShipState ship, IShipAtmosphereSource outside = null)
        {
            // 仅放行没有气密边界和舱气的结构样本，避免替身冒充气密算法。
            if (ship.Compartments.Count != 0 || ship.Pieces.Any(piece => piece.SealsAtmosphere))
                throw new NotSupportedException("离线质量测试不模拟气密拓扑。");
        }
    }
}
#endregion
