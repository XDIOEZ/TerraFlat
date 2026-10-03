using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>机械扭矩配置目录；首版数值集中在 Resources/Config/Mechanical/mechanical-catalog，MOD 可注册节点、扭矩条件和加工配方。</summary>
public static class MachineCatalog
{
    #region 目录与扩展
    private static Dictionary<string, MachineDefinition> definitions = new(StringComparer.Ordinal);
    private static Dictionary<string, MachineProcessDefinition> processes = new(StringComparer.Ordinal);
    /// <summary>仅供当前世界恢复旧节点用的临时身份集合。</summary>
    private static HashSet<string> retiredDefinitionIds = new(StringComparer.Ordinal);
    /// <summary>仅供当前世界继续处理旧机械配方的临时键集合。</summary>
    private static HashSet<string> retiredProcessKeys = new(StringComparer.Ordinal);
    private static bool loaded;
    public static MechanicalSettings Settings { get; private set; } = new(); // 整网调度参数
    public static IEnumerable<MachineProcessDefinition> Processes { get { EnsureLoaded(); return processes.Values; } }

    /// <summary>热更新只替换机械定义，不清除当前机械网络或 MOD 扭矩源注册。</summary>
    internal static void ConfigureResourceReload(ResourceReloadContext context)
    {
        context.AddDictionary(() => definitions, value => definitions = value);
        context.AddDictionary(() => processes, value => processes = value);
        context.AddSet(() => retiredDefinitionIds, value => retiredDefinitionIds = value);
        context.AddSet(() => retiredProcessKeys, value => retiredProcessKeys = value);
        context.Add(() => loaded, value => loaded = value, false);
        context.Add(() => Settings, value => Settings = value, new MechanicalSettings());
    }

    /// <summary>隔离加载前的机械目录快照；用于让当前世界保留已被重命名的节点与配方。</summary>
    internal sealed class ReloadSnapshot
    {
        /// <summary>上一个正式资源会话的机械节点目录。</summary>
        public KeyValuePair<string, MachineDefinition>[] Definitions { get; }
        /// <summary>上一个正式资源会话的机械加工目录。</summary>
        public KeyValuePair<string, MachineProcessDefinition>[] Processes { get; }

        public ReloadSnapshot(
            KeyValuePair<string, MachineDefinition>[] definitions,
            KeyValuePair<string, MachineProcessDefinition>[] processes)
        {
            Definitions = definitions;
            Processes = processes;
        }
    }

    /// <summary>复制正式机械目录，避免后续候选会话替换静态字典时丢失旧世界定义。</summary>
    internal static ReloadSnapshot CaptureReloadSnapshot()
    {
        EnsureLoaded();
        return new ReloadSnapshot(definitions.ToArray(), processes.ToArray());
    }

    /// <summary>候选校验结束后承接被移除的机械身份，当前世界结束时统一移除。</summary>
    internal static string[] RetainMissingEntries(ReloadSnapshot previous)
    {
        if (previous == null) throw new ArgumentNullException(nameof(previous));
        var retained = new List<string>();
        foreach (KeyValuePair<string, MachineDefinition> pair in previous.Definitions)
        {
            if (definitions.ContainsKey(pair.Key)) continue;
            definitions.Add(pair.Key, pair.Value);
            retiredDefinitionIds.Add(pair.Key);
            retained.Add("节点 " + pair.Key);
        }
        foreach (KeyValuePair<string, MachineProcessDefinition> pair in previous.Processes)
        {
            if (processes.ContainsKey(pair.Key)) continue;
            processes.Add(pair.Key, pair.Value);
            retiredProcessKeys.Add(pair.Key);
            retained.Add("配方 " + pair.Value.Station + "/" + pair.Value.Input);
        }
        return retained.ToArray();
    }

    /// <summary>世界退出并保存完毕后移除仅供该世界使用的旧机械身份。</summary>
    internal static void ReleaseRetiredEntries()
    {
        foreach (string id in retiredDefinitionIds) definitions.Remove(id);
        foreach (string key in retiredProcessKeys) processes.Remove(key);
        retiredDefinitionIds.Clear();
        retiredProcessKeys.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        loaded = false;
        definitions.Clear();
        processes.Clear();
        retiredDefinitionIds.Clear();
        retiredProcessKeys.Clear();
        Settings = new();
    }

    /// <summary>资源会话结束后清除目录，F5 下次加载读取当前配置。</summary>
    public static void Clear()
    {
        Reset();
        MachineLogicRegistry.ClearContentCache();
        MachineWorld.ClearSourceProviders();
    }

