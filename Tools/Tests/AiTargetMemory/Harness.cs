using Newtonsoft.Json.Linq;
using UnityEngine;

// 替身提供实体、可见性和导航输入；目标规则、记忆、搜索动作及群体情报入口取自生产代码。
internal static class Program
{
    #region 生产规则回归
    private static int passed;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        passed++;
    }

    private static (AnimalBehaviorTreeContext context, AnimalPackCapability pack, Item target) Create()
    {
        var context = new AnimalBehaviorTreeContext(new Item { itemData = new ItemData { Guid = 1 } });
        var pack = new AnimalPackCapability(context);
        context.Pack = pack;
        var target = new Item { itemData = new ItemData { Guid = 20 } };
        target.transform.position = new Vector3(8, 0);
        return (context, pack, target);
    }

    private static void Main()
    {
        var (context, pack, target) = Create();
        pack.TriggerGroupAttack(target);
        Vector3 seen = target.transform.position;
        target.Visible = false;
        target.transform.position = new Vector3(20, 7);
        pack.Memory.RefreshVisibility(44);
        Check(pack.Memory.IsSearching && pack.Memory.LastKnownPosition == seen,
            "集火确认后、第一次决策前丢视野仍保留搜索坐标");

        (context, pack, target) = Create();
        var (callerContext, caller, _) = Create();
        callerContext.Detector.CurrentItemsInArea.Add(context.Actor);
        caller.TriggerGroupAttack(target);
        Check(pack.Memory.HasMemory && pack.Memory.CanSearch, "首次集火立即通过正式群体呼叫入口传播目标");
        seen = target.transform.position;
        target.Visible = false;
        target.transform.position = new Vector3(20, 7);
        pack.Memory.RefreshVisibility(44);
        Check(pack.Memory.IsSearching && pack.Memory.LastKnownPosition == seen,
            "收到可见目标的同伴呼叫后、首次决策前丢视野仍搜索");

        (context, pack, target) = Create();
        pack.TriggerGroupAttack(target);
        seen = target.transform.position;
        target.Visible = false;
        target.transform.position = new Vector3(.1f, 0);
        pack.Memory.RefreshVisibility(44);
        var search = new AnimalSearchAction(context, new JObject());
        context.Mod_Mover.Result = WorldNavigationDestinationResult.Pending;
        search.Tick(0);
        Check(pack.Memory.IsSearching && context.Mod_Mover.RequestedDestination == seen && pack.Memory.LastKnownPosition == seen,
            "隐藏目标移动到身边也只向最后可见位置申请路径");
        Check(pack.Memory.HasMemory, "异步路径等待不会清除目标记忆");
        search.Tick(2);
        Check(pack.Memory.HasMemory && !pack.Memory.WaitingAtLastPosition, "赶路阶段不消费到点搜索时间");
        context.Actor.transform.position = seen;
        search.Tick(0);
        search.Tick(0);
        Check(pack.Memory.WaitingAtLastPosition && context.Detector.Requests == 1 && context.Mod_Mover.Stopped,
            "抵达最后位置停车且仅额外请求一次感知");
        search.Tick(2.9f);
        Check(pack.Memory.HasMemory, "到点搜索时间未满时保留记忆");
        search.Tick(.2f);
        Check(!pack.Memory.HasMemory && !pack.Memory.IsSearching, "到点搜索超时后清理目标");

        (context, pack, target) = Create();
        pack.TriggerGroupAttack(target);
        target.Visible = false;
        pack.Memory.RefreshVisibility(44);
        search = new AnimalSearchAction(context, new JObject());
        search.Tick(15.1f);
        Check(!pack.Memory.HasMemory, "不可抵达目标受独立赶路超时限制");

        (context, pack, target) = Create();
        pack.TriggerGroupAttack(target);
        target.Visible = false;
        pack.Memory.RefreshVisibility(44);
        context.Actor.transform.position = pack.Memory.LastKnownPosition;
        search = new AnimalSearchAction(context, new JObject());
        search.Tick(0);
        target.transform.position = new Vector3(12, 3);
        target.Visible = true;
        pack.Memory.RefreshVisibility(44);
        Check(pack.Memory.Visible && pack.Memory.LastKnownPosition == target.transform.position &&
              !pack.Memory.WaitingAtLastPosition && !pack.Memory.IsSearching, "重新看见目标更新记忆并退出到点等待");
        target.Visible = false;
        pack.Memory.RefreshVisibility(44);
        target.itemData.Guid++;
        pack.Memory.RefreshVisibility(44);
        Check(!pack.Memory.HasMemory, "回池复用的另一身份不能继承旧仇恨");

        (context, pack, target) = Create();
        pack.TriggerGroupAttack(target);
        target.Visible = false;
        target.RuntimeGeneration++;
        pack.Memory.RefreshVisibility(44);
        Check(!pack.Memory.HasMemory, "同 GUID 但运行代际变化时清除记忆");

        (context, pack, target) = Create();
        pack.TriggerGroupAttack(target);
        target.Visible = false;
        target.itemMods.Health.Hp = 0;
        pack.Memory.RefreshVisibility(44);
        Check(!pack.Memory.HasMemory, "死亡目标不会继续被追踪");

        (context, pack, target) = Create();
        (_, caller, _) = Create();
        caller.TriggerGroupAttack(target);
        seen = caller.Memory.LastKnownPosition;
        target.Visible = false;
        pack.ReceiveGroupCall(target, caller);
        target.transform.position = new Vector3(25, 8);
        pack.Memory.RefreshVisibility(44);
        Check(pack.Memory.IsSearching && pack.Memory.LastKnownPosition == seen && !pack.Memory.Visible,
            "未直接见目标的同伴只得到呼叫者已知坐标");
        caller.Memory.RefreshVisibility(44);
        var (_, uninformed, _) = Create();
        uninformed.ReceiveGroupCall(target, caller);
        Check(!uninformed.Memory.HasMemory, "没有视野的同伴不能继续传播目标情报");

        (context, pack, target) = Create();
        target.Visible = false;
        pack.TriggerGroupAttack(target);
        Check(!pack.Memory.HasMemory, "从未看到目标时不凭空建立追踪记忆");

        foreach (WorldNavigationDestinationResult result in new[]
                 { WorldNavigationDestinationResult.RejectedByPathCost, WorldNavigationDestinationResult.Failed })
        {
            (context, pack, target) = Create();
            pack.TriggerGroupAttack(target);
            target.Visible = false;
            pack.Memory.RefreshVisibility(44);
            context.Mod_Mover.Result = result;
            search = new AnimalSearchAction(context, new JObject());
            Check(search.Tick(0) == AnimalBehaviorTreeStatus.Failure && !pack.Memory.HasMemory && context.Mod_Mover.Stopped,
                "路径代价拒绝或寻路失败时没有清理搜索并停车");
        }
        Console.WriteLine($"PASS: {passed} production target-memory, search and pack-call checks");
    }
    #endregion
}

