using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

// 只替代捕食者和猎物的环境状态，树动作及捕食重置使用生产代码。
#region 捕食环境替身
public sealed class Item { }
public interface IAquaticPredatorCarryTarget
{
    bool IsCapturedBy(Item predator);
    void ReleaseFromPredator(Item predator);
}
public partial class Mod_AnimalFlightMovement
{
    private readonly Item item = new();
    private IAquaticPredatorCarryTarget fishHuntTarget, pendingAquaticPrey;
    private bool fishHunting, fishHuntHasLandPoint, fishHuntMeatReady;
    private object fishHuntLandPoint, fishHuntMeatTarget;
    private float fishHuntTimer, fishHuntVisualHeightOverride, fishPredationDamageOverride;
    private readonly List<object> fishHuntCandidates = new();
    private readonly HashSet<object> fishHuntDedupe = new();

    public void Capture(IAquaticPredatorCarryTarget prey)
    {
        fishHuntTarget = pendingAquaticPrey = prey;
        fishHunting = fishHuntHasLandPoint = fishHuntMeatReady = true;
        fishHuntLandPoint = fishHuntMeatTarget = new object();
        fishHuntTimer = fishHuntVisualHeightOverride = fishPredationDamageOverride = 1f;
        fishHuntCandidates.Add(prey);
        fishHuntDedupe.Add(prey);
    }
    public bool Cleared => fishHuntTarget == null && pendingAquaticPrey == null && !fishHunting &&
        !fishHuntHasLandPoint && !fishHuntMeatReady && fishHuntLandPoint == null && fishHuntMeatTarget == null &&
        fishHuntTimer == 0f && fishHuntVisualHeightOverride == -1f && fishPredationDamageOverride == -1f &&
        fishHuntCandidates.Count == 0 && fishHuntDedupe.Count == 0;
}
public static partial class AnimalSpecialMovementBehaviorNodes
{
    public static AnimalBehaviorTreeNode CreateAquaticHuntProbe(Mod_AnimalFlightMovement flight,
        Func<float, AnimalBehaviorTreeStatus> tick) => new AquaticHuntActionNode(flight, tick);
}
#endregion

public static class AquaticHuntCleanupFixture
{
    #region 捕食动作抢占与释放回归
    private sealed class Prey : IAquaticPredatorCarryTarget
    {
        public bool Captured = true;
        public int Releases;
        public bool IsCapturedBy(Item predator) => Captured;
        public void ReleaseFromPredator(Item predator) { Releases++; Captured = false; }
    }
    private sealed class Escape : AnimalBehaviorTreeNode
    {
        protected override AnimalBehaviorTreeStatus OnTick(float deltaTime) => AnimalBehaviorTreeStatus.Running;
    }
    public static void Verify()
    {
        int checks = 0;
        void Check(bool value, string detail)
        {
            checks++;
            if (!value) throw new InvalidOperationException("水生捕食清理错误：" + detail);
        }
        var flight = new Mod_AnimalFlightMovement();
        var prey = new Prey();
        flight.Capture(prey);
        bool emergency = false;
        AnimalBehaviorTreeRegistry.RegisterCondition("probe.emergency", (_, _) => () => emergency);
        AnimalBehaviorTreeRegistry.RegisterAction("probe.escape", (_, _) => new Escape());
        AnimalBehaviorTreeRegistry.RegisterAction("probe.hunt", (_, _) =>
            AnimalSpecialMovementBehaviorNodes.CreateAquaticHuntProbe(flight, _ => AnimalBehaviorTreeStatus.Running));
        var definition = JObject.Parse(@"{'schemaVersion':1,'root':{'type':'selector','children':[
          {'type':'sequence','children':[{'type':'condition','name':'probe.emergency'},{'type':'action','name':'probe.escape'}]},
          {'type':'action','name':'probe.hunt'}]}}").ToObject<AnimalBehaviorTreeDefinition>();
        AnimalBehaviorTreeRuntime runtime = AnimalBehaviorTreeCompiler.Compile(definition).CreateRuntime(null);
        Check(runtime.Tick(.1f) == AnimalBehaviorTreeStatus.Running && prey.Releases == 0, "捕食进行时保留猎物");
        emergency = true;
        Check(runtime.Tick(.1f) == AnimalBehaviorTreeStatus.Running && prey.Releases == 1, "紧急分支抢占时释放猎物");
        Check(flight.Cleared, "抢占时清除捕食临时状态");
        runtime.Abort();
        runtime.Abort();
        Check(prey.Releases == 1, "重复中止不能重复释放猎物");

        flight = new Mod_AnimalFlightMovement(); prey = new Prey(); flight.Capture(prey);
        var failed = AnimalSpecialMovementBehaviorNodes.CreateAquaticHuntProbe(flight, _ => AnimalBehaviorTreeStatus.Failure);
        Check(failed.Tick(.1f) == AnimalBehaviorTreeStatus.Failure && prey.Releases == 1 && flight.Cleared,
            "捕食失败时释放猎物并清除状态");
        flight = new Mod_AnimalFlightMovement(); prey = new Prey(); flight.Capture(prey);
        var completed = AnimalSpecialMovementBehaviorNodes.CreateAquaticHuntProbe(flight, _ => AnimalBehaviorTreeStatus.Success);
        Check(completed.Tick(.1f) == AnimalBehaviorTreeStatus.Success && prey.Releases == 0 && !flight.Cleared,
            "成功完成阶段时保留猎物供下一阶段使用");
        completed.Abort();
        Check(prey.Releases == 0, "已完成动作的中止不能误释放猎物");
        flight.CancelAquaticHunt();
        flight.CancelAquaticHunt();
        Check(prey.Releases == 1 && flight.Cleared, "卸载取消释放一次并清除状态");
        flight = new Mod_AnimalFlightMovement(); prey = new Prey { Captured = false }; flight.Capture(prey);
        flight.CancelAquaticHunt();
        Check(prey.Releases == 0 && flight.Cleared, "没有被本捕食者持有时不释放");
        Console.WriteLine($"PASS {checks} production aquatic hunt interruption and prey-release checks");
    }
    #endregion
}
