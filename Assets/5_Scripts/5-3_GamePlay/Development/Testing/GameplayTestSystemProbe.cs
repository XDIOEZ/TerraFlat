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
        RegisterShieldOperations(result);
        RegisterEntityOperations(result);
        RegisterActorControlOperations(result);
        RegisterWorldMatterOperations(result);
        RegisterTimedDespawnOperations(result);
        RegisterBirdFlightQueryOperations(result);
        RegisterNaturalEntityBatchOperations(result);
        RegisterWorldOperations(result);
        RegisterBuildingStreamingOperations(result);
        RegisterConveyorLoadOperations(result);
        RegisterRailwayOperations(result);
        RegisterServiceOperations(result);
        RegisterStartupPerformanceOperations(result);
        RegisterEcologyFrozenSnapshotCacheOperations(result);
        RegisterFixedStepSimulationOperations(result);
        result.Add("rendering/dropped_combustion_material", ServiceDroppedCombustionMaterial);
        result.Add("rendering/combustion_shader_smoke", ServiceCombustionShaderSmoke);
        RegisterDenseFogOperations(result);
        RegisterWindStreakOperations(result);
        RegisterUnifiedRenderingOperations(result);
        RegisterCharacterAppearanceOperations(result);
        RegisterChunkPresentationOperations(result);
        RegisterWorldPhysicsOperations(result);
        RegisterSwimmingAudioOperations(result);
        RegisterAnimalAudioOperations(result);
        RegisterAiAnimationOperations(result);
        RegisterWolfPackOperations(result);
        RegisterBearProvocationOperations(result);
        RegisterBiteOperations(result);
        RegisterSpawnCountdownOperations(result);
        RegisterInteractionOperations(result);
        RegisterNavigationOperations(result);
        result.Add("terrain/generation_stage_ownership", WorldGenerationStageOwnership);
        RegisterEnvironmentBatchOperations(result);
        RegisterWorldFireOperations(result);
        RegisterGreenhouseOperations(result);
        RegisterSurfaceThawOperations(result);
        RegisterLooseItemMotionOperations(result);
        RegisterTestingOperations(result);
        RegisterTerrainHeightShadowOperations(result);
        result.Add("terrain/petroleum_land_basins", WorldPetroleumLandBasins);
        RegisterShipFlightHudOperations(result);
        RegisterSpaceElevatorOperations(result);
        RegisterOreAsteroidOperations(result);
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

    #region 规则采样场景生命周期

    // 临时组件直接接收 Unity 的编辑态回调，密封模块也无需另造子类。
    private static T EnableGameplayTestProbeLifecycleCallbacks<T>(T component) where T : UnityEngine.MonoBehaviour
    {
#if UNITY_EDITOR
        component.runInEditMode = true;
#endif
        return component;
    }

    // 同步规则在编辑器使用独立预览场景，不切换或污染用户当前场景。
    private static UnityEngine.SceneManagement.Scene CreateGameplayTestProbeScene(string name)
    {
#if UNITY_EDITOR
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        scene.name = name;
        return scene;
#else
        return UnityEngine.SceneManagement.SceneManager.CreateScene(name);
#endif
    }

    private static void CloseGameplayTestProbeScene(UnityEngine.SceneManagement.Scene scene)
    {
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
#else
        UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
#endif
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
