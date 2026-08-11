using FlatWorld.GameTest.Shared;
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.AI
{
    /// <summary>AI 基础冒烟测试：保护状态机、感知和生物资源入口。</summary>
    public sealed class AISmokeTests
    {
        private enum AdvanceTestState
        {
            Advance
        }







        [Test]
        [Category("AI.StateMachine")]
        public void AdvanceNodeMovesUntilArrivalAndNotifiesOnce()
        {
            Vector3 actorPosition = Vector3.zero;
            Vector3 targetPosition = new(3f, 0f, 0f);
            int moveCount = 0;
            int stopCount = 0;
            int arrivalCount = 0;
            AIAdvanceStateNode<AdvanceTestState> node = new(
                AdvanceTestState.Advance,
                () => new AIAdvanceTarget(true, targetPosition),
                () => actorPosition,
                () => 0.1f,
                target =>
                {
                    moveCount++;
                    actorPosition = Vector3.MoveTowards(actorPosition, target, 1f);
                },
                () => stopCount++,
                () => arrivalCount++);

            node.Enter();
            for (int i = 0; i < 5; i++)
                node.Tick(1f);

            Assert.That(node.AnimationRole, Is.EqualTo(AIStateAnimationRole.Moving));
            Assert.That(actorPosition, Is.EqualTo(targetPosition));
            Assert.That(moveCount, Is.EqualTo(3));
            Assert.That(stopCount, Is.EqualTo(2));
            Assert.That(arrivalCount, Is.EqualTo(1));
        }

        [Test]
        [Category("AI.Smoke")]
        [Category("Smoke")]
        public void WolfPrefabAcceptsReusableAdvanceCommands()
        {
            GameObject wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/Wolf.prefab");
            AI_Wolf wolf = wolfPrefab != null
                ? wolfPrefab.GetComponentInChildren<AI_Wolf>(true)
                : null;

            Assert.That(wolfPrefab, Is.Not.Null);
            Assert.That(wolf, Is.Not.Null);
            Assert.That(wolf, Is.InstanceOf<IAIAdvanceCommandReceiver>());
            Assert.That((int)WolfState.Advance, Is.EqualTo(7), "推进状态必须追加在末尾，避免破坏旧存档枚举值。");
        }

        /// <summary>狼群追击槽位必须左右分线、彼此分离，并始终留在可进入攻击的安全半径内。</summary>
        [Test]
        [Category("AI.Smoke")]
        [Category("Smoke")]
        public void WolfChaseFormationSlotsStaySeparatedInsideAttackRange()
        {
            GameObject wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/Wolf.prefab");
            AI_Wolf wolf = wolfPrefab != null
                ? wolfPrefab.GetComponentInChildren<AI_Wolf>(true)
                : null;

            Assert.That(wolf, Is.Not.Null);
            Assert.That(wolf.enableChaseFormation, Is.True);
            Assert.That(
                wolf.chaseFormationAttackSlotTolerance,
                Is.LessThanOrEqualTo(wolf.chaseFormationAttackMargin),
                "攻击槽容差必须小于攻击安全余量，避免尚未到位时提前进入攻击状态。");

            float maximumRadius = Mathf.Max(
                0.05f,
                wolf.attackTriggerDistance - Mathf.Max(0.05f, wolf.chaseFormationAttackMargin));
            float formationRadius = Mathf.Clamp(wolf.chaseFormationRadius, 0.05f, maximumRadius);
            var slots = new List<Vector2>();
            for (int slotIndex = 0; slotIndex < 6; slotIndex++)
            {
                Vector2 slot = AI_Wolf.CalculateChaseFormationSlotOffset(
                    slotIndex,
                    formationRadius,
                    wolf.chaseFormationVerticalSpacing,
                    wolf.chaseFormationMaxVerticalRatio);
                slots.Add(slot);

                Assert.That(
                    slot.magnitude,
                    Is.LessThanOrEqualTo(maximumRadius + 0.0001f),
                    "追击站位不能落在攻击范围外。");
                Assert.That(
                    Mathf.Abs(slot.x),
                    Is.GreaterThan(Mathf.Abs(slot.y)),
                    "当前攻击只支持左右方向，站位必须保持左右主导。");
            }

            Assert.That(slots[0].x, Is.LessThan(0f));
            Assert.That(slots[1].x, Is.GreaterThan(0f));
            for (int left = 0; left < slots.Count; left++)
            {
                for (int right = left + 1; right < slots.Count; right++)
                {
                    Assert.That(
                        (slots[left] - slots[right]).sqrMagnitude,
                        Is.GreaterThan(0.01f),
                        $"追击槽 {left} 与 {right} 不应重叠。");
                }
            }
        }

        /// <summary>感知半径必须覆盖追击触发距离，避免状态机进入追击前就丢失目标。</summary>
        [Test]
        [Category("AI.Smoke")]
        [Category("Smoke")]
        public void CombatAnimalPrefabsCoverChaseTriggerDistance()
        {
            GameObject wildBoarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/WildBoar.prefab");
            GameObject wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/Wolf.prefab");

            AI_WildBoar wildBoar = wildBoarPrefab != null
                ? wildBoarPrefab.GetComponentInChildren<AI_WildBoar>(true)
                : null;
            AI_Wolf wolf = wolfPrefab != null
                ? wolfPrefab.GetComponentInChildren<AI_Wolf>(true)
                : null;

            Assert.That(wildBoar, Is.Not.Null);
            Assert.That(wolf, Is.Not.Null);
            AssertDetectorCoversTriggerDistance(wildBoar, wildBoarPrefab, wildBoar.chaseTriggerDistance);
            AssertDetectorCoversTriggerDistance(wolf, wolfPrefab, wolf.chaseTriggerDistance);
        }

        /// <summary>狼的玩家/同伴感知范围覆盖最大玩家视野，并明显大于野猪。</summary>
        [Test]
        [Category("AI.Smoke")]
        [Category("Smoke")]
        public void WolfPerceptionAndPackCallRangesCoverPlayerView()
        {
            const float expectedWolfPerceptionRadius = 120f;
            GameObject wildBoarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/WildBoar.prefab");
            GameObject wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/Wolf.prefab");
            Mod_ItemDetector wildBoarDetector = wildBoarPrefab != null
                ? wildBoarPrefab.GetComponentInChildren<Mod_ItemDetector>(true)
                : null;
            Mod_ItemDetector wolfDetector = wolfPrefab != null
                ? wolfPrefab.GetComponentInChildren<Mod_ItemDetector>(true)
                : null;
            AI_Wolf wolf = wolfPrefab != null
                ? wolfPrefab.GetComponentInChildren<AI_Wolf>(true)
                : null;

            Assert.That(wildBoarDetector, Is.Not.Null);
            Assert.That(wolfDetector, Is.Not.Null);
            Assert.That(wolf, Is.Not.Null);
            Assert.That(wolfDetector.DetectionRadius, Is.GreaterThan(wildBoarDetector.DetectionRadius));
            Assert.That(wolfDetector.DetectionRadius, Is.GreaterThanOrEqualTo(expectedWolfPerceptionRadius));
            Assert.That(wolf.alertDetectDistance, Is.GreaterThanOrEqualTo(20f));
            Assert.That(wolf.chaseTriggerDistance, Is.GreaterThanOrEqualTo(28f));
            Assert.That(wolf.chaseLossDistance, Is.GreaterThanOrEqualTo(44f));
            Assert.That(wolf.allyCallDistance, Is.GreaterThanOrEqualTo(expectedWolfPerceptionRadius));
            Assert.That(
                wolf.allyCallDistance,
                Is.LessThanOrEqualTo(wolfDetector.DetectionRadius + 0.0001f),
                "同伴呼叫范围不能超过实际感知范围。");
        }

        /// <summary>狼的检测遮罩必须包含玩家层和自身层，否则既无法锁定玩家也无法组成狼群。</summary>
        [Test]
        [Category("AI.Smoke")]
        [Category("Smoke")]
        public void WolfDetectorMasksIncludePlayerAndActorLayers()
        {
            AssertWolfDetectorMask("Assets/2_Prefabs/Entity_AI/Wolf.prefab");
        }

        /// <summary>幽灵亮度伤害必须在严格超过一半亮度后才开启。</summary>
        [Test]
        [Category("AI.Smoke")]
        [Category("Smoke")]
        public void GhostLightDamageStartsOnlyAboveHalfBrightness()
        {
            Assert.That(AI_Ghost.LightDamageThreshold, Is.EqualTo(0.5f));
            Assert.That(AI_Ghost.ShouldTakeLightDamage(0.5f), Is.False);
            Assert.That(AI_Ghost.ShouldTakeLightDamage(0.5001f), Is.True);
        }

        /// <summary>幽灵接触伤害复用通用伤害模块，并使用贴近身体轮廓的触发盒。</summary>
        [Test]
        [Category("AI.Smoke")]
        [Category("Smoke")]
        public void GhostUsesSharedDamageSenderOnBodyCollider()
        {
            GameObject ghostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/Ghost.prefab");
            BoxCollider2D bodyCollider = ghostPrefab != null
                ? ghostPrefab.GetComponent<BoxCollider2D>()
                : null;
            Mod_Damage damageSender = ghostPrefab != null
                ? ghostPrefab.GetComponent<Mod_Damage>()
                : null;

            Assert.That(bodyCollider, Is.Not.Null);
            Assert.That(damageSender, Is.Not.Null);
            Assert.That(bodyCollider.isTrigger, Is.True);
            Assert.That(bodyCollider.size.x, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(bodyCollider.size.y, Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(bodyCollider.offset.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(bodyCollider.offset.y, Is.EqualTo(0.15f).Within(0.0001f));
            Assert.That(damageSender.Damage.BaseValue, Is.EqualTo(20f).Within(0.0001f));
            Assert.That(damageSender.DamageInterval, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(damageSender.EnableOnTriggerEnterDamage, Is.True);
            Assert.That(damageSender.OnlyDealDamageWhenInHand, Is.False);
        }

        /// <summary>首次攻击必须先经历起手延迟，再开启唯一的伤害窗口。</summary>
        [Test]
        [Category("AI.Smoke")]
        [Category("Smoke")]
        public void AttackControllerFirstWindowStartsAtConfiguredDelay()
        {
            var attackController = new AI_AttackController
            {
                DamageWindowStartDelay = 0.06f,
                DamageWindow = 0.12f,
                Cooldown = 2f
            };

            attackController.StartWindow(null, string.Empty, Vector2.right);
            Assert.That(attackController.IsDamageWindowActive, Is.False);

            attackController.Update(0.059f);
            Assert.That(attackController.IsDamageWindowActive, Is.False);

            attackController.Update(0.002f);
            Assert.That(attackController.IsDamageWindowActive, Is.True);

            attackController.Update(0.12f);
            Assert.That(attackController.IsDamageWindowActive, Is.False);
        }

        /// <summary>首击窗口必须补查 AI 触发器内已有目标，且不能把扫描扩散到武器模块。</summary>
        [Test]
        [Category("AI.Smoke")]
        [Category("Smoke")]
        public void AttackControllerFirstWindowScansOnlyAIDamageModules()
        {
            GameObject wildBoarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/WildBoar.prefab");
            Mod_Damage_AI aiDamage = wildBoarPrefab != null
                ? wildBoarPrefab.GetComponentInChildren<Mod_Damage_AI>(true)
                : null;
            GameObject weaponObject = new GameObject("Generic Damage Test");

            try
            {
                Mod_Damage genericDamage = weaponObject.AddComponent<Mod_Damage>();

                Assert.That(aiDamage, Is.Not.Null);
                Assert.That(aiDamage.EnableOnTriggerEnterDamage, Is.True);
                Assert.That(aiDamage.DamageInterval, Is.EqualTo(-1f));
                Assert.That(
                    AI_AttackController.ShouldScanCurrentOverlapsOnWindowStart(aiDamage),
                    Is.True);
                Assert.That(
                    AI_AttackController.ShouldScanCurrentOverlapsOnWindowStart(genericDamage),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(weaponObject);
            }
        }

        /// <summary>野猪攻击触发器与 AI 判定必须共用横宽竖窄的椭圆范围。</summary>
        [Test]
        [Category("AI.Smoke")]
        [Category("Smoke")]
        public void WildBoarAttackRangeUsesHorizontalEllipse()
        {
            GameObject wildBoarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/WildBoar.prefab");
            AI_WildBoar wildBoar = wildBoarPrefab != null
                ? wildBoarPrefab.GetComponentInChildren<AI_WildBoar>(true)
                : null;
            Transform triggerTransform = wildBoarPrefab != null
                ? wildBoarPrefab.transform.Find("AttackTrigger_AI")
                : null;
            BoxCollider2D triggerCollider = triggerTransform != null
                ? triggerTransform.GetComponent<BoxCollider2D>()
                : null;

            Assert.That(wildBoar, Is.Not.Null);
            Assert.That(triggerCollider, Is.Not.Null);
            Assert.That(wildBoar.attackTriggerDistance, Is.EqualTo(1.6f).Within(0.0001f));
            Assert.That(wildBoar.attackVerticalTriggerDistance, Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(triggerCollider.size.x, Is.EqualTo(2.2f).Within(0.0001f));
            Assert.That(triggerCollider.size.y, Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(triggerCollider.edgeRadius, Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(
                AI_WildBoar.IsInsideEllipticalAttackRange(
                    new Vector2(1.6f, 0f),
                    wildBoar.attackTriggerDistance,
                    wildBoar.attackVerticalTriggerDistance),
                Is.True);
            Assert.That(
                AI_WildBoar.IsInsideEllipticalAttackRange(
                    new Vector2(0f, 0.46f),
                    wildBoar.attackTriggerDistance,
                    wildBoar.attackVerticalTriggerDistance),
                Is.False);
            Assert.That(
                AI_WildBoar.IsInsideEllipticalAttackRange(
                    new Vector2(1.5f, 0.2f),
                    wildBoar.attackTriggerDistance,
                    wildBoar.attackVerticalTriggerDistance),
                Is.False);
        }

        /// <summary>野猪动画有效帧、伤害窗口延迟与持续时间必须保持同一时间段。</summary>
        [Test]
        [Category("AI.Smoke")]
        [Category("Smoke")]
        public void WildBoarAttackWindowMatchesAttackAnimation()
        {
            GameObject wildBoarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Entity_AI/WildBoar.prefab");
            AI_WildBoar wildBoar = wildBoarPrefab != null
                ? wildBoarPrefab.GetComponentInChildren<AI_WildBoar>(true)
                : null;
            AnimationClip attackClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/8_Animations/Character/WildBoar/Attack.anim");

            Assert.That(wildBoar, Is.Not.Null);
            Assert.That(attackClip, Is.Not.Null);

            EditorCurveBinding attackingBinding = default;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(attackClip))
            {
                if (binding.propertyName == "IsAttacking")
                {
                    attackingBinding = binding;
                    break;
                }
            }

            AnimationCurve attackingCurve = AnimationUtility.GetEditorCurve(attackClip, attackingBinding);
            Assert.That(attackingCurve, Is.Not.Null);
            Assert.That(attackingCurve.keys.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(attackingCurve.keys[0].time, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(attackingCurve.keys[0].value, Is.EqualTo(0f));
            Assert.That(attackingCurve.keys[1].time, Is.EqualTo(wildBoar.attackDamageStartDelay).Within(0.0001f));
            Assert.That(
                attackingCurve.keys[2].time,
                Is.EqualTo(wildBoar.attackDamageStartDelay + wildBoar.attackDamageWindow).Within(0.0001f));

            // 攻击片段必须覆盖从本次起手到下一次可造成伤害的完整周期，且不能在冷却内循环播放。
            float expectedCycleDuration = wildBoar.attackDamageStartDelay
                + wildBoar.attackDamageWindow
                + wildBoar.attackCooldown;
            Assert.That(attackClip.length, Is.EqualTo(expectedCycleDuration).Within(0.0001f));

            SerializedObject serializedClip = new SerializedObject(attackClip);
            SerializedProperty loopTime = serializedClip.FindProperty("m_AnimationClipSettings.m_LoopTime");
            Assert.That(loopTime, Is.Not.Null);
            Assert.That(loopTime.boolValue, Is.False);
        }

        #region Helpers
        /// <summary>断言生物感知模块的半径覆盖指定追击触发距离。</summary>
        private static void AssertDetectorCoversTriggerDistance(
            Component actor,
            GameObject prefab,
            float triggerDistance)
        {
            Mod_ItemDetector detector = prefab.GetComponentInChildren<Mod_ItemDetector>(true);
            Assert.That(detector, Is.Not.Null, $"{actor.GetType().Name} 缺少 Mod_ItemDetector。");
            Assert.That(
                detector.DetectionRadius,
                Is.GreaterThanOrEqualTo(triggerDistance),
                $"{actor.GetType().Name} 的感知半径必须覆盖追击触发距离。");
        }

        /// <summary>校验现代与旧行为树狼 Prefab 的检测层配置。</summary>
        private static void AssertWolfDetectorMask(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Mod_ItemDetector detector = prefab != null
                ? prefab.GetComponentInChildren<Mod_ItemDetector>(true)
                : null;
            int playerLayer = LayerMask.NameToLayer("Player");

            Assert.That(prefab, Is.Not.Null, $"狼 Prefab 不存在：{prefabPath}");
            Assert.That(detector, Is.Not.Null, $"狼 Prefab 缺少 Mod_ItemDetector：{prefabPath}");
            Assert.That(playerLayer, Is.GreaterThanOrEqualTo(0), "项目必须注册 Player 层。");
            Assert.That(
                detector.itemLayer.value & (1 << playerLayer),
                Is.Not.EqualTo(0),
                $"狼检测遮罩必须包含 Player 层：{prefabPath}");
            Assert.That(
                detector.itemLayer.value & (1 << prefab.layer),
                Is.Not.EqualTo(0),
                $"狼检测遮罩必须包含自身根物体层：{prefabPath}");
        }
        #endregion

    }
}
