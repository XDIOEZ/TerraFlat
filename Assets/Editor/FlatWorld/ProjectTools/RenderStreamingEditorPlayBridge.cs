#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.RenderStreaming;
using Unity.RenderStreaming.Editor;
using Debug = UnityEngine.Debug;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

namespace FlatWorld.EditorTools
{
    /// <summary>
    /// 为开发期 Editor Play 自动准备 Unity Render Streaming：补齐后台输入设置、启动官方 WebApp，
    /// 并输出局域网手机可直接访问的 Receiver 地址。WebApp 二进制只缓存到 Library，不进入仓库。
    /// </summary>
    [InitializeOnLoad]
    internal static class RenderStreamingEditorPlayBridge
    {
        #region 配置

        private const int PreferredWebPort = 8080;
        private const int WebPortSearchCount = 20;
        private const string WebServerDownloadUrl =
            "https://github.com/Unity-Technologies/UnityRenderStreaming/releases/download/3.1.0-exp.7/webserver.exe";
        private const string CacheDirectory = "Library/FlatWorld/RenderStreaming";
        private const string WebServerFileName = "webserver.exe";
        private const string ReceiverPath = "/receiver/index.html";
        private const string InputSettingsAssetPath = "Assets/Settings/FlatWorldInputSystemSettings.inputsettings.asset";
        private const string RenderStreamingSettingsAssetPath = "Assets/Settings/FlatWorldRenderStreamingSettings.asset";
        private const string AutoStreamingPreferenceKey = "FlatWorld.RenderStreaming.AutoPlayEnabled";
        private const float StreamingFrameRate = 60f;
        private const int StreamingFrameRateConfigureMaxAttempts = 120;
        private const int WebServerReadyTimeoutMilliseconds = 3000;

        private static Process ownedWebServerProcess;
        private static int streamingFrameRateConfigureAttempts;
        private static int activeWebPort = PreferredWebPort;
        private static bool AutoStreamingEnabled => EditorPrefs.GetBool(AutoStreamingPreferenceKey, false);

        #endregion

        #region 初始化与生命周期

