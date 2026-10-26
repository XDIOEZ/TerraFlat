using System.Text.Json;

namespace UnityEngine
{
    public static class Application
    {
        public static string dataPath = "ProfilerAutoExport.Tests";
        public static bool isBatchMode;
    }
    public static class Mathf
    {
        public static float Clamp(float value, float minimum, float maximum) => Math.Clamp(value, minimum, maximum);
    }
    public static class Debug
    {
        public static readonly List<string> Messages = new();
        public static void Log(string message) => Messages.Add(message);
        public static void LogError(string message) => Messages.Add(message);
    }
    public static class JsonUtility
    {
        public static string ToJson(object value, bool prettyPrint) => JsonSerializer.Serialize(value, value.GetType(),
            new JsonSerializerOptions { IncludeFields = true, WriteIndented = prettyPrint });
    }
}

namespace UnityEditor
{
    public sealed class InitializeOnLoadAttribute : Attribute { }
    public sealed class MenuItemAttribute : Attribute
    {
        public MenuItemAttribute(string path, bool validate = false) { }
    }
    public enum PauseState { Paused, Unpaused }
    public enum PlayModeStateChange { EnteredEditMode, ExitingEditMode, EnteredPlayMode, ExitingPlayMode }
    public static class Menu { public static void SetChecked(string path, bool value) { } }
    public static class EditorGUIUtility { public static string systemCopyBuffer; }
    public static class EditorPrefs
    {
        private static readonly Dictionary<string, object> Values = new();
        public static bool GetBool(string key, bool fallback) => Values.TryGetValue(key, out var value) ? (bool)value : fallback;
        public static float GetFloat(string key, float fallback) => Values.TryGetValue(key, out var value) ? (float)value : fallback;
        public static void SetBool(string key, bool value) => Values[key] = value;
        public static void SetFloat(string key, float value) => Values[key] = value;
    }
    public static class SessionState
    {
        private static readonly Dictionary<string, string> Values = new();
        public static string GetString(string key, string fallback) => Values.GetValueOrDefault(key, fallback);
        public static void SetString(string key, string value) => Values[key] = value;
    }
    public static class AssemblyReloadEvents
    {
        public static event Action beforeAssemblyReload;
        public static void Reload() => beforeAssemblyReload?.Invoke();
    }
    public static class EditorApplication
    {
        public static event Action update, delayCall, quitting;
        public static event Action<PauseState> pauseStateChanged;
        public static event Action<PlayModeStateChange> playModeStateChanged;
        public static bool isPlaying, isCompiling, isUpdating;
        public static bool isPlayingOrWillChangePlaymode => isPlaying;
        public static double timeSinceStartup;
        private static bool paused;
        public static bool isPaused
        {
            get => paused;
            set
            {
                if (paused == value) return;
                paused = value;
                pauseStateChanged?.Invoke(value ? PauseState.Paused : PauseState.Unpaused);
            }
        }
        public static void Tick(double now) { timeSinceStartup = now; update?.Invoke(); }
        public static void FlushDelay()
        {
            Action callbacks = delayCall;
            delayCall = null;
            callbacks?.Invoke();
        }
        public static void ExitPlayMode()
        {
            isPlaying = false;
            playModeStateChanged?.Invoke(PlayModeStateChange.ExitingPlayMode);
        }
        public static void Quit() => quitting?.Invoke();
    }
}

namespace UnityEditorInternal
{
    public static class ProfilerDriver
    {
        public static bool enabled, profileEditor, localEditor = true;
        public static int connectedProfiler = 1;
        public static readonly SortedDictionary<int, double> Frames = new();
        public static readonly Dictionary<int, double> FrameStarts = new();
        public static readonly HashSet<int> Unavailable = new();
        public static int reads;
        public static int firstFrameIndex => Frames.Count == 0 ? -1 : Frames.Keys.First();
        public static int lastFrameIndex => Frames.Count == 0 ? -1 : Frames.Keys.Last();
        public static bool IsConnectionEditor() => localEditor;
        public static Frame GetRawFrameDataView(int index, int thread)
        {
            reads++;
            return new Frame
            {
                valid = Frames.ContainsKey(index) && !Unavailable.Contains(index),
                frameTimeMs = Frames.GetValueOrDefault(index),
                frameStartTimeMs = FrameStarts.TryGetValue(index, out double start) ? start :
                    Frames.Where(pair => pair.Key < index && double.IsFinite(pair.Value) && pair.Value > 0d).Sum(pair => pair.Value)
            };
        }
        public struct Frame : IDisposable
        {
            public bool valid;
            public double frameTimeMs, frameStartTimeMs;
            public void Dispose() { }
        }
    }
}

namespace FlatWorld.Automation
{
    internal static class ProfilerSnapshotToolbar
    {
        internal static bool IsExporting;
        internal static int Exports, SelectedFrame;
        internal static bool ThrowOnExport;
        internal static Action OnExport;
        internal static string Metadata;
        internal static string ExportFrame(int frame, int radius, Func<string, string, int, string[]> extra)
        {
            if (!UnityEditor.EditorApplication.isPaused) throw new Exception("Export happened before editor pause!");
            if (UnityEditorInternal.ProfilerDriver.enabled) throw new Exception("Trigger buffer was not frozen!");
            if (ThrowOnExport) throw new IOException("Simulated export failure");
            SelectedFrame = frame;
            Exports++;
            string directory = Path.Combine(Path.GetTempPath(), "FlatWorld-ProfilerAutoTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string[] files = extra(directory, "test-snapshot", frame + 1);
                if (files.Length != 1 || files[0] != "auto-trigger.json") throw new Exception("Missing trigger metadata");
                Metadata = File.ReadAllText(Path.Combine(directory, files[0]));
                OnExport?.Invoke();
            }
            finally { Directory.Delete(directory, true); }
            return directory;
        }
    }
}