    /// <summary>读取默认目录一次；覆盖前先校验，避免无效 MOD 配置污染已有目录。</summary>
    public static void EnsureLoaded()
    {
        if (loaded) return;
        TextAsset asset = Resources.Load<TextAsset>("Config/Mechanical/mechanical-catalog");
        if (asset == null) throw new InvalidOperationException("缺少机械扭矩配置目录。");
        MachineCatalogDocument document = JsonConvert.DeserializeObject<MachineCatalogDocument>(asset.text);
        if (document == null || document.Version != 1) throw new InvalidOperationException("机械目录版本无效。");
        if (document.Settings == null || document.Nodes == null || document.Processes == null)
            throw new InvalidOperationException("机械目录缺少参数或内容列表。");
        document.Settings.Validate();
        foreach (MachineDefinition definition in document.Nodes)
            (definition ?? throw new InvalidOperationException("机械目录含空节点。")).Validate();
        foreach (MachineProcessDefinition process in document.Processes)
            (process ?? throw new InvalidOperationException("机械目录含空加工规则。")).Validate();
        Settings = document.Settings;
        foreach (MachineDefinition definition in document.Nodes) RegisterDefinitionCore(definition);
        foreach (MachineProcessDefinition process in document.Processes) RegisterProcessCore(process);
        loaded = true;
    }

    public static MachineDefinition Get(string id)
    {
        EnsureLoaded();
        if (definitions.TryGetValue(id ?? string.Empty, out var value)) return value;
        return MachineLogicRegistry.ResolveContentDefinition(id);
    }

    public static void RegisterDefinition(MachineDefinition definition) { EnsureLoaded(); RegisterDefinitionCore(definition); }
    public static void RegisterProcess(MachineProcessDefinition process) { EnsureLoaded(); RegisterProcessCore(process); }
    public static bool TryGetProcess(string station, string input, out MachineProcessDefinition result)
    {
        EnsureLoaded();
        return processes.TryGetValue(station + "\u001f" + input, out result);
    }

    private static void RegisterDefinitionCore(MachineDefinition definition)
    {
        definition.Validate();
        definitions[definition.Id] = definition;
        if (definition.FormerIds != null)
            foreach (string id in definition.FormerIds) definitions[id] = definition;
    }

    private static void RegisterProcessCore(MachineProcessDefinition process)
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
    public float ReferenceRpm = 20f;
    public float WattsPerTorqueRpm = 1.5f; // 游戏扭矩与转速统一换算成功率，保持原有额定功率。
    public float ManualHoldThresholdSeconds = 0.25f; // 手摇轮按住达到此时间后开始供能，短按用于打开面板。
    public float ManualPulseSeconds = 0.2f; // 按住交互时维持的最小动力缓冲。
    public float ManualReserveSeconds = 0.2f; // 松开后允许残留的最大动力缓冲。
    public float BellowsHeatBonus = 500f; // 风箱在额定转速下给予炉体温度上限的最高增量。
    public void Validate()
    {
        if (!MachineDefinition.Positive(TickSeconds) || TickSeconds > 1 || ActivationChunks < 0 ||
            DeactivationChunks <= ActivationChunks || !MachineDefinition.Positive(UnloadDelaySeconds) ||
            !MachineDefinition.Positive(ReferenceRpm) || !MachineDefinition.Positive(WattsPerTorqueRpm) || !MachineDefinition.Positive(ManualHoldThresholdSeconds) ||
            !MachineDefinition.Positive(ManualPulseSeconds) ||
            !MachineDefinition.Positive(ManualReserveSeconds) || !MachineDefinition.NonNegative(BellowsHeatBonus))
            throw new ArgumentException("机械调度参数无效。");
    }
    #endregion
}

