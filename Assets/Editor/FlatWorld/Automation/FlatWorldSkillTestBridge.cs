using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>
    /// Consumes file-based test requests from the repository-local Codex skill so an already-open
    /// Unity Editor can run tests without an interactive MCP test call.
    /// </summary>
    [InitializeOnLoad]
    internal static class FlatWorldSkillTestBridge
    {
        private const string TestAssembly = "FlatWorld.GameTest";
        private const string RequestPrefix = "request-";
        private const string RunningPrefix = "running-";
        private const string PendingPrefix = "pending-";
        private const string ResultPrefix = "result-";
        private const string IsolationSnapshotPrefix = "isolation-";
        private const double PollIntervalSeconds = 0.25d;

        private static readonly string[] VolatilePlayModeAssetPaths =
        {
            "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset"
        };

        private static readonly string RequestDirectory = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Library", "FlatWorldSkillTests"));

        private static double _nextPollTime;
        private static string _activeId;
        private static string _activeRunningPath;
        private static TestRequest _activeRequest;
        private static DateTime _activeStartedUtc;
        private static TestRunnerApi _testRunnerApi;
        private static TestCallbacks _callbacks;
        private static Dictionary<string, byte[]> _volatileAssetSnapshots;
        private static TestResponse _pendingResponse;
        private static double _finalizeNotBefore;

        private static readonly MethodInfo IsTestRunActiveMethod = typeof(TestRunnerApi).GetMethod(
            "IsRunActive",
            BindingFlags.NonPublic | BindingFlags.Static);

        static FlatWorldSkillTestBridge()
        {
            Directory.CreateDirectory(RequestDirectory);
            EditorApplication.update += Poll;
            EditorApplication.delayCall += RecoverOrphanedRequests;
        }

        private static void Poll()
        {
            if (_pendingResponse != null)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                    !EditorApplication.isCompiling &&
                    !IsUnityTestRunActive() &&
                    EditorApplication.timeSinceStartup >= _finalizeNotBefore)
                {
                    FinalizePendingRun();
                }
                return;
            }

            if (_activeId != null || EditorApplication.timeSinceStartup < _nextPollTime)
                return;

            _nextPollTime = EditorApplication.timeSinceStartup + PollIntervalSeconds;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            string requestPath = Directory.GetFiles(RequestDirectory, RequestPrefix + "*.json")
                .OrderBy(File.GetCreationTimeUtc)
                .FirstOrDefault();
            if (requestPath == null)
                return;

            // AI 可能在 Editor 失焦或关闭 Auto Refresh 时修改了生产代码或测试。
            // 在接管请求前同步导入并编译；若触发 Domain Reload，请求仍保留给新 Domain。
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            string fileName = Path.GetFileNameWithoutExtension(requestPath);
            string id = fileName.Substring(RequestPrefix.Length);
            string runningPath = Path.Combine(RequestDirectory, RunningPrefix + id + ".json");

            try
            {
                File.Move(requestPath, runningPath);
            }
            catch (IOException)
            {
                return;
            }

            if (EditorUtility.scriptCompilationFailed)
            {
                WriteImmediateError(id, runningPath,
                    "Unity script compilation failed. Fix compilation errors before running tests.");
                return;
            }

            try
            {
                TestRequest request = JsonUtility.FromJson<TestRequest>(File.ReadAllText(runningPath));
                ValidateRequest(id, request);
                BeginRun(id, runningPath, request);
            }
            catch (Exception exception)
            {
                WriteImmediateError(id, runningPath, exception.Message);
            }
        }

        private static void RecoverOrphanedRequests()
        {
            string[] runningPaths = Directory.GetFiles(RequestDirectory, RunningPrefix + "*.json");
            if (runningPaths.Length == 0)
                return;

            if (runningPaths.Length > 1)
            {
                foreach (string orphanedPath in runningPaths)
                {
                    string orphanedId = GetRequestId(orphanedPath, RunningPrefix);
                    WriteImmediateError(
                        orphanedId,
                        orphanedPath,
                        "Multiple unfinished test requests were found after a Unity domain reload.");
                }
                return;
            }

            string runningPath = runningPaths[0];
            string id = GetRequestId(runningPath, RunningPrefix);
            try
            {
                TestRequest request = JsonUtility.FromJson<TestRequest>(File.ReadAllText(runningPath));
                ValidateRequest(id, request);
                RestoreActiveRequestState(id, runningPath, request);

                string pendingPath = GetRequestPath(PendingPrefix, id);
                if (File.Exists(pendingPath))
                {
                    TestResponse response = JsonUtility.FromJson<TestResponse>(File.ReadAllText(pendingPath));
                    if (response == null || !string.Equals(response.id, id, StringComparison.Ordinal))
                        throw new InvalidDataException("The pending test result is empty or has the wrong request ID.");

                    _pendingResponse = response;
                    _finalizeNotBefore = EditorApplication.timeSinceStartup + 1d;
                    Debug.Log($"[FlatWorldSkillTestBridge] Recovered completed request {id} after domain reload.");
                    return;
                }

                if (!IsUnityTestRunActive() && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    WriteImmediateError(
                        id,
                        runningPath,
                        "The Unity domain reloaded, but no active test run or pending result could be recovered.");
                    return;
                }

                AttachCallbacksToActiveRun();
                Debug.Log($"[FlatWorldSkillTestBridge] Reattached request {id} after domain reload.");
            }
            catch (Exception exception)
            {
                WriteImmediateError(id, runningPath, $"Failed to recover test request: {exception.Message}");
            }
        }

        private static void RestoreActiveRequestState(
            string id,
            string runningPath,
            TestRequest request)
        {
            _activeId = id;
            _activeRunningPath = runningPath;
            _activeRequest = request;
            _activeStartedUtc = DateTime.TryParse(request.createdUtc, out DateTime createdUtc)
                ? createdUtc.ToUniversalTime()
                : DateTime.UtcNow;
        }

        private static void AttachCallbacksToActiveRun()
        {
            _callbacks = new TestCallbacks();
            _testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            _testRunnerApi.RegisterCallbacks(_callbacks, 100);
        }

        private static void ValidateRequest(string fileId, TestRequest request)
        {
            if (request == null)
                throw new InvalidDataException("The test request JSON is empty or invalid.");
            if (!string.Equals(fileId, request.id, StringComparison.Ordinal))
                throw new InvalidDataException("The request id does not match its file name.");
            if (!string.Equals(request.mode, "EditMode", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(request.mode, "PlayMode", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("mode must be EditMode or PlayMode.");
            }
        }

        private static void BeginRun(string id, string runningPath, TestRequest request)
        {
            string[] categories = Normalize(request.categories);
            string[] testNames = Normalize(request.testNames);
            TestMode mode = string.Equals(request.mode, "PlayMode", StringComparison.OrdinalIgnoreCase)
                ? TestMode.PlayMode
                : TestMode.EditMode;

            var filter = new Filter
            {
                testMode = mode,
                assemblyNames = new[] { TestAssembly },
                categoryNames = categories.Length == 0 ? null : categories,
                testNames = testNames.Length == 0 ? null : testNames
            };

            try
            {
                RestoreActiveRequestState(id, runningPath, request);
                _activeStartedUtc = DateTime.UtcNow;
                if (mode == TestMode.PlayMode)
                    PreparePlayModeIsolation(id);

                AttachCallbacksToActiveRun();
                _testRunnerApi.Execute(new ExecutionSettings(filter));
            }
            catch
            {
                CleanupActiveRun();
                throw;
            }

            Debug.Log($"[FlatWorldSkillTestBridge] Started {id}: {request.mode}; " +
                      $"categories=[{string.Join(", ", categories)}]; tests=[{string.Join(", ", testNames)}]");
        }

        private static bool IsUnityTestRunActive()
        {
            if (IsTestRunActiveMethod == null)
                return true;

            try
            {
                return IsTestRunActiveMethod.Invoke(null, null) is true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return true;
            }
        }

        private static void PreparePlayModeIsolation(string requestId)
        {
            _volatileAssetSnapshots = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            for (int i = 0; i < VolatilePlayModeAssetPaths.Length; i++)
            {
                string assetPath = VolatilePlayModeAssetPaths[i];
                string fullPath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                {
                    byte[] snapshot = File.ReadAllBytes(fullPath);
                    _volatileAssetSnapshots[assetPath] = snapshot;
                    File.WriteAllBytes(GetIsolationSnapshotPath(requestId, i), snapshot);
                }
            }
        }

        private static void RestorePlayModeIsolation(string requestId = null)
        {
            Exception restoreError = null;
            try
            {
                string resolvedRequestId = string.IsNullOrWhiteSpace(requestId) ? _activeId : requestId;
                var snapshots = _volatileAssetSnapshots ??
                                new Dictionary<string, byte[]>(StringComparer.Ordinal);
                for (int i = 0; i < VolatilePlayModeAssetPaths.Length; i++)
                {
                    string assetPath = VolatilePlayModeAssetPaths[i];
                    string snapshotPath = GetIsolationSnapshotPath(resolvedRequestId, i);
                    if (!snapshots.ContainsKey(assetPath) && File.Exists(snapshotPath))
                        snapshots[assetPath] = File.ReadAllBytes(snapshotPath);
                }

                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                foreach (KeyValuePair<string, byte[]> snapshot in snapshots)
                {
                    string fullPath = Path.Combine(projectRoot,
                        snapshot.Key.Replace('/', Path.DirectorySeparatorChar));
                    byte[] current = File.Exists(fullPath)
                        ? File.ReadAllBytes(fullPath)
                        : Array.Empty<byte>();
                    if (current.SequenceEqual(snapshot.Value))
                        continue;

                    File.WriteAllBytes(fullPath, snapshot.Value);
                    AssetDatabase.ImportAsset(snapshot.Key, ImportAssetOptions.ForceUpdate);
                }
            }
            catch (Exception exception)
            {
                restoreError ??= exception;
            }
            finally
            {
                _volatileAssetSnapshots = null;
                DeleteIsolationSnapshots(requestId ?? _activeId);
            }

            if (restoreError != null)
                throw restoreError;
        }

        private static string[] Normalize(IEnumerable<string> values)
        {
            return values == null
                ? Array.Empty<string>()
                : values.Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
        }

        private static void FinishRun(ITestResultAdaptor result)
        {
            if (_activeId == null)
                return;

            int passed = result?.PassCount ?? 0;
            int failed = result?.FailCount ?? 0;
            int skipped = result?.SkipCount ?? 0;
            int inconclusive = result?.InconclusiveCount ?? 0;
            var response = new TestResponse
            {
                id = _activeId,
                state = "completed",
                outcome = result?.TestStatus.ToString() ?? "Unknown",
                mode = _activeRequest?.mode ?? "",
                categories = Normalize(_activeRequest?.categories),
                testNames = Normalize(_activeRequest?.testNames),
                startedUtc = _activeStartedUtc.ToString("O"),
                finishedUtc = DateTime.UtcNow.ToString("O"),
                durationSeconds = result?.Duration ?? 0d,
                total = passed + failed + skipped + inconclusive,
                passed = passed,
                failed = failed,
                skipped = skipped,
                inconclusive = inconclusive,
                failures = CollectFailures(result),
                message = result?.Message ?? ""
            };

            _pendingResponse = response;
            AtomicWrite(
                GetRequestPath(PendingPrefix, _activeId),
                JsonUtility.ToJson(response, true));
            bool playModeRun = string.Equals(_activeRequest?.mode, "PlayMode", StringComparison.OrdinalIgnoreCase);
            _finalizeNotBefore = EditorApplication.timeSinceStartup + (playModeRun ? 1d : 0d);
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !playModeRun)
                FinalizePendingRun();
        }

        private static void FinalizePendingRun()
        {
            TestResponse response = _pendingResponse;
            if (response == null)
                return;

            try
            {
                RestorePlayModeIsolation(response.id);
                string resultPath = Path.Combine(RequestDirectory, ResultPrefix + response.id + ".json");
                AtomicWrite(resultPath, JsonUtility.ToJson(response, true));
                Debug.Log($"[FlatWorldSkillTestBridge] Finished {response.id}: " +
                          $"{response.passed} passed, {response.failed} failed, " +
                          $"{response.skipped} skipped, {response.inconclusive} inconclusive.");
            }
            catch (Exception exception)
            {
                response.state = "error";
                response.outcome = "Error";
                response.message = $"Tests finished, but isolation cleanup failed: {exception.Message}";
                AtomicWrite(Path.Combine(RequestDirectory, ResultPrefix + response.id + ".json"),
                    JsonUtility.ToJson(response, true));
                Debug.LogException(exception);
            }
            finally
            {
                _pendingResponse = null;
                _finalizeNotBefore = 0d;
                CleanupActiveRun();
            }
        }

        private static void WriteImmediateError(string id, string runningPath, string message)
        {
            try
            {
                RestorePlayModeIsolation(id);
            }
            catch (Exception exception)
            {
                message = $"{message} Isolation cleanup also failed: {exception.Message}";
            }

            var response = new TestResponse
            {
                id = id,
                state = "error",
                outcome = "Error",
                startedUtc = DateTime.UtcNow.ToString("O"),
                finishedUtc = DateTime.UtcNow.ToString("O"),
                categories = Array.Empty<string>(),
                testNames = Array.Empty<string>(),
                failures = new List<TestFailure>(),
                message = message ?? "Unknown test bridge error."
            };
            AtomicWrite(Path.Combine(RequestDirectory, ResultPrefix + id + ".json"),
                JsonUtility.ToJson(response, true));
            DeleteIfExists(GetRequestPath(PendingPrefix, id));
            DeleteIfExists(runningPath);
            Debug.LogError($"[FlatWorldSkillTestBridge] Request {id} failed: {response.message}");

            if (string.Equals(_activeId, id, StringComparison.Ordinal))
            {
                try
                {
                    CleanupActiveRun();
                }
                catch (Exception cleanupException)
                {
                    Debug.LogException(cleanupException);
                }
            }
        }

        private static void CleanupActiveRun()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                RestorePlayModeIsolation();
            try
            {
                if (_testRunnerApi != null && _callbacks != null)
                    _testRunnerApi.UnregisterCallbacks(_callbacks);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            if (_testRunnerApi != null)
                UnityEngine.Object.DestroyImmediate(_testRunnerApi);
            DeleteIfExists(GetRequestPath(PendingPrefix, _activeId));
            DeleteIfExists(_activeRunningPath);
            _testRunnerApi = null;
            _callbacks = null;
            _pendingResponse = null;
            _finalizeNotBefore = 0d;
            _activeRequest = null;
            _activeRunningPath = null;
            _activeId = null;
        }

        private static List<TestFailure> CollectFailures(ITestResultAdaptor result)
        {
            var failures = new List<TestFailure>();
            CollectFailures(result, failures);
            return failures;
        }

        private static void CollectFailures(ITestResultAdaptor result, List<TestFailure> failures)
        {
            if (result == null)
                return;

            if (result.Test != null && !result.Test.IsSuite && result.TestStatus == TestStatus.Failed)
            {
                failures.Add(new TestFailure
                {
                    fullName = result.FullName,
                    resultState = result.ResultState,
                    message = result.Message,
                    stackTrace = result.StackTrace,
                    durationSeconds = result.Duration
                });
            }

            foreach (ITestResultAdaptor child in result.Children ?? Enumerable.Empty<ITestResultAdaptor>())
                CollectFailures(child, failures);
        }

        private static string GetRequestId(string path, string prefix)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            return fileName.Substring(prefix.Length);
        }

        private static string GetRequestPath(string prefix, string id)
        {
            return string.IsNullOrWhiteSpace(id)
                ? null
                : Path.Combine(RequestDirectory, prefix + id + ".json");
        }

        private static string GetIsolationSnapshotPath(string requestId, int index)
        {
            return string.IsNullOrWhiteSpace(requestId)
                ? null
                : Path.Combine(RequestDirectory, $"{IsolationSnapshotPrefix}{requestId}-{index}.bin");
        }

        private static void DeleteIsolationSnapshots(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return;

            for (int i = 0; i < VolatilePlayModeAssetPaths.Length; i++)
                DeleteIfExists(GetIsolationSnapshotPath(requestId, i));
        }

        private static void AtomicWrite(string path, string contents)
        {
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, contents);
            DeleteIfExists(path);
            File.Move(temporaryPath, path);
        }

        private static void DeleteIfExists(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                File.Delete(path);
        }

        [Serializable]
        private sealed class TestRequest
        {
            public string id;
            public string mode;
            public string[] categories;
            public string[] testNames;
            public string createdUtc;
        }

        [Serializable]
        private sealed class TestResponse
        {
            public string id;
            public string state;
            public string outcome;
            public string mode;
            public string[] categories;
            public string[] testNames;
            public string startedUtc;
            public string finishedUtc;
            public double durationSeconds;
            public int total;
            public int passed;
            public int failed;
            public int skipped;
            public int inconclusive;
            public List<TestFailure> failures;
            public string message;
        }

        [Serializable]
        private sealed class TestFailure
        {
            public string fullName;
            public string resultState;
            public string message;
            public string stackTrace;
            public double durationSeconds;
        }

        private sealed class TestCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                FinishRun(result);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
    }
}
