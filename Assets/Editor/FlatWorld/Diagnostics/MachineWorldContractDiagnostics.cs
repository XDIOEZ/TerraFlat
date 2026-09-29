using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>检查纯托管机器契约，不启动玩家世界、不安装 MOD、不读写真实存档。</summary>
public static class MachineWorldContractDiagnostics
{
    #region 显式检查入口
    private const string MenuPath = "FlatWorld/诊断/验证机器世界契约";

    [MenuItem(MenuPath)]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("机器契约诊断仅在编辑模式执行。");
        CheckRegistryReleaseOrder();
        CheckInventorySnapshot();
        CheckRecipeProgress();
        CheckMechanicalMembership();
        CheckMachinePersistence();
        CheckManagedPatchEntries();
        Debug.Log("[MachineWorldContractDiagnostics] 6 组检查通过：MOD 注销顺序、库存快照原子应用、加工余量、设施与传动分组、冷状态保存、托管补丁入口。未执行 Harmony DLL 或游戏内验收。");
    }

    [MenuItem(MenuPath, true)]
    private static bool CanValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[MachineWorldContractDiagnostics] " + message);
    }
    #endregion

    #region 注册与库存
    private static void CheckRegistryReleaseOrder()
    {
        string id = "diagnostic.machine." + Guid.NewGuid().ToString("N");
        IDisposable first = null;
        IDisposable second = null;
        try
        {
            first = MachineLogicRegistry.Register(id, entity => new DiagnosticLogic(entity));
            second = MachineLogicRegistry.Register(id, entity => new DiagnosticLogic(entity), replace: true);
            first.Dispose();
            Require(MachineLogicRegistry.IsRegistered(id), "提前释放旧工厂不能注销仍生效的新工厂。");
            second.Dispose();
            Require(!MachineLogicRegistry.IsRegistered(id), "最后一层释放后不能复活已经卸载的 MOD 工厂。");
            second.Dispose();
            Require(!MachineLogicRegistry.IsRegistered(id), "重复释放注册租约必须无副作用。");
        }
        finally { second?.Dispose(); first?.Dispose(); }
    }

    private static Inventory EmptyInventory(int slots = 1)
        => new Inventory { Data = MachineInventory.NewData("诊断库存", slots) };

    private static void CheckInventorySnapshot()
    {
        Inventory inventory = EmptyInventory();
        Inventory_Data data = inventory.Data;
        ItemSlot original = data.itemSlots[0];
        data.PanelPosition = new Vector3(23f, 47f, 0f);
        var invalid = MachineInventory.NewData("无效候选", 1);
        invalid.itemSlots[0] = null;
        bool rejected = false;
        try { MachineInventory.ApplySnapshot(inventory, invalid); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected && ReferenceEquals(inventory.Data, data) &&
            data.itemSlots.Count == 1 && ReferenceEquals(data.itemSlots[0], original),
            "损坏快照不能清空库存、替换数据身份或解除有效状态。");

        Inventory_Data incoming = MachineInventory.NewData("服务端库存", 2);
        incoming.IsDepositBlocked = true;
        incoming.IsInjected = true;
        MachineInventory.ApplySnapshot(inventory, incoming);
        Require(ReferenceEquals(inventory.Data, data) && data.itemSlots.Count == 2 && data.IsDepositBlocked && data.IsInjected,
            "同步必须保留库存身份，并同步槽位和禁止放入状态。");
        Require(ReferenceEquals(data.itemSlots[0], original), "网络同步不能替换拖拽事务正在引用的槽位。");
        Require(data.PanelPosition == new Vector3(23f, 47f, 0f), "服务端不能覆盖客户端面板布局。");
        MachineInventory.ApplySnapshot(inventory, inventory.Data);
        Require(data.itemSlots.Count == 2, "重复应用同一数据引用不能清空槽位。");
        inventory.UnbindRuntimeDataEvents();
    }

    private sealed class DiagnosticLogic : MachineLogic
    {
        public DiagnosticLogic(MachineEntity entity) : base(entity) { }
        public override void Capture() { }
    }
    #endregion

    #region 加工与机械分组
    private static void CheckRecipeProgress()
    {
        var state = new RecipeProcessingState();
        using var processor = new RecipeProcessor(EmptyInventory(), EmptyInventory(), new CraftingCapabilities(), state);
        var recipe = new RuntimeRecipe { Id = "diagnostic.recipe" };
        processor.SelectRecipe(recipe, 2f);
        processor.PreviewOverride = () => CraftingResult.Succeeded(recipe, Array.Empty<ItemData>());
        int completions = 0;
        processor.CommitOverride = actor =>
        {
            completions++;
            return CraftingResult.Succeeded(recipe, Array.Empty<ItemData>());
        };
        Require(processor.Advance(7f) && completions == 3 && Mathf.Abs(state.Progress - 1f) < .001f,
            "批量加工应保留完成多份后的工作量余数。");
        processor.SelectRecipe(recipe, 2f);
        Require(Mathf.Abs(state.Progress - 1f) < .001f, "重绑同一配方不能清空进度。");
        processor.PreviewOverride = () => CraftingResult.Failed(CraftingFailureReason.ConditionsNotMet, "诊断：输出已满");
        Require(!processor.Advance(10f) && completions == 3 && Mathf.Abs(state.Progress - 1f) < .001f,
            "预检失败不能提交配方或增加工作量。");
        processor.SelectRecipe(new RuntimeRecipe { Id = "diagnostic.other" }, 2f);
        Require(state.Progress == 0f, "切换配方必须重置旧配方进度。");
    }

    private static void CheckMechanicalMembership()
    {
        var graph = new MechanicalNetworkGraph(new Vector2Int(16, 16), Vector2Int.zero, cell => cell);
        var source = Node(1, new Vector2Int(0, 0), new MachineDefinition
            { Id = "test.source", Kind = "source", Ports = "axis", Torque = 20f, Rpm = 20f });
        var shaft = Node(2, new Vector2Int(1, 0), new MachineDefinition { Id = "test.shaft", Ports = "axis" });
        var consumer = Node(3, new Vector2Int(2, 0), new MachineDefinition
            { Id = "test.consumer", Kind = "consumer", Ports = "axis", TorqueLoad = 10f, RequiredRpm = 20f });
        var workbench = Node(4, new Vector2Int(1, 1), new MachineDefinition
            { Id = "test.workbench", LogicId = "workbench", Ports = "none" });
        graph.Rebuild(new[] { source, shaft, consumer, workbench });
        Require(graph.Networks.Count == 1 && graph.Networks[0].Nodes.Count == 3 &&
            graph.Facilities.Count == 1 && ReferenceEquals(graph.At(workbench.Cell, 0), workbench),
            "普通设施要共用空间索引，但不能伪造端口进入扭矩网络。");
        MechanicalNetworkGraph.Solve(graph.Networks[0], entity => ReferenceEquals(entity, source) ? 1f : 0f, 20f);
        Require(Mathf.Abs(consumer.Rpm - 20f) < .001f && consumer.LoadSatisfied,
            "泛化后的传动轴仍须把动力交给用力器。");
    }

    private static MachineEntity Node(int id, Vector2Int cell, MachineDefinition definition)
        => new MachineEntity { Id = id, Cell = cell, Definition = definition, State = new MachineState() };
    #endregion

    #region 冷状态与补丁入口
    private static void CheckMachinePersistence()
    {
        var snapshot = new Data_GeneralItem { ItemSpecialData = "{\"diagnostic.external\":{\"value\":7}}" };
        var state = new MachineState { Hp = 42f, ManualSeconds = 3f, RotationQuarterTurns = 2 };
        MachinePersistence.Write(snapshot, "core", state);
        MachineState restored = MachinePersistence.Read<MachineState>(snapshot, "core");
        Require(restored != null && restored.Hp == 42f && restored.ManualSeconds == 3f && restored.RotationQuarterTurns == 2,
            "机器冷快照必须保留耐久、人工供能和朝向。");
        Require((int)JObject.Parse(snapshot.ItemSpecialData)["diagnostic.external"]["value"] == 7,
            "保存机器状态不得覆盖其它 MOD 命名空间。");
        snapshot.CraftedDurabilityMultiplier = 2f;
        Require(CraftedDurabilityQuality.ResolveMaximumHp(100f, snapshot) == 200f &&
            CraftedDurabilityQuality.ResolveMaximumHp(100f, snapshot) == 200f && restored.Hp == 42f,
            "机器品质应提高最大生命，但不能重复乘已恢复的受损生命。");
        snapshot.CraftedDurabilityMultiplier = float.NaN;
        Require(CraftedDurabilityQuality.ResolveMaximumHp(100f, snapshot) == 100f,
            "无效品质倍率应复用普通 Item 的默认倍率规则。");
    }

    private static void CheckManagedPatchEntries()
    {
        CheckEntry(typeof(MachineWorld), "CalculateWorkAmount");
        CheckEntry(typeof(MachineWorld), "Execute");
        CheckEntry(typeof(RecipeProcessor), "Advance");
        CheckEntry(typeof(RecipeProcessor), "Commit");
        CheckEntry(typeof(FurnaceLogic), "CalculateMaximumTemperature");
        CheckEntry(typeof(WorkbenchLogic), "PerformWork");
        CheckEntry(typeof(MechanicalNetworkGraph), "Solve");
    }

    private static void CheckEntry(Type owner, string name)
    {
        int found = 0;
        foreach (MethodInfo method in owner.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
        {
            if (method.Name != name) continue;
            found++;
            Require(method.GetMethodBody() != null &&
                (method.GetMethodImplementationFlags() & MethodImplAttributes.NoInlining) != 0,
                owner.Name + "." + name + " 必须保留可定位且禁止内联的托管方法体。");
        }
        Require(found > 0, "补丁入口不存在：" + owner.Name + "." + name);
    }
    #endregion
}
