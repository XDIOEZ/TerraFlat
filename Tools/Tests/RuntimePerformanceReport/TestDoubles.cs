using System.Text.Json;

namespace UnityEngine
{
    public class MonoBehaviour
    {
        public GameObject gameObject;
        protected static void Destroy(GameObject gameObject) { }
        protected static void DontDestroyOnLoad(GameObject gameObject) { }
    }

    public sealed class GameObject
    {
        public static MonoBehaviour LastAddedComponent;
        public GameObject(string name) { }
        public T AddComponent<T>() where T : MonoBehaviour, new()
        {
            var component = new T { gameObject = this };
            LastAddedComponent = component;
            return component;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrderAttribute : Attribute
    {
        public DefaultExecutionOrderAttribute(int order) { }
    }

    public enum RuntimeInitializeLoadType { SubsystemRegistration, AfterSceneLoad }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }

    public enum RuntimePlatform { WindowsPlayer }
    public enum CompressionLevel { Fastest }

    public static class Application
    {
        public static string persistentDataPath, unityVersion = "2022.3", version = "test";
        public static RuntimePlatform platform = RuntimePlatform.WindowsPlayer;
    }

    public static class Time
    {
        public static double realtimeSinceStartupAsDouble;
        public static float unscaledDeltaTime;
    }

    public static class GUIUtility
    {
        public static string systemCopyBuffer;
    }

    public static class Debug
    {
        public static void Log(string message) { }
        public static void LogWarning(string message) { }
    }

    public static class JsonUtility
    {
        public static string ToJson(object value, bool prettyPrint) =>
            JsonSerializer.Serialize(value, value.GetType(),
                new JsonSerializerOptions { IncludeFields = true, WriteIndented = prettyPrint });
    }
}

namespace UnityEngine.Profiling
{
    public static class Profiler
    {
        public static long GetTotalAllocatedMemoryLong() => 1024;
        public static long GetTotalReservedMemoryLong() => 2048;
        public static long GetMonoUsedSizeLong() => 512;
    }
}

namespace UnityEngine.SceneManagement
{
    public readonly struct Scene { public string name => "OfflineTestScene"; }
    public static class SceneManager { public static Scene GetActiveScene() => new(); }
}

namespace FlatWorld.Diagnostics
{
    public static class PerformanceDebugRegistry
    {
        public static bool StatisticsEnabled;
        public readonly struct Snapshot
        {
            public readonly string Name;
            public readonly long Calls, Work, Gauge, PeakGauge;
            public readonly double TotalMilliseconds, MaximumMilliseconds;
            public readonly bool HasGauge;
        }
        public static Snapshot[] CaptureSnapshot() => Array.Empty<Snapshot>();
    }
}

public static class GameLogManager
{
    public static string GetRuntimeLogSnapshot(int maxCharacters, out bool truncated)
    {
        truncated = false;
        return "offline runtime log";
    }
}

public enum PersistentRuntimeObjectCategory { Debug }
public static class PersistentRuntimeObjectHierarchy
{
    public static bool MoveToPersistentCategory(UnityEngine.GameObject gameObject, PersistentRuntimeObjectCategory category) => true;
}
