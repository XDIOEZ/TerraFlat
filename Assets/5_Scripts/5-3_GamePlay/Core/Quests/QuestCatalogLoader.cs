using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Gameplay.Quests
{
    /// <summary>
    /// 从 StreamingAssets 任务清单读取内建任务分包；全部文件解析和跨包去重成功后才替换运行时目录。
    /// 路径解析限制在 Quests 根目录内，并通过 StreamingAssetsTextLoader 兼容 Android 与 WebGL。
    /// </summary>
    public static class QuestCatalogLoader
    {
        #region 常量

        public const string RelativeQuestRoot = "GameConfig/Quests";
        public const string ManifestFileName = "quest-manifest.json";
        public const string RelativeManifestPath = RelativeQuestRoot + "/" + ManifestFileName;

        #endregion

        #region 加载

        public static int LoadBuiltIn()
        {
            string questRoot = StreamingAssetsTextLoader.CombinePath(
                Application.streamingAssetsPath,
                RelativeQuestRoot);
            string manifestPath = StreamingAssetsTextLoader.CombinePath(questRoot, ManifestFileName);
            QuestManifestDto manifest = DeserializeManifest(
                StreamingAssetsTextLoader.ReadAllText(manifestPath));
            ValidateManifest(manifest);

            List<QuestDefinition> definitions = LoadPackages(
                questRoot,
                manifest,
                path => StreamingAssetsTextLoader.ReadAllText(path));
            QuestCatalog.ReplaceBuiltIns(definitions);
            QuestCatalog.FinalizeRegistration();
            Debug.Log($"[QuestCatalog] 已加载 {definitions.Count} 条内建任务：{manifestPath}");
            return definitions.Count;
        }

        public static IEnumerator LoadBuiltInAsync(
            Action<int> onCompleted,
            Action<Exception> onFailed)
        {
            string questRoot;
            string manifestPath;
            try
            {
                questRoot = StreamingAssetsTextLoader.CombinePath(
                    Application.streamingAssetsPath,
                    RelativeQuestRoot);
                manifestPath = StreamingAssetsTextLoader.CombinePath(questRoot, ManifestFileName);
            }
            catch (Exception exception)
            {
                onFailed?.Invoke(exception);
                yield break;
            }

            string manifestJson = null;
            Exception readError = null;
            yield return StreamingAssetsTextLoader.ReadAllTextAsync(
                manifestPath,
                text => manifestJson = text,
                exception => readError = exception);
            if (readError != null)
            {
                onFailed?.Invoke(readError);
                yield break;
            }

            QuestManifestDto manifest;
            try
            {
                manifest = DeserializeManifest(manifestJson);
                ValidateManifest(manifest);
            }
            catch (Exception exception)
            {
                onFailed?.Invoke(exception);
                yield break;
            }

            var definitions = new List<QuestDefinition>();
            var questIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuestPackageDto package in manifest.Packages.Where(value => value.Enabled))
            {
                string packagePath;
                try
                {
                    packagePath = ResolvePackagePath(questRoot, package.Path);
                }
                catch (Exception exception)
                {
                    onFailed?.Invoke(exception);
                    yield break;
                }

                string packageJson = null;
                readError = null;
                yield return StreamingAssetsTextLoader.ReadAllTextAsync(
                    packagePath,
                    text => packageJson = text,
                    exception => readError = exception);
                if (readError != null)
                {
                    onFailed?.Invoke(new IOException(
                        $"任务分包 {package.Id} 读取失败：{packagePath}",
                        readError));
                    yield break;
                }

                try
                {
                    AppendPackageDefinitions(package, packageJson, questIds, definitions);
                }
                catch (Exception exception)
                {
                    onFailed?.Invoke(exception);
                    yield break;
                }
            }

            try
            {
                QuestCatalog.ReplaceBuiltIns(definitions);
                QuestCatalog.FinalizeRegistration();
                Debug.Log($"[QuestCatalog] 已加载 {definitions.Count} 条内建任务：{manifestPath}");
                onCompleted?.Invoke(definitions.Count);
            }
            catch (Exception exception)
            {
                onFailed?.Invoke(exception);
            }
        }

        private static List<QuestDefinition> LoadPackages(
            string questRoot,
            QuestManifestDto manifest,
            Func<string, string> readText)
        {
            var definitions = new List<QuestDefinition>();
            var questIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuestPackageDto package in manifest.Packages.Where(value => value.Enabled))
            {
                string packagePath = ResolvePackagePath(questRoot, package.Path);
                AppendPackageDefinitions(package, readText(packagePath), questIds, definitions);
            }

            return definitions;
        }

        private static void AppendPackageDefinitions(
            QuestPackageDto package,
            string json,
            ISet<string> questIds,
            ICollection<QuestDefinition> definitions)
        {
            QuestCatalogDto catalog = DeserializeCatalog(json);
            if (catalog.SchemaVersion != QuestCatalog.SupportedSchemaVersion)
            {
                throw new InvalidDataException(
                    $"任务分包 {package.Id} 的 schemaVersion 不受支持：{catalog.SchemaVersion}");
            }

            int index = 0;
            foreach (QuestDefinition definition in catalog.Quests ?? Enumerable.Empty<QuestDefinition>())
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                    throw new InvalidDataException($"任务分包 {package.Id} 包含空任务定义：#{index}");
                if (!questIds.Add(definition.Id.Trim()))
                    throw new InvalidDataException($"跨分包存在重复任务 ID：{definition.Id}");

                definition.SourceFile = package.Path;
                definition.SourceIndex = index++;
                definitions.Add(definition);
            }
        }

        #endregion

        #region JSON 与路径

        public static QuestManifestDto DeserializeManifest(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("任务分包清单为空");
            return JsonConvert.DeserializeObject<QuestManifestDto>(json)
                   ?? throw new InvalidDataException("任务分包清单根对象为空");
        }

        public static QuestCatalogDto DeserializeCatalog(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("任务分包内容为空");
            return JsonConvert.DeserializeObject<QuestCatalogDto>(json)
                   ?? throw new InvalidDataException("任务分包根对象为空");
        }

        public static void ValidateManifest(QuestManifestDto manifest)
        {
            if (manifest == null)
                throw new InvalidDataException("任务分包清单根对象为空");
            if (manifest.SchemaVersion != QuestCatalog.SupportedSchemaVersion)
                throw new InvalidDataException($"不支持的任务清单 schemaVersion：{manifest.SchemaVersion}");

            var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var packagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuestPackageDto package in manifest.Packages ?? Enumerable.Empty<QuestPackageDto>())
            {
                if (package == null || string.IsNullOrWhiteSpace(package.Id))
                    throw new InvalidDataException("任务清单包含空分包或空分包 ID");
                if (string.IsNullOrWhiteSpace(package.Path))
                    throw new InvalidDataException($"任务分包 {package.Id} 缺少 path");
                if (!packageIds.Add(package.Id.Trim()))
                    throw new InvalidDataException($"任务清单包含重复分包 ID：{package.Id}");
                if (!packagePaths.Add(package.Path.Trim().Replace('\\', '/')))
                    throw new InvalidDataException($"任务清单包含重复文件路径：{package.Path}");
            }
        }

        public static string ResolvePackagePath(string questRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(questRoot))
                throw new ArgumentException("任务根目录不能为空", nameof(questRoot));
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new InvalidDataException($"任务分包路径无效：{relativePath}");

            if (StreamingAssetsTextLoader.RequiresWebRequest(questRoot))
                return StreamingAssetsTextLoader.CombinePath(questRoot, relativePath);

            string normalizedRoot = Path.GetFullPath(questRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(
                Path.Combine(normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"任务分包路径越出 Quests 目录：{relativePath}");
            return fullPath;
        }

        #endregion
    }
}
