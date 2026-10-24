using UnityEngine;

// 替身只提供环境输入与副作用采样，追踪记忆及集火入口直接编译生产代码。
internal static class Program
{
    #region 生产规则回归
    private static int passed, failed;
    private static void Check(bool condition, string label)
    {
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {label}");
        if (condition) passed++; else failed++;
    }

    private static (AI_Wolf wolf, Item target) Create()
    {
        var wolf = new AI_Wolf();
        var target = new Item { itemData = new ItemData { Guid = 20 } };
        target.transform.position = new Vector3(8, 0);
        wolf.TargetCandidate = target;
        return (wolf, target);
    }

    private static void Main()
    {
        var (wolf, target) = Create();
        wolf.TriggerPackAttack(target);
        Vector3 seen = target.transform.position;
        wolf.Visible = false;
        target.transform.position = new Vector3(20, 7);
        wolf.Refresh();
        Check(wolf.Searching && wolf.LastKnownThreatPosition == seen,
            "集火确认后、第一次状态评估前丢视野，仍保留搜索坐标");

        (wolf, target) = Create();
        var caller = new AI_Wolf { TargetCandidate = target };
        caller.TriggerPackAttack(target);
        wolf.ReceivePackCall(target, caller);
        seen = target.transform.position;
        wolf.Visible = false;
        target.transform.position = new Vector3(20, 7);
        wolf.Refresh();
        Check(wolf.Searching && wolf.LastKnownThreatPosition == seen,
            "收到可见目标的同伴呼叫后、第一次决策前丢视野仍搜索");

        (wolf, target) = Create();
        wolf.ObserveInChase(target);
        seen = target.transform.position;
        wolf.Visible = false;
        target.transform.position = new Vector3(.1f, 0);
        wolf.Refresh();
        wolf.EnterSearch();
        wolf.NavigationResult = WorldNavigationDestinationResult.Pending;
        wolf.SearchTick();
        Check(wolf.Searching && wolf.RequestedDestination == seen && wolf.LastKnownThreatPosition == seen,
            "隐藏目标移动到身边也只向最后可见位置申请路径");
        Check(wolf.HasThreatMemory, "异步路径等待不会清除目标记忆");
        wolf.TimerTick(2);
        Check(wolf.HasThreatMemory && !wolf.WaitingAtLastThreatPosition, "赶路阶段不消费到点搜索时间");
        wolf.transform.position = seen;
        wolf.SearchTick();
        wolf.SearchTick();
        Check(wolf.WaitingAtLastThreatPosition && wolf.DetectionRequests == 1 && wolf.Stopped,
            "抵达最后位置停车且仅额外请求一次感知");
        wolf.TimerTick(wolf.lostTargetSearchDuration - .1f);
        Check(wolf.HasThreatMemory, "到点搜索时间未满时保留记忆");
        wolf.TimerTick(.2f);
        Check(!wolf.HasThreatMemory && !wolf.Searching, "到点搜索超时后清理目标");

        (wolf, target) = Create();
        wolf.ObserveInChase(target);
        wolf.Visible = false;
        wolf.Refresh();
        wolf.EnterSearch();
        wolf.TimerTick(wolf.lostTargetTravelTimeout + .1f);
        Check(!wolf.HasThreatMemory, "不可抵达目标受独立赶路超时限制");

        (wolf, target) = Create();
        wolf.ObserveInChase(target);
        wolf.Visible = false;
        wolf.Refresh();
        wolf.EnterSearch();
        wolf.transform.position = wolf.LastKnownThreatPosition;
        wolf.SearchTick();
        target.transform.position = new Vector3(12, 3);
        wolf.Visible = true;
        wolf.Refresh();
        Check(wolf.ThreatVisible && wolf.LastKnownThreatPosition == target.transform.position &&
              !wolf.WaitingAtLastThreatPosition && !wolf.Searching, "重新看见目标更新记忆并退出到点等待");
        wolf.Visible = false;
        wolf.Refresh();
        target.itemData.Guid++;
        wolf.Refresh();
        Check(!wolf.HasThreatMemory, "回池复用的另一身份不能继承旧仇恨");

        (wolf, target) = Create();
        wolf.ObserveInChase(target);
        wolf.Visible = false;
        target.RuntimeGeneration++;
        wolf.Refresh();
        Check(!wolf.HasThreatMemory, "同GUID但运行代际变化时清除记忆");

        (wolf, target) = Create();
        wolf.ObserveInChase(target);
        wolf.Visible = false;
        target.itemMods.Health.Hp = 0;
        wolf.Refresh();
        Check(!wolf.HasThreatMemory, "死亡目标不会继续被追踪");

        (wolf, target) = Create();
        caller = new AI_Wolf { TargetCandidate = target };
        caller.TriggerPackAttack(target);
        seen = caller.LastKnownThreatPosition;
        wolf.Visible = false;
        wolf.ReceivePackCall(target, caller);
        target.transform.position = new Vector3(25, 8);
        wolf.Refresh();
        Check(wolf.Searching && wolf.LastKnownThreatPosition == seen && !wolf.ThreatVisible,
            "未直接见目标的同伴只得到呼叫者已知坐标");
        caller.Visible = false;
        caller.Refresh();
        var uninformed = new AI_Wolf { Visible = false };
        uninformed.ReceivePackCall(target, caller);
        Check(!uninformed.HasThreatMemory, "没有视野的同伴不能继续传播目标情报");

        (wolf, target) = Create();
        wolf.Visible = false;
        wolf.TriggerPackAttack(target);
        Check(!wolf.HasThreatMemory, "从未看到目标时不凭空建立追踪记忆");
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }
    #endregion
}

