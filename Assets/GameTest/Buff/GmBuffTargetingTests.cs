using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.Buff
{
    public sealed class GmBuffTargetingTestItem : Item
    {
        public Data_GeneralItem Data = new()
        {
            IDName = "GmBuffTargetingTestItem"
        };

        public override ItemData itemData => Data;

        protected override void SetItemData(ItemData value)
        {
            Data = RequireData<Data_GeneralItem>(value);
        }
    }

    /// <summary>覆盖 GM Buff 目标列表、施加与清除所依赖的运行时入口。</summary>
    public sealed class GmBuffTargetingTests
    {
        [Test]
        [Category("Buff.GM")]
        public void GmBuffTargetListFindsActiveBuffReceiverAndCanOverrideFiniteDuration()
        {
            using BuffTargetingFixture fixture = new();

            MethodInfo resolver = typeof(GMReflectionConsole).GetMethod(
                "FindBuffManagersInLoadedScenes",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(resolver, Is.Not.Null, "GM Buff 目标列表必须提供运行对象扫描入口。");

            var resolved = resolver.Invoke(null, null) as System.Collections.Generic.List<BuffManager>;
            Assert.That(resolved, Is.Not.Null);
            CollectionAssert.Contains(resolved, fixture.Manager);

            Assert.That(fixture.Manager.AddBuff(fixture.FiniteBuff.Id), Is.True);
            Assert.That(fixture.Manager.TrySetBuffDuration(fixture.FiniteBuff.Id, 12.5f), Is.True);
            Assert.That(fixture.Manager.TryGetBuff(fixture.FiniteBuff.Id, out BuffInstance runtime), Is.True);
            Assert.That(runtime.RemainingDurationSeconds, Is.EqualTo(12.5f).Within(0.001f));

            fixture.Manager.ClearAllBuffs();
            Assert.That(fixture.Manager.ActiveBuffs, Is.Empty);
        }

        [Test]
        [Category("Buff.GM")]
        public void GmConsoleRegistersDedicatedBuffPage()
        {
            Type pageId = typeof(GMReflectionConsole).GetNestedType(
                "GmPageId",
                BindingFlags.NonPublic);

            Assert.That(pageId, Is.Not.Null);
            CollectionAssert.Contains(Enum.GetNames(pageId), "Buff");
        }

        private sealed class BuffTargetingFixture : IDisposable
        {
            private readonly GameObject targetObject;
            private readonly GameRes gameRes;
            private readonly BuffDefinition previousDefinition;
            private readonly bool hadPreviousDefinition;

            public BuffManager Manager { get; }
            public BuffDefinition FiniteBuff { get; }

            public BuffTargetingFixture()
            {
                FiniteBuff = BuffCatalogLoader.LoadBuiltInDefinitions()
                    .Single(definition => definition.Id == "出血");

                gameRes = GameRes.Instance;
                Assert.That(gameRes, Is.Not.Null, "测试环境需要可用的 GameRes。");
                hadPreviousDefinition = gameRes.BuffDefinitions.TryGetValue(
                    FiniteBuff.Id,
                    out previousDefinition);
                gameRes.BuffDefinitions[FiniteBuff.Id] = FiniteBuff;

                targetObject = new GameObject("GmBuffTargetingFixture");
                targetObject.SetActive(false);

                GmBuffTargetingTestItem item = targetObject.AddComponent<GmBuffTargetingTestItem>();
                item.itemMods = new ItemMods(item);

                Manager = targetObject.AddComponent<BuffManager>();
                Manager.ModData = new Ex_ModData_MemoryPackable
                {
                    ID = ModText.BuffManager,
                    Name = "GmBuffTargetingManager"
                };
                item.itemMods.AddMod(Manager);
                Manager.ModuleInit(item, Manager.ModData);
                targetObject.SetActive(true);
            }

            public void Dispose()
            {
                if (gameRes != null)
                {
                    if (hadPreviousDefinition)
                        gameRes.BuffDefinitions[FiniteBuff.Id] = previousDefinition;
                    else
                        gameRes.BuffDefinitions.Remove(FiniteBuff.Id);
                }

                if (targetObject != null)
                    UnityEngine.Object.DestroyImmediate(targetObject);
            }
        }
    }
}
