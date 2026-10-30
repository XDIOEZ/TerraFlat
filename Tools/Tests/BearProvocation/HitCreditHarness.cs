using FlatWorld.Combat;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class BearHitCreditHarness
{
    #region 正式受击与归属入口离线回归
    private static int count;

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        count++;
    }

    // 替身提供活体、场景、视野和注册表，仇恨、范围观察与目标选择均执行正式能力。
    internal static void Run()
    {
        ItemMgr.Instance.Clear();
        var context = CreateContext();
        var bear = CreateCapability(context);
        Player first = Target(11, 4f);
        Player second = Target(12, 1f);
        var weapon = new Item { itemData = new ItemData { Guid = 21 }, Identity = Identity(21), Owner = first };
        DamageReceiverDamageInfo firstHit = Hit(weapon, first, 0f);
        Check(AnimalTerritorialHatredCapability.ResolveRetaliationCreditActor(firstHit) == first, "武器命中归属实际角色");
        bear.HandleConfirmedHit(firstHit);
        Check(bear.Ledger.GetHatred(first.Identity) == 30 && bear.Ledger.GetHatred(weapon.Identity) == 0 && bear.CurrentTarget == first,
            "有效零伤命中立即对角色记 30 仇恨，武器不记仇");
        bear.HandleConfirmedHit(firstHit);
        Check(bear.Ledger.GetHatred(first.Identity) == 60, "同一攻击者每次命中独立增加 30");
        for (int i = 0; i < 3; i++) bear.HandleConfirmedHit(Hit(second, second, 1));
        Check(bear.CurrentTarget == second && bear.Ledger.GetHatred(second.Identity) == 90, "真实选敌入口选择最高仇恨");
        context.Combat = new AnimalCombatCapability();
        bear.LockCurrentTarget = true;
        second.Visible = false;
        bear.Tick(0, null);
        Check(context.Combat.Interrupted && !bear.LockCurrentTarget && bear.CurrentTarget == first,
            "锁定目标失去视野时先中断攻击，再选择其余可见目标");
        first.Visible = false;
        bear.Tick(0, null);
        Check(bear.CurrentTarget == null && bear.Ledger.GetHatred(second.Identity) == 90, "全部隐藏时不隔墙锁敌且保留仇恨");
        first.Visible = second.Visible = true;
        weapon.Owner = second;
        Check(AnimalTerritorialHatredCapability.ResolveRetaliationCreditActor(firstHit) == first,
            "武器换持有者后仍按命中时冻结的完整身份归因");
        bear.HandleConfirmedHit(firstHit);
        Check(bear.Ledger.GetHatred(first.Identity) == 90 && bear.Ledger.GetHatred(second.Identity) == 90,
            "换持有者不把旧命中的仇恨转给新角色");
        CombatIdentity oldFirst = first.Identity;
        first.Identity = oldFirst with { Generation = oldFirst.Generation + 1 };
        Check(AnimalTerritorialHatredCapability.ResolveRetaliationCreditActor(firstHit) == null,
            "同 GUID 新代际不继承冻结的旧攻击归属");
        bear.Tick(0, null);
        Check(bear.Ledger.GetHatred(oldFirst) == 0 && bear.CurrentTarget == second, "生产清理移除已复用身份的旧仇恨");
        foreach (CombatIdentity wrong in new[]
        {
            first.Identity with { World = 2 }, first.Identity with { Dimension = 2 },
            first.Identity with { Backend = CombatBackend.Entity }
        })
        {
            var mismatch = Hit(weapon, first, 0);
            mismatch.Context.Credit = wrong;
            Check(AnimalTerritorialHatredCapability.ResolveRetaliationCreditActor(mismatch) == null,
                "世界、维度或后端不同的冻结归属被拒绝");
        }
        var environment = Hit(second, second, 0);
        environment.Context.Attack.Source.Backend = CombatBackend.Environment;
        bear.HandleConfirmedHit(environment);
        bear.HandleConfirmedHit(Hit(second, second, -1));
        bear.HandleConfirmedHit(null);
        Check(bear.Ledger.GetHatred(second.Identity) == 90, "环境、拒绝命中与空事件不添加仇恨");
        var ownerFallback = Hit(weapon, second, 0);
        ownerFallback.Context.Credit = default;
        Check(AnimalTerritorialHatredCapability.ResolveRetaliationCreditActor(ownerFallback) == second,
            "没有冻结归属时沿正式 Owner 链归给角色");
        weapon.Owner = null;
        bear.HandleConfirmedHit(ownerFallback);
        Check(bear.Ledger.GetHatred(weapon.Identity) == 0, "无活体 Owner 的武器实体不能成为反击对象");
        bear.Tick(90, null);
        Check(bear.CurrentTarget == null && bear.Ledger.GetHatred(second.Identity) == 0, "正式时间推进在所有仇恨归零后脱战");
        bear.Dispose();

        ItemMgr.Instance.Clear();
        context = CreateContext();
        bear = CreateCapability(context);
        first = Target(31, 4f);
        first.Visible = false;
        ItemMgr.Instance.Player_DIC.Add(31, first);
        bear.Tick(60);
        Check(bear.Ledger.GetContinuousPresenceSeconds(first.Identity) == 60 && bear.Ledger.GetHatred(first.Identity) == 0,
            "真实范围观察在恰好 60 秒保持和平");
        bear.Tick(.01f);
        Check(bear.Ledger.GetContinuousPresenceSeconds(first.Identity) > 60 && bear.Ledger.GetHatred(first.Identity) == 120 && bear.CurrentTarget == null,
            "无视野也累计周围驻留，激怒后不隔墙攻击");
        first.Visible = true;
        bear.Tick(0);
        Check(bear.CurrentTarget == first, "重新可见后才锁定已有仇恨对象");
        first.transform.position = new Vector3(8.01f, 0);
        bear.Tick(1);
        Check(bear.Ledger.GetContinuousPresenceSeconds(first.Identity) == 0 && bear.Ledger.GetHatred(first.Identity) == 119,
            "越出 8 米重置驻留但既有仇恨继续衰减");
        first.transform.position = new Vector3(8f, 0);
        bear.Tick(60);
        Check(bear.Ledger.GetContinuousPresenceSeconds(first.Identity) == 60 && bear.Ledger.GetHatred(first.Identity) == 59,
            "8 米边界属于周围，新驻留仍须严格超时");
        context.Actor.MaximumPerceptionRadius = 3f;
        bear.Tick(0);
        Check(bear.Ledger.GetContinuousPresenceSeconds(first.Identity) == 0, "通用外部感知半径上限限制驻留范围");
        context.Actor.MaximumPerceptionRadius = float.PositiveInfinity;
        first.gameObject.scene = 2;
        bear.Tick(0);
        Check(bear.Ledger.GetHatred(first.Identity) == 0, "跨场景玩家的旧仇恨被清理");
        bear.Dispose();
        Console.WriteLine($"PASS {count} production territorial hit-credit, identity, proximity and target-selection checks");
    }

    private static AnimalBehaviorTreeContext CreateContext() => new()
    {
        Actor = new Item { itemData = new ItemData { Guid = 1 }, Identity = Identity(1) },
        Health = new Mod_DamageReceiver(), Detector = new Mod_ItemDetector()
    };

    private static AnimalTerritorialHatredCapability CreateCapability(AnimalBehaviorTreeContext context) =>
        new(context, new JObject { ["presenceRadius"] = 8, ["presenceThresholdSeconds"] = 60,
            ["initialHatred"] = 120, ["decayPerSecond"] = 1, ["hatredPerHit"] = 30, ["chaseDistance"] = 30 }, null);

    private static CombatIdentity Identity(int guid) => new()
    { Value = unchecked((uint)guid), Generation = 1, Backend = CombatBackend.External };

    private static Player Target(int guid, float x)
    {
        var target = new Player { itemData = new ItemData { Guid = guid }, Identity = Identity(guid) };
        target.itemMods.Health = new Mod_DamageReceiver();
        target.transform.position = new Vector3(x, 0);
        ItemMgr.Instance.Items[guid] = target;
        return target;
    }

    private static DamageReceiverDamageInfo Hit(Item source, Item credit, float damage) => new()
    {
        Attacker = source, DamageValue = damage,
        Context = new CombatDamageContext
        { Credit = credit.Identity, Attack = new CombatAttackKey { Source = source.Identity } }
    };
    #endregion
}