public partial class AI_Wolf
{
    #region 环境替身与正式入口适配
    public Item item = new() { itemData = new ItemData { Guid = 1 } };
    public Transform transform => item.transform;
    public string name => "Wolf";
    public Item TargetCandidate;
    public bool Visible = true;
    public WorldNavigationDestinationResult NavigationResult;
    public Vector3 RequestedDestination;
    public bool Stopped => _mover.Stopped;
    public int DetectionRequests => _detector.Requests;
    public bool Searching => ShouldSearch();
    private Item _currentThreat;
    private WolfState _currentState = WolfState.Idle;
    private float _packAssistTimer, _alertTimer;
    private readonly Mover _mover = new();
    private readonly Detector _detector = new();
    private readonly bool debugLog = false;
    private readonly float chaseLossDistance = 44, avoidSafeDistance = 30, alertKeepDuration = 2,
        packAssistDuration = 5, chasePathRetryDelay = 3;
    private readonly int chasePathCostLimit = 440;
    public void ObserveInChase(Item target) { _currentState = WolfState.Chase; RememberThreat(target, target.transform.position, true); }
    public void EnterSearch() => _currentState = WolfState.Search;
    public void Refresh() => RefreshThreatTarget();
    public void SearchTick() => TickSearch();
    public void TimerTick(float seconds) => TickThreatMemory(seconds);
    private void ClearChaseFormation() { }
    private bool IsChaseTargetBlockedByPathCost(Item target) => false;
    private static float DecrementTimer(float remaining, float deltaTime) => MathF.Max(0, remaining - deltaTime);
    private void StopMove() => _mover.StopMovement();
    private WorldNavigationDestinationResult MoveToChaseTarget(Vector3 position, int cost, float range)
    { RequestedDestination = position; return NavigationResult; }
    private bool TryHandleRejectedChasePath(WorldNavigationDestinationResult result, Item target, float retry)
        => result == WorldNavigationDestinationResult.RejectedByPathCost;
    private void FaceTarget(Vector3 position) { }
    private bool IsWithinEffectivePerceptionRange(Item target, float range)
        => Visible && target != null && WorldTopologyRuntime.SqrDistance(transform.position, target.transform.position) <= range * range;
    private void CallNearbyWolves(Item target) { }
    private bool IsAggressiveAdvanceActive() => false;
    private Item FindClosestAdvanceAggressionTarget() => FindClosestPlayerThreat();
    private Item FindClosestPlayerThreat()
        => IsUsableThreat(TargetCandidate) && IsWithinEffectivePerceptionRange(TargetCandidate, chaseLossDistance) ? TargetCandidate : null;
    #endregion
}

#region 非Unity运行环境
public enum WolfState { Idle, Move, Alert, Chase, Attack, Avoid, Flee, Advance, Search }
public enum WorldNavigationDestinationResult { Accepted, Pending, Failed, RejectedByPathCost }
public class Item
{
    public ItemData itemData = new();
    public Transform transform = new();
    public GameObject gameObject = new();
    public ItemMods itemMods = new();
    public bool DestructionHandled, IsUnloadingModules;
    public uint RuntimeGeneration;
    public string name => "Target";
}
public class ItemData { public int Guid; }
public class GameObject { public int scene; }
public class Transform { public Vector3 position; }
public class Mod_DamageReceiver { public float Hp = 100; }
public class ItemMods
{
    public Mod_DamageReceiver Health = new();
    public T GetMod_ByID<T>(string id) where T : class => Health as T;
}
public static class ModText { public const string Hp = "Hp"; }
public enum FactionRelation { Friendly }
public static class FactionRelationService
{
    public static bool CanAttack(Item source, Item target) => source != target;
    public static FactionRelation GetRelation(Item source, Item target) => FactionRelation.Friendly;
}
public static class WorldTopologyRuntime
{
    public static float SqrDistance(Vector3 a, Vector3 b)
        => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);
}
public class Mover { public bool Stopped; public void StopMovement() => Stopped = true; }
public class Detector { public int Requests; public void RequestDetectorUpdate() => Requests++; }
namespace UnityEngine
{
    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z;
        public Vector3(float x, float y, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public bool Equals(Vector3 other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object value) => value is Vector3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public static bool operator ==(Vector3 a, Vector3 b) => a.Equals(b);
        public static bool operator !=(Vector3 a, Vector3 b) => !a.Equals(b);
    }
    public static class Mathf { public static float Max(float a, float b) => MathF.Max(a, b); }
    public static class Debug { public static void Log(string text, object context) { } }
}
#endregion
