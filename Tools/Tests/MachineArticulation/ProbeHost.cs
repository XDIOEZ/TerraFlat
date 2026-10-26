using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 离线规则宿主
    public static double Number(JObject arguments, string name, double fallback) => arguments?[name]?.Value<double>() ?? fallback;
    public static bool Flag(JObject arguments, string name, bool fallback) => arguments?[name]?.Value<bool>() ?? fallback;
    public static void Register(string system, string operation, Func<JObject, JObject> handler) { }
    internal static JObject Cycle(JObject arguments) => CaptureMachineArticulatedPumpCycle(arguments);
    internal static JObject Working() => CaptureMachineArticulatedPumpWorkingStates(new JObject());
    internal static JObject Clock() => CaptureMachineArticulatedMotionClock(new JObject());
    #endregion
}

namespace UnityEngine
{
    #region 不执行编辑器生命周期的特性声明
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public static class Application
    {
        public static string streamingAssetsPath
        {
            get
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ProjectSettings/ProjectVersion.txt")))
                    directory = directory.Parent;
                if (directory == null) throw new DirectoryNotFoundException("未找到FlatWorld项目根目录。");
                return Path.Combine(directory.FullName, "Assets/StreamingAssets");
            }
        }
    }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }
    #endregion
}
