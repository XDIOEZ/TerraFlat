using System.Collections.Generic;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Combat
{
    /// <summary>战斗基础冒烟测试：保护伤害与技能系统入口。</summary>
    public sealed class CombatSmokeTests
    {
        [Test]
        [Category("Combat.Smoke")]
        [Category("Smoke")]
        public void TypedDamageUsesIndependentSubtractionAndZeroFloor()
        {
            var axe = new CombatDamage(1f, 0f, 5f, 3f);
            Assert.That(axe.TotalCombatPower, Is.EqualTo(9f));

            var knife = new CombatDamage(3f, 0f, 0f, 0f);
            var plate = new CombatDefense(10f, 0f, 0f, 0f);
            Assert.That(knife.CalculateAgainst(plate), Is.Zero);

            var machete = new CombatDamage(20f, 0f, 0f, 0f);
            Assert.That(machete.CalculateAgainst(plate), Is.EqualTo(10f));

            var hoe = new CombatDamage(0f, 5f, 6f, 0f);
            Assert.That(hoe.CalculateAgainst(new CombatDefense()), Is.EqualTo(11f));
        }

        [Test]
        [Category("Combat.Smoke")]
        [Category("Smoke")]
        public void BasicMeleePrefabsUseExplicitTypedDamageAndKeepHitEffects()
        {
            AssertBasicMeleePrefab("Assets/2_Prefabs/Item/Log.prefab", 5f);
            AssertBasicMeleePrefab("Assets/2_Prefabs/Item/Stick.prefab", 6f);
        }

        [Test]
        [Category("Combat.Smoke")]
        [Category("Smoke")]
        public void HitSlowdownReducesAndRestoresMoverSpeed()
        {
            GameObject root = new GameObject("HitSlowdown_Test");
            root.SetActive(false);

            try
            {
                GameItem item = root.AddComponent<GameItem>();
                item.BindData(new Data_GeneralItem());
                item.itemMods = new ItemMods(item);

                GameObject moverObject = new GameObject("Mover");
                moverObject.transform.SetParent(root.transform, false);
                Mover mover = moverObject.AddComponent<Mover>();
                mover.ModDataMemoryPack.ID = ModText.Mover;
                mover.ModDataMemoryPack.Name = "Mover_Test";
                mover.Speed = new GameValue_float(10f);
                mover.ModuleInit(item, mover.ModDataMemoryPack, item.Data);
                item.itemMods.AddMod(mover);

                GameObject receiverObject = new GameObject("DamageReceiver");
                receiverObject.transform.SetParent(root.transform, false);
                DamageReceiver receiver = receiverObject.AddComponent<DamageReceiver>();
                receiver.modData = new Ex_ModData
                {
                    ID = ModText.Hp,
                    Name = "DamageReceiver_Test"
                };
                receiver.ModuleInit(item, receiver.modData, item.Data);
                item.itemMods.AddMod(receiver);

                float baseSpeed = mover.Speed.Value;
                receiver.ApplyHitSlowdown();

                Assert.That(
                    mover.Speed.Value,
                    Is.EqualTo(baseSpeed * receiver.HitSlowMultiplier).Within(0.001f));

                receiver.ModUpdate(receiver.HitSlowDuration);
                Assert.That(mover.Speed.Value, Is.EqualTo(baseSpeed).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        [Category("Combat.Smoke")]
        [Category("Smoke")]
        public void DirectHitDrainsLastLivingBodyPartWhenRandomWeightsAreZero()
        {
            GameObject root = new GameObject("BodyPartFallback_Test");
            root.SetActive(false);

            try
            {
                GameItem item = root.AddComponent<GameItem>();
                item.BindData(new Data_GeneralItem());
                item.itemMods = new ItemMods(item);

                GameObject receiverObject = new GameObject("DamageReceiver");
                receiverObject.transform.SetParent(root.transform, false);
                DamageReceiver receiver = receiverObject.AddComponent<DamageReceiver>();
                receiver.modData = new Ex_ModData
                {
                    ID = ModText.Hp,
                    Name = "DamageReceiver_Test"
                };
                receiver.ModuleInit(item, receiver.modData, item.Data);
                item.itemMods.AddMod(receiver);
                receiver.Data = new DamageReceiver.DamageReceiver_SaveData
                {
                    Hp = 0.2f,
                    MaxHp = 1f,
                    UseBodyPartHealth = true,
                    BodyPartDataVersion = 1,
                    DestroyDelay = -1f,
                    BodyParts = new List<BodyPartHealth>
                    {
                        new BodyPartHealth
                        {
                            Part = BodyPartType.Head,
                            Hp = 0.2f,
                            MaxHp = 1f,
                            AreaRatio = 0f,
                            InjuryProbability = 0f
                        }
                    }
                };

                receiver.PanleInstance = new GameObject("HealthPanelPlaceholder");
                receiver.PanleInstance.transform.SetParent(root.transform, false);
                root.SetActive(true);

                receiver.Hurt(new TestDamageSender(1f));

                Assert.That(receiver.Hp, Is.Zero.Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>动物死亡必须按物种配置必掉骨头数量，且保留随机上下界。</summary>
        [Test]
        [Category("Combat.Smoke")]
        [Category("Smoke")]
        public void AnimalPrefabsDropConfiguredBoneAmounts()
        {
            AssertBoneLoot("Chicken", 1, 1);
            AssertBoneLoot("Wolf", 1, 3);
            AssertBoneLoot("WildBoar", 1, 5);
        }

        private static void AssertBoneLoot(string prefabName, int expectedMin, int expectedMax)
        {
            string path = $"Assets/2_Prefabs/Gameplay/AI/{prefabName}.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            DamageReceiver receiver = prefab != null
                ? prefab.GetComponentInChildren<DamageReceiver>(true)
                : null;

            Assert.That(prefab, Is.Not.Null, $"动物 Prefab 不存在：{path}");
            Assert.That(receiver, Is.Not.Null, $"动物缺少 DamageReceiver：{path}");

            LootEntry boneLoot = receiver.Data.LootTable.Find(entry => entry.LootPrefabName == "Bone");
            Assert.That(boneLoot, Is.Not.Null, $"动物死亡战利品缺少 Bone：{path}");
            Assert.That(boneLoot.DropChance, Is.EqualTo(1f), $"骨头必须为必掉：{path}");
            Assert.That(boneLoot.MinAmount, Is.EqualTo(expectedMin), $"骨头最小数量错误：{path}");
            Assert.That(boneLoot.MaxAmount, Is.EqualTo(expectedMax), $"骨头最大数量错误：{path}");
        }

        /// <summary>基础近战物品必须显式使用钝击伤害，并继承通用命中特效。</summary>
        private static void AssertBasicMeleePrefab(string path, float expectedBluntDamage)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Mod_Damage damage = prefab != null
                ? prefab.GetComponentInChildren<Mod_Damage>(true)
                : null;

            Assert.That(prefab, Is.Not.Null, $"基础近战 Prefab 不存在：{path}");
            Assert.That(damage, Is.Not.Null, $"基础近战物品缺少 Mod_Damage：{path}");

            CombatDamage values = damage.ResolveDamageValues();
            Assert.That(values.Blunt, Is.EqualTo(expectedBluntDamage), $"钝击伤害配置错误：{path}");
            Assert.That(values.TotalCombatPower, Is.EqualTo(expectedBluntDamage), $"伤害类型配置不纯：{path}");
            Assert.That(damage.AttackEffects, Is.Not.Null.And.Not.Empty, $"命中特效配置为空：{path}");
        }

        private sealed class TestDamageSender : IDamageSender
        {
            public TestDamageSender(float damage)
            {
                DamageValues = new CombatDamage(0f, 0f, 0f, damage);
            }

            public CombatDamage DamageValues { get; }
            public Item attacker { get; set; }
        }
    }
}