#region 非 Unity 场景与身份输入
public abstract class AnimalBehaviorTreeCapability
{
    public virtual void Tick(float deltaTime) { }
    public virtual void Dispose() { }
}
public class AnimalBehaviorTreeContext
{
    public Item Actor;
    public Vector3 Position => Actor.transform.position;
    public Mod_ItemDetector Detector;
    public Mod_DamageReceiver Health;
    public AnimalCombatCapability Combat;
    public bool TryGetCapability<T>(string key, out T value) where T : AnimalBehaviorTreeCapability
    { value = Combat as T; return value != null; }
}
public class AnimalCombatCapability : AnimalBehaviorTreeCapability
{
    public bool Interrupted;
    public void StopAttack(bool interrupted) => Interrupted = interrupted;
}
public class Item
{
    public ItemData itemData;
    public Item Owner;
    public ItemMods itemMods = new();
    public ProbeGameObject gameObject = new();
    public Transform transform = new();
    public CombatIdentity Identity;
    public bool DestructionHandled, IsUnloadingModules;
    public bool Visible = true, FactionAllows = true;
    public float MaximumPerceptionRadius = float.PositiveInfinity;
}
public class Player : Item { }
public class ItemData { public int Guid; public List<string> Tags = new(); }
public class ItemMods
{
    public Mod_DamageReceiver Health;
    public T GetMod_ByID<T>(string id) where T : class => Health as T;
}
public class Mod_DamageReceiver
{
    public float Hp = 100;
    public event Action<DamageReceiverDamageInfo> OnHitReceived;
}
public class Mod_ItemDetector
{
    public float DetectionRadius;
    public static float CalculateEffectiveDetectionRadius(float radius, Item target) => radius;
    public bool HasLineOfSight(Item target) => target.Visible;
}
public class ProbeGameObject { public bool activeInHierarchy = true; public int scene; }
public class Transform { public Vector3 position; }
public static class Tag { public const string Player = "Player"; }
public static class ProbeTagExtensions { public static bool ContainsTag(this List<string> list, string value) => list.Contains(value); }
public static class ModText { public const string Hp = "Hp"; }
public static class FactionRelationService { public static bool CanAttack(Item source, Item target) => source != target && target.FactionAllows; }
public static class GameplayWorldRegistry { public static int GetItemWorldIdentity(Item item) => 1; }
public static class WorldTopologyRuntime
{
    public static float SqrDistance(Vector3 a, Vector3 b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);
    public static float Distance(Vector3 a, Vector3 b) => MathF.Sqrt(SqrDistance(a, b));
}
public sealed class ItemMgr
{
    public static readonly ItemMgr Instance = new();
    public readonly Dictionary<int, Item> Items = new();
    public readonly Dictionary<int, Player> Player_DIC = new();
    public static ItemMgr GetInstance() => Instance;
    public Item GetItemByGuid(int guid) => Items.GetValueOrDefault(guid);
    public bool TryIsWithinDataPerceptionRange(Vector3 position, Item target, float radius, out bool within)
    { within = false; return false; }
    public void Clear() { Items.Clear(); Player_DIC.Clear(); }
}
public static class GameplayCombatBridge { public static CombatIdentity Identity(Item item) => item?.Identity ?? default; }
public class DamageReceiverDamageInfo { public Item Attacker; public float DamageValue; public CombatDamageContext Context; }
namespace UnityEngine
{
    public readonly record struct Vector3(float x, float y, float z = 0);
    public static class Mathf
    {
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Max(float a, float b, float c) => MathF.Max(a, MathF.Max(b, c));
        public static float Min(float a, float b) => MathF.Min(a, b);
    }
}
namespace FlatWorld.Combat
{
    public enum CombatBackend : byte { None, Entity, External, Environment }
    public record struct CombatIdentity
    {
        public ulong Value;
        public uint Generation, World, Dimension;
        public CombatBackend Backend;
        public bool IsValid => Backend != CombatBackend.None && Value != 0;
    }
    public struct CombatAttackKey { public CombatIdentity Source; }
    public struct CombatDamageContext { public CombatIdentity Credit; public CombatAttackKey Attack; }
}
#endregion
