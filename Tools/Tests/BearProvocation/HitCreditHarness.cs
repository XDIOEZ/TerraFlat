using FlatWorld.Combat;
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

    // 替身只提供活体、场景、视野和注册表，受击、归属、范围观察和目标选择原样编译生产方法。
    internal static void Run()
    {
        ItemMgr.Instance.Clear();
        var bear = new AI_BearTerritorialRetaliation();
        Player first = Target(11, 4f);
        Player second = Target(12, 1f);
        var weapon = new Item { itemData = new ItemData { Guid = 21 }, Identity = Identity(21), Owner = first };
        DamageReceiverDamageInfo firstHit = Hit(weapon, first, 0f);
        Check(AI_BearTerritorialRetaliation.ResolveRetaliationCreditActor(firstHit) == first, "武器命中归属实际角色");
        bear.HandleConfirmedHit(firstHit);
        Check(bear.Hatred(first.Identity) == 30 && bear.Hatred(weapon.Identity) == 0 && bear.Selected == first,
            "有效零伤命中立即对角色记30仇恨，武器不记仇");
        bear.HandleConfirmedHit(firstHit);
        Check(bear.Hatred(first.Identity) == 60, "同一攻击者每次命中独立增加30");
        for (int i = 0; i < 3; i++) bear.HandleConfirmedHit(Hit(second, second, 1));
        Check(bear.Selected == second && bear.Hatred(second.Identity) == 90, "真实选敌入口选择最高仇恨");
        second.Visible = false;
        bear.Refresh();
        Check(bear.Selected == first, "最高仇恨失去视野时选择其余可见目标");
        first.Visible = false;
        bear.Refresh();
        Check(bear.Selected == null && bear.Hatred(second.Identity) == 90, "全部隐藏时不隔墙锁敌且保留仇恨");
        first.Visible = second.Visible = true;
        weapon.Owner = second;
        Check(AI_BearTerritorialRetaliation.ResolveRetaliationCreditActor(firstHit) == first,
            "武器换持有者后仍按命中时冻结的完整身份归因");
        bear.HandleConfirmedHit(firstHit);
        Check(bear.Hatred(first.Identity) == 90 && bear.Hatred(second.Identity) == 90,
            "换持有者不把旧命中的仇恨转给新角色");
        CombatIdentity oldFirst = first.Identity;
        first.Identity = oldFirst with { Generation = oldFirst.Generation + 1 };
        Check(AI_BearTerritorialRetaliation.ResolveRetaliationCreditActor(firstHit) == null,
            "同GUID新代际不继承冻结的旧攻击归属");
        bear.Advance(0);
        Check(bear.Hatred(oldFirst) == 0 && bear.Selected == second, "生产清理移除已复用身份的旧仇恨");
        foreach (CombatIdentity wrong in new[]
        {
            first.Identity with { World = 2 }, first.Identity with { Dimension = 2 },
            first.Identity with { Backend = CombatBackend.Entity }
        })
        {
            var mismatch = Hit(weapon, first, 0);
            mismatch.Context.Credit = wrong;
            Check(AI_BearTerritorialRetaliation.ResolveRetaliationCreditActor(mismatch) == null,
                "世界、维度或后端不同的冻结归属被拒绝");
        }
        var environment = Hit(second, second, 0);
        environment.Context.Attack.Source.Backend = CombatBackend.Environment;
        bear.HandleConfirmedHit(environment);
        bear.HandleConfirmedHit(Hit(second, second, -1));
        bear.HandleConfirmedHit(null);
        Check(bear.Hatred(second.Identity) == 90, "环境、拒绝命中与空事件不添加仇恨");
        var ownerFallback = Hit(weapon, second, 0);
        ownerFallback.Context.Credit = default;
        Check(AI_BearTerritorialRetaliation.ResolveRetaliationCreditActor(ownerFallback) == second,
            "没有冻结归属时沿正式Owner链归给角色");
        weapon.Owner = null;
        bear.HandleConfirmedHit(ownerFallback);
        Check(bear.Hatred(weapon.Identity) == 0, "无活体Owner的武器实体不能成为反击对象");
        bear.Advance(90);
        Check(bear.Selected == null && bear.Hatred(second.Identity) == 0, "正式时间推进在所有仇恨归零后脱战");

        ItemMgr.Instance.Clear();
        bear = new AI_BearTerritorialRetaliation();
        first = Target(31, 4f);
        first.Visible = false;
        ItemMgr.Instance.Player_DIC.Add(31, first);
        bear.Advance(60);
        Check(bear.Presence(first.Identity) == 60 && bear.Hatred(first.Identity) == 0,
            "真实范围观察在恰好60秒保持和平");
        bear.Advance(.01f);
        Check(bear.Presence(first.Identity) > 60 && bear.Hatred(first.Identity) == 120 && bear.Selected == null,
            "无视野也累计周围驻留，激怒后不隔墙攻击");
        first.Visible = true;
        bear.Advance(0);
        Check(bear.Selected == first, "重新可见后才锁定已有仇恨对象");
        first.transform.position = new Vector3(8.01f, 0);
        bear.Advance(1);
        Check(bear.Presence(first.Identity) == 0 && bear.Hatred(first.Identity) == 119,
            "越出8米重置驻留但既有仇恨继续衰减");
        first.transform.position = new Vector3(8f, 0);
        bear.Advance(60);
        Check(bear.Presence(first.Identity) == 60 && bear.Hatred(first.Identity) == 59,
            "8米边界属于周围，新驻留仍须严格超时");
        bear.item.MaximumPerceptionRadius = 3f;
        bear.Advance(0);
        Check(bear.Presence(first.Identity) == 0, "通用外部感知半径上限限制驻留范围");
        bear.item.MaximumPerceptionRadius = float.PositiveInfinity;
        first.gameObject.scene = 2;
        bear.Advance(0);
        Check(bear.Hatred(first.Identity) == 0, "跨场景玩家的旧仇恨被清理");
        Console.WriteLine($"PASS {count} production Bear hit-credit, identity, proximity and target-selection checks");
    }

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

