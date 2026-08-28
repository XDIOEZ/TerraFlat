#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.EditorTools
{
    /// <summary>
    /// 持久化 Hierarchy 原生“眼睛 / 手”状态（Scene Visibility / Scene Picking）。
    /// 数据仅保存在本机 EditorPrefs；恢复时按已记录对象定向定位，不扫描整个运行时 Hierarchy。
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneInteractionStatePersistence
    {
        #region 数据

        private const string MenuRoot = "FlatWorld/编辑器/Hierarchy 眼睛与手持久化/";
        private const string EnabledMenu = MenuRoot + "启用持久化";
        private const string CaptureMenu = MenuRoot + "记录当前状态";
        private const string RestoreMenu = MenuRoot + "恢复已记录状态";
        private const string ClearMenu = MenuRoot + "清除已记录状态";
        private const string PreferencePrefix = "FlatWorld.SceneInteractionStatePersistence.";
        private const string NullGlobalId =
            "GlobalObjectId_V1-0-00000000000000000000000000000000-0-0";
        private const double RuntimeRestoreIntervalSeconds = 1.0d;

        [Serializable]
        private sealed class Database
        {
            public List<Entry> entries = new List<Entry>();
        }

        [Serializable]
        private sealed class Entry
        {
            public string globalId;
            public string fallbackKey;
            public bool hidden;
            public bool pickingDisabled;
        }

        private struct Snapshot
        {
            public string globalId;
            public string fallbackKey;
            public bool hidden;
            public bool pickingDisabled;
        }

        private static Database database;
        private static bool suppressEvents;
        private static bool transitionInProgress;
        private static bool captureQueued;
        private static bool runtimeRestorePending;
        private static bool hasPersistedDatabase;
        private static int restorePasses;
        private static double lastRuntimeRestoreTime;

        private static string ProjectKey =>
            Hash128.Compute(Application.dataPath.Replace('\\', '/').ToLowerInvariant()).ToString();

        private static string StateKey => PreferencePrefix + ProjectKey + ".States";
        private static string EnabledKey => PreferencePrefix + ProjectKey + ".Enabled";
        private static bool IsEnabled => EditorPrefs.GetBool(EnabledKey, true);

        #endregion

        #region 初始化

        static SceneInteractionStatePersistence()
        {
            Load();
            RegisterEvents();
            EditorApplication.delayCall += InitialSync;
        }

        private static void RegisterEvents()
        {
            SceneVisibilityManager.pickingChanged -= OnNativeStateChanged;
            SceneVisibilityManager.pickingChanged += OnNativeStateChanged;
            SceneVisibilityManager.visibilityChanged -= OnNativeStateChanged;
            SceneVisibilityManager.visibilityChanged += OnNativeStateChanged;

            EditorSceneManager.sceneOpening -= OnSceneOpening;
            EditorSceneManager.sceneOpening += OnSceneOpening;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;

            SceneManager.sceneLoaded -= OnRuntimeSceneLoaded;
            SceneManager.sceneLoaded += OnRuntimeSceneLoaded;
            SceneManager.sceneUnloaded -= OnRuntimeSceneUnloaded;
            SceneManager.sceneUnloaded += OnRuntimeSceneUnloaded;

            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.update -= RestoreRuntimeObjects;
            EditorApplication.update += RestoreRuntimeObjects;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= SaveBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += SaveBeforeReload;
            EditorApplication.quitting -= SaveBeforeReload;
            EditorApplication.quitting += SaveBeforeReload;
        }

        private static void InitialSync()
        {
            if (IsEnabled)
            {
                // 首次安装时只扫描一次，后续域重载直接按已保存条目恢复。
                if (!hasPersistedDatabase)
                {
                    MergeCurrentNonDefaultStates();
                    Save();
                }

                ApplySavedStates();
            }
        }

        #endregion

        #region 生命周期

        private static void OnNativeStateChanged()
        {
            if (!IsEnabled || suppressEvents || transitionInProgress || captureQueued)
                return;

            captureQueued = true;
            EditorApplication.delayCall += CaptureChangedStates;
        }

        private static void OnSceneOpening(string path, OpenSceneMode mode)
        {
            BeginTransition();
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            QueueRestore();
        }

        private static void OnRuntimeSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (EditorApplication.isPlaying)
                QueueRestore();
        }

        private static void OnRuntimeSceneUnloaded(Scene scene)
        {
            if (EditorApplication.isPlaying)
            {
                // CreateScene 不触发 sceneLoaded；卸载旧场景后必须主动结束有限恢复。
                QueueRestore();
            }
        }

        /// <summary>动态对象进入 Hierarchy 后标记一次增量恢复。</summary>
        private static void OnHierarchyChanged()
        {
            if (IsEnabled && EditorApplication.isPlaying && !suppressEvents)
                runtimeRestorePending = true;
        }

        /// <summary>按层级变化恢复晚于切场景流程生成的运行时对象。</summary>
        private static void RestoreRuntimeObjects()
        {
            if (!runtimeRestorePending || !IsEnabled || !EditorApplication.isPlaying ||
                transitionInProgress || suppressEvents)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now - lastRuntimeRestoreTime < RuntimeRestoreIntervalSeconds)
                return;

            runtimeRestorePending = false;
            lastRuntimeRestoreTime = now;

            // 用户刚点的新状态优先写入数据库，避免轮询把操作反向覆盖。
            FlushCapture();
            EnsureDatabase();
            if (database.entries.Count == 0)
                return;

            ApplySavedStates();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.ExitingPlayMode:
                    BeginTransition();
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    QueueRestore();
                    break;
            }
        }

        private static void BeginTransition()
        {
            if (!IsEnabled)
                return;

            FlushCapture();
            transitionInProgress = true;
        }

        private static void QueueRestore()
        {
            if (!IsEnabled)
            {
                transitionInProgress = false;
                return;
            }

            transitionInProgress = true;
            runtimeRestorePending = true;
            restorePasses = Mathf.Max(restorePasses, 3);
            EditorApplication.delayCall -= RestorePass;
            EditorApplication.delayCall += RestorePass;
        }

        private static void RestorePass()
        {
            if (!IsEnabled)
            {
                restorePasses = 0;
                transitionInProgress = false;
                return;
            }

            ApplySavedStates();

            restorePasses--;
            if (restorePasses > 0)
            {
                EditorApplication.delayCall += RestorePass;
                return;
            }

            runtimeRestorePending = false;
            transitionInProgress = false;
        }

        #endregion

        #region 捕获与恢复

        private static void FlushCapture()
        {
            if (!captureQueued || transitionInProgress || suppressEvents || !IsEnabled)
                return;

            captureQueued = false;
            CaptureChangedStatesInternal();
        }

        private static void CaptureChangedStates()
        {
            captureQueued = false;
            if (transitionInProgress || suppressEvents || !IsEnabled)
                return;

            CaptureChangedStatesInternal();
        }

        private static void CaptureChangedStatesInternal()
        {
            SceneVisibilityManager manager = SceneVisibilityManager.instance;
            GameObject[] objects = GetSceneObjects();
            Dictionary<int, Entry> resolvedEntries = BuildResolvedEntryMap();
            bool changed = false;

            for (int i = 0; i < objects.Length; i++)
            {
                GameObject go = objects[i];
                bool hidden = manager.IsHidden(go, false);
                bool pickingDisabled = manager.IsPickingDisabled(go, false);
                resolvedEntries.TryGetValue(go.GetInstanceID(), out Entry resolvedEntry);
                if (resolvedEntry == null && !hidden && !pickingDisabled)
                    continue;

                changed |= SetState(
                    CreateSnapshot(go, hidden, pickingDisabled),
                    resolvedEntry);
            }

            if (changed)
                Save();
        }

        private static void MergeCurrentNonDefaultStates()
        {
            SceneVisibilityManager manager = SceneVisibilityManager.instance;
            GameObject[] objects = GetSceneObjects();
            Dictionary<int, Entry> resolvedEntries = BuildResolvedEntryMap();
            bool changed = false;

            for (int i = 0; i < objects.Length; i++)
            {
                GameObject go = objects[i];
                bool hidden = manager.IsHidden(go, false);
                bool pickingDisabled = manager.IsPickingDisabled(go, false);
                if (!hidden && !pickingDisabled)
                    continue;

                resolvedEntries.TryGetValue(go.GetInstanceID(), out Entry resolvedEntry);
                changed |= SetState(
                    CreateSnapshot(go, hidden, pickingDisabled),
                    resolvedEntry);
            }

            if (changed)
                Save();
        }

        private static void ApplySavedStates()
        {
            EnsureDatabase();
            if (database.entries.Count == 0)
                return;

            SceneVisibilityManager manager = SceneVisibilityManager.instance;
            bool changed = false;
            suppressEvents = true;

            try
            {
                for (int i = 0; i < database.entries.Count; i++)
                {
                    Entry entry = database.entries[i];
                    GameObject go = ResolveEntry(entry);
                    if (go == null)
                        continue;

                    bool hidden = manager.IsHidden(go, false);
                    bool pickingDisabled = manager.IsPickingDisabled(go, false);

                    if (hidden != entry.hidden)
                    {
                        if (entry.hidden)
                            manager.Hide(go, false);
                        else
                            manager.Show(go, false);

                        changed = true;
                    }

                    if (pickingDisabled != entry.pickingDisabled)
                    {
                        if (entry.pickingDisabled)
                            manager.DisablePicking(go, false);
                        else
                            manager.EnablePicking(go, false);

                        changed = true;
                    }
                }
            }
            finally
            {
                suppressEvents = false;
            }

            if (changed)
            {
                EditorApplication.RepaintHierarchyWindow();
                SceneView.RepaintAll();
            }
        }

        #endregion

        #region 对象定位

        /// <summary>按稳定 ID 或精确层级路径直接定位已记录对象。</summary>
        private static GameObject ResolveEntry(Entry entry)
        {
            // PlayMode 优先走轻量层级路径；对象被移入 DontDestroyOnLoad 时再使用稳定 ID。
            if (EditorApplication.isPlaying)
                return ResolveFallbackKey(entry.fallbackKey) ?? ResolveGlobalId(entry.globalId);

            return ResolveGlobalId(entry.globalId) ?? ResolveFallbackKey(entry.fallbackKey);
        }

        /// <summary>建立当前对象实例到已保存记录的临时映射，仅用于用户主动修改状态时。</summary>
        private static Dictionary<int, Entry> BuildResolvedEntryMap()
        {
            EnsureDatabase();
            Dictionary<int, Entry> result = new Dictionary<int, Entry>(database.entries.Count);

            for (int i = 0; i < database.entries.Count; i++)
            {
                Entry entry = database.entries[i];
                GameObject go = ResolveEntry(entry);
                if (go != null && !result.ContainsKey(go.GetInstanceID()))
                    result.Add(go.GetInstanceID(), entry);
            }

            return result;
        }

        /// <summary>通过 Unity 稳定 ID 定位当前已加载的场景对象。</summary>
        private static GameObject ResolveGlobalId(string value)
        {
            if (string.IsNullOrEmpty(value) || !GlobalObjectId.TryParse(value, out GlobalObjectId id))
                return null;

            GameObject go = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
            return IsLoadedSceneObject(go) ? go : null;
        }

        /// <summary>通过“场景标识|层级路径”精确定位对象。</summary>
        private static GameObject ResolveFallbackKey(string fallbackKey)
        {
            if (string.IsNullOrEmpty(fallbackKey))
                return null;

            int separatorIndex = fallbackKey.IndexOf('|');
            if (separatorIndex <= 0 || separatorIndex >= fallbackKey.Length - 1)
                return null;

            string sceneIdentity = fallbackKey.Substring(0, separatorIndex);
            string hierarchyPath = fallbackKey.Substring(separatorIndex + 1);
            Scene scene = FindLoadedScene(sceneIdentity);
            if (!scene.IsValid())
                return null;

            string[] parts = hierarchyPath.Split('/');
            if (parts.Length == 0)
                return null;

            GameObject[] roots = scene.GetRootGameObjects();
            Transform current = null;

            for (int i = 0; i < parts.Length; i++)
            {
                if (!TryParseHierarchyPart(parts[i], out int siblingIndex, out string expectedName))
                    return null;

                if (current == null)
                {
                    if (siblingIndex < 0 || siblingIndex >= roots.Length)
                        return null;

                    current = roots[siblingIndex].transform;
                }
                else
                {
                    if (siblingIndex < 0 || siblingIndex >= current.childCount)
                        return null;

                    current = current.GetChild(siblingIndex);
                }

                if (!string.Equals(current.name, expectedName, StringComparison.Ordinal))
                    return null;
            }

            return current != null ? current.gameObject : null;
        }

        /// <summary>查找当前已加载且非预览的目标场景。</summary>
        private static Scene FindLoadedScene(string sceneIdentity)
        {
            bool matchByName = sceneIdentity.Length >= 2 &&
                               sceneIdentity[0] == '<' &&
                               sceneIdentity[sceneIdentity.Length - 1] == '>';
            string expectedName = matchByName
                ? sceneIdentity.Substring(1, sceneIdentity.Length - 2)
                : string.Empty;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
                    continue;

                bool matches = matchByName
                    ? string.Equals(scene.name, expectedName, StringComparison.Ordinal)
                    : string.Equals(scene.path, sceneIdentity, StringComparison.Ordinal);
                if (matches)
                    return scene;
            }

            return default;
        }

        /// <summary>解析“同级序号:对象名”层级片段。</summary>
        private static bool TryParseHierarchyPart(string part, out int siblingIndex, out string objectName)
        {
            siblingIndex = -1;
            objectName = string.Empty;
            int separatorIndex = part.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= part.Length - 1)
                return false;

            objectName = part.Substring(separatorIndex + 1);
            return int.TryParse(part.Substring(0, separatorIndex), out siblingIndex);
        }

        /// <summary>判断对象是否属于当前已加载的普通场景。</summary>
        private static bool IsLoadedSceneObject(GameObject go)
        {
            if (go == null || EditorUtility.IsPersistent(go))
                return false;

            Scene scene = go.scene;
            return scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene);
        }

        private static GameObject[] GetSceneObjects()
        {
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            List<GameObject> result = new List<GameObject>(all.Length);

            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i];
                if (go == null || EditorUtility.IsPersistent(go))
                    continue;

                Scene scene = go.scene;
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
                    continue;

                result.Add(go);
            }

            return result.ToArray();
        }

        private static Snapshot CreateSnapshot(GameObject go, bool hidden, bool pickingDisabled)
        {
            string globalId = string.Empty;
            if (!EditorApplication.isPlaying && !string.IsNullOrEmpty(go.scene.path))
            {
                globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                if (string.Equals(globalId, NullGlobalId, StringComparison.Ordinal))
                    globalId = string.Empty;
            }

            string sceneIdentity = string.IsNullOrEmpty(go.scene.path)
                ? "<" + go.scene.name + ">"
                : go.scene.path;

            return new Snapshot
            {
                globalId = globalId,
                fallbackKey = sceneIdentity + "|" + GetHierarchyPath(go.transform),
                hidden = hidden,
                pickingDisabled = pickingDisabled
            };
        }

        private static string GetHierarchyPath(Transform transform)
        {
            List<string> parts = new List<string>();
            Transform current = transform;

            while (current != null)
            {
                parts.Add(current.GetSiblingIndex() + ":" + current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        #endregion

        #region 数据库

        private static void EnsureDatabase()
        {
            if (database == null)
                database = new Database();
            if (database.entries == null)
                database.entries = new List<Entry>();
        }

        private static void Load()
        {
            hasPersistedDatabase = EditorPrefs.HasKey(StateKey);
            string json = EditorPrefs.GetString(StateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                database = new Database();
                return;
            }

            try
            {
                database = JsonUtility.FromJson<Database>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[FlatWorld][Hierarchy状态] 读取失败，已重置本机记录：" + exception.Message);
                database = new Database();
            }

            EnsureDatabase();
        }

        private static void Save()
        {
            EnsureDatabase();
            EditorPrefs.SetString(StateKey, JsonUtility.ToJson(database));
            hasPersistedDatabase = true;
        }

        private static void SaveBeforeReload()
        {
            FlushCapture();
            Save();
        }

        private static int FindEntry(Snapshot state)
        {
            EnsureDatabase();

            if (!string.IsNullOrEmpty(state.globalId))
            {
                for (int i = 0; i < database.entries.Count; i++)
                {
                    if (string.Equals(database.entries[i].globalId, state.globalId, StringComparison.Ordinal))
                        return i;
                }
            }

            for (int i = 0; i < database.entries.Count; i++)
            {
                if (string.Equals(database.entries[i].fallbackKey, state.fallbackKey, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        private static bool SetState(Snapshot state)
        {
            return SetState(state, null);
        }

        /// <summary>写入状态；已解析记录用于 PlayMode 中对象跨场景后保持身份。</summary>
        private static bool SetState(Snapshot state, Entry resolvedEntry)
        {
            EnsureDatabase();
            int index = resolvedEntry != null
                ? database.entries.IndexOf(resolvedEntry)
                : FindEntry(state);
            if (index < 0 && resolvedEntry != null)
                index = FindEntry(state);

            // 两个原生开关都回到默认值时删除记录，之后不会再强制覆盖 Unity。
            if (!state.hidden && !state.pickingDisabled)
            {
                if (index < 0)
                    return false;

                database.entries.RemoveAt(index);
                return true;
            }

            if (index < 0)
            {
                database.entries.Add(new Entry
                {
                    globalId = state.globalId,
                    fallbackKey = state.fallbackKey,
                    hidden = state.hidden,
                    pickingDisabled = state.pickingDisabled
                });
                return true;
            }

            Entry entry = database.entries[index];
            string globalId = string.IsNullOrEmpty(state.globalId)
                ? entry.globalId
                : state.globalId;
            bool changed =
                entry.hidden != state.hidden ||
                entry.pickingDisabled != state.pickingDisabled ||
                !string.Equals(entry.globalId, globalId, StringComparison.Ordinal) ||
                !string.Equals(entry.fallbackKey, state.fallbackKey, StringComparison.Ordinal);

            entry.globalId = globalId;
            entry.fallbackKey = state.fallbackKey;
            entry.hidden = state.hidden;
            entry.pickingDisabled = state.pickingDisabled;
            return changed;
        }

        #endregion

        #region 菜单

        [MenuItem(EnabledMenu, priority = 2000)]
        private static void ToggleEnabled()
        {
            bool enabled = !IsEnabled;
            EditorPrefs.SetBool(EnabledKey, enabled);
            Menu.SetChecked(EnabledMenu, enabled);
            transitionInProgress = false;
            captureQueued = false;
            runtimeRestorePending = false;
            restorePasses = 0;
            lastRuntimeRestoreTime = 0d;

            if (enabled)
            {
                MergeCurrentNonDefaultStates();
                ApplySavedStates();
            }
            Debug.Log("[FlatWorld][Hierarchy状态] 眼睛/手持久化：" + (enabled ? "已启用" : "已停用"));
        }

        [MenuItem(EnabledMenu, true)]
        private static bool ValidateToggleEnabled()
        {
            Menu.SetChecked(EnabledMenu, IsEnabled);
            return true;
        }

        [MenuItem(CaptureMenu, priority = 2001)]
        private static void CaptureCurrent()
        {
            CaptureChangedStatesInternal();
            Debug.Log("[FlatWorld][Hierarchy状态] 已记录当前眼睛/手状态。");
        }

        [MenuItem(RestoreMenu, priority = 2002)]
        private static void RestoreCurrent()
        {
            ApplySavedStates();
            Debug.Log("[FlatWorld][Hierarchy状态] 已恢复当前眼睛/手状态。");
        }

        [MenuItem(ClearMenu, priority = 2003)]
        private static void ClearSaved()
        {
            EnsureDatabase();
            database.entries.Clear();
            Save();
            Debug.Log("[FlatWorld][Hierarchy状态] 已清除持久化记录；当前场景不会被改动。");
        }

        #endregion
    }
}
#endif
