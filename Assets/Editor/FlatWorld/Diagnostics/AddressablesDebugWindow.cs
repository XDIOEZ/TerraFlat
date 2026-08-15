#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace FlatWorld.Editor.Diagnostics
{
    /// <summary>
    /// Addressables 资源逐个加载诊断窗口。
    /// 不依赖 GameRes、场景或 Play Mode，记录 PrimaryKey、InternalId、ProviderId，
    /// 并在单个资源超时后继续下一个资源，定位 AssetDatabaseProvider 的具体触发项。
    /// </summary>
    internal sealed class AddressablesDebugWindow : EditorWindow
    {
        #region 常量与字段

        private const string MenuPath = "FlatWorld/诊断/Addressables 逐个加载测试";
        private const string LogPrefix = "[AddressablesDebug]";
        private const double LocationTimeoutSeconds = 10d;
        private const double AssetTimeoutSeconds = 5d;

        private static AddressablesDebugWindow instance;
        private static Action<AsyncOperationHandle, Exception> previousExceptionHandler;
        private static bool exceptionTraceInstalled;

        private readonly Stack<IEnumerator> routineStack = new Stack<IEnumerator>();
        private string status = "未开始。请先点击一个测试按钮。";
        private string specifiedKey = string.Empty;
        private DebugAssetType specifiedType = DebugAssetType.Sprite;

        /// <summary>指定地址测试的资源类型。</summary>
        private enum DebugAssetType
        {
            GameObject,
            Sprite,
            RuntimeAnimatorController
        }

        #endregion

        #region 窗口入口

        /// <summary>打开独立 Addressables 诊断窗口。</summary>
        [MenuItem(MenuPath)]
        private static void OpenWindow()
        {
            instance = GetWindow<AddressablesDebugWindow>("Addressables 诊断");
            instance.minSize = new Vector2(520f, 420f);
            instance.Show();
        }

        /// <summary>菜单项始终可用，不要求进入 Play Mode。</summary>
        [MenuItem(MenuPath, true)]
        private static bool ValidateOpenWindow()
        {
            return true;
        }

        private void OnDisable()
        {
            StopRunningRoutine();
            UninstallExceptionTrace();
        }

        #endregion

        #region 界面

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "请保持 Edit Mode 使用本窗口，不要点击 Play。测试会把每个资源单独加载，" +
                "在 Console 中搜索 [AddressablesDebug]。",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("安装异常追踪", GUILayout.Height(26f)))
                    InstallExceptionTrace();
                if (GUILayout.Button("停止当前测试", GUILayout.Height(26f)))
                    StopRunningRoutine();
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Prefab / 标签测试", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("打印 Prefab 位置"))
                {
                    StartRoutine(
                        PrintResourceLocations(
                            new List<object> { "ItemPrefab", "Prefab" },
                            typeof(GameObject),
                            "Prefab 标签"),
                        "正在查询 Prefab 位置...");
                }

                if (GUILayout.Button("逐个测试 Prefab"))
                {
                    StartRoutine(
                        TestResourceLocations<GameObject>(
                            new List<object> { "ItemPrefab", "Prefab" },
                            "Prefab 标签"),
                        "正在逐个测试 Prefab...");
                }
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("JSON 地址测试", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("逐个测试 Actor"))
                    StartRoutine(TestBuiltInActors(), "正在逐个测试 Actor 地址...");

                if (GUILayout.Button("逐个测试 JSON Sprite"))
                {
                    StartRoutine(
                        TestResourceLocations<Sprite>(
                            CollectBuiltInSpriteKeys(),
                            "JSON Sprite 地址"),
                        "正在逐个测试 JSON Sprite...");
                }
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("指定地址测试", EditorStyles.boldLabel);
            specifiedKey = EditorGUILayout.TextField("Addressable Key", specifiedKey);
            specifiedType = (DebugAssetType)EditorGUILayout.EnumPopup("资源类型", specifiedType);
            if (GUILayout.Button("测试指定地址"))
                StartSpecifiedTest();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("当前状态", status, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                "结果判断：LOAD_OK 表示通过；LOAD_FAILED 表示句柄正常失败；" +
                "LOAD_TIMEOUT 且同时出现 DynamicInvoke 时，该 LOCATION 就是异常候选。",
                EditorStyles.wordWrappedMiniLabel);
        }

        #endregion

        #region 异常追踪

        /// <summary>安装 Addressables 全局异常处理器，补充句柄 DebugName。</summary>
        private static void InstallExceptionTrace()
        {
            if (exceptionTraceInstalled)
            {
                Debug.Log($"{LogPrefix} 异常追踪已经安装。");
                return;
            }

            previousExceptionHandler = ResourceManager.ExceptionHandler;
            ResourceManager.ExceptionHandler = HandleAddressablesException;
            exceptionTraceInstalled = true;
            Debug.Log(
                $"{LogPrefix} 异常追踪已安装。注意：AssetDatabaseProvider 在包内部直接抛出的异常，" +
                "可能只会表现为 LOAD_TIMEOUT + DynamicInvoke，请同时保留两类日志。");
        }

        /// <summary>恢复安装诊断前的 Addressables 异常处理器。</summary>
        private static void UninstallExceptionTrace()
        {
            if (!exceptionTraceInstalled)
                return;

            ResourceManager.ExceptionHandler = previousExceptionHandler;
            previousExceptionHandler = null;
            exceptionTraceInstalled = false;
        }

        /// <summary>输出句柄异常与 DebugName，并尽量保留原处理器行为。</summary>
        private static void HandleAddressablesException(
            AsyncOperationHandle handle,
            Exception exception)
        {
            string debugName = "<invalid handle>";
            try
            {
                if (handle.IsValid())
                    debugName = handle.DebugName;
            }
            catch (Exception debugException)
            {
                debugName = $"<读取 DebugName 失败：{debugException.Message}>";
            }

            Debug.LogError(
                $"{LogPrefix} EXCEPTION_HANDLER\nDebugName={debugName}\nException={exception}");

            try
            {
                previousExceptionHandler?.Invoke(handle, exception);
            }
            catch (Exception callbackException)
            {
                Debug.LogError($"{LogPrefix} 原异常处理器执行失败：\n{callbackException}");
            }
        }

        #endregion

        #region 协程调度

        /// <summary>用 EditorApplication.update 驱动诊断协程，不依赖场景中的 MonoBehaviour。</summary>
        private void StartRoutine(IEnumerator routine, string runningStatus)
        {
            StopRunningRoutine();
            if (routine == null)
                return;

            InstallExceptionTrace();
            routineStack.Push(routine);
            status = runningStatus;
            EditorApplication.update += AdvanceRoutine;
            AdvanceRoutine();
        }

        /// <summary>每次编辑器更新推进一次诊断协程，并把异常变成明确日志。</summary>
        private void AdvanceRoutine()
        {
            if (routineStack.Count == 0)
                return;

            try
            {
                while (routineStack.Count > 0)
                {
                    IEnumerator currentRoutine = routineStack.Peek();
                    if (!currentRoutine.MoveNext())
                    {
                        routineStack.Pop();
                        continue;
                    }

                    if (currentRoutine.Current is IEnumerator nestedRoutine)
                        routineStack.Push(nestedRoutine);

                    break;
                }

                if (routineStack.Count == 0)
                {
                    StopRunningRoutine();
                    status = "测试完成，请把 Console 中的 [AddressablesDebug] 日志发回。";
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"{LogPrefix} 诊断协程未处理异常：\n{exception}");
                StopRunningRoutine();
                status = "测试自身抛异常，请把 Console 中完整堆栈发回。";
            }

            Repaint();
        }

        /// <summary>停止当前测试并解除 Editor 更新回调。</summary>
        private void StopRunningRoutine()
        {
            EditorApplication.update -= AdvanceRoutine;
            routineStack.Clear();
            if (status.StartsWith("正在", StringComparison.Ordinal))
                status = "测试已停止。";
        }

        #endregion

        #region 位置查询与逐个加载

        /// <summary>只查询资源位置，打印方括号、InternalId 和 ProviderId。</summary>
        private static IEnumerator PrintResourceLocations(
            IList<object> keys,
            Type requestedType,
            string source)
        {
            AsyncOperationHandle<IList<IResourceLocation>> locationsHandle;
            if (!TryLoadResourceLocations(keys, requestedType, source, out locationsHandle))
                yield break;

            yield return WaitForLocations(locationsHandle, source);
            if (!locationsHandle.IsDone)
            {
                ReleaseHandle(locationsHandle);
                yield break;
            }

            if (locationsHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError(
                    $"{LogPrefix} {source} 查询失败：{FormatHandleException(locationsHandle)}");
                ReleaseHandle(locationsHandle);
                yield break;
            }

            IList<IResourceLocation> locations = locationsHandle.Result ??
                                                 new List<IResourceLocation>();
            Debug.Log($"{LogPrefix} {source} 位置数量：{locations.Count}");
            for (int index = 0; index < locations.Count; index++)
            {
                Debug.Log(
                    $"{LogPrefix} LOCATION {index + 1}/{locations.Count} " +
                    DescribeLocation(locations[index]));
            }

            ReleaseHandle(locationsHandle);
        }

        /// <summary>按位置逐个加载资源；单项超时后继续执行，避免卡死在首个异常。</summary>
        private static IEnumerator TestResourceLocations<T>(
            IList<object> keys,
            string source)
            where T : UnityEngine.Object
        {
            if (keys == null || keys.Count == 0)
            {
                Debug.LogWarning($"{LogPrefix} {source} 没有可测试的 Key。");
                yield break;
            }

            AsyncOperationHandle<IList<IResourceLocation>> locationsHandle;
            if (!TryLoadResourceLocations(keys, typeof(T), source, out locationsHandle))
                yield break;

            yield return WaitForLocations(locationsHandle, source);
            if (!locationsHandle.IsDone)
            {
                ReleaseHandle(locationsHandle);
                yield break;
            }

            if (locationsHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError(
                    $"{LogPrefix} {source} 查询失败：{FormatHandleException(locationsHandle)}");
                ReleaseHandle(locationsHandle);
                yield break;
            }

            List<IResourceLocation> locations = locationsHandle.Result
                ?.Where(location => location != null)
                .GroupBy(DescribeLocation, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList() ?? new List<IResourceLocation>();

            Debug.Log(
                $"{LogPrefix} {source} 开始逐个加载：{locations.Count} 个位置，" +
                $"请求类型={typeof(T).FullName}");

            int successCount = 0;
            int failureCount = 0;
            for (int index = 0; index < locations.Count; index++)
            {
                IResourceLocation location = locations[index];
                Debug.Log(
                    $"{LogPrefix} TEST {index + 1}/{locations.Count} " +
                    DescribeLocation(location));

                AsyncOperationHandle<T> assetHandle;
                try
                {
                    assetHandle = Addressables.LoadAssetAsync<T>(location);
                }
                catch (Exception exception)
                {
                    failureCount++;
                    Debug.LogError(
                        $"{LogPrefix} LOAD_THROWN {DescribeLocation(location)}\n{exception}");
                    continue;
                }

                double startTime = EditorApplication.timeSinceStartup;
                while (!assetHandle.IsDone &&
                       EditorApplication.timeSinceStartup - startTime < AssetTimeoutSeconds)
                {
                    yield return null;
                }

                if (!assetHandle.IsDone)
                {
                    failureCount++;
                    Debug.LogError(
                        $"{LogPrefix} LOAD_TIMEOUT {DescribeLocation(location)}\n" +
                        $"等待超过 {AssetTimeoutSeconds:F1} 秒；如果同时出现 DynamicInvoke，" +
                        "该 LOCATION 就是当前异常候选。");
                    ReleaseHandle(assetHandle);
                    yield return null;
                    continue;
                }

                bool succeeded = assetHandle.IsValid() &&
                                 assetHandle.Status == AsyncOperationStatus.Succeeded &&
                                 assetHandle.Result != null;
                if (succeeded)
                {
                    successCount++;
                    Debug.Log(
                        $"{LogPrefix} LOAD_OK {DescribeLocation(location)} " +
                        $"result={assetHandle.Result.name}");
                }
                else
                {
                    failureCount++;
                    Debug.LogError(
                        $"{LogPrefix} LOAD_FAILED {DescribeLocation(location)}\n" +
                        $"exception={FormatHandleException(assetHandle)}");
                }

                ReleaseHandle(assetHandle);
                yield return null;
            }

            ReleaseHandle(locationsHandle);
            Debug.Log(
                $"{LogPrefix} {source} 测试完成：成功={successCount}，失败={failureCount}。");
        }

        /// <summary>发起位置查询并捕获同步抛出的异常。</summary>
        private static bool TryLoadResourceLocations(
            IList<object> keys,
            Type requestedType,
            string source,
            out AsyncOperationHandle<IList<IResourceLocation>> handle)
        {
            try
            {
                handle = Addressables.LoadResourceLocationsAsync(
                    (IEnumerable)keys,
                    Addressables.MergeMode.Union,
                    requestedType);
                return true;
            }
            catch (Exception exception)
            {
                handle = default;
                Debug.LogError($"{LogPrefix} {source} 查询位置抛异常：\n{exception}");
                return false;
            }
        }

        /// <summary>等待位置查询，超时后退出而不阻塞编辑器。</summary>
        private static IEnumerator WaitForLocations(
            AsyncOperationHandle<IList<IResourceLocation>> handle,
            string source)
        {
            double startTime = EditorApplication.timeSinceStartup;
            while (!handle.IsDone &&
                   EditorApplication.timeSinceStartup - startTime < LocationTimeoutSeconds)
            {
                yield return null;
            }

            if (!handle.IsDone)
            {
                Debug.LogError(
                    $"{LogPrefix} {source} 查询位置超时：超过 {LocationTimeoutSeconds:F1} 秒。");
            }
        }

        #endregion

        #region 目录 Key 收集

        /// <summary>逐个测试 Actor 外壳、Sprite 与 AnimatorController 地址。</summary>
        private static IEnumerator TestBuiltInActors()
        {
            List<ItemDefinitionDto> definitions;
            try
            {
                definitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions();
            }
            catch (Exception exception)
            {
                Debug.LogError($"{LogPrefix} 读取 Actor JSON 失败：\n{exception}");
                yield break;
            }

            List<object> shellKeys = GetDistinctKeys(
                definitions.Select(definition => definition.ShellAddress));
            List<object> spriteKeys = GetDistinctKeys(
                definitions.Select(definition => definition.Visual?.SpriteAddress));
            List<object> controllerKeys = GetDistinctKeys(
                definitions.Select(definition => definition.Visual?.AnimatorControllerAddress));

            yield return TestResourceLocations<GameObject>(shellKeys, "JSON Actor 外壳地址");
            yield return TestResourceLocations<Sprite>(spriteKeys, "JSON Actor Sprite 地址");
            yield return TestResourceLocations<RuntimeAnimatorController>(
                controllerKeys,
                "JSON Actor AnimatorController 地址");
        }

        /// <summary>收集本体 Item 与 Actor 定义中的全部 Sprite 地址。</summary>
        private static List<object> CollectBuiltInSpriteKeys()
        {
            var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (ItemDefinitionDto definition in
                         ItemDefinitionCatalogLoader.LoadBuiltInDefinitions())
                {
                    AddKey(addresses, definition.Visual?.SpriteAddress);
                }

                foreach (ItemDefinitionDto definition in
                         ActorDefinitionCatalogLoader.LoadBuiltInDefinitions())
                {
                    AddKey(addresses, definition.Visual?.SpriteAddress);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"{LogPrefix} 读取 JSON Sprite 地址失败：\n{exception}");
            }

            return addresses.Cast<object>().ToList();
        }

        /// <summary>把非空地址去重为 Addressables 查询参数。</summary>
        private static List<object> GetDistinctKeys(IEnumerable<string> values)
        {
            return values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<object>()
                .ToList();
        }

        /// <summary>向地址集合添加一个非空地址。</summary>
        private static void AddKey(HashSet<string> addresses, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                addresses.Add(value.Trim());
        }

        /// <summary>按 Inspector 填写的 Key 和类型启动测试。</summary>
        private void StartSpecifiedTest()
        {
            string key = specifiedKey?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                Debug.LogWarning($"{LogPrefix} 请先填写 Addressable Key。");
                return;
            }

            var keys = new List<object> { key };
            switch (specifiedType)
            {
                case DebugAssetType.GameObject:
                    StartRoutine(
                        TestResourceLocations<GameObject>(
                            keys,
                            $"指定地址 GameObject：{key}"),
                        "正在测试指定 GameObject...");
                    break;
                case DebugAssetType.Sprite:
                    StartRoutine(
                        TestResourceLocations<Sprite>(
                            keys,
                            $"指定地址 Sprite：{key}"),
                        "正在测试指定 Sprite...");
                    break;
                case DebugAssetType.RuntimeAnimatorController:
                    StartRoutine(
                        TestResourceLocations<RuntimeAnimatorController>(
                            keys,
                            $"指定地址 AnimatorController：{key}"),
                        "正在测试指定 AnimatorController...");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        #endregion

        #region 日志格式与句柄释放

        /// <summary>输出位置关键字段，并标记会触发子资源解析的方括号。</summary>
        private static string DescribeLocation(IResourceLocation location)
        {
            if (location == null)
                return "location=<null>";

            string primaryKey = location.PrimaryKey ?? "<null>";
            string internalId = location.InternalId ?? "<null>";
            string providerId = location.ProviderId ?? "<null>";
            string resourceType = location.ResourceType?.FullName ?? "<null>";
            int dependencyCount = location.Dependencies?.Count ?? 0;
            bool containsBracket = primaryKey.Contains("[") || primaryKey.Contains("]") ||
                                   internalId.Contains("[") || internalId.Contains("]");
            string marker = containsBracket ? " | !! 含方括号 !!" : string.Empty;
            return
                $"PrimaryKey={primaryKey} | InternalId={internalId} | " +
                $"ProviderId={providerId} | ResourceType={resourceType} | " +
                $"Dependencies={dependencyCount}{marker}";
        }

        /// <summary>安全读取 Addressables 句柄异常，避免诊断代码覆盖原错误。</summary>
        private static string FormatHandleException(AsyncOperationHandle handle)
        {
            try
            {
                return handle.OperationException?.ToString() ?? "<none>";
            }
            catch (Exception exception)
            {
                return $"读取 OperationException 失败：{exception}";
            }
        }

        /// <summary>只释放有效句柄。</summary>
        private static void ReleaseHandle(AsyncOperationHandle handle)
        {
            if (handle.IsValid())
                Addressables.Release(handle);
        }

        #endregion
    }
}

#endif
