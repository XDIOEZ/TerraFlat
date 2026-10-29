using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PuertsUnityMcp.Editor
{
    internal static class UnityMcpEditorMainWindowScreenshot
    {
        #region Unity 主窗口截图

        // 用独立进程采集本编辑器主窗口，不切换窗口焦点。
        internal static byte[] Capture(out ScreenshotInfo info)
        {
#if UNITY_EDITOR_WIN
            string root = UnityMcpPaths.ProjectRoot;
            string source = Path.Combine(root, "Tools", "ProfilerWindowCapture");
            string project = Path.Combine(source, "WindowCapture.csproj");
            string program = Path.Combine(source, "Program.cs");
            string buildRoot = Path.Combine(root, "Library", "PuertsUnityMcp", "ProfilerWindowCapture");
            string binaryRoot = Path.Combine(buildRoot, "bin");
            string exe = Path.Combine(binaryRoot, "WindowCapture.exe");
            Directory.CreateDirectory(buildRoot);
            if (!File.Exists(exe) || File.GetLastWriteTimeUtc(exe) < File.GetLastWriteTimeUtc(program) ||
                File.GetLastWriteTimeUtc(exe) < File.GetLastWriteTimeUtc(project))
            {
                string obj = Path.Combine(buildRoot, "obj").Replace('\\', '/') + "/";
                Run("dotnet", "build " + Quote(project) + " --configuration Release --nologo --disable-build-servers --output " +
                    Quote(binaryRoot) + " -p:BaseIntermediateOutputPath=" + Quote(obj) +
                    " -p:RestoreIgnoreFailedSources=true -p:UseSharedCompilation=false", 60000);
            }

            string pngPath = Path.Combine(buildRoot, Guid.NewGuid().ToString("N") + ".png");
            try
            {
                string projectName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                string output = Run(exe, Process.GetCurrentProcess().Id + " " + Quote(pngPath) + " " + Quote(projectName), 15000);
                string[] fields = output.Trim().Split(' ');
                info = new ScreenshotInfo
                {
                    status = "captured", file = "screen.png", width = int.Parse(fields[0]),
                    height = int.Parse(fields[1]), capturedAtUtc = fields[2]
                };
                return File.ReadAllBytes(pngPath);
            }
            finally { if (File.Exists(pngPath)) File.Delete(pngPath); }
#else
            throw new PlatformNotSupportedException("Unity 主窗口截图目前只支持 Windows 编辑器。");
#endif
        }

        [Serializable]
        internal sealed class ScreenshotInfo
        {
            public string status, file, error, capturedAtUtc;
            public string scope = "Unity Editor main window at export time, not the recorded Profiler frame.";
            public int width, height;
        }

        #endregion

#if UNITY_EDITOR_WIN
        #region 采集程序调用

        private static string Quote(string value) => "\"" + value + "\"";

        private static string Run(string executable, string arguments, int timeoutMs)
        {
            var start = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(start))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMs))
                {
                    process.Kill();
                    throw new TimeoutException("Unity 窗口截图工具执行超时。");
                }
                string output = stdout.Result, error = stderr.Result;
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Unity 窗口截图工具失败：" + (string.IsNullOrWhiteSpace(error) ? output : error).Trim());
                return output;
            }
        }

        #endregion
#endif
    }
}
