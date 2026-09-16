using System;
using System.IO;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// GamePlayMCP 的低频视觉观察工具。
    /// 在真实帧末抓取包含 UI 的完整 Game View，并固定写入 Library，避免测试证据污染项目资源与 Git 工作区。
    /// </summary>
    [McpForUnityTool(
        "gameplay_screenshot",
        Description = "Capture the live composited FlatWorld Game View (including UI) for visual bug discovery. Use as a low-frequency supplement to gameplay_observe; screenshots are stored under Library/FlatWorldGameplayMCP/Screenshots so autonomous testing does not dirty Assets.",
        Group = "core")]
    public static class GameplayScreenshotTool
    {
        private const string OutputFolder = "Library/FlatWorldGameplayMCP/Screenshots";
        private const int DefaultMaxResolution = 640;
        private const int MinMaxResolution = 256;
        private const int MaxMaxResolution = 1280;

        public sealed class Parameters
        {
            [ToolParameter("Return the screenshot inline so the Agent can visually inspect it.", Required = false, DefaultValue = "true")]
            public bool includeImage { get; set; }

            [ToolParameter("Maximum screenshot long-edge resolution. Defaults to 640 and is clamped to 256-1280.", Required = false, DefaultValue = "640")]
            public int maxResolution { get; set; }

            [ToolParameter("Optional evidence file name. The screenshot is always written under the GamePlayMCP Library folder.", Required = false)]
            public string fileName { get; set; }
        }

        /// <summary>在真实帧末异步捕获完整 Game View，作为结构化观察之外的视觉 Bug 证据。</summary>
        public static async Task<object> HandleCommand(JObject parameters)
        {
            if (!Application.isPlaying)
            {
                return new ErrorResponse(
                    "gameplay_screenshot requires Play Mode. Start the game first, then capture the live Game View.");
            }

            bool includeImage = ReadBool(parameters, "includeImage", "include_image", true);
            int maxResolution = Mathf.Clamp(
                ReadInt(parameters, "maxResolution", "max_resolution", DefaultMaxResolution),
                MinMaxResolution,
                MaxMaxResolution);

            var completion = new TaskCompletionSource<Texture2D>();
            bool timedOut = false;
            GameplayScreenshotCapturer.Begin(texture =>
            {
                if (timedOut)
                {
                    if (texture != null)
                        UnityEngine.Object.Destroy(texture);
                    return;
                }

                completion.TrySetResult(texture);
            });

            Task completed = await Task.WhenAny(completion.Task, Task.Delay(5000));
            if (completed != completion.Task)
            {
                timedOut = true;
                return new ErrorResponse(
                    "gameplay_screenshot timed out waiting for WaitForEndOfFrame. The game may be paused or not rendering frames.");
            }

            Texture2D texture = await completion.Task;
            if (texture == null)
                return new ErrorResponse("gameplay_screenshot failed: ScreenCapture returned no texture.");

            Texture2D inlineTexture = null;
            try
            {
                string requestedFileName = ReadString(parameters, "fileName", "file_name");
                string fileName = BuildSafeFileName(requestedFileName);
                string projectRoot = Directory.GetCurrentDirectory();
                string folderPath = Path.Combine(projectRoot, OutputFolder.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(folderPath);

                string fullPath = BuildUniquePath(folderPath, fileName);
                byte[] fullPng = texture.EncodeToPNG();
                File.WriteAllBytes(fullPath, fullPng);

                string imageBase64 = null;
                int imageWidth = 0;
                int imageHeight = 0;
                if (includeImage)
                {
                    inlineTexture = DownscaleIfNeeded(texture, maxResolution);
                    Texture2D encodedTexture = inlineTexture ?? texture;
                    byte[] inlinePng = ReferenceEquals(encodedTexture, texture)
                        ? fullPng
                        : encodedTexture.EncodeToPNG();
                    imageBase64 = Convert.ToBase64String(inlinePng);
                    imageWidth = encodedTexture.width;
                    imageHeight = encodedTexture.height;
                }

                string relativePath = fullPath
                    .Replace('\\', '/')
                    .Substring(projectRoot.Replace('\\', '/').TrimEnd('/').Length)
                    .TrimStart('/');

                var data = new JObject
                {
                    ["path"] = relativePath,
                    ["captureSource"] = "game_view",
                    ["imageWidth"] = imageWidth,
                    ["imageHeight"] = imageHeight
                };

                if (includeImage && imageBase64 != null)
                    data["imageBase64"] = imageBase64;

                return new SuccessResponse(
                    $"FlatWorld gameplay screenshot captured to '{relativePath}'.",
                    data);
            }
            finally
            {
                if (inlineTexture != null)
                    UnityEngine.Object.Destroy(inlineTexture);
                UnityEngine.Object.Destroy(texture);
            }
        }

        /// <summary>构造安全且始终为 PNG 的截图文件名。</summary>
        private static string BuildSafeFileName(string requestedFileName)
        {
            string raw = string.IsNullOrWhiteSpace(requestedFileName)
                ? $"gameplay-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png"
                : Path.GetFileName(requestedFileName.Trim());

            foreach (char invalid in Path.GetInvalidFileNameChars())
                raw = raw.Replace(invalid, '_');

            if (!raw.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                raw += ".png";

            return raw;
        }

        /// <summary>避免同一毫秒内的证据图覆盖。</summary>
        private static string BuildUniquePath(string folderPath, string fileName)
        {
            string fullPath = Path.Combine(folderPath, fileName);
            if (!File.Exists(fullPath))
                return fullPath;

            string stem = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(folderPath, $"{stem}-{i}{extension}");
                if (!File.Exists(candidate))
                    return candidate;
            }

            throw new IOException($"Unable to allocate a unique gameplay screenshot file name for '{fileName}'.");
        }

        /// <summary>仅在图片超过上下文友好的最大边长时缩小副本。</summary>
        private static Texture2D DownscaleIfNeeded(Texture2D source, int maxResolution)
        {
            int longest = Mathf.Max(source.width, source.height);
            if (longest <= maxResolution)
                return null;

            float scale = maxResolution / (float)longest;
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));

            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                var result = new Texture2D(width, height, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply(false, false);
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        /// <summary>读取布尔参数，兼容 camelCase 与 snake_case。</summary>
        private static bool ReadBool(JObject parameters, string camelKey, string snakeKey, bool fallback)
        {
            JToken token = parameters?[camelKey] ?? parameters?[snakeKey];
            return bool.TryParse(token?.ToString(), out bool value) ? value : fallback;
        }

        /// <summary>读取整数参数，兼容 camelCase 与 snake_case。</summary>
        private static int ReadInt(JObject parameters, string camelKey, string snakeKey, int fallback)
        {
            JToken token = parameters?[camelKey] ?? parameters?[snakeKey];
            return int.TryParse(token?.ToString(), out int value) ? value : fallback;
        }

        /// <summary>读取字符串参数，兼容 camelCase 与 snake_case。</summary>
        private static string ReadString(JObject parameters, string camelKey, string snakeKey)
        {
            return (parameters?[camelKey] ?? parameters?[snakeKey])?.ToString();
        }
    }

    /// <summary>只负责在真实帧末抓取一次 Game View，并在完成后自销毁。</summary>
    internal sealed class GameplayScreenshotCapturer : MonoBehaviour
    {
        private Action<Texture2D> onComplete;

        /// <summary>创建隐藏捕获器，实际抓屏发生在下一次 WaitForEndOfFrame。</summary>
        public static void Begin(Action<Texture2D> callback)
        {
            var gameObject = new GameObject("__FlatWorld_GameplayScreenshot__")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            var capturer = gameObject.AddComponent<GameplayScreenshotCapturer>();
            capturer.onComplete = callback;
        }

        /// <summary>等待合成帧完成后抓取包含 UI 的最终画面。</summary>
        private System.Collections.IEnumerator Start()
        {
            yield return new WaitForEndOfFrame();

            Texture2D texture = null;
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[GamePlayMCP] Screenshot capture failed: {exception.Message}");
            }

            onComplete?.Invoke(texture);
            Destroy(gameObject);
        }
    }
}