        static RenderStreamingEditorPlayBridge()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
            EditorApplication.delayCall += EnsurePlayModePrerequisites;
            EditorApplication.delayCall += ApplyAutomaticStreamingPreference;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    EnsurePlayModePrerequisites();
                    if (AutoStreamingEnabled)
                        EnsureWebServerRunning();
                    else
                        EnsureProjectRenderStreamingSettings(activeWebPort, automaticStreaming: false);
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    ApplyAutomaticStreamingPreference();
                    if (AutoStreamingEnabled)
                    {
                        QueueStreamingFrameRateConfiguration();
                        LogMobileAccessUrl();
                    }
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    EditorApplication.update -= ConfigureStreamingFrameRateWhenReady;
                    streamingFrameRateConfigureAttempts = 0;
                    break;
            }
        }

        private static void OnEditorQuitting()
        {
            StopOwnedWebServer();
        }

        #endregion

        #region 菜单

        [MenuItem("Tools/Render Streaming/启动手机浏览器测试服务器")]
        private static void StartWebServerFromMenu()
        {
            EnsurePlayModePrerequisites();
            EnsureWebServerRunning();
            LogMobileAccessUrl();
        }

        [MenuItem("Tools/Render Streaming/Play Mode 自动手机串流")]
        private static void ToggleAutoStreaming()
        {
            bool enabled = !AutoStreamingEnabled;
            EditorPrefs.SetBool(AutoStreamingPreferenceKey, enabled);

            if (enabled)
            {
                EnsurePlayModePrerequisites();
                EnsureWebServerRunning();
            }
            else
            {
                EditorApplication.update -= ConfigureStreamingFrameRateWhenReady;
                streamingFrameRateConfigureAttempts = 0;
                EnsureProjectRenderStreamingSettings(activeWebPort, automaticStreaming: false);
            }

            ApplyAutomaticStreamingPreference();
            Debug.Log($"[RenderStreaming] Play Mode 自动手机串流：{(enabled ? "已启用" : "已关闭（默认性能模式）")}");
        }

        [MenuItem("Tools/Render Streaming/Play Mode 自动手机串流", true)]
        private static bool ValidateToggleAutoStreaming()
        {
            Menu.SetChecked("Tools/Render Streaming/Play Mode 自动手机串流", AutoStreamingEnabled);
            return true;
        }

        [MenuItem("Tools/Render Streaming/复制手机测试地址")]
        private static void CopyMobileAccessUrl()
        {
            string url = GetMobileAccessUrl();
            EditorGUIUtility.systemCopyBuffer = url;
            Debug.Log($"[RenderStreaming] 已复制手机测试地址：{url}");
        }

        [MenuItem("Tools/Render Streaming/停止本工具启动的测试服务器")]
        private static void StopWebServerFromMenu()
        {
            StopOwnedWebServer();
        }

        #endregion

        #region 串流画质

        /// <summary>
        /// 普通 Editor Play 默认关闭 Automatic Streaming，避免 Screen 源每帧执行整屏捕获与纹理转换。
        /// 只有开发者显式打开手机串流开关时才创建 Sender。
        /// </summary>
        private static void ApplyAutomaticStreamingPreference()
        {
            bool enabled = AutoStreamingEnabled;
            if (!enabled)
                EnsureProjectRenderStreamingSettings(activeWebPort, automaticStreaming: false);

            if (!EditorApplication.isPlaying)
                return;

            Unity.RenderStreaming.RenderStreaming.AutomaticStreaming = enabled;
        }

        /// <summary>等待 Automatic Streaming 创建 Screen Sender 后，将编码目标帧率固定为 60 FPS。</summary>
        private static void QueueStreamingFrameRateConfiguration()
        {
            streamingFrameRateConfigureAttempts = 0;
            EditorApplication.update -= ConfigureStreamingFrameRateWhenReady;
            EditorApplication.update += ConfigureStreamingFrameRateWhenReady;
        }

        private static void ConfigureStreamingFrameRateWhenReady()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.update -= ConfigureStreamingFrameRateWhenReady;
                return;
            }

            Unity.RenderStreaming.VideoStreamSender[] senders =
                Resources.FindObjectsOfTypeAll<Unity.RenderStreaming.VideoStreamSender>();
            Unity.RenderStreaming.VideoStreamSender[] screenSenders = senders
                .Where(sender => sender != null &&
                                 sender.gameObject.scene.IsValid() &&
                                 sender.gameObject.scene.isLoaded &&
                                 sender.source == Unity.RenderStreaming.VideoStreamSource.Screen)
                .ToArray();

            if (screenSenders.Length > 0)
            {
                try
                {
                    foreach (Unity.RenderStreaming.VideoStreamSender sender in screenSenders)
                        sender.SetFrameRate(StreamingFrameRate);

                    EditorApplication.update -= ConfigureStreamingFrameRateWhenReady;
                    Debug.Log($"[RenderStreaming] Editor Play 视频串流目标帧率已设置为 {StreamingFrameRate:0} FPS。");
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[RenderStreaming] 设置 {StreamingFrameRate:0} FPS 失败，将继续重试：{exception.Message}");
                }
            }

            streamingFrameRateConfigureAttempts++;
            if (streamingFrameRateConfigureAttempts < StreamingFrameRateConfigureMaxAttempts)
                return;

            EditorApplication.update -= ConfigureStreamingFrameRateWhenReady;
            Debug.LogWarning(
                $"[RenderStreaming] 在 {StreamingFrameRateConfigureMaxAttempts} 次 Editor 更新内未找到 Screen VideoStreamSender，" +
                $"未能应用 {StreamingFrameRate:0} FPS 设置。"
            );
        }

        #endregion

        #region Editor Play 前置条件

        /// <summary>应用 Render Streaming 官方 Wizard 对 Editor Play 的必要设置。</summary>
        private static void EnsurePlayModePrerequisites()
        {
            bool changed = false;

            if (!PlayerSettings.runInBackground)
            {
                PlayerSettings.runInBackground = true;
                changed = true;
            }

            InputSettings settings = UnityInputSystem.settings;
            string currentPath = settings != null ? AssetDatabase.GetAssetPath(settings) : string.Empty;
            if (settings == null || string.IsNullOrEmpty(currentPath) ||
                !currentPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                settings = AssetDatabase.LoadAssetAtPath<InputSettings>(InputSettingsAssetPath);
                if (settings == null)
                {
                    EnsureAssetDirectory(InputSettingsAssetPath);
                    settings = ScriptableObject.CreateInstance<InputSettings>();
                    AssetDatabase.CreateAsset(settings, InputSettingsAssetPath);
                }

                UnityInputSystem.settings = settings;
                changed = true;
            }

            if (settings.backgroundBehavior != InputSettings.BackgroundBehavior.IgnoreFocus)
            {
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                changed = true;
            }

            if (settings.editorInputBehaviorInPlayMode !=
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView)
            {
                settings.editorInputBehaviorInPlayMode =
                    InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                changed = true;
            }

            if (!changed)
                return;

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            Debug.Log("[RenderStreaming] 已应用 Editor Play 后台运行与远程输入设置。");
        }

        /// <summary>
        /// 使用项目自有设置承载 Automatic Streaming 与信令地址，禁止把 Editor 会话配置写回不可变 PackageCache。
        /// </summary>
        private static void EnsureProjectRenderStreamingSettings(int webPort, bool automaticStreaming)
        {
            EnsureAssetDirectory(RenderStreamingSettingsAssetPath);
            RenderStreamingSettings settings =
                AssetDatabase.LoadAssetAtPath<RenderStreamingSettings>(RenderStreamingSettingsAssetPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<RenderStreamingSettings>();
                AssetDatabase.CreateAsset(settings, RenderStreamingSettingsAssetPath);
            }

            bool changed = false;
            if (settings.automaticStreaming != automaticStreaming)
            {
                settings.automaticStreaming = automaticStreaming;
                changed = true;
            }

            string signalingUrl = $"ws://127.0.0.1:{webPort}";
            WebSocketSignalingSettings current = settings.signalingSettings as WebSocketSignalingSettings;
            if (current == null || !string.Equals(current.url, signalingUrl, StringComparison.OrdinalIgnoreCase))
            {
                IceServer[] iceServers = current?.iceServers?.ToArray();
                if (iceServers == null || iceServers.Length == 0)
                    iceServers = new WebSocketSignalingSettings().iceServers.ToArray();
                settings.signalingSettings = new WebSocketSignalingSettings(signalingUrl, iceServers);
                changed = true;
            }

            // 官方 Editor API 会同时把项目设置登记到 EditorBuildSettings；后续 Domain Reload 将继续使用该资产。
            RenderStreamingEditor.SetRenderStreamingSettings(settings);
            if (!changed)
                return;

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            Debug.Log($"[RenderStreaming] 项目信令已配置为 {signalingUrl}。");
        }

        private static void EnsureAssetDirectory(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory) || AssetDatabase.IsValidFolder(directory))
                return;

            string parent = Path.GetDirectoryName(directory)?.Replace('\\', '/');
            string folderName = Path.GetFileName(directory);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureAssetDirectory(parent + "/placeholder.asset");

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(directory))
                AssetDatabase.CreateFolder(parent, folderName);
        }

        #endregion

        #region WebApp 生命周期

        private static void EnsureWebServerRunning()
        {
            int webPort = ResolveWebPort();
            if (webPort <= 0)
                return;

            activeWebPort = webPort;
            // 先登记正确 URL 但保持自动串流关闭；只有官方 WebApp 真正可访问后才允许进入 Play 时连接。
            EnsureProjectRenderStreamingSettings(webPort, automaticStreaming: false);
            if (IsRenderStreamingWebAppAvailable(webPort))
            {
                EnsureProjectRenderStreamingSettings(webPort, automaticStreaming: true);
                return;
            }

            if (IsPortListening(webPort))
            {
                Debug.LogError($"[RenderStreaming] TCP {webPort} 已被其它程序占用，无法启动手机测试服务器。");
                return;
            }

            string executablePath = EnsureWebServerExecutable();
            if (string.IsNullOrEmpty(executablePath))
                return;

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = $"-p {webPort}",
                    WorkingDirectory = Path.GetDirectoryName(executablePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                ownedWebServerProcess = Process.Start(startInfo);
                if (!WaitForRenderStreamingWebApp(webPort, WebServerReadyTimeoutMilliseconds))
                {
                    Debug.LogError(
                        $"[RenderStreaming] 官方 WebApp 已启动但未在 {WebServerReadyTimeoutMilliseconds} ms 内就绪，" +
                        $"端口 {webPort}。"
                    );
                    return;
                }
                EnsureProjectRenderStreamingSettings(webPort, automaticStreaming: true);
                Debug.Log($"[RenderStreaming] 手机测试服务器已启动：{GetMobileAccessUrl()}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[RenderStreaming] 启动官方 WebApp 失败：{exception.Message}");
            }
        }

        private static string EnsureWebServerExecutable()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
            string cacheDirectory = Path.Combine(projectRoot, CacheDirectory.Replace('/', Path.DirectorySeparatorChar));
            string executablePath = Path.Combine(cacheDirectory, WebServerFileName);
            if (File.Exists(executablePath) && new FileInfo(executablePath).Length > 1024 * 1024)
                return executablePath;

            try
            {
                Directory.CreateDirectory(cacheDirectory);
                string temporaryPath = executablePath + ".download";
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);

                Debug.Log("[RenderStreaming] 首次使用，正在下载 Unity 官方 WebApp……");
                using (WebClient client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "TerraFlat-Unity-Editor";
                    client.DownloadFile(WebServerDownloadUrl, temporaryPath);
                }

                if (File.Exists(executablePath))
                    File.Delete(executablePath);
                File.Move(temporaryPath, executablePath);
                return executablePath;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[RenderStreaming] 下载 Unity 官方 WebApp 失败：{exception.Message}");
                return null;
            }
        }

        /// <summary>优先复用项目标准端口；被其它程序占用时选择后续空闲端口，不再抢占系统 HTTP 端口。</summary>
        private static int ResolveWebPort()
        {
            if (IsRenderStreamingWebAppAvailable(activeWebPort))
                return activeWebPort;

            for (int offset = 0; offset < WebPortSearchCount; offset++)
            {
                int port = PreferredWebPort + offset;
                if (IsRenderStreamingWebAppAvailable(port) || !IsPortListening(port))
                    return port;
            }

            Debug.LogError(
                $"[RenderStreaming] TCP {PreferredWebPort}~{PreferredWebPort + WebPortSearchCount - 1} 均被占用，" +
                "无法为手机测试服务器选择端口。"
            );
            return -1;
        }

        private static bool WaitForRenderStreamingWebApp(int port, int timeoutMilliseconds)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                if (IsRenderStreamingWebAppAvailable(port))
                    return true;
                if (ownedWebServerProcess == null || ownedWebServerProcess.HasExited)
                    return false;
                Thread.Sleep(50);
            }
            return IsRenderStreamingWebAppAvailable(port);
        }

        private static bool IsRenderStreamingWebAppAvailable(int port)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create($"http://127.0.0.1:{port}{ReceiverPath}");
                request.Method = "GET";
                request.Timeout = 350;
                request.ReadWriteTimeout = 350;
                using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                return response.StatusCode == HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPortListening(int port)
        {
            try
            {
                return IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Any(endpoint => endpoint.Port == port);
            }
            catch
            {
                return false;
            }
        }

        private static void StopOwnedWebServer()
        {
            Process process = ownedWebServerProcess;
            ownedWebServerProcess = null;
            if (process == null)
                return;

            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[RenderStreaming] 停止测试服务器失败：{exception.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        #endregion

        #region 地址发现

        private static void LogMobileAccessUrl()
        {
            Debug.Log(
                $"[RenderStreaming] 手机与电脑连接同一局域网后，在浏览器打开：{GetMobileAccessUrl()} " +
                "然后点击页面中央的播放按钮即可控制当前 Editor Play。Android Chrome 可直接使用 HTTP；iOS Safari 需要 HTTPS。"
            );
        }

        private static string GetMobileAccessUrl()
        {
            IPAddress address = GetPreferredLanAddress();
            string host = address?.ToString() ?? "127.0.0.1";
            string portSuffix = activeWebPort == 80 ? string.Empty : $":{activeWebPort}";
            return $"http://{host}{portSuffix}{ReceiverPath}";
        }

        private static IPAddress GetPreferredLanAddress()
        {
            IPAddress[] candidates;
            try
            {
                candidates = Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork &&
                                      !IPAddress.IsLoopback(address) &&
                                      !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    .ToArray();
            }
            catch
            {
                return null;
            }

            return candidates
                .OrderByDescending(GetLanAddressScore)
                .FirstOrDefault();
        }

        private static int GetLanAddressScore(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            if (bytes[0] == 192 && bytes[1] == 168)
                return 30;
            if (bytes[0] == 10)
                return 20;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return 20;
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                return 5;
            return 10;
        }

        #endregion
    }
}
#endif
