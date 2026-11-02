using System;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
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
            var invalid = new JObject
            {
                ["schemaVersion"] = catalog.SchemaVersion,
                ["skills"] = new JArray(new JObject
                {
                    ["id"] = definition.Id, ["type"] = definition.Type, ["tickIntervalSeconds"] = 0f
                })
            };
            try { AnimalSkillCatalogLoader.Deserialize(invalid.ToString()); }
            catch (InvalidDataException) { invalidTimingRejected = true; }
            invalid["skills"][0]["tickIntervalSeconds"] = definition.TickIntervalSeconds;
            invalid["skills"][0]["targetSlowFraction"] = 1f;
            try { AnimalSkillCatalogLoader.Deserialize(invalid.ToString()); }
            catch (InvalidDataException) { invalidSlowRejected = true; }

            // 模拟狼从右侧转向左侧的被咬者，再转回右侧，验证身体、注视点和嘴前命中盒同步。
            Mod_TurnBack facing = root.AddComponent<Mod_TurnBack>();
            facing.item = actor;
            Mod_FocusPoint focus = root.AddComponent<Mod_FocusPoint>();
            facing.faceMouse = focus;
            Transform visual = new GameObject("[BiteFacingVisual]").transform;
            visual.SetParent(root.transform, false);
            facing.controlledTransforms_Direction.Add(visual);
            Transform mouth = new GameObject("[BiteMouthHitbox]").transform;
            mouth.SetParent(root.transform, false);
            mouth.localPosition = new Vector3(.5f, .29f, 0f);
            Mod_Damage_AI mouthDamage = mouth.gameObject.AddComponent<Mod_Damage_AI>();
            facing.OnTrun += mouthDamage.SnapToDirection;
            focus.Data.See_Point = Vector2.right * 6f;
            bool facedLeft = facing.FaceWorldPosition(Vector3.left * 2f, true);
            facing.ModUpdate(.016f);
            bool leftFacingCorrect = facedLeft && facing.currentDirection.x < 0f && mouth.localPosition.x < 0f &&
                Mathf.Abs(Mathf.DeltaAngle(visual.localEulerAngles.y, 180f)) < .5f && focus.Data.See_Point.x < 0f;
            bool facedRight = facing.FaceWorldPosition(Vector3.right * 2f, true);
            facing.ModUpdate(.016f);
            bool rightFacingCorrect = facedRight && facing.currentDirection.x > 0f && mouth.localPosition.x > 0f &&
                Mathf.Abs(Mathf.DeltaAngle(visual.localEulerAngles.y, 0f)) < .5f && focus.Data.See_Point.x > 0f;
            bool mouthHeightUnchanged = Mathf.Approximately(mouth.localPosition.y, .29f);
            bool followsAnchorAtEveryRate = true;
            foreach (int rate in new[] { 20, 30, 60, 120 })
            {
                float seconds = 1f / rate;
                Vector2 velocity = ActorActionConstraints.ResolveFollowWorldVelocity(
                    new Vector2(.2f, -.1f) * seconds, new Vector2(2f, 1f), seconds, 4f);
                followsAnchorAtEveryRate &= (velocity - new Vector2(2.2f, .9f)).sqrMagnitude < .000001f;
            }
            Vector2 fastAnchor = new(8f, 0f);
            Vector2 cappedFollow = ActorActionConstraints.ResolveFollowWorldVelocity(
                Vector2.right * 100f, fastAnchor, 1f / 30f, 4f);
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
                ["invalidTimingRejected"] = invalidTimingRejected, ["invalidSlowRejected"] = invalidSlowRejected,
                ["leftFacingCorrect"] = leftFacingCorrect, ["rightFacingCorrect"] = rightFacingCorrect,
                ["mouthHeightUnchanged"] = mouthHeightUnchanged,
                ["followsAnchorAtEveryRate"] = followsAnchorAtEveryRate,
                ["followCorrectionCapped"] = Mathf.Abs((cappedFollow - fastAnchor).magnitude - 4f) < .0001f,
                ["zeroErrorKeepsAnchorVelocity"] = ActorActionConstraints.ResolveFollowWorldVelocity(
                    Vector2.zero, fastAnchor, 1f / 30f, 4f) == fastAnchor,
                ["invalidFollowStepRejected"] = ActorActionConstraints.ResolveFollowWorldVelocity(
                    Vector2.one, fastAnchor, float.NaN, 4f) == Vector2.zero
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