/// <summary>机械节点声明端口流向、扭矩和负载扭矩。普通传动件可反向传动；变速箱按实际输入侧选择转速与扭矩倍率。</summary>
[Serializable]
public sealed class MachineDefinition
{
    #region 参数
    public string Id;
    public string[] FormerIds; // 合并后的旧身份只用于查找，实际节点使用当前定义。
    public string LogicId = ""; // 可注册的粗粒度玩法；留空表示原机械网络设备。
    [JsonIgnore] public MachineContent Content;
    public string Kind = "shaft"; // shaft/gear/gearbox/clutch/bridge/source/consumer/bellows
    public string Ports = "axis"; // axis 为朝向两端，all 为四向
    public string PortMode = "auto"; // auto/input/output/relay；MOD 可显式覆盖默认端口流向。
    public string[] AxlePorts; // 与非齿轮节点直连的局部传动轴方向；未声明时四向均可连接。
    public string Source = ""; // manual/water/wind 或 MOD 条件
    public int SourceRotationDirection = 1; // 默认动力源方向：1 正转、-1 反转；RPM 配置仍为正数大小。
    public float SourceRadius; // 由水流线速度换算转速时使用的动力轮半径，单位为世界格。
    public string Station = "";
    public string ProcessCapability = ""; // grind 等物品加工能力；填写后具体产物从输入物品自身解析。
    public int ProcessCapabilityLevel; // 0 表示无等级；有等级区间的加工必须由正等级来源匹配。
    public float TorqueCapacity = 60f; // 旧目录兼容字段；传动件现只传递扭矩，不以容量限制运行。
    public float Torque;
    public float Rpm = 20f;
    public float RequiredRpm = 20f; // 用力器达到 100% 工作效率所需的转速。
    public float TorqueLoad;
    public float ManualWorkSecondsPerPress; // 面板一次手动推动贡献的加工时间；0 表示不开放手动推进。
    public float ManualDriveTorque; // 手推时向机械网络输出的扭矩；0 表示不能手推供能。
    public float ManualDriveRpm; // 手推时的低速转速，单位 RPM。
    public float ManualDriveSecondsPerPress; // 每次点击维持手推供能的秒数。
    public float[] Ratios = { 0.5f, 1f, 2f };
    public float ReverseSpeedRatio; // 0 表示兼容旧配置，逆向采用正向速比的倒数。
    public float ForwardTorqueRatio = 1f; // 从左/下侧输入时输出侧的扭矩倍率。
    public float ReverseTorqueRatio = 1f; // 从右/上侧输入时输出侧的扭矩倍率。
    public bool BlocksMovement = true; // 是否作为实体障碍阻挡角色与导航。
    public float PlayerMoveSpeedMultiplier = 1f; // 可通行机械占格对玩家主动移速的倍率。
    public bool? CastVisualShadows; // MOD 可覆盖用力器与发力器的默认两类世界阴影。
    public int PlacementLayer = -1; // -1 沿用机械默认层；电线等覆盖层可显式使用独立层。
    public MachineTransportDefinition Transport; // 输送能力独立于物品名称，MOD 可以配置自己的传送设备。
    public ElectricalDefinition Electrical; // 可选电气能力；同一机器可同时属于机械网与电网。
    public int Layer => PlacementLayer >= 0 ? PlacementLayer : Kind == "bridge" ? 1 : 0;
    public bool HasMechanicalPorts => Ports == "axis" || Ports == "all";
    public bool HasElectricalPorts => Electrical?.HasConnection == true;
    public bool IsConverter => Electrical?.IsConverter == true;
    public bool Rotatable => Transport != null || Ports == "axis" || Kind == "bellows" || (Kind == "gear" && AxlePorts?.Length > 0);
    /// <summary>动力源、加工设备与机械风箱默认投影；传动件保持原有无影表现。</summary>
    public bool ShouldCastVisualShadows()
        => CastVisualShadows ?? (Kind == "source" || Kind == "consumer" || Kind == "bellows" ||
            !string.IsNullOrEmpty(Source) || !string.IsNullOrEmpty(Station));
    /// <summary>扭矩源为输出端，用力器为输入终点，其余节点按驱动方向传递；MOD 可以显式声明。</summary>
    public string GetPortMode()
    {
        if (PortMode != "auto") return PortMode;
        if (IsConverter) return "relay";
        if (ManualDriveTorque > 0) return "relay"; // 建图允许接入，实际输入/输出由节点当前动力状态决定。
        if (Kind == "consumer" || Kind == "bellows" || !string.IsNullOrEmpty(Station)) return "input";
        return Kind == "source" || Torque > 0 ? "output" : "relay";
    }
    /// <summary>按输入侧换算转速大小与扭矩倍率；旋转方向由网络的啮合关系计算。</summary>
    public void GetTransmission(bool highSideInput, int ratioIndex, out float speedRatio, out float torqueRatio)
    {
        float forward = Ratios[Mathf.Clamp(ratioIndex, 0, Ratios.Length - 1)];
        speedRatio = highSideInput ? (ReverseSpeedRatio > 0 ? ReverseSpeedRatio : 1f / forward) : forward;
        torqueRatio = highSideInput ? ReverseTorqueRatio : ForwardTorqueRatio;
    }
    /// <summary>传动轴直连允许的未旋转局部方向；齿轮间啮合仍由普通端口决定。</summary>
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
        if (string.IsNullOrWhiteSpace(Id) || (Ports != "axis" && Ports != "all" && Ports != "none") ||
            (PortMode != "auto" && PortMode != "input" && PortMode != "output" && PortMode != "relay") ||
            !NonNegative(Torque) || !Positive(Rpm) || !Positive(RequiredRpm) || !NonNegative(TorqueLoad) ||
            (SourceRotationDirection != 1 && SourceRotationDirection != -1) ||
            (Source == "water" && !Positive(SourceRadius)) ||
            !Positive(PlayerMoveSpeedMultiplier) || PlayerMoveSpeedMultiplier > 1f || !NonNegative(ManualWorkSecondsPerPress) ||
            !NonNegative(ManualDriveTorque) || !NonNegative(ManualDriveRpm) || !NonNegative(ManualDriveSecondsPerPress) ||
            PlacementLayer < -1 || PlacementLayer > 15 ||
            Ratios == null || Ratios.Length == 0 ||
            (ReverseSpeedRatio != 0 && !Positive(ReverseSpeedRatio)) ||
            !Positive(ForwardTorqueRatio) || !Positive(ReverseTorqueRatio))
            throw new ArgumentException("机械节点参数无效：" + Id);
        if (ManualDriveTorque > 0 && (Kind != "consumer" || PortMode != "auto" || Torque > 0 ||
            ManualWorkSecondsPerPress > 0 ||
            !string.IsNullOrEmpty(Source) || !Positive(ManualDriveRpm) || !Positive(ManualDriveSecondsPerPress)))
            throw new ArgumentException("手推供能参数无效：" + Id);
        if (ManualDriveTorque == 0 && (ManualDriveRpm != 0 || ManualDriveSecondsPerPress != 0))
            throw new ArgumentException("手推供能参数不完整：" + Id);
        foreach (float ratio in Ratios) if (!Positive(ratio)) throw new ArgumentException("变速比必须为正数。");
        if (AxlePorts != null)
            foreach (string port in AxlePorts)
                if (port != "right" && port != "up" && port != "left" && port != "down")
                    throw new ArgumentException("传动轴接口方向无效：" + Id);
        if (ProcessCapabilityLevel < 0 ||
            ProcessCapabilityLevel > 0 && string.IsNullOrWhiteSpace(ProcessCapability))
            throw new ArgumentException("机械加工能力等级无效：" + Id);
        Transport?.Validate(Id, HasMechanicalPorts);
        Electrical?.Validate(Id);
        if (FormerIds != null)
            foreach (string id in FormerIds)
                if (string.IsNullOrWhiteSpace(id) || id == Id) throw new ArgumentException("机械旧身份无效：" + Id);
        if (IsConverter && (!HasMechanicalPorts || !Positive(Torque) || !Positive(TorqueLoad) ||
            AxlePorts?.Length != 1 || Electrical.Connection == "cell"))
            throw new ArgumentException("双向电机必须分别声明一个机械接口和一个电气接口：" + Id);
    }
    internal static bool Positive(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
    internal static bool NonNegative(float value) => value >= 0 && !float.IsInfinity(value) && !float.IsNaN(value);
    #endregion
}

