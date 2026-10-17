using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Combat;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 正式感染目录与属性采样

    private static JObject EntityInfectionAttributes(JObject args)
    {
        BuffDefinition definition = BuffCatalogLoader.LoadBuiltInDefinitions().Find(value => value.Id == InfectionBuffIds.Infection)
            ?? throw new InvalidOperationException("正式 Buff 清单没有感染定义。");
        using var first = new GameplayTestInfectionFixture();
        using var other = new GameplayTestInfectionFixture();
        var effects = new JArray();
        foreach (BuffEffectDefinition effect in definition.Effects)
            effects.Add(new JObject
            {
                ["type"] = effect.TypeId, ["phase"] = effect.Phase.ToString(),
                ["value"] = effect.Value, ["scaled"] = effect.ScaleWithStacks
            });
        var result = new JObject
        {
            ["duration"] = definition.DurationSeconds, ["interval"] = definition.TickIntervalSeconds,
            ["mode"] = definition.StackMode.ToString(), ["maximum"] = definition.MaxStacks,
            ["decay"] = definition.DecayStacksOnExpiry, ["effects"] = effects,
            ["baseline"] = first.Snapshot(), ["unregisteredHost"] = !first.Actor.IsInitialized && !first.Mover.IsRuntimeLoaded
        };
        BuffInstance infection = first.CreateBuff(definition);
        infection.Start();
        infection.Start();
        result["one"] = first.Snapshot(infection);
        infection.SetStackCount(2);
        result["two"] = first.Snapshot(infection);
        infection.SetStackCount(3);
        result["three"] = first.Snapshot(infection);
        first.Weapon.SetDamageValues(new CombatDamage(.5f, .5f, .5f, .5f));
        result["minimum"] = first.Snapshot(infection);
        first.Weapon.SetDamageValues(new CombatDamage(1f, 2f, 3f, 4f));
        infection.SetStackCount(2);
        result["downTwo"] = first.Snapshot(infection);
        infection.SetStackCount(1);
        result["downOne"] = first.Snapshot(infection);

        BuffInstance independent = first.CreateBuff(definition);
        independent.Start();
        BuffInstance isolated = other.CreateBuff(definition, 3);
        isolated.Start();
        result["twoSources"] = first.Snapshot();
        result["otherReceiver"] = other.Snapshot(isolated);
        infection.Stop();
        result["oneSourceLeft"] = first.Snapshot();
        isolated.Stop();
        result["otherRestored"] = other.Snapshot();
        result["firstUnchanged"] = first.Snapshot();
        independent.Stop();
        infection.Stop();
        independent.Stop();
        result["restored"] = first.Snapshot();

        BuffInstance initialThree = first.CreateBuff(definition, 3);
        result["beforeStart"] = first.Snapshot(initialThree);
        initialThree.Start();
        result["startThree"] = first.Snapshot(initialThree);
        initialThree.Stop();
        result["startThreeRestored"] = first.Snapshot();

        BuffInstance periodic = first.CreateBuff(definition);
        periodic.Start();
        var losses = new JArray();
        for (int stacks = 1; stacks <= 3; stacks++)
        {
            periodic.SetStackCount(stacks);
            float before = first.Health.Hp;
            periodic.Tick(10f);
            losses.Add(before - first.Health.Hp);
        }
        periodic.Stop();
        result["periodicLosses"] = losses;
        result["periodicRestored"] = first.Snapshot();
        return result;
    }

    private static JObject EntityBuffAttributeValidation(JObject args)
    {
        try
        {
            BuffDefinitionFactory.Build(new BuffDefinitionDto
            {
                Id = "test:attribute_validation", DurationSeconds = 300f, TickIntervalSeconds = 10f,
                StackMode = "add_stacks", MaxStacks = (int)Number(args, "maximum", 3d),
                Effects = new List<BuffEffectDto>
                {
                    new()
                    {
                        TypeId = Text(args, "type", "core:move_speed_reduction"), Phase = Text(args, "phase", "start"),
                        Value = (float)Number(args, "value", .25d), ScaleWithStacks = Flag(args, "scaled", true)
                    }
                }
            });
            return new JObject { ["accepted"] = true };
        }
        catch (InvalidDataException exception)
        {
            return new JObject { ["accepted"] = false, ["reason"] = exception.Message };
        }
    }

    #endregion
}

/// <summary>感染测试只装配独立未注册宿主，用正式模块和攻击上下文采集结果。</summary>
internal sealed class GameplayTestInfectionFixture : IDisposable
{
    #region 独立宿主与正式消费端

