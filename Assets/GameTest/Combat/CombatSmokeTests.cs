using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.Combat
{
    /// <summary>战斗基础冒烟测试：保护伤害与技能系统入口。</summary>
    public sealed class CombatSmokeTests
    {

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
    }
}
