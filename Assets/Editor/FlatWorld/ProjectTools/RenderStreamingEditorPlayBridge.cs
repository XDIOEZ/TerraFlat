#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
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

        private const int WebPort = 80;
        private const string WebServerDownloadUrl =
            "https://github.com/Unity-Technologies/UnityRenderStreaming/releases/download/3.1.0-exp.7/webserver.exe";
        private const string CacheDirectory = "Library/FlatWorld/RenderStreaming";
        private const string WebServerFileName = "webserver.exe";
        private const string ReceiverPath = "/receiver/index.html";
        private const string InputSettingsAssetPath = "Assets/Settings/FlatWorldInputSystemSettings.inputsettings.asset";
        private const float StreamingFrameRate = 60f;
        private const int StreamingFrameRateConfigureMaxAttempts = 120;

        private static Process ownedWebServerProcess;
        private static int streamingFrameRateConfigureAttempts;

        #endregion

        #region 初始化与生命周期

        static RenderStreamingEditorPlayBridge()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
            EditorApplication.delayCall += EnsurePlayModePrerequisites;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    EnsurePlayModePrerequisites();
                    EnsureWebServerRunning();
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    // 包内默认设置已开启 Automatic Streaming；这里再显式保证当前 Editor 会话处于开启状态。
                    Unity.RenderStreaming.RenderStreaming.AutomaticStreaming = true;
                    QueueStreamingFrameRateConfiguration();
                    LogMobileAccessUrl();
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
            AssetDatabase.SaveAssets();
            Debug.Log("[RenderStreaming] 已应用 Editor Play 后台运行与远程输入设置。");
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
            if (IsRenderStreamingWebAppAvailable())
                return;

            if (IsPortListening(WebPort))
            {
                Debug.LogError($"[RenderStreaming] TCP {WebPort} 已被其它程序占用，无法启动手机测试服务器。");
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
                    Arguments = $"-p {WebPort}",
                    WorkingDirectory = Path.GetDirectoryName(executablePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                ownedWebServerProcess = Process.Start(startInfo);
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

        private static bool IsRenderStreamingWebAppAvailable()
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create($"http://127.0.0.1:{WebPort}{ReceiverPath}");
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
            string portSuffix = WebPort == 80 ? string.Empty : $":{WebPort}";
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
