using System;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 撕咬规则与来源租约采样
    private static void RegisterBiteOperations(Dictionary<string, Func<JObject, JObject>> result)
        => result.Add("combat/bite_attack", BiteAttackRules);

    private static JObject BiteAttackRules(JObject args)
    {
        AnimalSkillCatalog catalog = AnimalSkillCatalogLoader.LoadBuiltIn();
        AnimalSkillDefinition definition = catalog.Skills.Find(value => value.Id == "animal.bite_attack");
        var root = new GameObject("[LuaBiteRules]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        IDisposable slowA = null, slowB = null, blockA = null, blockB = null;
        try
        {
            GameItem actor = root.AddComponent<GameItem>();
            Data_GeneralItem body = EntityProbeData("probe:bite_body", 1f, 40f);
            actor.BindData(body); actor.itemMods.BindOwner(actor);
            Mod_Mover mover = root.AddComponent<Mod_Mover>();
            mover.item = actor; mover.Data.Speed = new GameValue_float(4f);
            slowA = ActorActionConstraints.ScaleSpeed(actor, .75f);
            float oneSlow = mover.Speed.Value;
            slowB = ActorActionConstraints.ScaleSpeed(actor, .75f);
            float twoSlows = mover.Speed.Value;
            slowA.Dispose(); slowA = null;
            float oneRemaining = mover.Speed.Value;
            slowB.Dispose(); slowB = null;
            float restored = mover.Speed.Value;
            blockA = ActorActionConstraints.BlockAttacks(actor);
            blockB = ActorActionConstraints.BlockAttacks(actor);
            var attack = new AI_AttackController { WindupDuration = .5f, DamageWindow = .2f };
            attack.Bind(actor); attack.StartWindow(null, "Attack", Vector2.right);
            bool blockedStart = !attack.IsAttackLocked;
            blockA.Dispose(); blockA = null;
            bool retainedBlock = ActorActionConstraints.IsAttackBlocked(actor);
            blockB.Dispose(); blockB = null;
            bool releasedBlock = !ActorActionConstraints.IsAttackBlocked(actor);
            float persistedCooldown = MemoryPackSerializer.Deserialize<BiteCooldownState>(
                MemoryPackSerializer.Serialize(new BiteCooldownState { CooldownRemaining = 123f })).CooldownRemaining;
            bool invalidTimingRejected = false, invalidSlowRejected = false;
            var invalid = JsonConvert.DeserializeObject<AnimalSkillCatalog>(JsonConvert.SerializeObject(catalog));
            invalid.Skills.Find(value => value.Id == definition.Id).TickIntervalSeconds = 0f;
            try { AnimalSkillCatalogLoader.Deserialize(JsonConvert.SerializeObject(invalid)); }
            catch (InvalidDataException) { invalidTimingRejected = true; }
            invalid.Skills.Find(value => value.Id == definition.Id).TickIntervalSeconds = 3f;
            invalid.Skills.Find(value => value.Id == definition.Id).TargetSlowFraction = 1f;
            try { AnimalSkillCatalogLoader.Deserialize(JsonConvert.SerializeObject(invalid)); }
            catch (InvalidDataException) { invalidSlowRejected = true; }
            return new JObject
            {
                ["equalHalfUsesA"] = !Mod_BiteAttack.ShouldCarry(40f, 20f),
                ["lighterUsesB"] = Mod_BiteAttack.ShouldCarry(40f, 19.999f),
                ["heavierUsesA"] = !Mod_BiteAttack.ShouldCarry(40f, 80f),
                ["bodyWeight"] = body.Stack.Weight,
                ["duration"] = definition.DurationSeconds, ["interval"] = definition.TickIntervalSeconds,
                ["cooldown"] = definition.CooldownSeconds, ["holdDamage"] = definition.HoldDamage,
                ["carryDamage"] = definition.CarryDamage, ["targetSlow"] = definition.TargetSlowFraction,
                ["casterSlow"] = definition.CasterSlowFraction, ["rangeUsesAttackRange"] = definition.TriggerDistance == 0f,
                ["oneSlow"] = oneSlow, ["twoSlows"] = twoSlows, ["oneRemaining"] = oneRemaining,
                ["restored"] = restored, ["blockedStart"] = blockedStart, ["retainedBlock"] = retainedBlock,
                ["releasedBlock"] = releasedBlock, ["persistedCooldown"] = persistedCooldown,
                ["invalidTimingRejected"] = invalidTimingRejected, ["invalidSlowRejected"] = invalidSlowRejected
            };
        }
        finally
        {
            slowA?.Dispose(); slowB?.Dispose(); blockA?.Dispose(); blockB?.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
    #endregion
}
