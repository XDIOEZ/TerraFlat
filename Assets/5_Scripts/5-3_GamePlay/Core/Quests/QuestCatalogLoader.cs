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
            List<QuestDefinition> definitions = LoadBuiltInDefinitions();
            PublishBuiltInDefinitions(definitions);
            return definitions.Count;
        }

        [BuiltInContentBake]
        public static void BakeBuiltInConfiguration() => LoadBuiltInDefinitions();

        private static List<QuestDefinition> LoadBuiltInDefinitions()
        {
            string root = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeQuestRoot);
            string manifestPath = StreamingAssetsTextLoader.CombinePath(root, ManifestFileName);
            QuestManifestDto manifest = BuiltInContentBinaryCatalog.ReadFile(manifestPath, DeserializeManifest);
            ValidateManifest(manifest);
            var definitions = new List<QuestDefinition>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuestPackageDto package in manifest.Packages.Where(value => value.Enabled))
                AppendPackageDefinitions(package, BuiltInContentBinaryCatalog.ReadFile(
                    ResolvePackagePath(root, package.Path), DeserializeCatalog), ids, definitions);
            return definitions;
        }

        public static IEnumerator LoadBuiltInAsync(Action<int> onCompleted, Action<Exception> onFailed)
        {
            string root = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeQuestRoot);
            QuestManifestDto manifest = null;
            Exception error = null;
            yield return BuiltInContentBinaryCatalog.ReadFileAsync(
                StreamingAssetsTextLoader.CombinePath(root, ManifestFileName), DeserializeManifest,
                value => manifest = value, value => error = value);
            if (error != null) { onFailed?.Invoke(error); yield break; }
            try { ValidateManifest(manifest); }
            catch (Exception exception) { onFailed?.Invoke(exception); yield break; }
            var definitions = new List<QuestDefinition>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuestPackageDto package in manifest.Packages.Where(value => value.Enabled))
            {
                string path;
                try { path = ResolvePackagePath(root, package.Path); }
                catch (Exception exception) { onFailed?.Invoke(exception); yield break; }
                QuestCatalogDto catalog = null;
                yield return BuiltInContentBinaryCatalog.ReadFileAsync(path, DeserializeCatalog,
                    value => catalog = value, value => error = value);
                if (error != null) { onFailed?.Invoke(error); yield break; }
                try { AppendPackageDefinitions(package, catalog, ids, definitions); }
                catch (Exception exception) { onFailed?.Invoke(exception); yield break; }
            }
            try { PublishBuiltInDefinitions(definitions); onCompleted?.Invoke(definitions.Count); }
            catch (Exception exception) { onFailed?.Invoke(exception); }
        }

        private static void PublishBuiltInDefinitions(List<QuestDefinition> definitions)
        {
            QuestCatalog.ReplaceBuiltIns(definitions);
            QuestCatalog.FinalizeRegistration();
            Debug.Log($"[QuestCatalog] 已加载 {definitions.Count} 条内建任务。");
        }

        private static void AppendPackageDefinitions(
            QuestPackageDto package,
            QuestCatalogDto catalog,
            ISet<string> questIds,
            ICollection<QuestDefinition> definitions)
        {
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
            return JsonConfigurationCache.Deserialize<QuestManifestDto>(json)
                   ?? throw new InvalidDataException("任务分包清单根对象为空");
        }

        public static QuestCatalogDto DeserializeCatalog(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("任务分包内容为空");
            return JsonConfigurationCache.Deserialize<QuestCatalogDto>(json)
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
            return StreamingAssetsTextLoader.ResolveCatalogPackagePathWithinRoot(questRoot, relativePath, "任务", "Quests");
        }

        #endregion
    }
}
