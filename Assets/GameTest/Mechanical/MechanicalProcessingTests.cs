using System.Collections.Generic;
using System.Reflection;
using MemoryPack;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.Mechanical
{
    /// <summary>以隔离资源目录验证真实 CraftingService 原子加工和建筑共享模块迁移，不启动资源加载或读写玩家存档。</summary>
    [Category("Mechanical.Core")]
    public sealed class MechanicalProcessingTests
    {
        #region 隔离资源
        private GameObject root;
        private GameRes previous;
        private static readonly FieldInfo InstanceField = typeof(SingletonAutoMono<GameRes>)
            .GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        [SetUp]
        public void SetUp()
        {
            previous = (GameRes)InstanceField.GetValue(null);
            root = new GameObject("MechanicalTestResources");
            root.SetActive(false); // 不触发 GameRes.Awake 的正式会话加载。
            var resources = root.AddComponent<GameRes>();
            InstanceField.SetValue(null, resources);
            foreach (string id in new[] { "Log", "Plank", "Stick_Wood", "StoneSlab", "Ore_Stone",
                         "DrilledLog", "DrilledPlank", "DrilledStick", "DrilledStoneSlab", "DrilledStone",
                         "RiceGrain", "Rice", "Ingot_RawIron", "Ingot_WroughtIron" })
                resources.RegisterItemDefinition(new RuntimeItemDefinition(id, "test_shell", root, Item(id, 1),
                    null, null, null, null, null, null, null, null, null));
        }
        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            InstanceField.SetValue(null, previous);
        }
        private static ItemData Item(string id, int amount) => new Data_GeneralItem
            { IDName = id, Stack = new ItemStack { Amount = amount, Volume = 1, Stackable = true } };
        #endregion

        #region 加工与状态守恒
        [TestCase("Log", "DrilledLog", 4)]
        [TestCase("Plank", "DrilledPlank", 4)]
        [TestCase("Stick_Wood", "DrilledStick", 4)]
        [TestCase("StoneSlab", "DrilledStoneSlab", 6)]
        [TestCase("Ore_Stone", "DrilledStone", 6)]
        public void Drilling_SaveHalfway_ThenProducesExactlyOne(string input, string output, int work)
        {
            var state = new MechanicalProcessingState();
            state.Input.GetItemSlot(0).itemData = Item(input, 2);
            using (var processor = new MechanicalProcessor("hand_drill", state))
            {
                Assert.That(processor.Preview().Success, Is.True);
                Assert.That(processor.Advance(work / 2f), Is.False);
                Assert.That(processor.Progress01, Is.EqualTo(.5f));
            }
            var copy = MemoryPackSerializer.Deserialize<MechanicalProcessingState>(MemoryPackSerializer.Serialize(state));
            using var resumed = new MechanicalProcessor("hand_drill", copy);
            Assert.That(resumed.Advance(work / 2f), Is.True);
            Assert.That(copy.Input.GetItemSlot(0).itemData.Stack.Amount, Is.EqualTo(1));
            Assert.That(copy.Output.GetItemSlot(0).itemData.IDName, Is.EqualTo(output));
            Assert.That(copy.Output.GetItemSlot(0).itemData.Stack.Amount, Is.EqualTo(1));
            Assert.That(copy.Progress, Is.Zero);
        }

        [Test]
        public void FullOutput_PreservesExistingProgressAndAllInput()
        {
            var state = new MechanicalProcessingState();
            state.Input.GetItemSlot(0).itemData = Item("Log", 2);
            using var processor = new MechanicalProcessor("hand_drill", state);
            processor.Advance(2);
            state.Output.GetItemSlot(0).itemData = Item("StoneSlab", 1);
            Assert.That(processor.Advance(100), Is.False);
            Assert.That(state.Progress, Is.EqualTo(2));
            Assert.That(state.Input.GetItemSlot(0).itemData.Stack.Amount, Is.EqualTo(2));
            state.Output.GetItemSlot(0).itemData = null;
            Assert.That(processor.Advance(2), Is.True);
            Assert.That(state.Output.GetItemSlot(0).itemData.Stack.Amount, Is.EqualTo(1));
        }

        [TestCase("millstone", "RiceGrain", "Rice", 3, 1)]
        [TestCase("sawmill", "Log", "Plank", 6, 4)]
        [TestCase("mechanical_hammer", "Ingot_RawIron", "Ingot_WroughtIron", 8, 1)]
        public void MachineProcess_ConsumesOneInputAndProducesConfiguredOutput(string station, string input, string output, int seconds, int amount)
        {
            var state = new MechanicalProcessingState();
            state.Input.GetItemSlot(0).itemData = Item(input, 1);
            using var processor = new MechanicalProcessor(station, state);
            Assert.That(processor.Advance(0), Is.False);
            Assert.That(state.Input.GetItemSlot(0).itemData.Stack.Amount, Is.EqualTo(1));
            Assert.That(processor.Advance(seconds), Is.True);
            Assert.That(state.Input.GetItemSlot(0).itemData, Is.Null);
            Assert.That(state.Output.GetItemSlot(0).itemData.IDName, Is.EqualTo(output));
            Assert.That(state.Output.GetItemSlot(0).itemData.Stack.Amount, Is.EqualTo(amount));
        }

        [Test]
        public void SharedDrillModule_PlaceAndRepackRetainStateWithoutAliasing()
        {
            var state = new MechanicalProcessingState { Progress = 2, RecipeId = "mechanical.hand_drill.Log" };
            state.Input.GetItemSlot(0).itemData = Item("Log", 3);
            state.Output.GetItemSlot(0).itemData = Item("DrilledLog", 2);
            ItemData carrier = WithModule("HandDrill_Summoner", Mod_HandDrill.ModuleId);
            ((Ex_ModData_MemoryPackable)carrier.ModuleDataDic["state"]).WriteData(state);
            ItemData body = WithModule("HandDrill", Mod_HandDrill.ModuleId);
            BuildingModuleStateTransfer.Copy(carrier, body, new[] { Mod_HandDrill.ModuleId });
            ItemData repacked = WithModule("HandDrill_Summoner", Mod_HandDrill.ModuleId);
            BuildingModuleStateTransfer.Copy(body, repacked, new[] { Mod_HandDrill.ModuleId });
            var result = ((Ex_ModData_MemoryPackable)repacked.ModuleDataDic["state"]).GetData<MechanicalProcessingState>();
            Assert.That(result.Progress, Is.EqualTo(2));
            Assert.That(result.Input.GetItemSlot(0).itemData.Stack.Amount, Is.EqualTo(3));
            Assert.That(result.Output.GetItemSlot(0).itemData.Stack.Amount, Is.EqualTo(2));
            Assert.That(repacked.ModuleDataDic["state"], Is.Not.SameAs(body.ModuleDataDic["state"]));
            Assert.That(Mod_HandDrill.ResolveCarrierDefinition(Item("HandDrill", 1)), Is.EqualTo("HandDrill_Summoner"));
        }

        [Test]
        public void RepackSnapshot_ResetsDirectionWithoutChangingWorldOrInventory()
        {
            var state = new MechanicalNodeState { Vertical = true };
            state.Processing.Input.GetItemSlot(0).itemData = Item("Log", 2);
            ItemData world = WithModule("CrossShaft", Mod_MechanicalNode.ModuleId);
            ((Ex_ModData_MemoryPackable)world.ModuleDataDic["state"]).WriteData(state);
            ItemData snapshot = MemoryPackSerializer.Deserialize<ItemData>(MemoryPackSerializer.Serialize(world));
            root.AddComponent<Mod_MechanicalNode>().PrepareRepackedSnapshot(snapshot);
            Assert.That(((Ex_ModData_MemoryPackable)snapshot.ModuleDataDic["state"]).GetData<MechanicalNodeState>().Vertical, Is.False);
            var original = ((Ex_ModData_MemoryPackable)world.ModuleDataDic["state"]).GetData<MechanicalNodeState>();
            Assert.That(original.Vertical, Is.True);
            Assert.That(original.Processing.Input.GetItemSlot(0).itemData.Stack.Amount, Is.EqualTo(2));
        }
        private static ItemData WithModule(string id, string moduleId)
        {
            var data = Item(id, 1);
            data.ModuleDataDic = new Dictionary<string, ModuleData>
                { ["state"] = new Ex_ModData_MemoryPackable { ID = moduleId, Name = "state" } };
            return data;
        }
        #endregion
    }
}
