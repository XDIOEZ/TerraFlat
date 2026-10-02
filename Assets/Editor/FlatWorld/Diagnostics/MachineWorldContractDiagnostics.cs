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
        CheckMaterialHeating();
        CheckMechanicalMembership();
        CheckMachinePersistence();
        CheckMachineDamage();
        CheckManagedPatchEntries();
        Debug.Log("[MachineWorldContractDiagnostics] 7 组检查通过：MOD 注销顺序、库存快照原子应用、加工余量、材料供热与保存、设施与传动分组、冷状态保存、托管补丁入口。未执行 Harmony DLL 或游戏内验收。");
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

    // 供热只改材料温度，温度阈值、散热、堆叠与载体切换分别验证。
    private static void CheckMaterialHeating()
    {
        var material = new Data_GeneralItem { Stack = new ItemStack { Amount = 1f }, HeatConductionRate = 1f };
        Require(ItemMatterRuntime.AddHeat(material, 20f, 150f, 10f) && material.MatterState.TemperatureCelsius == 30f,
            "首次摩擦必须从环境温度升温，不能直接初始化成热源温度。");
        var transition = new RuntimeItemMatterTransition("diagnostic.heat", 100f, null, null, null, "FireSeed", 1f, null);
        material.MatterState.TemperatureCelsius = 99.99f;
        Require(!transition.Matches(material.MatterState), "材料不到 100°C 不能触发转化。");
        material.MatterState.TemperatureCelsius = 100f;
        Require(transition.Matches(material.MatterState), "材料恰好达到 100°C 必须能够转化。");
        material.MatterState.TemperatureCelsius = 149f;
        ItemMatterRuntime.AddHeat(material, 20f, 150f, 10f);
        Require(material.MatterState.TemperatureCelsius == 150f, "连续摩擦不能超过热源上限。");
        material.MatterState.TemperatureCelsius = 200f;
        ItemMatterRuntime.AddHeat(material, 20f, 150f, 10f);
        Require(material.MatterState.TemperatureCelsius == 200f, "摩擦不能冷却已经更热的材料。");
        ItemMatterRuntime.Advance(material, 20f, 1f, 1f);
        Require(material.MatterState.TemperatureCelsius == 199f, "停止摩擦后必须按材料传热速率向环境散热。");
        material.Stack.Amount = 2f;
        material.MatterState.TemperatureCelsius = 20f;
        ItemMatterRuntime.AddHeat(material, 20f, 150f, 10f);
        Require(material.MatterState.TemperatureCelsius == 25f, "同一次摩擦热量必须分摊给堆叠中的材料。");
        Require(!ItemMatterRuntime.AddHeat(material, 20f, float.NaN, 10f) &&
            material.MatterState.TemperatureCelsius == 25f, "无效热源不能污染材料状态。");

        Inventory input = EmptyInventory();
        Inventory output = EmptyInventory();
        input.Data.itemSlots[0].itemData = material;
        _ = new MaterialHeatingProcessor(input, output);
        _ = new MaterialHeatingProcessor(input, output);
        Require(material.MatterState.TemperatureCelsius == 25f, "重开面板或切换载体不能重置材料温度。");
        byte[] bytes = MemoryPack.MemoryPackSerializer.Serialize(new FireDrillRuntimeState { Input = input.Data, Output = output.Data });
        FireDrillRuntimeState restored = MemoryPack.MemoryPackSerializer.Deserialize<FireDrillRuntimeState>(bytes);
        Require(restored.Input.itemSlots[0].itemData.MatterState.TemperatureCelsius == 25f &&
            restored.Input.itemSlots[0].itemData.Stack.Amount == 2f, "共享模块快照必须保留材料自身的温度及数量。");
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

    #region 工作方块受击契约
    private static void CheckMachineDamage()
    {
        var health = new ItemHealthDefinitionDto
        {
            Hp = 180f, MaxHp = 180f,
            Defense = new ItemDefenseDefinitionDto { Cutting = 8f, Piercing = 8f, Chopping = 8f, Blunt = 8f },
            Collider = new ItemColliderDefinitionDto
            {
                Type = nameof(BoxCollider2D), Enabled = true, IsTrigger = true,
                Size = Vector2.one, Offset = Vector2.zero
            }
        };
        MachineCombatBridge.ValidateHealth(health);
        var hit = new FlatWorld.Combat.CombatDamageContext
        {
            Damage = new Unity.Mathematics.float4(0f, 0f, 0f, 12f), BuildingMultiplier = 10f
        };
        Require(MachineCombatBridge.CalculateDamage(health, hit, 1f) == 40f,
            "石锤的 12 点钝击应先扣 8 点防御，再应用 10 倍建筑伤害。");
        hit.Damage.w = 6f;
        Require(MachineCombatBridge.CalculateDamage(health, hit, 1f) == 0f,
            "完全被防御抵消的命中仍应是有效零伤害，不能绕过防御凭空扣血。");
        hit.Damage.w = 12f;
        hit.IsTrueDamage = 1;
        Require(MachineCombatBridge.CalculateDamage(health, hit, 1f) == 120f,
            "真实伤害应跳过防御，但仍使用建筑倍率。");

        health.ModuleLocalPosition = Vector3.up;
        health.Collider.Offset = new Vector2(3f, 2f);
        health.Collider.Size = new Vector2(2f, 4f);
        var shape = MachineCombatBridge.ResolveHitShape(health, new Vector2(10f, 20f), 1);
        Require(shape.Center.x == 7f && shape.Center.y == 23f && shape.Extents.x == 2f && shape.Extents.y == 1f,
            "偏移受击框必须连同中心和长宽一起旋转，不能回退成锚点上的一格方框。");
        shape = MachineCombatBridge.ResolveHitShape(health, Vector2.zero, 2, true);
        Require(shape.Center.x == -3f && shape.Center.y == 3f && shape.Extents.y == 2f,
            "转换器左右镜像只翻转横向偏移，不能把竖直受击中心翻到地下。");
        health.Collider.Type = nameof(CircleCollider2D);
        health.Collider.Radius = 2f;
        MachineCombatBridge.ValidateHealth(health);
        shape = MachineCombatBridge.ResolveHitShape(health, Vector2.zero, 0);
        Require(shape.IsCircle != 0 && shape.Radius == 2f && shape.Center.y == 3f,
            "圆形和跨格偏移的受击范围必须原样进入物理查询。");

        health.Collider.Enabled = false;
        RequireInvalidMachineHealth(health, "关闭受击碰撞体的机器必须在内容加载时被拒绝。");
        health.Collider.Enabled = true;
        health.HasHp = false;
        RequireInvalidMachineHealth(health, "没有生命的工作方块不能被当成有效机器加载。");
        health.HasHp = true;
        health.MaxHp = float.PositiveInfinity;
        RequireInvalidMachineHealth(health, "无限耐久不能绕过机器生命校验。");
        health.MaxHp = 180f;
        health.Collider.Radius = 0f;
        RequireInvalidMachineHealth(health, "零尺寸受击范围不能产生无敌的挡路建筑。");
        Debug.Log("[MachineWorldContractDiagnostics] 工作方块受击检查通过：防御与倍率、真实伤害、旋转与镜像、圆形范围、无效生命和碰撞配置。未执行游戏内挥击验收。");
    }

    private static void RequireInvalidMachineHealth(ItemHealthDefinitionDto health, string message)
    {
        bool rejected = false;
        try { MachineCombatBridge.ValidateHealth(health); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, message);
    }
    #endregion

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
