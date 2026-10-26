using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.EditorTools
{
    public sealed class SpriteWorldSizeValidationWindow : EditorWindow
    {
        #region 检测窗口状态

        private const string PythonPreferenceKey = "FlatWorld.SpriteWorldSizeValidator.Python";
        private const double ProcessTimeoutSeconds = 45;
        [SerializeField] private UnityEngine.Object spriteSourceAsset;
        [SerializeField] private UnityEngine.Object referenceSourceAsset;
        [SerializeField] private string pythonExecutable = "python";
        [SerializeField] private Vector2 extraWorldScale = Vector2.one;
        [SerializeField] private Vector2 referenceWorldScale = Vector2.one;
        [SerializeField] private float targetLongestWorldUnits = 0.7f;
        [SerializeField] private float relativeTolerance = 0.05f;
        [SerializeField] private int alphaThreshold = 1;
        private Process validationProcess;
        private Task<string> standardOutputTask;
        private Task<string> standardErrorTask;
        private double processStartedAt;
        private string reportText = "选择一张贴图或 Sprite，再点击检测。";
        private Vector2 reportScroll;

        [MenuItem("FlatWorld/其他工具/美术/贴图世界尺寸检测")]
        public static void OpenSpriteWorldSizeValidationWindow()
        {
            GetWindow<SpriteWorldSizeValidationWindow>("贴图世界尺寸检测");
        }

        private void OnEnable()
        {
            pythonExecutable = EditorPrefs.GetString(PythonPreferenceKey, "python");
            minSize = new Vector2(460, 400);
            if (spriteSourceAsset == null && (Selection.activeObject is Sprite || Selection.activeObject is Texture2D))
                spriteSourceAsset = Selection.activeObject;
        }

        private void OnDisable()
        {
            StopOwnedValidationProcess();
        }

        #endregion

        #region 显式输入与结果展示

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("按源图可见像素 ÷ PPU × 缩放计算 Unity 单位。只读检测，不改贴图或导入设置；当前已加载的旧 Sprite 不会自动更新。", MessageType.Info);
            using (new EditorGUI.DisabledScope(validationProcess != null))
            {
                spriteSourceAsset = EditorGUILayout.ObjectField("待测贴图 / Sprite", spriteSourceAsset, typeof(UnityEngine.Object), false);
                referenceSourceAsset = EditorGUILayout.ObjectField("参考贴图（可选）", referenceSourceAsset, typeof(UnityEngine.Object), false);
                extraWorldScale = EditorGUILayout.Vector2Field("额外世界缩放 X/Y", extraWorldScale);
                if (referenceSourceAsset != null)
                    referenceWorldScale = EditorGUILayout.Vector2Field("参考世界缩放 X/Y", referenceWorldScale);
                else
                    targetLongestWorldUnits = EditorGUILayout.FloatField("目标最长边（Unity单位）", targetLongestWorldUnits);
                relativeTolerance = EditorGUILayout.FloatField("相对容差（0.05 = 5%）", relativeTolerance);
                alphaThreshold = EditorGUILayout.IntSlider("可见 Alpha 阈值", alphaThreshold, 1, 255);
                pythonExecutable = EditorGUILayout.TextField("Python 路径或命令", pythonExecutable);
                EditorGUILayout.HelpBox("0.70 是本次钻木取火器的目标，可按物品改。额外缩放需包含 visual、Render 及父级缩放；参考图按可见宽高对照。图集切片用 CLI 的 --sprite-rect 参数。", MessageType.None);
                if (GUILayout.Button("载入钻木取火器测试样本（0.70单位）"))
                {
                    spriteSourceAsset = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/6_Art/Generated/Tools/PrimitiveSet_20261023/SparkMaker_Summoner/SparkMaker_Summoner_Icon.png");
                    referenceSourceAsset = null;
                    extraWorldScale = Vector2.one;
                    targetLongestWorldUnits = 0.7f;
                    relativeTolerance = 0.05f;
                    alphaThreshold = 1;
                }
                if (GUILayout.Button("检测尺寸（只读）"))
                    StartSelectedSpriteWorldSizeValidation();
            }
            if (validationProcess != null)
                EditorGUILayout.LabelField("正在读取源图并检测……");
            reportScroll = EditorGUILayout.BeginScrollView(reportScroll);
            EditorGUILayout.TextArea(reportText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region 调用唯一离线检测实现

        private void StartSelectedSpriteWorldSizeValidation()
        {
            // 尺寸规则全部交给同一 Python 检测器，避免编辑器与离线工具各算一套。
            try
            {
                string sourcePath = ResolveSelectedImageAssetPath(spriteSourceAsset);
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string scriptPath = Path.Combine(projectRoot, "Tools", "Art", "validate_sprite_world_size.py");
                if (!File.Exists(scriptPath))
                    throw new FileNotFoundException("未找到尺寸检测器。", scriptPath);
                var arguments = new List<string> { scriptPath, sourcePath, "--scale", FormatNumber(extraWorldScale.x), FormatNumber(extraWorldScale.y), "--alpha-threshold", alphaThreshold.ToString(CultureInfo.InvariantCulture), "--tolerance", FormatNumber(relativeTolerance) };
                if (referenceSourceAsset != null)
                {
                    arguments.AddRange(new[] { "--reference", ResolveSelectedImageAssetPath(referenceSourceAsset), "--reference-scale", FormatNumber(referenceWorldScale.x), FormatNumber(referenceWorldScale.y) });
                }
                else
                    arguments.AddRange(new[] { "--target-longest", FormatNumber(targetLongestWorldUnits) });
                var commandLine = new StringBuilder();
                foreach (string argument in arguments)
                {
                    if (commandLine.Length != 0)
                        commandLine.Append(' ');
                    commandLine.Append(QuoteProcessArgument(argument));
                }
                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExecutable,
                    Arguments = commandLine.ToString(),
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                startInfo.EnvironmentVariables["PYTHONDONTWRITEBYTECODE"] = "1";
                validationProcess = Process.Start(startInfo);
                if (validationProcess == null)
                    throw new InvalidOperationException("Python 进程未启动。");
                EditorPrefs.SetString(PythonPreferenceKey, pythonExecutable);
                standardOutputTask = validationProcess.StandardOutput.ReadToEndAsync();
                standardErrorTask = validationProcess.StandardError.ReadToEndAsync();
                processStartedAt = EditorApplication.timeSinceStartup;
                reportText = "正在检测：" + sourcePath;
                EditorApplication.update += PollOwnedSpriteValidationProcess;
            }
            catch (Exception exception)
            {
                StopOwnedValidationProcess();
                reportText = "检测未启动：" + exception.Message + "\n请设置已安装 Pillow 的 Python 完整路径。CLI：Tools/Art/validate_sprite_world_size.py";
            }
        }

        private static string ResolveSelectedImageAssetPath(UnityEngine.Object asset)
        {
            if (!(asset is Sprite) && !(asset is Texture2D))
                throw new ArgumentException("请选择 Texture2D 或 Sprite 资源。");
            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("请选择磁盘上的源贴图。");
            return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath));
        }

        private void PollOwnedSpriteValidationProcess()
        {
            try
            {
                if (validationProcess == null)
                    return;
                if (EditorApplication.timeSinceStartup - processStartedAt > ProcessTimeoutSeconds)
                {
                    reportText = "检测超时，请检查 Python 路径与图片大小。";
                    StopOwnedValidationProcess();
                    Repaint();
                    return;
                }
                if (!validationProcess.HasExited || !standardOutputTask.IsCompleted || !standardErrorTask.IsCompleted)
                    return;
                string output = standardOutputTask.GetAwaiter().GetResult();
                string error = standardErrorTask.GetAwaiter().GetResult();
                reportText = output + (string.IsNullOrWhiteSpace(error) ? "" : "\n" + error);
                if (validationProcess.ExitCode != 0)
                    reportText += "\n检测未通过，退出码：" + validationProcess.ExitCode;
                StopOwnedValidationProcess();
                Repaint();
            }
            catch (Exception exception)
            {
                reportText = "读取检测结果失败：" + exception.Message;
                StopOwnedValidationProcess();
                Repaint();
            }
        }

        private void StopOwnedValidationProcess()
        {
            EditorApplication.update -= PollOwnedSpriteValidationProcess;
            if (validationProcess == null)
                return;
            try
            {
                if (!validationProcess.HasExited)
                    validationProcess.Kill();
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally
            {
                validationProcess.Dispose();
                validationProcess = null;
                standardOutputTask = null;
                standardErrorTask = null;
            }
        }

        private static string FormatNumber(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string QuoteProcessArgument(string argument)
        {
            var result = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                result.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
                result.Append(character);
                backslashes = 0;
            }
            return result.Append('\\', backslashes * 2).Append('"').ToString();
        }

        #endregion
    }
}