/// <summary>额定转速下的输送速度与有效带宽，方向由放置朝向和有符号 RPM 决定。</summary>
[Serializable]
public sealed class MachineTransportDefinition
{
    #region 地面输送配置
    public float Speed = .8f;
    public float HalfWidth = .45f;
    public void Validate(string id, bool hasMechanicalPorts)
    {
        if (!hasMechanicalPorts || !MachineDefinition.Positive(Speed) ||
            !MachineDefinition.Positive(HalfWidth) || HalfWidth > .5f)
            throw new ArgumentException("输送设备参数无效：" + id);
    }
    #endregion
}

/// <summary>电气节点的通用配置；首版按整网功率解算，同时保留电压、电流和电阻接口。</summary>
[Serializable]
public sealed class ElectricalDefinition
{
    #region 电气参数
    public string Role = "wire"; // wire/generator/consumer/battery
    public string Connection = "cell"; // cell 接同格电线，其余方向表示随设备旋转的邻格端口。
    public float ConversionEfficiency = 1f; // 双向电机的转换效率不允许超过百分之百。
    public float NominalVoltage = 120f;
    public float MinimumVoltage;
    public float MaximumVoltage;
    public float PowerWatts; // generator 为额定发电功率，consumer 为满负载需求。
    public float MaxCurrentAmps; // 电线允许的整网聚合电流上限。
    public float ResistanceOhms; // 首版保留但不参与压降求解。
    public float CapacityJoules; // battery 专用。
    public float MaxChargeWatts; // battery 专用。
    public float MaxDischargeWatts; // battery 专用。
    public string PowerProvider = ""; // generator 可选动态供电比例，例如 mechanical。
    public string DemandProvider = ""; // consumer 可选动态需求比例，例如 motor。

