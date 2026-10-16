using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FlatWorld.GameplayMCP
{
    /// <summary>仅为 Lua 测试保留源资产引用，正式资源会话仍重新建立自己的运行态。</summary>
    [FilePath(CacheRelativePath, FilePathAttribute.Location.ProjectFolder)]
    internal sealed class GameplayTestResourceCacheStore : ScriptableSingleton<GameplayTestResourceCacheStore>
    {
        #region 持久缓存元数据
        private const string CacheRelativePath = "Library/FlatWorldGameplayTests/GameResCache/cache.asset";
        private const int CurrentSchema = 2;

        [Serializable]
        private sealed class AssetEntry
        {
            public string Key;
            public Object Asset;
        }

        [Serializable]
        private sealed class DocumentEntry
        {
            public string Key;
            public string Json;
        }

        [Serializable]
        private sealed class InputEntry
        {
            public string Key, Hash, ImportHash;
            public long Length, WriteTicks;
        }

        private sealed class InputFingerprint
        {
            public string Stamp;
            public List<InputEntry> Entries;
            public int HashedFiles, ReusedHashes;
            public int IgnoredTestScenes;
            public double Seconds;
        }

        private sealed class FileInput
        {
            public string AbsolutePath;
            public InputEntry Entry, Previous;
        }

        [SerializeField] private int schemaVersion = CurrentSchema;
        [SerializeField] private string inputStamp;
        [SerializeField] private List<AssetEntry> assets = new();
        [SerializeField] private List<DocumentEntry> documents = new();
        [SerializeField] private List<InputEntry> inputEntries = new();

        private static string ProjectDirectory => Path.GetDirectoryName(Application.dataPath);
        private static string CachePath => Path.Combine(ProjectDirectory, CacheRelativePath);
        private static string ChecksumPath => CachePath + ".sha256";

        private void ResetMetadata(string stamp)
        {
            schemaVersion = CurrentSchema;
            inputStamp = stamp;
            assets = new List<AssetEntry>();
            documents = new List<DocumentEntry>();
            inputEntries = new List<InputEntry>();
        }

        private void SaveMetadata()
        {
            // 只保存 Library 中本缓存，避免顺带保存工作区里其他脏资源。
            Save(true);
            File.WriteAllText(ChecksumPath, HashFile(CachePath), new UTF8Encoding(false));
        }

        private static string RemoveCorruptMetadata()
        {
            // 文件缺失时也重建，防止域内残留的旧对象仍被当成可恢复缓存。
            if (!File.Exists(CachePath)) return "metadata_missing";
            bool checksumExists = File.Exists(ChecksumPath);
            if (checksumExists &&
                string.Equals(File.ReadAllText(ChecksumPath, Encoding.UTF8).Trim(), HashFile(CachePath),
                    StringComparison.Ordinal)) return null;
            // 原生反序列化之前丢弃损坏的自有元数据，不接触任何源资源。
            File.Delete(CachePath);
            if (File.Exists(ChecksumPath)) File.Delete(ChecksumPath);
            return checksumExists ? "metadata_checksum_mismatch" : "metadata_checksum_missing";
        }
        #endregion

        #region Editor 桥接与测试会话
        [InitializeOnLoadMethod]
        private static void RegisterBackend() => GameplayTestResourceCache.Backend = new Backend();

        [MenuItem("FlatWorld/测试/清理Lua测试GameRes缓存")]
        private static void ClearFromMenu() => GameplayTestResourceCache.Backend?.Clear();

        [MenuItem("FlatWorld/测试/清理Lua测试GameRes缓存", true)]
        private static bool CanClearFromMenu() => !EditorApplication.isPlayingOrWillChangePlaymode;

        private sealed class Backend : IGameplayTestResourceCacheBackend
        {
            private const string SessionRunKey = "FlatWorld.GameplayTestPipeline.ActiveRun";
            private const string TestScenePath = "Assets/3_Scenes/Development/GameplayTestScene.unity";
            private GameplayTestResourceCacheStore store;
            private readonly Dictionary<string, Object> assetIndex = new(StringComparer.Ordinal);
            private readonly Dictionary<string, string> documentIndex = new(StringComparer.Ordinal);
            private readonly Dictionary<string, Object> pendingAssets = new(StringComparer.Ordinal);
            private readonly Dictionary<string, string> pendingDocuments = new(StringComparer.Ordinal);
            private bool ready;
            private bool loggedSessionReadFailure;
            private InputFingerprint sessionInputs;

            public bool IsTestRun
            {
                get
                {
                    if (SceneManager.sceneCount > 0 && SceneManager.GetSceneAt(0).path == TestScenePath)
                        return true;
                    string runId = SessionState.GetString(SessionRunKey, string.Empty);
                    if (string.IsNullOrEmpty(runId)) return false;
                    try
                    {
                        GameplayTestSelection.ValidateRunId(runId);
                        if (!File.Exists(GameplayTestSelection.SelectionPath)) return false;
                        JObject selection = JObject.Parse(File.ReadAllText(GameplayTestSelection.SelectionPath, Encoding.UTF8));
                        if (selection.Value<string>("runId") != runId) return false;
                        string reportPath = Path.Combine(GameplayTestSelection.RootDirectory, "Pipeline", runId + ".json");
                        if (!File.Exists(reportPath)) return false;
                        JObject report = JObject.Parse(File.ReadAllText(reportPath, Encoding.UTF8));
                        return report.Value<string>("runId") == runId &&
                               report.Value<string>("executionMode") == "gameplay" &&
                               report.Value<string>("status") == "running" &&
                               report.Value<string>("phase") == "playmode";
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                                                  error is Newtonsoft.Json.JsonException || error is ArgumentException)
                    {
                        if (!loggedSessionReadFailure)
                        {
                            loggedSessionReadFailure = true;
                            Debug.Log("[Lua GameRes缓存] 测试会话指针不可读，使用正式冷加载：" + error.Message);
                        }
                        return false;
                    }
                }
            }

            public void BeginSession()
            {
                ready = false;
                assetIndex.Clear();
                documentIndex.Clear();
                pendingAssets.Clear();
                pendingDocuments.Clear();
                string invalidReason = RemoveCorruptMetadata();
                store = instance;
                if (store == null) throw new InvalidDataException("缓存元数据无法恢复。");
                sessionInputs = ComputeInputStamp(invalidReason == null ? store.inputEntries : null);
                if (invalidReason == null && store.schemaVersion != CurrentSchema) invalidReason = "schema_changed";
                if (invalidReason == null && (store.assets == null || store.documents == null || store.inputEntries == null))
                    invalidReason = "metadata_fields_missing";
                if (invalidReason == null && store.inputStamp != sessionInputs.Stamp) invalidReason = "resource_inputs_changed";
                if (invalidReason != null)
                {
                    LogInvalidation(invalidReason, store.inputEntries, sessionInputs.Entries);
                }
                else
                {
                    foreach (AssetEntry entry in store.assets)
                        if (entry != null && !string.IsNullOrWhiteSpace(entry.Key) && IsSourceAsset(entry.Asset))
                            assetIndex[entry.Key] = entry.Asset;
                    foreach (DocumentEntry entry in store.documents)
                        if (entry != null && !string.IsNullOrWhiteSpace(entry.Key) && IsDocument(entry.Json))
                            documentIndex[entry.Key] = entry.Json;
                }
                Debug.Log($"[Lua GameRes缓存] 输入核对 {sessionInputs.Seconds:F3}s：" +
                          $"重新散列 {sessionInputs.HashedFiles}，复用散列 {sessionInputs.ReusedHashes}，" +
                          $"忽略测试引导场景 {sessionInputs.IgnoredTestScenes}，" +
                          $"源资产可恢复 {assetIndex.Count}/已存 {store.assets?.Count ?? 0}，" +
                          $"文档可恢复 {documentIndex.Count}/已存 {store.documents?.Count ?? 0}。");
                ready = true;
            }

            public bool TryGetAsset(string key, Type expected, out Object asset)
            {
                asset = null;
                if (!ready || string.IsNullOrWhiteSpace(key) || expected == null ||
                    !typeof(Object).IsAssignableFrom(expected)) return false;
                if (!assetIndex.TryGetValue(key, out Object value))
                    return false;
                if (!IsSourceAsset(value) || !expected.IsInstanceOfType(value)) return false;
                asset = value;
                return true;
            }

            public void RecordAsset(string key, Object asset)
            {
                if (ready && !string.IsNullOrWhiteSpace(key) && IsSourceAsset(asset)) pendingAssets[key] = asset;
            }

            public bool TryGetDocument(string key, out string json)
            {
                json = null;
                return ready && !string.IsNullOrWhiteSpace(key) &&
                       documentIndex.TryGetValue(key, out json);
            }

            public void RecordDocument(string key, string json)
            {
                if (ready && !string.IsNullOrWhiteSpace(key) && IsDocument(json)) pendingDocuments[key] = json;
            }

            public void Flush()
            {
                try { FlushReadySession(); }
                finally { AbortSession(); }
            }

            private void FlushReadySession()
            {
                if (!ready) return;
                InputFingerprint finalInputs = ComputeInputStamp(sessionInputs.Entries);
                if (finalInputs.Stamp != sessionInputs.Stamp)
                {
                    LogInvalidation("inputs_changed_during_load_not_published", sessionInputs.Entries, finalInputs.Entries);
                    AbortSession();
                    return;
                }
                // 只有整轮资源加载成功才发布新条目，失败会话不进入跨域缓存。
                foreach (KeyValuePair<string, Object> entry in pendingAssets) assetIndex[entry.Key] = entry.Value;
                foreach (KeyValuePair<string, string> entry in pendingDocuments) documentIndex[entry.Key] = entry.Value;
                var nextAssets = new List<AssetEntry>();
                foreach (KeyValuePair<string, Object> entry in assetIndex)
                    if (IsSourceAsset(entry.Value)) nextAssets.Add(new AssetEntry { Key = entry.Key, Asset = entry.Value });
                var nextDocuments = new List<DocumentEntry>();
                foreach (KeyValuePair<string, string> entry in documentIndex)
                    nextDocuments.Add(new DocumentEntry { Key = entry.Key, Json = entry.Value });
                List<AssetEntry> oldAssets = store.assets;
                List<DocumentEntry> oldDocuments = store.documents;
                List<InputEntry> oldInputs = store.inputEntries;
                string oldStamp = store.inputStamp;
                int oldSchema = store.schemaVersion;
                store.assets = nextAssets;
                store.documents = nextDocuments;
                store.schemaVersion = CurrentSchema;
                store.inputStamp = finalInputs.Stamp;
                store.inputEntries = finalInputs.Entries;
                try { store.SaveMetadata(); }
                catch
                {
                    store.assets = oldAssets; store.documents = oldDocuments; store.inputEntries = oldInputs;
                    store.inputStamp = oldStamp; store.schemaVersion = oldSchema;
                    throw;
                }
                Debug.Log($"[Lua GameRes缓存] 发布成功：源资产 {store.assets.Count}，文档 {store.documents.Count}，" +
                          $"指纹条目 {store.inputEntries.Count}，发布核对 {finalInputs.Seconds:F3}s。");
                pendingAssets.Clear();
                pendingDocuments.Clear();
                ready = false;
                sessionInputs = null;
            }

            public void Clear()
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Debug.Log("[Lua GameRes缓存] 请退出播放后清理，本轮仍持有资源引用。");
                    return;
                }
                ready = false;
                sessionInputs = null;
                assetIndex.Clear();
                documentIndex.Clear();
                pendingAssets.Clear();
                pendingDocuments.Clear();
                RemoveCorruptMetadata();
                store = instance;
                store.ResetMetadata(string.Empty);
                store.SaveMetadata();
                Debug.Log("[Lua GameRes缓存] 已清理测试缓存元数据。");
            }

            public void AbortSession()
            {
                // 取消只丢本轮暂存，保留上一轮成功生成的磁盘缓存。
                ready = false;
                sessionInputs = null;
                pendingAssets.Clear();
                pendingDocuments.Clear();
                assetIndex.Clear();
                documentIndex.Clear();
            }
        }
        #endregion

        #region 输入指纹与合法源资产
        private static bool IsSourceAsset(Object asset) => asset != null && EditorUtility.IsPersistent(asset) &&
            AssetDatabase.Contains(asset) && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset));

        private static bool IsDocument(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                JToken document = JToken.Parse(json);
                return document.Type == JTokenType.Object || document.Type == JTokenType.Array;
            }
            catch (Newtonsoft.Json.JsonException) { return false; }
        }

        private static InputFingerprint ComputeInputStamp(List<InputEntry> previousInputs)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            string sourceRoot = ProjectDirectory;
            var previous = new Dictionary<string, InputEntry>(StringComparer.Ordinal);
            if (previousInputs != null)
                foreach (InputEntry entry in previousInputs)
                    if (entry != null && !string.IsNullOrEmpty(entry.Key)) previous[entry.Key] = entry;
            var entries = new SortedDictionary<string, InputEntry>(StringComparer.Ordinal);
            var files = new List<FileInput>();
            int ignoredTestScenes = 0;
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (IsTestRunnerBootstrapScene(path)) { ignoredTestScenes++; continue; }
                if (!IsResourcePath(path)) continue;
                var file = new FileInfo(Path.Combine(sourceRoot, path));
                if (!file.Exists) continue;
                // Unity 的导入哈希只在主线程读取，后台接收冻结后的字符串和文件元数据。
                string importHash = CanReuseImportedHash(path)
                    ? AssetDatabase.GetAssetDependencyHash(path).ToString() : string.Empty;
                if (importHash == "00000000000000000000000000000000") importHash = string.Empty;
                AddFileInput(path, file, importHash, previous, entries, files);
                var meta = new FileInfo(file.FullName + ".meta");
                if (meta.Exists) AddFileInput(path + ".meta", meta, importHash, previous, entries, files);
            }
            // DTO 与解析器按内容失效，普通玩法重新编译仍允许恢复源资产。
            foreach (string contract in new[]
                     {
                         "Assets/5_Scripts/5-3_GamePlay/Entities/Item/Definitions/ItemDefinitionCatalogLoader.cs",
                         "Assets/5_Scripts/5-3_GamePlay/Entities/Item/Definitions/ItemDefinition.cs",
                         "Assets/5_Scripts/5-3_GamePlay/Entities/Item/Definitions/ItemMatterReaction.cs",
                         "Assets/5_Scripts/5-0_WorldModel/WorldGridOccupancyData.cs",
                         "Assets/5_Scripts/5-3_GamePlay/Development/Testing/GameplayTestResourceCache.cs",
                         "Packages/manifest.json", "Packages/packages-lock.json"
                     })
                AddFileInput(contract, new FileInfo(Path.Combine(sourceRoot, contract)), string.Empty,
                    previous, entries, files);
            int hashedFiles = 0, reusedHashes = 0;
            System.Threading.Tasks.Parallel.For<SHA256>(0, files.Count,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 4 },
                SHA256.Create,
                (index, _, hash) =>
                {
                    FileInput file = files[index];
                    InputEntry entry = file.Entry, old = file.Previous;
                    if (!string.IsNullOrEmpty(entry.ImportHash) && old != null &&
                        old.ImportHash == entry.ImportHash && old.Length == entry.Length &&
                        old.WriteTicks == entry.WriteTicks && !string.IsNullOrEmpty(old.Hash))
                    {
                        entry.Hash = old.Hash;
                        System.Threading.Interlocked.Increment(ref reusedHashes);
                    }
                    else
                    {
                        // 每个工作线程复用自己的 SHA，不在后台访问 Unity 或共享散列实例。
                        hash.Initialize();
                        using var stream = new FileStream(file.AbsolutePath, FileMode.Open, FileAccess.Read,
                            FileShare.Read, 65536, FileOptions.SequentialScan);
                        entry.Hash = ToHex(hash.ComputeHash(stream));
                        System.Threading.Interlocked.Increment(ref hashedFiles);
                    }
                    return hash;
                }, hash => hash.Dispose());
            using SHA256 metadataHash = SHA256.Create();
            AddSettingsInputs("ProjectSettings/ProjectSettings.asset", sourceRoot, entries, metadataHash);
            AddSettingsInputs("ProjectSettings/EditorSettings.asset", sourceRoot, entries, metadataHash);
            AddValueInput(entries, "@project", Path.GetFullPath(sourceRoot));
            AddValueInput(entries, "@unity", Application.unityVersion);
            AddValueInput(entries, "@target", EditorUserBuildSettings.activeBuildTarget.ToString());
            AddValueInput(entries, "@spritePacker", EditorSettings.spritePackerMode.ToString());
            foreach (string guid in AssetDatabase.FindAssets("t:AddressableAssetSettings", new[] { "Assets/AddressableAssetsData" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Object settings = AssetDatabase.LoadMainAssetAtPath(path);
                if (settings == null) continue;
                object builder = settings.GetType().GetProperty("ActivePlayModeDataBuilderIndex")?.GetValue(settings);
                AddValueInput(entries, "@addressables-builder:" + path, builder?.ToString() ?? "missing");
            }
            var combined = new StringBuilder("LuaGameResCache:" + CurrentSchema + "\n");
            foreach (InputEntry entry in entries.Values)
                combined.Append(entry.Key).Append('\0').Append(entry.Hash).Append('\n');
            string stamp = ToHex(metadataHash.ComputeHash(Encoding.UTF8.GetBytes(combined.ToString())));
            return new InputFingerprint
            {
                Stamp = stamp, Entries = new List<InputEntry>(entries.Values),
                HashedFiles = hashedFiles, ReusedHashes = reusedHashes, IgnoredTestScenes = ignoredTestScenes,
                Seconds = timer.Elapsed.TotalSeconds
            };
        }

        private static void AddFileInput(string path, FileInfo file, string importHash,
            Dictionary<string, InputEntry> previous, SortedDictionary<string, InputEntry> entries, List<FileInput> files)
        {
            if (entries.ContainsKey(path)) return;
            var entry = new InputEntry
            { Key = path, Length = file.Length, WriteTicks = file.LastWriteTimeUtc.Ticks, ImportHash = importHash };
            entries.Add(path, entry);
            previous.TryGetValue(path, out InputEntry old);
            files.Add(new FileInput { AbsolutePath = file.FullName, Entry = entry, Previous = old });
        }

        private static bool CanReuseImportedHash(string path)
        {
            // 直接读盘的文档始终计算内容 SHA，不能只靠导入库或文件时间判断 JSON 是否变化。
            if (path.StartsWith("Assets/StreamingAssets/", StringComparison.Ordinal)) return false;
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".json": case ".txt": case ".bytes": case ".csv": case ".tsv":
                case ".xml": case ".lua": case ".yaml": case ".yml": return false;
                default: return true;
            }
        }

        private static void AddValueInput(SortedDictionary<string, InputEntry> entries, string key, string value)
            => entries[key] = new InputEntry { Key = key, Hash = value ?? string.Empty };

        private static void AddSettingsInputs(string path, string sourceRoot,
            SortedDictionary<string, InputEntry> entries, SHA256 hash)
        {
            string key = null;
            var block = new StringBuilder();
            foreach (string line in File.ReadAllLines(Path.Combine(sourceRoot, path)))
            {
                bool property = line.StartsWith("  ", StringComparison.Ordinal) && line.Length > 2 &&
                                line[2] != ' ' && line[2] != '-' && line.IndexOf(':', 2) > 2;
                if (property)
                {
                    AddSettingsBlock(path, key, block, entries, hash);
                    key = line.Substring(2, line.IndexOf(':', 2) - 2);
                    block.Clear();
                }
                if (key != null) block.Append(line).Append('\n');
            }
            AddSettingsBlock(path, key, block, entries, hash);
        }

        private static void AddSettingsBlock(string path, string key, StringBuilder block,
            SortedDictionary<string, InputEntry> entries, SHA256 hash)
        {
            if (key == null) return;
            // 这些开关只控制测试启动、焦点与窗口运行，不改变源资产或 DTO 契约。
            if (path.EndsWith("/EditorSettings.asset", StringComparison.Ordinal) &&
                (key == "m_EnterPlayModeOptionsEnabled" || key == "m_EnterPlayModeOptions")) return;
            if (path.EndsWith("/ProjectSettings.asset", StringComparison.Ordinal) &&
                (key == "runInBackground" || key == "playModeTestRunnerEnabled" || key == "runPlayModeTestAsEditModeTest")) return;
            hash.Initialize();
            entries[path + "#" + key] = new InputEntry
            { Key = path + "#" + key, Hash = ToHex(hash.ComputeHash(Encoding.UTF8.GetBytes(block.ToString()))) };
        }

        private static void LogInvalidation(string reason, List<InputEntry> previous, List<InputEntry> current)
        {
            Debug.Log("[Lua GameRes缓存] 缓存不复用，原因：" + reason + "；仅完整 Ready 后发布新的缓存。");
            var old = new Dictionary<string, InputEntry>(StringComparer.Ordinal);
            if (previous != null)
                foreach (InputEntry entry in previous)
                    if (entry != null && !string.IsNullOrEmpty(entry.Key)) old[entry.Key] = entry;
            int changed = 0;
            const int logLimit = 16;
            foreach (InputEntry entry in current)
            {
                if (!old.Remove(entry.Key, out InputEntry before))
                {
                    if (changed++ < logLimit) Debug.Log("[Lua GameRes缓存] 输入新增：" + entry.Key);
                }
                else if (before.Hash != entry.Hash)
                {
                    if (changed++ < logLimit)
                        Debug.Log("[Lua GameRes缓存] 输入改变：" + entry.Key + "；" + before.Hash + " → " + entry.Hash);
                }
            }
            foreach (string key in old.Keys)
                if (changed++ < logLimit) Debug.Log("[Lua GameRes缓存] 输入移除：" + key);
            Debug.Log($"[Lua GameRes缓存] 差异共 {changed} 项，控制台最多显示 {logLimit} 项；" +
                      "完整逐路径 Hash/ImportHash/Length/WriteTicks 保存在 Library 缓存 inputEntries。");
        }

        private static bool IsResourcePath(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                // 串流会话在进入/退出 PlayMode 时自动更新，不影响 GameRes 源资源和 DTO。
                path == "Assets/Settings/FlatWorldRenderStreamingSettings.asset" ||
                path.StartsWith("Assets/StreamingAssets/GameplayTests/", StringComparison.Ordinal) ||
                path.StartsWith("Assets/Screenshots/", StringComparison.Ordinal) ||
                path.StartsWith("Assets/Tests/", StringComparison.Ordinal) ||
                path.StartsWith("Assets/GameTest/", StringComparison.Ordinal) ||
                path.StartsWith("Assets/Editor/", StringComparison.Ordinal) || IsTestRunnerBootstrapScene(path)) return false;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension != ".cs" && extension != ".dll" && extension != ".asmdef" &&
                   extension != ".asmref" && extension != ".pdb" && extension != ".mdb" &&
                   extension != ".rsp" && extension != ".md" && extension != ".meta";
        }

        private static bool IsTestRunnerBootstrapScene(string path)
        {
            const string prefix = "Assets/InitTestScene";
            const string suffix = ".unity";
            return path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith(suffix, StringComparison.Ordinal) &&
                   Guid.TryParseExact(path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length), "D", out _);
        }

        private static string HashFile(string path)
        {
            using SHA256 hash = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return ToHex(hash.ComputeHash(stream));
        }

        private static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty);
        #endregion
    }
}
