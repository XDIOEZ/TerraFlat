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
        private const string OwnedWebServerProcessIdPreferenceKey = "FlatWorld.RenderStreaming.OwnedWebServerProcessId";
        private const string PreferredSpeedVideoCodecMimeType = "video/H264";
        private const float StreamingFrameRate = 60f;
        private const int StreamingFrameRateConfigureMaxAttempts = 120;
        private const int WebServerReadyTimeoutMilliseconds = 3000;

        private static Process ownedWebServerProcess;
        private static int streamingSenderConfigureAttempts;
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
                        QueueStreamingSenderConfiguration();
                        LogMobileAccessUrl();
                    }
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    EditorApplication.update -= ConfigureStreamingSenderWhenReady;
                    streamingSenderConfigureAttempts = 0;
                    // 自动串流是否启用由本机 EditorPrefs 决定；退出 Play 后把仓库内设置资产恢复为关闭，避免开发偏好污染 Git。
                    EnsureProjectRenderStreamingSettings(activeWebPort, automaticStreaming: false);
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
            // “启动测试服务器”本身就是一次显式开发者选择；同步打开自动串流，避免下一次进入 Play 时又被性能模式关掉。
            EditorPrefs.SetBool(AutoStreamingPreferenceKey, true);
            EnsurePlayModePrerequisites();
            EnsureWebServerRunning();
            ApplyAutomaticStreamingPreference();
            if (EditorApplication.isPlaying)
                QueueStreamingSenderConfiguration();
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
                EditorApplication.update -= ConfigureStreamingSenderWhenReady;
                streamingSenderConfigureAttempts = 0;
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

        /// <summary>等待 Automatic Streaming 创建 Screen Sender 后，应用速度优先的视频编码参数。</summary>
        private static void QueueStreamingSenderConfiguration()
        {
            streamingSenderConfigureAttempts = 0;
            EditorApplication.update -= ConfigureStreamingSenderWhenReady;
            EditorApplication.update += ConfigureStreamingSenderWhenReady;
        }

        private static void ConfigureStreamingSenderWhenReady()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.update -= ConfigureStreamingSenderWhenReady;
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
                    VideoCodecInfo preferredCodec = ResolveSpeedPreferredVideoCodec();
                    bool codecApplied = false;
                    foreach (Unity.RenderStreaming.VideoStreamSender sender in screenSenders)
                    {
                        // Codec 必须在连接开始前设置；已有连接或已有显式选择时只调整帧率，不强行覆盖。
                        if (!sender.isPlaying && sender.codec == null && preferredCodec != null)
                        {
                            sender.SetCodec(preferredCodec);
                            codecApplied = true;
                        }

                        sender.SetFrameRate(StreamingFrameRate);
                    }

                    EditorApplication.update -= ConfigureStreamingSenderWhenReady;
                    string codecDescription = preferredCodec == null
                        ? "默认 WebRTC 协商"
                        : $"{preferredCodec.mimeType} / {preferredCodec.codecImplementation}";
                    Debug.Log(
                        $"[RenderStreaming] Editor Play 视频串流已应用速度优先配置：{StreamingFrameRate:0} FPS，" +
                        $"编码={codecDescription}{(codecApplied ? "" : "（未覆盖已有连接/显式编码）")}。"
                    );
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[RenderStreaming] 应用速度优先串流配置失败，将继续重试：{exception.Message}");
                }
            }

            streamingSenderConfigureAttempts++;
            if (streamingSenderConfigureAttempts < StreamingFrameRateConfigureMaxAttempts)
                return;

            EditorApplication.update -= ConfigureStreamingSenderWhenReady;
            Debug.LogWarning(
                $"[RenderStreaming] 在 {StreamingFrameRateConfigureMaxAttempts} 次 Editor 更新内未找到 Screen VideoStreamSender，" +
                "未能应用速度优先串流配置。"
            );
        }

        /// <summary>优先使用浏览器兼容性较好的 H.264 Constrained Baseline；不可用时保留包默认协商。</summary>
        private static VideoCodecInfo ResolveSpeedPreferredVideoCodec()
        {
            VideoCodecInfo[] codecs = Unity.RenderStreaming.VideoStreamSender.GetAvailableCodecs()
                .Where(codec => codec != null)
                .ToArray();

            H264CodecInfo constrainedBaseline = codecs
                .OfType<H264CodecInfo>()
                .FirstOrDefault(codec => codec.profile == H264Profile.ConstrainedBaseline);
            if (constrainedBaseline != null)
                return constrainedBaseline;

            return codecs.FirstOrDefault(codec =>
                string.Equals(codec.mimeType, PreferredSpeedVideoCodecMimeType, StringComparison.OrdinalIgnoreCase));
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
                EnsureProjectRenderStreamingSettings(
                    webPort,
                    automaticStreaming: AutoStreamingEnabled && EditorApplication.isPlayingOrWillChangePlaymode
                );
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
                RememberOwnedWebServerProcess(ownedWebServerProcess);
                if (!WaitForRenderStreamingWebApp(webPort, WebServerReadyTimeoutMilliseconds))
                {
                    Debug.LogError(
                        $"[RenderStreaming] 官方 WebApp 已启动但未在 {WebServerReadyTimeoutMilliseconds} ms 内就绪，" +
                        $"端口 {webPort}。"
                    );
                    return;
                }
                EnsureProjectRenderStreamingSettings(
                    webPort,
                    automaticStreaming: AutoStreamingEnabled && EditorApplication.isPlayingOrWillChangePlaymode
                );
                Debug.Log($"[RenderStreaming] 手机测试服务器已启动：{GetMobileAccessUrl()}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[RenderStreaming] 启动官方 WebApp 失败：{exception.Message}");
            }
        }

        private static string EnsureWebServerExecutable()
        {
            string executablePath = GetWebServerExecutablePath();
            string cacheDirectory = Path.GetDirectoryName(executablePath);
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

        /// <summary>返回当前项目专属的官方 WebApp 缓存路径。</summary>
        private static string GetWebServerExecutablePath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
            string cacheDirectory = Path.Combine(projectRoot, CacheDirectory.Replace('/', Path.DirectorySeparatorChar));
            return Path.Combine(cacheDirectory, WebServerFileName);
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
            Process process = ownedWebServerProcess ?? TryRecoverOwnedWebServerProcess();
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
                EditorPrefs.DeleteKey(OwnedWebServerProcessIdPreferenceKey);
            }
        }

        /// <summary>记录本工具启动的 WebApp PID，使停止/退出逻辑能够跨 Domain Reload 恢复进程句柄。</summary>
        private static void RememberOwnedWebServerProcess(Process process)
        {
            if (process == null)
            {
                EditorPrefs.DeleteKey(OwnedWebServerProcessIdPreferenceKey);
                return;
            }

            try
            {
                EditorPrefs.SetInt(OwnedWebServerProcessIdPreferenceKey, process.Id);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[RenderStreaming] 无法记录测试服务器进程：{exception.Message}");
            }
        }

        /// <summary>仅在 PID 对应当前项目缓存的 webserver.exe 时恢复句柄，避免 PID 复用误杀其它进程。</summary>
        private static Process TryRecoverOwnedWebServerProcess()
        {
            if (!EditorPrefs.HasKey(OwnedWebServerProcessIdPreferenceKey))
                return null;

            int processId = EditorPrefs.GetInt(OwnedWebServerProcessIdPreferenceKey, -1);
            if (processId <= 0)
            {
                EditorPrefs.DeleteKey(OwnedWebServerProcessIdPreferenceKey);
                return null;
            }

            try
            {
                Process process = Process.GetProcessById(processId);
                string actualPath = process.MainModule?.FileName;
                string expectedPath = GetWebServerExecutablePath();
                if (string.IsNullOrEmpty(actualPath) ||
                    !string.Equals(
                        Path.GetFullPath(actualPath),
                        Path.GetFullPath(expectedPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    process.Dispose();
                    EditorPrefs.DeleteKey(OwnedWebServerProcessIdPreferenceKey);
                    return null;
                }

                return process;
            }
            catch
            {
                EditorPrefs.DeleteKey(OwnedWebServerProcessIdPreferenceKey);
                return null;
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
