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
public sealed class FluidInventoryState { }
public static class FluidUnits { public static bool IsFinite(double value) => double.IsFinite(value); }
public readonly struct WorldAddress
{
    public const string SurfaceDimensionId = "surface";
    public WorldAddress(string planet, string dimension) { }
}
namespace FlatWorld.Spaceflight
{
    public static class ShipStructureService
    {
        public static bool HasConsole(ShipState ship) => ship.Pieces.Any(piece => piece.IsAlive && piece.Kind == ShipPieceKind.Console);
    }
}
#endregion
