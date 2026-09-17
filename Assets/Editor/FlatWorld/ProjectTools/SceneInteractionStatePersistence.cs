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
    /// 持久化 Hierarchy 原生“小眼睛 / 手”状态（Scene Visibility / Scene Picking）。
    /// 开发者通过顶部菜单显式记录；数据仅保存在本机 EditorPrefs，恢复时按已记录对象定向定位。
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneInteractionStatePersistence
    {
        #region 数据

        private const string QuickCaptureMenu = "Tools/记录 Hierarchy 小眼睛与手状态";
        private const string MenuRoot = "Tools/Hierarchy 小眼睛与手持久化/";
        private const string EnabledMenu = MenuRoot + "启用持久化";
        private const string RestoreMenu = MenuRoot + "恢复已记录状态";
        private const string ClearMenu = MenuRoot + "清除已记录状态";
        private const string PreferencePrefix = "FlatWorld.SceneInteractionStatePersistence.";
        private const string NullGlobalId =
            "GlobalObjectId_V1-0-00000000000000000000000000000000-0-0";

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
        }

        private static Database database;
        private static bool suppressEvents;
        private static bool transitionInProgress;
        private static bool hierarchyRestoreQueued;
        private static int restorePasses;

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
                ApplySavedStates();
        }

        #endregion

        #region 生命周期

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

        /// <summary>PlayMode 动态对象进入 Hierarchy 后，事件驱动地安排一次恢复。</summary>
        private static void OnHierarchyChanged()
        {
            if (!IsEnabled || !EditorApplication.isPlaying || suppressEvents || transitionInProgress ||
                hierarchyRestoreQueued)
            {
                return;
            }

            hierarchyRestoreQueued = true;
            EditorApplication.delayCall -= RestoreAfterHierarchyChanged;
            EditorApplication.delayCall += RestoreAfterHierarchyChanged;
        }

        /// <summary>层级变化后的下一次编辑器回调中恢复已记录对象，不使用 Update 轮询。</summary>
        private static void RestoreAfterHierarchyChanged()
        {
            hierarchyRestoreQueued = false;
            if (!IsEnabled || !EditorApplication.isPlaying || transitionInProgress || suppressEvents)
            {
                return;
            }

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
            transitionInProgress = false;
        }

        #endregion

        #region 捕获与恢复

        /// <summary>手动扫描当前已加载场景，并用当前小眼睛/手状态完整替换这些场景的本机记录。</summary>
        private static int CaptureCurrentInteractionStates()
        {
            SceneVisibilityManager manager = SceneVisibilityManager.instance;
            GameObject[] objects = GetSceneObjects();
            EnsureDatabase();

            // 只替换当前已加载场景的记录，避免在一个场景点击“记录”时清掉其它场景的持久化状态。
            HashSet<string> loadedSceneIdentities = GetLoadedSceneIdentities();
            for (int i = database.entries.Count - 1; i >= 0; i--)
            {
                if (BelongsToScene(database.entries[i], loadedSceneIdentities))
                    database.entries.RemoveAt(i);
            }

            int capturedCount = 0;

            for (int i = 0; i < objects.Length; i++)
            {
                GameObject go = objects[i];
                bool hidden = manager.IsHidden(go, false);
                bool pickingDisabled = manager.IsPickingDisabled(go, false);
                if (!hidden && !pickingDisabled)
                    continue;

                Snapshot snapshot = CreateSnapshot(go);
                database.entries.Add(new Entry
                {
                    globalId = snapshot.globalId,
                    fallbackKey = snapshot.fallbackKey,
                    hidden = hidden,
                    pickingDisabled = pickingDisabled
                });
                capturedCount++;
            }

            Save();
            return capturedCount;
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

        /// <summary>通过 Unity 稳定 ID 定位当前已加载的场景对象。</summary>
        private static GameObject ResolveGlobalId(string value)
        {
            if (string.IsNullOrEmpty(value) || !GlobalObjectId.TryParse(value, out GlobalObjectId id))
                return null;

            // Unity 只允许解析所属场景已加载的场景对象，否则原生层会触发 manager 断言。
            if (!IsGlobalIdSceneLoaded(id))
                return null;

            GameObject go = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
            return IsLoadedSceneObject(go) ? go : null;
        }

        /// <summary>确认稳定 ID 指向当前已加载的普通场景。</summary>
        private static bool IsGlobalIdSceneLoaded(GlobalObjectId id)
        {
            if (id.identifierType != 2)
                return false;

            string scenePath = AssetDatabase.GUIDToAssetPath(id.assetGUID.ToString());
            if (string.IsNullOrEmpty(scenePath))
                return false;

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            return scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene);
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

        /// <summary>收集当前已加载普通场景的稳定标识。</summary>
        private static HashSet<string> GetLoadedSceneIdentities()
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
                    continue;

                result.Add(GetSceneIdentity(scene));
            }

            return result;
        }

        /// <summary>判断记录是否属于本次手动采集覆盖的已加载场景。</summary>
        private static bool BelongsToScene(Entry entry, HashSet<string> sceneIdentities)
        {
            if (entry == null || string.IsNullOrEmpty(entry.fallbackKey))
                return false;

            int separatorIndex = entry.fallbackKey.IndexOf('|');
            if (separatorIndex <= 0)
                return false;

            return sceneIdentities.Contains(entry.fallbackKey.Substring(0, separatorIndex));
        }

        private static Snapshot CreateSnapshot(GameObject go)
        {
            string globalId = string.Empty;
            if (!EditorApplication.isPlaying && !string.IsNullOrEmpty(go.scene.path))
            {
                globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
                if (string.Equals(globalId, NullGlobalId, StringComparison.Ordinal))
                    globalId = string.Empty;
            }

            return new Snapshot
            {
                globalId = globalId,
                fallbackKey = GetSceneIdentity(go.scene) + "|" + GetHierarchyPath(go.transform)
            };
        }

        /// <summary>统一生成用于本机持久化的场景标识。</summary>
        private static string GetSceneIdentity(Scene scene)
        {
            return string.IsNullOrEmpty(scene.path)
                ? "<" + scene.name + ">"
                : scene.path;
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
            Save();
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
            hierarchyRestoreQueued = false;
            restorePasses = 0;
            EditorApplication.delayCall -= RestoreAfterHierarchyChanged;
            EditorApplication.delayCall -= RestorePass;

            if (enabled)
                ApplySavedStates();

            Debug.Log("[FlatWorld][Hierarchy状态] 小眼睛/手持久化：" + (enabled ? "已启用" : "已停用"));
        }

        [MenuItem(EnabledMenu, true)]
        private static bool ValidateToggleEnabled()
        {
            Menu.SetChecked(EnabledMenu, IsEnabled);
            return true;
        }

        [MenuItem(QuickCaptureMenu, priority = 100)]
        private static void CaptureCurrent()
        {
            int count = CaptureCurrentInteractionStates();
            Debug.Log("[FlatWorld][Hierarchy状态] 已手动记录当前小眼睛/手状态，共 " + count + " 个非默认对象。");
        }

        [MenuItem(RestoreMenu, priority = 2002)]
        private static void RestoreCurrent()
        {
            ApplySavedStates();
            Debug.Log("[FlatWorld][Hierarchy状态] 已恢复当前小眼睛/手状态。");
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