public abstract class BearRuleProbeBase
{
    protected virtual void UpdateExtraTimers(float deltaTime) { }
    protected virtual void OnPreEvaluate() { }
}

public partial class AI_BearTerritorialRetaliation : BearRuleProbeBase
{
    #region 环境替身输入
    public Item item = new() { itemData = new ItemData { Guid = 1 }, Identity = new CombatIdentity { Value = 1, Backend = CombatBackend.External } };
    private readonly AIProximityHatredLedger<CombatIdentity> hatredLedger = new();
    private readonly Dictionary<CombatIdentity, Item> trackedTargets = new();
    private readonly List<CombatIdentity> invalidTargets = new();
    private readonly ProbeAttackController attack = new();
    private readonly Predicate<CombatIdentity> eligibleTargetPredicate;
    private Item currentThreat;
    public bool IsAlive = true;
    public float territorialPresenceRadius = 8f, chasePerceptionDistance = 30f;
    public Transform transform => item.transform;
    public Item Selected => currentThreat;
    public AI_BearTerritorialRetaliation()
    { hatredLedger.Configure(60, 120, 1, 30); eligibleTargetPredicate = IsEligibleVisibleHatredTarget; }
    public float Hatred(CombatIdentity identity) => hatredLedger.GetHatred(identity);
    public float Presence(CombatIdentity identity) => hatredLedger.GetContinuousPresenceSeconds(identity);
    public void Refresh() => OnPreEvaluate();
    public void Advance(float delta) { UpdateExtraTimers(delta); OnPreEvaluate(); }
    private bool IsChaseTargetBlockedByPathCost(Item target) => false;
    private bool IsWithinEffectivePerceptionRange(Item target, float range)
        => target.Visible && WorldTopologyRuntime.SqrDistance(transform.position, target.transform.position) <= range * range;
    #endregion
}

#region 非Unity场景与身份替身
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
public class Mod_DamageReceiver { public float Hp = 100; }
public class ProbeGameObject { public bool activeInHierarchy = true; public int scene; }
public class Transform { public Vector3 position; }
public class ProbeAttackController
{
    public bool IsAttackLocked;
    public void StopWindow() => IsAttackLocked = false;
    public void Tick(float delta) { }
}
public static class Tag { public const string Player = "Player"; }
public static class ProbeTagExtensions { public static bool ContainsTag(this List<string> list, string value) => list.Contains(value); }
public static class ModText { public const string Hp = "Hp"; }
public static class FactionRelationService { public static bool CanAttack(Item source, Item target) => source != target && target.FactionAllows; }
public static class WorldTopologyRuntime
{
    public static float SqrDistance(Vector3 a, Vector3 b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);
}
public sealed class ItemMgr
{
    public static readonly ItemMgr Instance = new();
    public readonly Dictionary<int, Item> Items = new();
    public readonly Dictionary<int, Player> Player_DIC = new();
    public static ItemMgr GetInstance() => Instance;
    public Item GetItemByGuid(int guid) => Items.GetValueOrDefault(guid);
    public void Clear() { Items.Clear(); Player_DIC.Clear(); }
}
public static class GameplayCombatBridge { public static CombatIdentity Identity(Item item) => item?.Identity ?? default; }
public class DamageReceiverDamageInfo { public Item Attacker; public float DamageValue; public CombatDamageContext Context; }
namespace UnityEngine
{
    public readonly record struct Vector3(float x, float y, float z = 0);
    public static class Mathf { public static float Max(float a, float b) => MathF.Max(a, b); public static float Min(float a, float b) => MathF.Min(a, b); }
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
