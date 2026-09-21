using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>机械配置目录；首版数值集中在 Resources/Config/Mechanical/mechanical-catalog，MOD 可注册节点、动力条件和加工配方。</summary>
public static class MechanicalCatalog
{
    #region 目录与扩展
    private static readonly Dictionary<string, MechanicalDefinition> definitions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MechanicalProcessDefinition> processes = new(StringComparer.Ordinal);
    private static bool loaded;
    public static MechanicalSettings Settings { get; private set; } = new(); // 整网调度参数
    public static IEnumerable<MechanicalProcessDefinition> Processes { get { EnsureLoaded(); return processes.Values; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { loaded = false; definitions.Clear(); processes.Clear(); Settings = new(); }

    /// <summary>资源会话结束后清除目录，F5 下次加载读取当前配置。</summary>
    public static void Clear() { Reset(); MechanicalWorld.ClearSourceProviders(); }

    /// <summary>读取默认目录一次；覆盖前先校验，避免无效 MOD 配置污染已有目录。</summary>
    public static void EnsureLoaded()
    {
        if (loaded) return;
        TextAsset asset = Resources.Load<TextAsset>("Config/Mechanical/mechanical-catalog");
        if (asset == null) throw new InvalidOperationException("缺少机械动力配置目录。");
        MechanicalCatalogDocument document = JsonConvert.DeserializeObject<MechanicalCatalogDocument>(asset.text);
        if (document == null || document.Version != 1) throw new InvalidOperationException("机械目录版本无效。");
        if (document.Settings == null || document.Nodes == null || document.Processes == null)
            throw new InvalidOperationException("机械目录缺少参数或内容列表。");
        document.Settings.Validate();
        foreach (MechanicalDefinition definition in document.Nodes)
            (definition ?? throw new InvalidOperationException("机械目录含空节点。")).Validate();
        foreach (MechanicalProcessDefinition process in document.Processes)
            (process ?? throw new InvalidOperationException("机械目录含空加工规则。")).Validate();
        Settings = document.Settings;
        foreach (MechanicalDefinition definition in document.Nodes) RegisterDefinitionCore(definition);
        foreach (MechanicalProcessDefinition process in document.Processes) RegisterProcessCore(process);
        loaded = true;
    }

    public static MechanicalDefinition Get(string id)
    {
        EnsureLoaded();
        return definitions.TryGetValue(id ?? string.Empty, out var value) ? value : null;
    }

    public static void RegisterDefinition(MechanicalDefinition definition) { EnsureLoaded(); RegisterDefinitionCore(definition); }
    public static void RegisterProcess(MechanicalProcessDefinition process) { EnsureLoaded(); RegisterProcessCore(process); }
    public static bool TryGetProcess(string station, string input, out MechanicalProcessDefinition result)
    {
        EnsureLoaded();
        return processes.TryGetValue(station + "\u001f" + input, out result);
    }

    private static void RegisterDefinitionCore(MechanicalDefinition definition)
    {
        definition.Validate();
        definitions[definition.Id] = definition;
    }

    private static void RegisterProcessCore(MechanicalProcessDefinition process)
    {
        process.Validate();
        processes[process.Station + "\u001f" + process.Input] = process;
    }
    #endregion
}

/// <summary>机械首版加载参数：默认 0.1 秒模拟步长、1/2 区块激活滞回、5 秒远离冷却。</summary>
[Serializable]
public sealed class MechanicalSettings
{
    #region 参数
    public float TickSeconds = 0.1f;
    public int ActivationChunks = 1;
    public int DeactivationChunks = 2;
    public float UnloadDelaySeconds = 5f;
    public float ReferenceRpm = 60f;
    public float ManualPulseSeconds = 5f;
    public float ManualReserveSeconds = 15f;
    public float BellowsHeatBonus = 250f;
    public void Validate()
    {
        if (!MechanicalDefinition.Positive(TickSeconds) || TickSeconds > 1 || ActivationChunks < 0 ||
            DeactivationChunks <= ActivationChunks || !MechanicalDefinition.Positive(UnloadDelaySeconds) ||
            !MechanicalDefinition.Positive(ReferenceRpm) || !MechanicalDefinition.Positive(ManualPulseSeconds) ||
            !MechanicalDefinition.Positive(ManualReserveSeconds) || !MechanicalDefinition.NonNegative(BellowsHeatBonus))
            throw new ArgumentException("机械调度参数无效。");
    }
    #endregion
}

/// <summary>节点只声明端口、动力和负载；Source 使用注册条件名，Station 使用可扩展字符串身份。</summary>
[Serializable]
public sealed class MechanicalDefinition
{
    #region 参数
    public string Id;
    public string Kind = "shaft"; // shaft/gear/gearbox/clutch/bridge/source/consumer
    public string Ports = "axis"; // axis 为朝向两端，all 为四向
    public string Source = ""; // manual/water/wind 或 MOD 条件
    public string Station = "";
    public float Capacity = 64f; // 整网保守传动容量
    public float Power;
    public float Rpm = 60f;
    public float Load;
    public float[] Ratios = { 0.5f, 1f, 2f };
    public int Layer => Kind == "bridge" ? 1 : 0;
    public bool Rotatable => Ports == "axis";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || (Ports != "axis" && Ports != "all") ||
            !Positive(Capacity) || !NonNegative(Power) || !Positive(Rpm) || !NonNegative(Load) ||
            Ratios == null || Ratios.Length == 0)
            throw new ArgumentException("机械节点参数无效：" + Id);
        foreach (float ratio in Ratios) if (!Positive(ratio)) throw new ArgumentException("变速比必须为正数。");
    }
    internal static bool Positive(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
    internal static bool NonNegative(float value) => value >= 0 && !float.IsInfinity(value) && !float.IsNaN(value);
    #endregion
}

/// <summary>独立加工关系；每个工作站与输入身份只对应一条规则，默认一进一出，可注册多产物。</summary>
[Serializable]
public sealed class MechanicalProcessDefinition
{
    #region 配方
    public string Station;
    public string Input;
    public int InputAmount = 1;
    public List<RuntimeRecipeResult> Outputs = new();
    public float WorkSeconds = 4f;
    [JsonIgnore] private RuntimeRecipe recipe;
    public RuntimeRecipe Recipe => recipe ??= new RuntimeRecipe
    {
        Id = "mechanical." + Station + "." + Input,
        RequiredStation = Station,
        inputs = new RuntimeRecipeInput { RowItems_List = new List<RuntimeRecipeIngredient>
            { new() { ItemName = Input, amount = InputAmount } } },
        outputs = new RuntimeRecipeOutput { results = Outputs }
    };
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Station) || string.IsNullOrWhiteSpace(Input) || InputAmount < 1 ||
            !MechanicalDefinition.Positive(WorkSeconds) || Outputs == null || Outputs.Count == 0)
            throw new ArgumentException("机械加工关系无效。");
        foreach (var output in Outputs)
            if (output == null || string.IsNullOrWhiteSpace(output.ItemName) || output.amount < 1)
                throw new ArgumentException("机械加工产物无效。");
        recipe = null;
    }
    #endregion
}

/// <summary>本体机械目录的明确版本边界。</summary>
public sealed class MechanicalCatalogDocument
{
    public int Version = 1;
    public MechanicalSettings Settings = new();
    public List<MechanicalDefinition> Nodes = new();
    public List<MechanicalProcessDefinition> Processes = new();
}
