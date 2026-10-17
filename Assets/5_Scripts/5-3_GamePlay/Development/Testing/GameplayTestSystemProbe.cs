using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>系统测试只开放明确登记的生产规则，Lua 负责组合参数与断言。</summary>
public static partial class GameplayTestSystemProbe
{
    #region 系统操作白名单
    private static readonly Dictionary<string, Func<JObject, JObject>> operations = CreateOperations();

    private static Dictionary<string, Func<JObject, JObject>> CreateOperations()
    {
        var result = new Dictionary<string, Func<JObject, JObject>>(StringComparer.Ordinal);
        RegisterEntityOperations(result);
        RegisterWorldOperations(result);
        RegisterServiceOperations(result);
        RegisterDenseFogOperations(result);
        RegisterChunkPresentationOperations(result);
        RegisterSwimmingAudioOperations(result);
        RegisterAnimalAudioOperations(result);
        RegisterAiAnimationOperations(result);
        RegisterInteractionOperations(result);
        RegisterNavigationOperations(result);
        RegisterEnvironmentBatchOperations(result);
        RegisterTestingOperations(result);
        RegisterTerrainHeightShadowOperations(result);
        return result;
    }

    public static void Register(string systemId, string operationId, Func<JObject, JObject> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        string key = Key(systemId, operationId);
        if (operations.TryGetValue(key, out var existing))
        {
            if (existing.Method == handler.Method && Equals(existing.Target, handler.Target)) return;
            throw new InvalidOperationException("系统测试操作重复登记：" + key);
        }
        operations.Add(key, handler);
    }

    public static bool Supports(string systemId, string operationId)
        => operations.ContainsKey(Key(systemId, operationId));

    public static IReadOnlyList<string> RegisteredOperations => operations.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

    public static JObject Capture(string systemId, string operationId, JObject arguments)
    {
        string key = Key(systemId, operationId);
        if (!operations.TryGetValue(key, out var operation))
            throw new ArgumentException("未登记的系统测试操作：" + key);
        // 每次调用只持有自己的参数快照，探针不得借此共享或污染后续用例。
        JObject data = operation(arguments == null ? new JObject() : (JObject)arguments.DeepClone());
        if (data == null) throw new InvalidOperationException("系统测试没有返回采样：" + key);
        data["_system"] = systemId;
        data["_operation"] = operationId;
        return data;
    }

    private static string Key(string systemId, string operationId)
    {
        foreach (string id in new[] { systemId, operationId })
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 80)
                throw new ArgumentException("系统和操作 ID 必须是 1 至 80 位小写字母、数字或下划线。");
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_')
                    throw new ArgumentException("系统或操作 ID 含非法字符：" + id);
        }
        return systemId + "/" + operationId;
    }
    #endregion

    #region 参数读取
    public static double Number(JObject arguments, string name, double fallback)
    {
        JToken value = arguments?[name];
        if (value == null) return fallback;
        if (value.Type is not (JTokenType.Integer or JTokenType.Float))
            throw new ArgumentException("测试参数必须是数字：" + name);
        double number = value.Value<double>();
        if (double.IsNaN(number) || double.IsInfinity(number))
            throw new ArgumentException("测试参数必须是有限数字：" + name);
        return number;
    }

    public static string Text(JObject arguments, string name, string fallback)
    {
        JToken value = arguments?[name];
        if (value == null) return fallback;
        if (value.Type != JTokenType.String) throw new ArgumentException("测试参数必须是文本：" + name);
        return value.Value<string>();
    }

    public static bool Flag(JObject arguments, string name, bool fallback)
    {
        JToken value = arguments?[name];
        if (value == null) return fallback;
        if (value.Type != JTokenType.Boolean) throw new ArgumentException("测试参数必须是布尔值：" + name);
        return value.Value<bool>();
    }
    #endregion
}
