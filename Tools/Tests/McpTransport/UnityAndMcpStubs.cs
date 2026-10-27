using System.Collections.Concurrent;

#region 非 Unity 宿主适配

// 仅替代编辑器入口与命令宿主，WebSocket 收发、命令排队和取消使用生产源码。
namespace UnityEngine
{
    internal static class Application
    {
        public static string unityVersion => "transport-test";
        public static string dataPath => Directory.GetCurrentDirectory() + "/Assets";
    }
}

namespace UnityEditor
{
    internal static class EditorPrefs
    {
        public static string GetString(string key, string fallback) => fallback;
    }
}

namespace MCPForUnity.Editor.Constants
{
    internal static class EditorPrefKeys { public const string ApiKey = "test"; }
    internal static class AuthConstants { public const string ApiKeyHeader = "X-Test-Key"; }
}

namespace MCPForUnity.Editor.Helpers
{
    internal static class McpLog
    {
        public static readonly ConcurrentQueue<string> Warnings = new();
        public static void Warn(string message) => Warnings.Enqueue(message);
        public static void Error(string message) => Warnings.Enqueue(message);
        public static void Info(string message, bool ignored = true) { }
        public static void Debug(string message) { }
    }

    internal static class ProjectIdentityUtility
    {
        public static string GetProjectName() => "test";
        public static string GetProjectHash() => "test";
        public static void SetSessionId(string session) { }
    }

    internal static class HttpEndpointUtility
    {
        public static bool IsRemoteScope() => false;
        public static bool IsCurrentRemoteUrlAllowed(out string error) { error = null; return true; }
        public static string GetBaseUrl() => "http://127.0.0.1";
    }
}

namespace MCPForUnity.Editor.Services
{
    public interface IToolDiscoveryService { List<ToolMetadata> GetEnabledTools(); }
    public sealed class ToolMetadata
    {
        public string Name, Description, PollAction, Group;
        public bool StructuredOutput, RequiresPolling;
        public int MaxPollSeconds;
        public List<ToolParameterMetadata> Parameters;
    }
    public sealed class ToolParameterMetadata
    {
        public string Name, Description, Type, DefaultValue;
        public bool Required;
    }
}

namespace MCPForUnity.Editor.Services.Transport
{
    internal static class TransportCommandDispatcher
    {
        public static Func<string, CancellationToken, Task<string>> CommandHandler;
        public static Task<List<MCPForUnity.Editor.Services.ToolMetadata>> PendingToolDiscovery;
        public static Action ToolDiscoveryRequested;
        public static Task<string> ExecuteCommandJsonAsync(string json, CancellationToken token)
            => CommandHandler(json, token).WaitAsync(token);
        public static Task<T> RunOnMainThreadAsync<T>(Func<T> function, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (typeof(T) == typeof(List<MCPForUnity.Editor.Services.ToolMetadata>) && PendingToolDiscovery != null)
            {
                ToolDiscoveryRequested?.Invoke();
                return (Task<T>)(object)PendingToolDiscovery.WaitAsync(token);
            }
            return Task.FromResult(function());
        }
    }
}

#endregion