    private readonly GameObject root;
    private readonly GameItem carrier;
    private readonly GameItem weaponItem;
    private readonly List<BuffInstance> ownedBuffs = new();
    public readonly GameItem Actor;
    public readonly Mod_Mover Mover;
    public readonly Mod_DamageReceiver Health;
    public readonly Mod_BuffManager Manager;
    public readonly Mod_Damage Weapon;
    private bool disposed;

    public GameplayTestInfectionFixture()
    {
        root = new GameObject("GameplayTest Independent Infection") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        Actor = CreateItem(root, "test:infection_actor");
        Mover = root.AddComponent<Mod_Mover>();
        Mover.item = Actor;
        Mover._Data = new Ex_ModData_MemoryPackable { ModuleId = ModText.Mod_Mover, StableName = "test:infection_mover" };
        Mover.Speed = new GameValue_float(8f) { MultiplicativeModifier = 1.5f };
        Actor.itemMods.AddMod(Mover);
        Health = root.AddComponent<Mod_DamageReceiver>();
        Health.item = Actor;
        Health._Data = new Ex_ModData { ModuleId = ModText.Hp, StableName = "test:infection_health" };
        Health.Data.Hp = Health.Data.MaxHp = 1000f;
        Health.Data.UseBodyPartHealth = false;
        Actor.itemMods.AddMod(Health);
        Manager = root.AddComponent<Mod_BuffManager>();
        Manager.item = Actor;
        Manager.ModData = new Ex_ModData_MemoryPackable { ModuleId = ModText.Mod_BuffManager, StableName = "test:infection_buffs" };
        Actor.itemMods.AddMod(Manager);

        var carrierRoot = new GameObject("Infection Weapon Owner");
        carrierRoot.transform.SetParent(root.transform, false);
        carrier = CreateItem(carrierRoot, "test:infection_carrier");
        carrier.Owner = Actor;
        var weaponRoot = new GameObject("Infection Actual Weapon");
        weaponRoot.transform.SetParent(root.transform, false);
        weaponItem = CreateItem(weaponRoot, "test:infection_weapon");
        weaponItem.Owner = carrier;
        Weapon = weaponRoot.AddComponent<Mod_Damage>();
        Weapon.item = weaponItem;
        Weapon.SetDamageValues(new CombatDamage(1f, 2f, 3f, 4f));
    }

    private static GameItem CreateItem(GameObject host, string id)
    {
        GameItem item = host.AddComponent<GameItem>();
        item.BindData(new Data_GeneralItem { IDName = id, Guid = host.GetInstanceID() });
        return item;
    }

    public BuffInstance CreateBuff(BuffDefinition definition, int stacks = 1)
    {
        var runtime = new BuffInstance();
        if (!runtime.Initialize(definition, Actor)) throw new InvalidOperationException("独立感染 Buff 初始化失败。");
        ownedBuffs.Add(runtime);
        runtime.SetStackCount(stacks);
        return runtime;
    }

    public JObject Snapshot(BuffInstance runtime = null)
    {
        CombatDamageContext context = GameplayCombatBridge.Context(Weapon, new CombatClock { Time = 1d });
        return new JObject
        {
            ["speed"] = Mover.Speed.Value, ["speedMultiplier"] = Mover.Speed.MultiplicativeModifier,
            ["attack"] = math.csum(context.Damage),
            ["damage"] = new JArray(context.Damage.x, context.Damage.y, context.Damage.z, context.Damage.w),
            ["baseAttack"] = Weapon.ResolveDamageValues().TotalCombatPower,
            ["ownerCredit"] = context.Credit.Equals(GameplayCombatBridge.Identity(Actor)),
            ["weaponSource"] = context.Attack.Source.Equals(GameplayCombatBridge.Identity(weaponItem)),
            ["hp"] = Health.Hp, ["stacks"] = runtime?.StackCount ?? 0,
            ["remaining"] = runtime?.RemainingDurationSeconds ?? 0f, ["phase"] = runtime?.TickElapsedSeconds ?? 0f
        };
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            foreach (BuffInstance runtime in ownedBuffs) runtime.Stop();
        }
        finally
        {
            try { Manager.ClearAllBuffs(); }
            finally
            {
                try { weaponItem.PrepareForDespawn(saveData: false); }
                finally
                {
                    try { carrier.PrepareForDespawn(saveData: false); }
                    finally
                    {
                        try { Actor.PrepareForDespawn(saveData: false); }
                        finally { UnityEngine.Object.DestroyImmediate(root); }
                    }
                }
            }
        }
    }

    #endregion
}
