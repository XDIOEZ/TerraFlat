using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Editor 提供跨域源资产存储，运行时只通过此接口重建本轮资源所有权。</summary>
public interface IGameplayTestResourceCacheBackend
{
    bool IsTestRun { get; }
    void BeginSession();
    bool TryGetAsset(string key, Type expected, out UnityEngine.Object asset);
    void RecordAsset(string key, UnityEngine.Object asset);
    bool TryGetDocument(string key, out string json);
    void RecordDocument(string key, string json);
    void Flush();
    void AbortSession();
    void Clear();
}

public static class GameplayTestResourceCache
{
    private static readonly JsonSerializerSettings documentSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        TypeNameHandling = TypeNameHandling.None,
        Converters = { new UnityValueConverter() }
    };

    #region 测试会话与诊断
    public static IGameplayTestResourceCacheBackend Backend { get; set; }
    public static bool Active { get; private set; }
    public static int AssetHits { get; private set; }
    public static int AssetMisses { get; private set; }
    public static int DocumentHits { get; private set; }
    public static string Status { get; private set; } = "disabled";
    private static readonly System.Diagnostics.Stopwatch timer = new();
    public static double ResourceSeconds { get; private set; }
    public static double CacheReadSeconds { get; private set; }
    public static double CacheWriteSeconds { get; private set; }
    public static void Begin()
    {
        Active = false;
        AssetHits = AssetMisses = DocumentHits = 0;
        ResourceSeconds = 0;
        CacheReadSeconds = 0;
        CacheWriteSeconds = 0;
        Status = "disabled";
        if (!Application.isEditor || !Application.isPlaying || Backend == null ||
            !(Backend.IsTestRun || SceneManager.GetActiveScene().path == "Assets/3_Scenes/Development/GameplayTestScene.unity")) return;
        timer.Restart();
        try { Backend.BeginSession(); CacheReadSeconds = timer.Elapsed.TotalSeconds; Active = true; Status = "building"; }
        catch (Exception error) { Debug.Log("[Lua GameRes缓存] 无法启用，使用正式冷加载：" + error.Message); }
    }
    public static void Complete(bool success)
    {
        if (!Active) return;
        double beforeWrite = timer.Elapsed.TotalSeconds;
        try
        {
            if (success) { Backend.Flush(); Status = AssetHits > 0 ? "hit" : "cold"; }
            else { Backend.AbortSession(); Status = "failed"; }
        }
        catch (Exception error) { Status = "write_failed"; Debug.Log("[Lua GameRes缓存] 保存失败，不影响本轮正式目录：" + error.Message); }
        finally
        {
            timer.Stop();
            ResourceSeconds = timer.Elapsed.TotalSeconds;
            CacheWriteSeconds = ResourceSeconds - beforeWrite;
            Active = false;
        }
        Debug.Log($"[Lua GameRes缓存] {Status}：源资产命中 {AssetHits}，新请求 {AssetMisses}，解析命中 {DocumentHits}，资源 {ResourceSeconds:F3}s");
    }
    public static JObject Sample() => new()
    { ["status"] = Status, ["assetHits"] = AssetHits, ["assetMisses"] = AssetMisses,
        ["documentHits"] = DocumentHits, ["resourceSeconds"] = ResourceSeconds,
        ["cacheReadSeconds"] = CacheReadSeconds,
        ["cacheWriteSeconds"] = CacheWriteSeconds,
        ["ready"] = GameRes.ExistingInstance?.isLoadFinish == true,
        ["items"] = GameRes.ExistingInstance?.ItemDefinitions.Count ?? 0 };
    #endregion

    #region 持久源资产与纯 DTO 输入
    public static bool TryAsset<T>(string key, out T asset) where T : UnityEngine.Object
    {
        asset = null;
        if (!Active) return false;
        if (Backend.TryGetAsset(key, typeof(T), out UnityEngine.Object value) && value is T typed)
        { asset = typed; AssetHits++; return true; }
        AssetMisses++; return false;
    }
    public static void RecordAsset(string key, UnityEngine.Object asset)
    { if (Active && asset != null) Backend.RecordAsset(key, asset); }

    public static IEnumerator LoadItemInputs(Action<List<ItemDefinitionDto>> done, Action<Exception> failed, Action<float> progress)
    {
        const string key = "resolved-item-inputs-v1";
        List<ItemDefinitionDto> cached = null;
        if (Active && Backend.TryGetDocument(key, out string json))
        {
            try { cached = JsonConvert.DeserializeObject<List<ItemDefinitionDto>>(json, documentSettings); }
            catch (Exception error) { Debug.Log("[Lua GameRes缓存] DTO 无法恢复，重读正式输入：" + error.Message); }
        }
        if (cached != null && cached.Count > 0) { DocumentHits++; done(cached); yield break; }
        yield return ItemDefinitionCatalogLoader.LoadBuiltInDefinitionsAsync(value =>
        {
            if (Active)
            {
                try { Backend.RecordDocument(key, JsonConvert.SerializeObject(value, documentSettings)); }
                catch (Exception error) { Debug.Log("[Lua GameRes缓存] 输入保存失败，保留本轮正式解析结果：" + error.Message); }
            }
            done(value);
        }, failed, progress);
    }

    private sealed class UnityValueConverter : JsonConverter
    {
        // Unity 值类型只保存数值字段，不遍历 normalized、linear 等计算属性。
        public override bool CanConvert(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Color) || type == typeof(Rect);
        }
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            JObject token;
            if (value is Vector2 v2) token = new JObject { ["x"] = v2.x, ["y"] = v2.y };
            else if (value is Vector3 v3) token = new JObject { ["x"] = v3.x, ["y"] = v3.y, ["z"] = v3.z };
            else if (value is Color color) token = new JObject { ["r"] = color.r, ["g"] = color.g, ["b"] = color.b, ["a"] = color.a };
            else if (value is Rect rect) token = new JObject { ["x"] = rect.x, ["y"] = rect.y, ["width"] = rect.width, ["height"] = rect.height };
            else throw new JsonSerializationException("不支持的缓存值类型。");
            token.WriteTo(writer);
        }
        public override object ReadJson(JsonReader reader, Type type, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null && Nullable.GetUnderlyingType(type) != null) return null;
            type = Nullable.GetUnderlyingType(type) ?? type;
            JObject token = JObject.Load(reader);
            float Read(string field) => token[field]?.Value<float>() ?? throw new JsonSerializationException("缓存缺少数值字段：" + field);
            if (type == typeof(Vector2)) return new Vector2(Read("x"), Read("y"));
            if (type == typeof(Vector3)) return new Vector3(Read("x"), Read("y"), Read("z"));
            if (type == typeof(Rect)) return new Rect(Read("x"), Read("y"), Read("width"), Read("height"));
            return new Color(Read("r"), Read("g"), Read("b"), Read("a"));
        }
    }
    #endregion
}
