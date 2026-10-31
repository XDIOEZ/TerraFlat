using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 构建期二进制目录规则采样

    private static void RegisterBuiltInBinaryCatalogOperations(Dictionary<string, Func<JObject, JObject>> target)
    {
        target.Add("core_lifecycle/baked_configuration_catalog", ProbeBakedConfigurationCatalog);
        target.Add("core_lifecycle/baked_sprite_reference_catalog", ProbeBakedSpriteReferenceCatalog);
    }

    private static JObject ProbeBakedSpriteReferenceCatalog(JObject args)
    {
        var texture = new Texture2D(2, 2);
        Sprite source = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f));
        StartupSpriteAtlasCatalog catalog = ScriptableObject.CreateInstance<StartupSpriteAtlasCatalog>();
        catalog.Entries = new[] { new StartupSpriteAtlasEntry { Address = "probe/shared-sprite", Sprite = source } };
        var scope = new ResourceAssetScope();
        int sourceRequests = FlatWorldSpriteAtlasDiagnostics.SourceSpriteAddressRequests;
        try
        {
            scope.BindStartupSpriteCatalog(catalog);
            var first = scope.Load<Sprite>("probe/shared-sprite");
            var second = scope.Load<Sprite>("probe/shared-sprite");
            bool completed = first.IsDone && ReferenceEquals(first.Result, source);
            bool reused = first.Equals(second) && scope.HandleCount == 1;
            bool duplicateRejected = false;
            catalog.Entries = new[] { catalog.Entries[0], catalog.Entries[0] };
            try { scope.BindStartupSpriteCatalog(catalog); }
            catch (ArgumentException) { duplicateRejected = true; }
            bool preserved = ReferenceEquals(scope.Load<Sprite>("probe/shared-sprite").Result, source);
            scope.Dispose();
            // 已停止消费者之后模拟迟到的目录回调，不能产生成功-null。
            var previousExceptionHandler = UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler;
            Exception lateFailure = null;
            UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<Sprite> late;
            try
            {
                UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler = (_, error) => lateFailure = error;
                late = scope.CompleteSpritePurposeRequest(() =>
                    UnityEngine.AddressableAssets.Addressables.ResourceManager.CreateCompletedOperation(source, (string)null));
            }
            finally { UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler = previousExceptionHandler; }
            bool lateAfterDisposeFailed = late.IsDone &&
                late.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Failed &&
                late.Result == null && lateFailure != null;
            if (late.IsValid()) UnityEngine.AddressableAssets.Addressables.Release(late);
            return new JObject
            {
                ["completed"] = completed, ["reused"] = reused, ["duplicateRejected"] = duplicateRejected,
                ["preserved"] = preserved, ["released"] = !first.IsValid() && scope.HandleCount == 0,
                ["sourcePreserved"] = source != null && texture != null,
                ["lateAfterDisposeFailed"] = lateAfterDisposeFailed,
                ["nativeRequests"] = FlatWorldSpriteAtlasDiagnostics.SourceSpriteAddressRequests - sourceRequests
            };
        }
        finally
        {
            scope.Dispose();
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static JObject ProbeBakedConfigurationCatalog(JObject args)
    {
        const string key = "file:GameConfig/__baked_binary_probe_missing_source.json";
        BuiltInContentBinaryCatalog.BeginRecording();
        byte[] bytes;
        bool duplicateRejected = false;
        try
        {
            BuiltInContentBinaryCatalog.Record(key, 3, new BuiltInBinaryConfigurationProbeValue { Value = 41, Values = new[] { 7, 9 } });
            BuiltInContentBinaryCatalog.Record(key, 3, new BuiltInBinaryConfigurationProbeValue { Value = 41, Values = new[] { 7, 9 } });
            try { BuiltInContentBinaryCatalog.Record(key, 3, new BuiltInBinaryConfigurationProbeValue { Value = 42 }); }
            catch (InvalidDataException) { duplicateRejected = true; }
            bytes = BuiltInContentBinaryCatalog.FinishRecording();
        }
        finally { BuiltInContentBinaryCatalog.CancelRecording(); }

        bool independent = false, mismatchRejected = false, invalidPreserved = false, deferred = false;
        bool sourcesDisabled = false, nestedPreserved = false, sourcesRestored = false;
        bool wasActive = BuiltInContentBinaryCatalog.IsActive;
        int count = 0, asyncValue = 0, parserCalls = 0;
        using (BuiltInContentBinaryCatalog.UseArchive(bytes))
        {
            count = BuiltInContentBinaryCatalog.EntryCount;
            BuiltInContentBinaryCatalog.TryRead(key, 3, out BuiltInBinaryConfigurationProbeValue first);
            first.Values[0] = 99;
            BuiltInContentBinaryCatalog.TryRead(key, 3, out BuiltInBinaryConfigurationProbeValue second);
            independent = second.Value == 41 && second.Values[0] == 7 && !ReferenceEquals(first, second);
            try { BuiltInContentBinaryCatalog.TryRead(key, 2, out BuiltInBinaryConfigurationProbeValue _); }
            catch (InvalidDataException) { mismatchRejected = true; }
            try { BuiltInContentBinaryCatalog.LoadArchive(new byte[] { 1, 2, 3 }); }
            catch (Exception)
            {
                invalidPreserved = BuiltInContentBinaryCatalog.TryRead(key, 3, out BuiltInBinaryConfigurationProbeValue intact) && intact.Value == 41;
            }
            string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath,
                "GameConfig/__baked_binary_probe_missing_source.json");
            BuiltInBinaryConfigurationProbeValue Parse(string _) { parserCalls++; return null; }
            BuiltInBinaryConfigurationProbeValue direct = BuiltInContentBinaryCatalog.ReadFile(path, Parse, 3);
            IEnumerator operation = BuiltInContentBinaryCatalog.ReadFileAsync(path, Parse,
                value => asyncValue = value.Value, exception => throw exception, 3);
            deferred = asyncValue == 0;
            try { while (operation.MoveNext()) { } }
            finally { (operation as IDisposable)?.Dispose(); }
            if (direct.Value != 41) throw new InvalidOperationException("同步二进制配置值错误。");
            using (BuiltInContentBinaryCatalog.UseSourceConfigurations())
            {
                using (BuiltInContentBinaryCatalog.UseSourceConfigurations())
                    sourcesDisabled = !BuiltInContentBinaryCatalog.IsActive;
                nestedPreserved = !BuiltInContentBinaryCatalog.IsActive;
            }
            sourcesRestored = BuiltInContentBinaryCatalog.IsActive;
        }
        return new JObject
        {
            ["entryCount"] = count, ["duplicateRejected"] = duplicateRejected,
            ["independent"] = independent, ["mismatchRejected"] = mismatchRejected,
            ["invalidPreserved"] = invalidPreserved, ["deferred"] = deferred,
            ["asyncValue"] = asyncValue, ["parserCalls"] = parserCalls,
            ["sourcesDisabled"] = sourcesDisabled, ["nestedPreserved"] = nestedPreserved,
            ["sourcesRestored"] = sourcesRestored, ["catalogRestored"] = BuiltInContentBinaryCatalog.IsActive == wasActive
        };
    }

    #endregion
}

[MemoryPack.MemoryPackable]
public sealed partial class BuiltInBinaryConfigurationProbeValue
{
    public int Value;
    public int[] Values;
}
