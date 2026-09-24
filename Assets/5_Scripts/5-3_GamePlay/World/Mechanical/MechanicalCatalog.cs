using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>机械扭矩配置目录；首版数值集中在 Resources/Config/Mechanical/mechanical-catalog，MOD 可注册节点、扭矩条件和加工配方。</summary>
public static class MechanicalCatalog
{
    #region 目录与扩展
    private static Dictionary<string, MechanicalDefinition> definitions = new(StringComparer.Ordinal);
    private static Dictionary<string, MechanicalProcessDefinition> processes = new(StringComparer.Ordinal);
    private static bool loaded;
    public static MechanicalSettings Settings { get; private set; } = new(); // 整网调度参数
    public static IEnumerable<MechanicalProcessDefinition> Processes { get { EnsureLoaded(); return processes.Values; } }

    /// <summary>热更新只替换机械定义，不清除当前机械网络或 MOD 扭矩源注册。</summary>
    internal static void ConfigureResourceReload(ResourceReloadContext context)
    {
        context.AddDictionary(() => definitions, value => definitions = value);
        context.AddDictionary(() => processes, value => processes = value);
        context.Add(() => loaded, value => loaded = value, false);
        context.Add(() => Settings, value => Settings = value, new MechanicalSettings());
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { loaded = false; definitions.Clear(); processes.Clear(); Settings = new(); }

    /// <summary>资源会话结束后清除目录，F5 下次加载读取当前配置。</summary>
    public static void Clear() { Reset(); MechanicalWorld.ClearSourceProviders(); }

    /// <summary>读取默认目录一次；覆盖前先校验，避免无效 MOD 配置污染已有目录。</summary>
    public static void EnsureLoaded()
    {
        if (loaded) return;
        TextAsset asset = Resources.Load<TextAsset>("Config/Mechanical/mechanical-catalog");
        if (asset == null) throw new InvalidOperationException("缺少机械扭矩配置目录。");
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
    public float ManualPulseSeconds = 0.2f; // 按住交互时维持的最小动力缓冲。
    public float ManualReserveSeconds = 0.2f; // 松开后允许残留的最大动力缓冲。
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

/// <summary>机械节点声明端口流向、扭矩和负载扭矩。普通传动件可反向传动；变速箱按实际输入侧选择转速与扭矩倍率。</summary>
[Serializable]
public sealed class MechanicalDefinition
{
    #region 参数
    public string Id;
    public string Kind = "shaft"; // shaft/gear/gearbox/clutch/bridge/source/consumer
    public string Ports = "axis"; // axis 为朝向两端，all 为四向
    public string PortMode = "auto"; // auto/input/output/relay；MOD 可显式覆盖默认端口流向。
    public string[] AxlePorts; // 齿轮与非齿轮节点相接的方向；未声明时兼容原有四向连接。
    public string Source = ""; // manual/water/wind 或 MOD 条件
    public string Station = "";
    public float TorqueCapacity = 64f; // 整网保守扭矩传动容量。
    public float Torque;
    public float Rpm = 60f;
    public float RequiredRpm = 60f; // 用力器达到 100% 工作效率所需的转速。
    public float TorqueLoad;
    public float[] Ratios = { 0.5f, 1f, 2f };
    public float ReverseSpeedRatio; // 0 表示兼容旧配置，逆向采用正向速比的倒数。
    public float ForwardTorqueRatio = 1f; // 从左/下侧输入时输出侧的扭矩倍率。
    public float ReverseTorqueRatio = 1f; // 从右/上侧输入时输出侧的扭矩倍率。
    public int Layer => Kind == "bridge" ? 1 : 0;
    public bool Rotatable => Ports == "axis";
    /// <summary>扭矩源为输出端，用力器为输入终点，其余节点按驱动方向传递；MOD 可以显式声明。</summary>
    public string GetPortMode()
    {
        if (PortMode != "auto") return PortMode;
        if (Kind == "consumer" || Kind == "bellows" || !string.IsNullOrEmpty(Station)) return "input";
        return Kind == "source" || Torque > 0 ? "output" : "relay";
    }
    /// <summary>从左/下侧输入为正向；从右/上侧输入为逆向，倍率由配置提供。</summary>
    public void GetTransmission(bool highSideInput, int ratioIndex, out float speedRatio, out float torqueRatio)
    {
        float forward = Ratios[Mathf.Clamp(ratioIndex, 0, Ratios.Length - 1)];
        speedRatio = highSideInput ? (ReverseSpeedRatio > 0 ? ReverseSpeedRatio : 1f / forward) : forward;
        torqueRatio = highSideInput ? ReverseTorqueRatio : ForwardTorqueRatio;
    }
    /// <summary>齿轮轴接头允许的世界方向；齿牙啮合仍由普通端口决定。</summary>
    public bool HasAxlePort(int direction)
    {
        if (AxlePorts == null) return true;
        string name = direction switch { 0 => "right", 1 => "up", 2 => "left", 3 => "down", _ => null };
        if (name == null) return false;
        foreach (string port in AxlePorts)
            if (string.Equals(port, name, StringComparison.Ordinal)) return true;
        return false;
    }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || (Ports != "axis" && Ports != "all") ||
            (PortMode != "auto" && PortMode != "input" && PortMode != "output" && PortMode != "relay") ||
            !Positive(TorqueCapacity) || !NonNegative(Torque) || !Positive(Rpm) || !Positive(RequiredRpm) || !NonNegative(TorqueLoad) ||
            Ratios == null || Ratios.Length == 0 ||
            (ReverseSpeedRatio != 0 && !Positive(ReverseSpeedRatio)) ||
            !Positive(ForwardTorqueRatio) || !Positive(ReverseTorqueRatio))
            throw new ArgumentException("机械节点参数无效：" + Id);
        foreach (float ratio in Ratios) if (!Positive(ratio)) throw new ArgumentException("变速比必须为正数。");
        if (AxlePorts != null)
            foreach (string port in AxlePorts)
                if (port != "right" && port != "up" && port != "left" && port != "down")
                    throw new ArgumentException("齿轮轴接口方向无效：" + Id);
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
    public float WorkSeconds = 4f; // 设备达到 RequiredRpm 时的满效率工作时间。
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
