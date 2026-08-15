#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>MOD 作者使用的校验、AssetBundle 构建和本地安装工具。</summary>
public sealed class ModAuthoringWindow : EditorWindow
{
    #region 状态

    private DefaultAsset sourceFolder;
    private string sourcePath = string.Empty;
    private Vector2 reportScroll;
    private string report = "请选择包含 manifest.json 的 MOD 源目录。";

    #endregion

    #region 菜单

    [MenuItem("FlatWorld/MOD/创作与打包工具")]
    private static void OpenWindow()
    {
        GetWindow<ModAuthoringWindow>("MOD 创作工具").minSize = new Vector2(680f, 480f);
    }

    #endregion

    #region 界面

    private void OnGUI()
    {
        EditorGUILayout.LabelField("FlatWorld MOD 创作与打包", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "源目录应包含 manifest.json、Defs、Patches、Localization、Settings 和 Lua。" +
            "Bundle 资源请在 Inspector 的 AssetBundle 名称中填写 manifest 的 bundle.id。",
            MessageType.Info);

        DefaultAsset selectedFolder = (DefaultAsset)EditorGUILayout.ObjectField("项目内源目录", sourceFolder, typeof(DefaultAsset), false);
        if (selectedFolder != sourceFolder)
        {
            sourceFolder = selectedFolder;
            if (sourceFolder != null)
                sourcePath = Path.GetFullPath(AssetDatabase.GetAssetPath(sourceFolder));
        }

        EditorGUILayout.BeginHorizontal();
        sourcePath = EditorGUILayout.TextField("源目录路径", sourcePath);
        if (GUILayout.Button("浏览", GUILayout.Width(70f)))
        {
            string selected = EditorUtility.OpenFolderPanel("选择 FlatWorld MOD 源目录", sourcePath, string.Empty);
            if (!string.IsNullOrWhiteSpace(selected))
                sourcePath = selected;
        }
        EditorGUILayout.EndHorizontal();

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(sourcePath) && sourceFolder == null))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("校验源包", GUILayout.Height(30f)))
                ValidateSelectedSource();
            if (GUILayout.Button("构建并安装到本机", GUILayout.Height(30f)))
                BuildAndInstallSelectedSource();
            if (GUILayout.Button("导出已构建 ZIP", GUILayout.Height(30f)))
                ExportSelectedZip();
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("报告", EditorStyles.boldLabel);
        reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUI.skin.box);
        EditorGUILayout.SelectableLabel(report, EditorStyles.wordWrappedLabel, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    #endregion

    #region 校验

    private void ValidateSelectedSource()
    {
        try
        {
            string sourceRoot = GetSourceRoot();
            ModManifest manifest = ValidatePackageSource(sourceRoot, out List<string> messages);
            report = $"校验通过：{manifest.Id} {manifest.Version}\n" + string.Join("\n", messages);
        }
        catch (Exception ex)
        {
            report = "校验失败：\n" + ex;
        }
    }

    private static ModManifest ValidatePackageSource(string sourceRoot, out List<string> messages)
    {
        messages = new List<string>();
        string manifestPath = ResolveInside(sourceRoot, "manifest.json", true);
        ModManifest manifest = JsonConvert.DeserializeObject<ModManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("manifest.json 为空");

        if (manifest.ApiVersion != ModRuntimeManager.SupportedApiVersion)
            throw new InvalidDataException($"apiVersion 必须为 {ModRuntimeManager.SupportedApiVersion}");
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("manifest 必须填写 id 和 version");

        HashSet<string> contentIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> playerTemplateIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in manifest.DefinitionFiles ?? Enumerable.Empty<string>())
        {
            JObject document = JObject.Parse(File.ReadAllText(ResolveInside(sourceRoot, file, true)));
            int itemCount = 0;
            foreach (JToken item in document["items"] as JArray ?? new JArray())
            {
                string id = item.Value<string>("id");
                if (string.IsNullOrWhiteSpace(id) || !id.StartsWith(manifest.Id + ":", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{file} 中的物品 ID 必须使用 {manifest.Id}: 命名空间");
                if (!contentIds.Add(id))
                    throw new InvalidDataException($"重复内容 ID：{id}");
                itemCount++;
            }

            int playerTemplateCount = 0;
            foreach (JToken template in document["playerCreationTemplates"] as JArray ?? new JArray())
            {
                string id = template.Value<string>("id")?.Trim();
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidDataException($"{file} 中的玩家创建模板缺少 id");
                if (id.Contains(":", StringComparison.Ordinal) &&
                    !id.StartsWith(manifest.Id + ":", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{file} 中的玩家创建模板 ID 必须使用 {manifest.Id}: 命名空间");

                string normalizedId = id.Contains(":", StringComparison.Ordinal) ? id : $"{manifest.Id}:{id}";
                if (!playerTemplateIds.Add(normalizedId))
                    throw new InvalidDataException($"重复玩家创建模板 ID：{normalizedId}");
                playerTemplateCount++;
            }

            messages.Add($"Def：{file}，物品 {itemCount} 个，玩家模板 {playerTemplateCount} 个");
        }

        foreach (string file in manifest.PatchFiles ?? Enumerable.Empty<string>())
        {
            ModPatchDocument document = JsonConvert.DeserializeObject<ModPatchDocument>(File.ReadAllText(ResolveInside(sourceRoot, file, true)))
                ?? throw new InvalidDataException($"Patch 文件为空：{file}");
            foreach (ModPatchOperation patch in document.Patches ?? Enumerable.Empty<ModPatchOperation>())
            {
                if (string.IsNullOrWhiteSpace(patch.Target) || string.IsNullOrWhiteSpace(patch.Operation) || string.IsNullOrWhiteSpace(patch.Path))
                    throw new InvalidDataException($"Patch 字段不完整：{file}");
            }
            messages.Add($"Patch：{file}，操作 {document.Patches?.Count ?? 0} 个");
        }

        foreach (string file in manifest.LocalizationFiles ?? Enumerable.Empty<string>())
        {
            ModLocalizationDocument document = JsonConvert.DeserializeObject<ModLocalizationDocument>(File.ReadAllText(ResolveInside(sourceRoot, file, true)))
                ?? throw new InvalidDataException($"本地化文件为空：{file}");
            if (string.IsNullOrWhiteSpace(document.Language))
                throw new InvalidDataException($"本地化文件缺少 language：{file}");
            messages.Add($"本地化：{file}，文本 {document.Entries?.Count ?? 0} 条");
        }

        if (!string.IsNullOrWhiteSpace(manifest.SettingsFile))
        {
            ModSettingsDocument settings = JsonConvert.DeserializeObject<ModSettingsDocument>(
                File.ReadAllText(ResolveInside(sourceRoot, manifest.SettingsFile, true)))
                ?? throw new InvalidDataException("设置 schema 为空");
            messages.Add($"设置：{manifest.SettingsFile}，项目 {settings.Settings?.Count ?? 0} 个");
        }

        foreach (string file in manifest.LocalizationFiles ?? Enumerable.Empty<string>())
            ResolveInside(sourceRoot, file, true);
        if (!string.IsNullOrWhiteSpace(manifest.EntryLua))
            ResolveInside(sourceRoot, manifest.EntryLua, true);

        foreach (ModBundleDefinition bundle in manifest.Bundles ?? Enumerable.Empty<ModBundleDefinition>())
        {
            string[] paths = AssetDatabase.GetAssetPathsFromAssetBundle(bundle.Id);
            if (paths == null || paths.Length == 0)
                throw new InvalidDataException($"Bundle {bundle.Id} 没有分配任何 Unity 资源");
            messages.Add($"Bundle：{bundle.Id}，资源 {paths.Length} 个");
        }

        messages.Add($"内容 ID 总数：{contentIds.Count}");
        return manifest;
    }

    #endregion

    #region 构建安装

    private void BuildAndInstallSelectedSource()
    {
        try
        {
            string sourceRoot = GetSourceRoot();
            ModManifest manifest = ValidatePackageSource(sourceRoot, out List<string> messages);
            string destinationRoot = Path.Combine(Application.persistentDataPath, "Mods", manifest.Id);
            string fullSourceRoot = Path.GetFullPath(sourceRoot);
            string fullDestinationRoot = Path.GetFullPath(destinationRoot);
            if (string.Equals(fullSourceRoot, fullDestinationRoot, StringComparison.OrdinalIgnoreCase) ||
                fullSourceRoot.StartsWith(fullDestinationRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("作者源目录不能位于同 ID 的本机安装目录中，请把源目录放到 Assets/FlatWorldMods 或其他独立位置");
            }

            if (Directory.Exists(destinationRoot) &&
                !EditorUtility.DisplayDialog("覆盖已安装 MOD", $"将覆盖：\n{destinationRoot}\n\n是否继续？", "覆盖", "取消"))
            {
                return;
            }

            if (Directory.Exists(destinationRoot))
                Directory.Delete(destinationRoot, true);
            Directory.CreateDirectory(destinationRoot);
            CopySourceFiles(sourceRoot, destinationRoot);
            BuildBundles(manifest, destinationRoot);

            string destinationManifestPath = Path.Combine(destinationRoot, "manifest.json");
            manifest.ContentHash = ModRuntimeManager.CalculatePackageHash(destinationRoot);
            File.WriteAllText(destinationManifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));

            report = $"构建安装完成：{manifest.Id}\n输出：{destinationRoot}\n内容哈希：{manifest.ContentHash}\n" +
                     string.Join("\n", messages);
            GUIUtility.systemCopyBuffer = destinationRoot;
            AssetDatabase.Refresh();
            Debug.Log($"[MOD SDK] 已构建并安装 {manifest.Id}：{destinationRoot}");
        }
        catch (Exception ex)
        {
            report = "构建失败：\n" + ex;
            Debug.LogException(ex);
        }
    }

    private void ExportSelectedZip()
    {
        try
        {
            string sourceRoot = GetSourceRoot();
            ModManifest manifest = ValidatePackageSource(sourceRoot, out _);
            string installedRoot = Path.Combine(Application.persistentDataPath, "Mods", manifest.Id);
            if (!File.Exists(Path.Combine(installedRoot, "manifest.json")))
                throw new InvalidOperationException("尚未构建本机安装包，请先执行“构建并安装到本机”");

            string outputPath = EditorUtility.SaveFilePanel(
                "导出 FlatWorld MOD 分发包",
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"{manifest.Id}-{manifest.Version}.zip",
                "zip");
            if (string.IsNullOrWhiteSpace(outputPath))
                return;

            if (File.Exists(outputPath))
                File.Delete(outputPath);
            ZipFile.CreateFromDirectory(installedRoot, outputPath, System.IO.Compression.CompressionLevel.Optimal, false);
            report = $"ZIP 导出完成：\n{outputPath}";
            Debug.Log($"[MOD SDK] 已导出分发包：{outputPath}");
        }
        catch (Exception ex)
        {
            report = "ZIP 导出失败：\n" + ex;
            Debug.LogException(ex);
        }
    }

    private static void BuildBundles(ModManifest manifest, string destinationRoot)
    {
        List<AssetBundleBuild> builds = new();
        foreach (ModBundleDefinition bundle in manifest.Bundles ?? Enumerable.Empty<ModBundleDefinition>())
        {
            string[] paths = AssetDatabase.GetAssetPathsFromAssetBundle(bundle.Id);
            if (paths == null || paths.Length == 0)
                continue;
            builds.Add(new AssetBundleBuild { assetBundleName = bundle.Id, assetNames = paths });
        }

        if (builds.Count == 0)
            return;

        string temporaryRoot = Path.Combine("Temp", "FlatWorldModBundles", manifest.Id);
        if (Directory.Exists(temporaryRoot))
            Directory.Delete(temporaryRoot, true);
        Directory.CreateDirectory(temporaryRoot);

        AssetBundleManifest result = BuildPipeline.BuildAssetBundles(
            temporaryRoot,
            builds.ToArray(),
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
            EditorUserBuildSettings.activeBuildTarget);
        if (result == null)
            throw new InvalidOperationException("Unity AssetBundle 构建失败，请检查 Console");

        foreach (ModBundleDefinition bundle in manifest.Bundles ?? Enumerable.Empty<ModBundleDefinition>())
        {
            string builtPath = Path.Combine(temporaryRoot, bundle.Id);
            if (!File.Exists(builtPath))
                continue;

            string outputPath = ResolveInside(destinationRoot, bundle.Path, false);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.Copy(builtPath, outputPath, true);
        }
    }

    private static void CopySourceFiles(string sourceRoot, string destinationRoot)
    {
        foreach (string file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(file);
            if (extension.Equals(".meta", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(sourceRoot, file);
            string destination = ResolveInside(destinationRoot, relative, false);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(file, destination, true);
        }
    }

    #endregion

    #region 路径

    private string GetSourceRoot()
    {
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            string selectedPath = Path.GetFullPath(sourcePath);
            if (!Directory.Exists(selectedPath))
                throw new DirectoryNotFoundException(selectedPath);
            return selectedPath;
        }

        if (sourceFolder == null)
            throw new InvalidOperationException("尚未选择 MOD 源目录");

        string assetPath = AssetDatabase.GetAssetPath(sourceFolder);
        string fullPath = Path.GetFullPath(assetPath);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException(fullPath);
        return fullPath;
    }

    private static string ResolveInside(string root, string relativePath, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"必须使用包内相对路径：{relativePath}");

        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"路径越界：{relativePath}");
        if (mustExist && !File.Exists(fullPath))
            throw new FileNotFoundException($"文件不存在：{relativePath}", fullPath);
        return fullPath;
    }

    #endregion
}
#endif
