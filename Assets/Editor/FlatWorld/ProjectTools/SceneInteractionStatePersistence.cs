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
    /// 数据仅保存在本机 EditorPrefs，不修改场景、Prefab 或运行时存档。
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
        private const double RuntimeNamePollIntervalSeconds = 1.0d;

        [Serializable]
        private sealed class Database
        {
            public List<Entry> entries = new List<Entry>();
            public List<RuntimeNameEntry> runtimeNameEntries = new List<RuntimeNameEntry>();
        }

        [Serializable]
        private sealed class Entry
        {
            public string globalId;
            public string fallbackKey;
            public bool hidden;
            public bool pickingDisabled;
        }

        [Serializable]
        private sealed class RuntimeNameEntry
        {
            public string name;
            public bool hidden;
            public bool pickingDisabled;
        }

        private struct Snapshot
        {
            public string name;
            public string globalId;
            public string fallbackKey;
            public bool hidden;
            public bool pickingDisabled;
        }

        private static Database database;
        private static Dictionary<string, Snapshot> previous = new Dictionary<string, Snapshot>();
        private static bool suppressEvents;
        private static bool transitionInProgress;
        private static bool captureQueued;
        private static int restorePasses;
        private static double lastRuntimeNamePollTime;

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
                // 安装插件前已经点过的眼睛/手也直接纳入记录。
                MergeCurrentNonDefaultStates();
                ApplySavedStates();
            }

            previous = BuildSnapshot();
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
                BeginTransition();
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
                previous = BuildSnapshot();
                return;
            }

            transitionInProgress = true;
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
                previous = BuildSnapshot();
                return;
            }

            ApplySavedStates();

            restorePasses--;
            if (restorePasses > 0)
            {
                EditorApplication.delayCall += RestorePass;
                return;
            }

            transitionInProgress = false;
            previous = BuildSnapshot();
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
            Dictionary<string, Snapshot> current = BuildSnapshot();
            bool changed = false;

            foreach (KeyValuePair<string, Snapshot> pair in current)
            {
                Snapshot now = pair.Value;

                if (previous.TryGetValue(pair.Key, out Snapshot before))
                {
                    if (before.hidden == now.hidden &&
                        before.pickingDisabled == now.pickingDisabled)
                    {
                        continue;
                    }
                }
                else if (!now.hidden && !now.pickingDisabled)
                {
                    continue;
                }

                changed |= SetState(now);
            }

            previous = current;
            if (changed)
                Save();
        }

        private static void MergeCurrentNonDefaultStates()
        {
            SceneVisibilityManager manager = SceneVisibilityManager.instance;
            GameObject[] objects = GetSceneObjects();
            bool changed = false;

            for (int i = 0; i < objects.Length; i++)
            {
                GameObject go = objects[i];
                Snapshot state = CreateSnapshot(
                    go,
                    manager.IsHidden(go, false),
                    manager.IsPickingDisabled(go, false));

                if (state.hidden || state.pickingDisabled)
                    changed |= SetState(state);
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
            GameObject[] objects = GetSceneObjects();
            suppressEvents = true;

            try
            {
                for (int i = 0; i < objects.Length; i++)
                {
                    GameObject go = objects[i];
                    Snapshot identity = CreateSnapshot(go, false, false);
                    int index = FindEntry(identity);
                    if (index < 0)
                        continue;

                    Entry entry = database.entries[index];
                    bool hidden = manager.IsHidden(go, false);
                    bool pickingDisabled = manager.IsPickingDisabled(go, false);

                    if (hidden != entry.hidden)
                    {
                        if (entry.hidden)
                            manager.Hide(go, false);
                        else
                            manager.Show(go, false);
                    }

                    if (pickingDisabled != entry.pickingDisabled)
                    {
                        if (entry.pickingDisabled)
                            manager.DisablePicking(go, false);
                        else
                            manager.EnablePicking(go, false);
                    }
                }
            }
            finally
            {
                suppressEvents = false;
            }

            EditorApplication.RepaintHierarchyWindow();
            SceneView.RepaintAll();
        }

        #endregion

        #region 对象定位

        private static Dictionary<string, Snapshot> BuildSnapshot()
        {
            SceneVisibilityManager manager = SceneVisibilityManager.instance;
            GameObject[] objects = GetSceneObjects();
            Dictionary<string, Snapshot> result = new Dictionary<string, Snapshot>(objects.Length);

            for (int i = 0; i < objects.Length; i++)
            {
                GameObject go = objects[i];
                Snapshot state = CreateSnapshot(
                    go,
                    manager.IsHidden(go, false),
                    manager.IsPickingDisabled(go, false));
                result[GetCanonicalKey(state)] = state;
            }

            return result;
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
            string globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
            if (string.Equals(globalId, NullGlobalId, StringComparison.Ordinal))
                globalId = string.Empty;

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

        private static string GetCanonicalKey(Snapshot state)
        {
            return !string.IsNullOrEmpty(state.globalId)
                ? "gid:" + state.globalId
                : "path:" + state.fallbackKey;
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
            EnsureDatabase();
            int index = FindEntry(state);

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
            bool changed =
                entry.hidden != state.hidden ||
                entry.pickingDisabled != state.pickingDisabled ||
                !string.Equals(entry.globalId, state.globalId, StringComparison.Ordinal) ||
                !string.Equals(entry.fallbackKey, state.fallbackKey, StringComparison.Ordinal);

            entry.globalId = state.globalId;
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
            restorePasses = 0;

            if (enabled)
            {
                MergeCurrentNonDefaultStates();
                ApplySavedStates();
            }

            previous = BuildSnapshot();
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
            MergeCurrentNonDefaultStates();
            previous = BuildSnapshot();
            Debug.Log("[FlatWorld][Hierarchy状态] 已记录当前眼睛/手状态。");
        }

        [MenuItem(RestoreMenu, priority = 2002)]
        private static void RestoreCurrent()
        {
            ApplySavedStates();
            previous = BuildSnapshot();
            Debug.Log("[FlatWorld][Hierarchy状态] 已恢复当前眼睛/手状态。");
        }

        [MenuItem(ClearMenu, priority = 2003)]
        private static void ClearSaved()
        {
            EnsureDatabase();
            database.entries.Clear();
            Save();
            previous = BuildSnapshot();
            Debug.Log("[FlatWorld][Hierarchy状态] 已清除持久化记录；当前场景不会被改动。");
        }

        #endregion
    }
}
#endif