public partial class AnimalPackCapability
{
    #region 群体入口环境
    private readonly AnimalBehaviorTreeContext context;
    private readonly AnimalTargetMemoryCapability memory;
    private readonly JObject config = new();
    private float alertTimer, assistTimer, callTimer;
    public Item Actor => context.Actor;
    public int MemberCount => context.Detector.CurrentItemsInArea.Count + 1;
    public AnimalTargetMemoryCapability Memory => memory;
    public AnimalPackCapability(AnimalBehaviorTreeContext context)
    { this.context = context; context.Actor.Pack = this; memory = new AnimalTargetMemoryCapability(context, new JObject(), null); }
    private bool TryGetAlly(Item item, out AnimalPackCapability ally) { ally = item.Pack; return ally != null; }
    public void ClearFormation() { }
    #endregion
}

#region 非 Unity 环境输入
public abstract class AnimalBehaviorTreeCapability { public virtual void Dispose() { } }
public class AnimalBehaviorTreeContext
{
    public Item Actor;
    public Vector3 Position => Actor.transform.position;
    public Mod_ItemDetector Detector = new();
    public Mod_Mover_AI Mod_Mover = new();
    public AnimalPackCapability Pack;
    public AnimalBehaviorTreeContext(Item actor) => Actor = actor;
    public JObject GetCapabilityConfiguration(string key) => new();
    public void BeginAction(string action, string animation, AIStateAnimationRole role) { }
    public void FaceTarget(Vector3 position, bool immediate) { }
}
public static class AnimalLandActionSupport
{
    public static AnimalPackCapability Pack(AnimalBehaviorTreeContext context, JObject parameters) => context.Pack;
}
public static class AnimalLandBehaviorNodes
{
    public static AnimalChasePositionCapability Chase(AnimalBehaviorTreeContext context, JObject parameters) => new();
}
public class AnimalChasePositionCapability { }
public enum AIStateAnimationRole { Moving }
public enum WorldNavigationDestinationResult { Accepted, Pending, Failed, RejectedByPathCost }
public class Mod_Mover_AI
{
    public bool Stopped;
    public Vector3 RequestedDestination;
    public WorldNavigationDestinationResult Result = WorldNavigationDestinationResult.Accepted;
    public void StopMovement() => Stopped = true;
    public WorldNavigationDestinationResult SetInteractionDestination(Vector3 position, float range, int cost)
    { RequestedDestination = position; return Result; }
}
public class Mod_ItemDetector
{
    public int Requests;
    public List<Item> CurrentItemsInArea = new();
    public static float CalculateEffectiveDetectionRadius(float radius, Item target) => radius;
    public bool HasLineOfSight(Item target) => target.Visible;
    public void RequestDetectorUpdate() => Requests++;
}
public class Item
{
    public ItemData itemData = new();
    public Transform transform = new();
    public GameObject gameObject = new();
    public ItemMods itemMods = new();
    public AnimalPackCapability Pack;
    public bool DestructionHandled, IsUnloadingModules, Visible = true;
    public uint RuntimeGeneration;
}
public class Player : Item { }
public class ItemData { public int Guid; public List<string> Tags = new(); }
public class GameObject { public bool activeInHierarchy = true; public int scene; }
public class Transform { public Vector3 position; }
public class Mod_DamageReceiver { public float Hp = 100; }
public class ItemMods
{
    public Mod_DamageReceiver Health = new();
    public T GetMod_ByID<T>(string id) where T : class => Health as T;
}
public static class Tag { public const string Player = "Player"; }
public static class TagExtensions { public static bool ContainsTag(this List<string> tags, string tag) => tags.Contains(tag); }
public static class ModText { public const string Hp = "Hp"; }
public enum FactionRelation { Friendly }
public static class FactionRelationService
{
    public static bool CanAttack(Item source, Item target) => source != target;
    public static FactionRelation GetRelation(Item source, Item target) => FactionRelation.Friendly;
}
public static class GameplayWorldRegistry { public static int GetItemWorldIdentity(Item item) => 1; }
public class ItemMgr
{
    public static ItemMgr GetInstance() => null;
    public bool TryIsWithinDataPerceptionRange(Vector3 position, Item target, float radius, out bool within)
    { within = false; return false; }
}
public static class WorldTopologyRuntime
{
    public static float SqrDistance(Vector3 a, Vector3 b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);
    public static float Distance(Vector3 a, Vector3 b) => MathF.Sqrt(SqrDistance(a, b));
}
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
    public static class Mathf
    {
        public static float Max(float a, float b) => MathF.Max(a, b);
    }
}
#endregion
