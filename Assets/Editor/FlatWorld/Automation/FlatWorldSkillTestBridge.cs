using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private const string ResultPrefix = "result-";
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

        static FlatWorldSkillTestBridge()
        {
            Directory.CreateDirectory(RequestDirectory);
            RecoverOrphanedRequests();
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (_pendingResponse != null)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                    !EditorApplication.isCompiling &&
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
            foreach (string runningPath in Directory.GetFiles(RequestDirectory, RunningPrefix + "*.json"))
            {
                string fileName = Path.GetFileNameWithoutExtension(runningPath);
                string id = fileName.Substring(RunningPrefix.Length);
                WriteImmediateError(id, runningPath,
                    "The Unity domain reloaded before the test run returned a result. Run the request again.");
            }
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

            if (mode == TestMode.PlayMode)
                PreparePlayModeIsolation();

            var filter = new Filter
            {
                testMode = mode,
                assemblyNames = new[] { TestAssembly },
                categoryNames = categories.Length == 0 ? null : categories,
                testNames = testNames.Length == 0 ? null : testNames
            };

            try
            {
                _activeId = id;
                _activeRunningPath = runningPath;
                _activeRequest = request;
                _activeStartedUtc = DateTime.UtcNow;
                _callbacks = new TestCallbacks();
                _testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
                _testRunnerApi.RegisterCallbacks(_callbacks, 100);
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

        private static void PreparePlayModeIsolation()
        {
            _volatileAssetSnapshots = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (string assetPath in VolatilePlayModeAssetPaths)
            {
                string fullPath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                    _volatileAssetSnapshots[assetPath] = File.ReadAllBytes(fullPath);
            }
        }

        private static void RestorePlayModeIsolation()
        {
            Exception restoreError = null;
            try
            {
                if (_volatileAssetSnapshots != null)
                {
                    string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    foreach (KeyValuePair<string, byte[]> snapshot in _volatileAssetSnapshots)
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
            }
            catch (Exception exception)
            {
                restoreError ??= exception;
            }
            finally
            {
                _volatileAssetSnapshots = null;
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

        private static void FinishRun(ITestResultAdaptor result, List<TestFailure> failures)
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
                failures = failures ?? new List<TestFailure>(),
                message = result?.Message ?? ""
            };

            _pendingResponse = response;
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
                RestorePlayModeIsolation();
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
            DeleteIfExists(runningPath);
            Debug.LogError($"[FlatWorldSkillTestBridge] Request {id} failed: {response.message}");
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
            DeleteIfExists(_activeRunningPath);
            _testRunnerApi = null;
            _callbacks = null;
            _activeRequest = null;
            _activeRunningPath = null;
            _activeId = null;
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
            private readonly List<TestFailure> _failures = new();

            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                FinishRun(result, _failures);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result?.Test == null || result.Test.IsSuite || result.TestStatus != TestStatus.Failed)
                    return;

                _failures.Add(new TestFailure
                {
                    fullName = result.FullName,
                    resultState = result.ResultState,
                    message = result.Message,
                    stackTrace = result.StackTrace,
                    durationSeconds = result.Duration
                });
            }
        }
    }
}