    public bool HasConnection => Connection == "cell" || Connection == "right" || Connection == "up" ||
        Connection == "left" || Connection == "down";
    public bool IsWire => Role == "wire";
    public bool IsGenerator => Role == "generator";
    public bool IsConsumer => Role == "consumer";
    public bool IsBattery => Role == "battery";
    public bool IsConverter => Role == "converter";
    public Vector2Int GetConnectionOffset(int rotation)
    {
        int direction = Connection switch { "right" => 0, "up" => 1, "left" => 2, "down" => 3, _ => -1 };
        if (direction < 0) return Vector2Int.zero;
        return ((direction + rotation) & 3) switch
        { 0 => Vector2Int.right, 1 => Vector2Int.up, 2 => Vector2Int.left, _ => Vector2Int.down };
    }

    public bool AcceptsVoltage(float voltage)
    {
        if (!MachineDefinition.Positive(voltage)) return false;
        if (IsWire) return MaximumVoltage <= 0f || voltage <= MaximumVoltage + 0.001f;
        float minimum = MinimumVoltage > 0f ? MinimumVoltage : NominalVoltage;
        float maximum = MaximumVoltage > 0f ? MaximumVoltage : NominalVoltage;
        return voltage + 0.001f >= minimum && voltage <= maximum + 0.001f;
    }

    public void Validate(string ownerId)
    {
        if ((Role != "wire" && Role != "generator" && Role != "consumer" && Role != "battery" && Role != "converter") ||
            !HasConnection || !MachineDefinition.Positive(NominalVoltage) ||
            !MachineDefinition.Positive(ConversionEfficiency) || ConversionEfficiency > 1f ||
            !MachineDefinition.NonNegative(MinimumVoltage) || !MachineDefinition.NonNegative(MaximumVoltage) ||
            !MachineDefinition.NonNegative(PowerWatts) || !MachineDefinition.NonNegative(MaxCurrentAmps) ||
            !MachineDefinition.NonNegative(ResistanceOhms) || !MachineDefinition.NonNegative(CapacityJoules) ||
            !MachineDefinition.NonNegative(MaxChargeWatts) || !MachineDefinition.NonNegative(MaxDischargeWatts))
            throw new ArgumentException("电气节点参数无效：" + ownerId);
        if (MinimumVoltage > 0f && MaximumVoltage > 0f && MaximumVoltage < MinimumVoltage)
            throw new ArgumentException("电气节点电压范围无效：" + ownerId);
        if ((IsGenerator || IsConsumer || IsConverter) && !MachineDefinition.Positive(PowerWatts))
            throw new ArgumentException("电气节点功率无效：" + ownerId);
        if (IsWire && !MachineDefinition.Positive(MaxCurrentAmps))
            throw new ArgumentException("电线载流上限无效：" + ownerId);
        if (IsBattery && (!MachineDefinition.Positive(CapacityJoules) || !MachineDefinition.Positive(MaxChargeWatts) ||
            !MachineDefinition.Positive(MaxDischargeWatts)))
            throw new ArgumentException("电池参数无效：" + ownerId);
    }
    #endregion
}

/// <summary>独立加工关系；每个工作站与输入身份只对应一条规则，默认一进一出，可注册多产物。</summary>
[Serializable]
public sealed class MachineProcessDefinition
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
            !MachineDefinition.Positive(WorkSeconds) || Outputs == null || Outputs.Count == 0)
            throw new ArgumentException("机械加工关系无效。");
        foreach (var output in Outputs)
            if (output == null || string.IsNullOrWhiteSpace(output.ItemName) || output.amount < 1)
                throw new ArgumentException("机械加工产物无效。");
        recipe = null;
    }
    #endregion
}

/// <summary>本体机械目录的明确版本边界。</summary>
public sealed class MachineCatalogDocument
{
    public int Version = 1;
    public MechanicalSettings Settings = new();
    public List<MachineDefinition> Nodes = new();
    public List<MachineProcessDefinition> Processes = new();
}
