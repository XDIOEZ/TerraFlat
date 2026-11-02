using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace FlatWorld.Editor.ContentTools
{
    /// <summary>构建前调用各领域的纯配置烘焙入口，生成 Player 共用的二进制资源目录。</summary>
    public sealed class BuiltInContentBinaryCatalogBaker : BuildPlayerProcessor
    {
        #region 构建期烘焙

        public override int callbackOrder => -10000;
        public override void PrepareForBuild(BuildPlayerContext context) => BakeBuiltInConfigurations();

        [MenuItem("FlatWorld/资源/烘焙完整启动目录和图集")]
        public static void BakeCompleteStartupContent()
        {
            FlatWorldSpriteAtlasPrebuildGenerator.GenerateSpriteAtlasesAndPurposeCatalogs();
            BakeBuiltInConfigurations();
        }

        [MenuItem("FlatWorld/资源/烘焙启动二进制配置")]
        public static void BakeBuiltInConfigurations()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请在非运行状态烘焙启动配置。");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            MethodInfo[] contributors = TypeCache.GetMethodsWithAttribute<BuiltInContentBakeAttribute>()
                .OrderBy(method => method.DeclaringType?.FullName, StringComparer.Ordinal)
                .ThenBy(method => method.Name, StringComparer.Ordinal).ToArray();
            using var sourceConfigurations = BuiltInContentBinaryCatalog.UseSourceConfigurations();
            BuiltInContentBinaryCatalog.BeginRecording();
            try
            {
                foreach (MethodInfo method in contributors)
                {
                    if (!method.IsStatic || method.GetParameters().Length != 0)
                        throw new BuildFailedException("配置烘焙入口必须是无参静态方法：" + method);
                    try { method.Invoke(null, null); }
                    catch (TargetInvocationException exception)
                    {
                        throw new BuildFailedException($"配置烘焙失败：{method.DeclaringType?.Name}.{method.Name}\n{exception.InnerException}");
                    }
                }
                string[] keys = BuiltInContentBinaryCatalog.RecordingKeys;
                byte[] bytes = BuiltInContentBinaryCatalog.FinishRecording();
                string assetPath = "Assets/Resources/" + BuiltInContentBinaryCatalog.ResourcePath + ".bytes";
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                // 相同内容不触发资源重导入，失败时继续保留上一次完整产物。
                if (!File.Exists(assetPath) || !File.ReadAllBytes(assetPath).AsSpan().SequenceEqual(bytes))
                {
                    string temporary = assetPath + ".tmp";
                    File.WriteAllBytes(temporary, bytes);
                    if (File.Exists(assetPath)) File.Replace(temporary, assetPath, null);
                    else File.Move(temporary, assetPath);
                }
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                Directory.CreateDirectory("Library/FlatWorldContentBake");
                File.WriteAllLines("Library/FlatWorldContentBake/recorded-keys.txt", keys);
                Debug.Log($"[构建期二进制目录] {contributors.Length} 个领域入口，{keys.Length} 个载荷，{bytes.Length / 1024d:F1} KB，烘焙 {timer.Elapsed.TotalMilliseconds:F0} ms。");
            }
            finally { BuiltInContentBinaryCatalog.CancelRecording(); }
        }

        #endregion
    }
}
