using System;
using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using NUnit.Framework;
using UnityEngine;

namespace FlatWorld.GameTest.Mechanical
{
    /// <summary>分层连通性、整网加载、速比、加工守恒与存档往返的隔离测试；不打开或写入玩家存档。</summary>
    [Category("Mechanical.Core")]
    public sealed class MechanicalSystemTests
    {
        #region 网络测试
        private static MechanicalNode Node(int id, int x, int y, string kind = "shaft", bool vertical = false)
            => new() { Id = id, Cell = new Vector2Int(x, y), Definition = new MechanicalDefinition
                { Id = "test_" + id, Kind = kind, Ports = kind == "gear" ? "all" : "axis" },
                State = new MechanicalNodeState(), Vertical = vertical };
        private static MechanicalNetworkGraph Graph() => new(new Vector2Int(16, 16), Vector2Int.zero, null);

        [Test]
        public void Bridge_ConnectsOnlyEndpoints_LeavesLowerCrossingIndependent()
        {
            var left = Node(1, -1, 0); var right = Node(2, 1, 0); var bridge = Node(3, 0, 0, "bridge");
            var lower = Node(4, 0, 0, "shaft", true); var up = Node(5, 0, 1, "shaft", true);
            var down = Node(6, 0, -1, "shaft", true);
            var graph = Graph(); graph.Rebuild(new[] { left, right, bridge, lower, up, down });
            Assert.That(graph.Networks.Count, Is.EqualTo(2));
            Assert.That(left.Network, Is.SameAs(right.Network));
            Assert.That(lower.Network, Is.SameAs(up.Network).And.SameAs(down.Network));
            Assert.That(bridge.Network, Is.Not.SameAs(lower.Network));
            graph.Rebuild(new[] { left, right, lower, up, down });
            Assert.That(lower.Network.Nodes.Count, Is.EqualTo(3));
            Assert.That(left.Network, Is.Not.SameAs(right.Network));
        }

        [Test]
        public void Bridge_RequiresMatchingPortDirection()
        {
            var left = Node(1, -1, 0, "shaft", true); var right = Node(2, 1, 0); var bridge = Node(3, 0, 0, "bridge");
            var graph = Graph(); graph.Rebuild(new[] { left, right, bridge });
            Assert.That(left.Network, Is.Not.SameAs(bridge.Network));
            Assert.That(right.Network, Is.SameAs(bridge.Network));
        }

        [Test]
        public void Clutch_SplitsNetwork_AndSubnetworkReturnsToSingleChunk()
        {
            var a = Node(1, 15, 0); var clutch = Node(2, 16, 0, "clutch"); var b = Node(3, 17, 0);
            var graph = Graph(); graph.Rebuild(new[] { a, clutch, b });
            Assert.That(graph.Networks.Count, Is.EqualTo(1));
            Assert.That(a.Network.SingleChunk, Is.False);
            clutch.Engaged = false; graph.Rebuild(new[] { a, clutch, b });
            Assert.That(graph.Networks.Count, Is.EqualTo(3));
            Assert.That(graph.Networks.All(network => network.SingleChunk), Is.True);
        }

        [Test]
        public void Bounds_NegativeCellsUseFloorDivision()
        {
            var graph = Graph(); var a = Node(1, -1, 0); var b = Node(2, 0, 0);
            graph.Rebuild(new[] { a, b });
            Assert.That(a.Network.Bounds.xMin, Is.EqualTo(-1));
            Assert.That(a.Network.Bounds.size.x, Is.EqualTo(2));
        }

        [Test]
        public void WrappedSeam_HasSmallBounds_AndDetectsEitherSide()
        {
            Vector2Int Normalize(Vector2Int cell) => new(cell.x < -32 ? cell.x + 64 : cell.x >= 32 ? cell.x - 64 : cell.x, cell.y);
            var graph = new MechanicalNetworkGraph(new Vector2Int(16, 16), new Vector2Int(4, 0), Normalize);
            var a = Node(1, 31, 0); var b = Node(2, -32, 0); graph.Rebuild(new[] { a, b });
            Assert.That(graph.Networks.Count, Is.EqualTo(1));
            Assert.That(a.Network.Bounds.size.x, Is.EqualTo(2));
            Assert.That(graph.IsNear(a.Network, new Vector2Int(-2, 0), 0), Is.True);
            Assert.That(graph.IsNear(a.Network, new Vector2Int(1, 0), 0), Is.True);
            Assert.That(graph.IsNear(a.Network, Vector2Int.zero, 0), Is.False);
        }

        [Test]
        public void Loading_AnyPlayerKeepsWholeNetworkAlive_ThenCooldownSleeps()
        {
            var graph = Graph(); var a = Node(1, 0, 0); graph.Rebuild(new[] { a });
            var settings = new MechanicalSettings(); var network = a.Network;
            network.Active = false;
            Assert.That(graph.ShouldBeActive(network, new[] { new Vector2Int(1, 0) }, .1f, settings), Is.True);
            network.Active = true;
            Assert.That(graph.ShouldBeActive(network, new[] { new Vector2Int(50, 0), new Vector2Int(2, 0) }, 10, settings), Is.True);
            Assert.That(graph.ShouldBeActive(network, new[] { new Vector2Int(3, 0) }, 4, settings), Is.True);
            Assert.That(graph.ShouldBeActive(network, new[] { new Vector2Int(3, 0) }, 1, settings), Is.False);
        }

        [Test]
        public void Gearbox_ChangesConsumerRpm_AndLoadScalesWithSpeed()
        {
            var source = Node(1, 0, 0); source.Definition.Power = 40;
            var gearbox = Node(2, 1, 0, "gearbox"); gearbox.RatioIndex = 2;
            var consumer = Node(3, 2, 0); consumer.Definition.Load = 8;
            var graph = Graph(); graph.Rebuild(new[] { source, gearbox, consumer });
            MechanicalNetworkGraph.Solve(source.Network, _ => 1, 60);
            Assert.That(source.Rpm, Is.EqualTo(60)); Assert.That(consumer.Rpm, Is.EqualTo(120));
            Assert.That(source.Network.Demand, Is.EqualTo(16));
        }

        [Test]
        public void PowerLossOrOverload_StopsEveryConsumerImmediately()
        {
            var source = Node(1, 0, 0); source.Definition.Power = 10;
            var consumer = Node(2, 1, 0); consumer.Definition.Load = 8;
            var graph = Graph(); graph.Rebuild(new[] { source, consumer });
            MechanicalNetworkGraph.Solve(source.Network, _ => 1, 60); Assert.That(consumer.Rpm, Is.GreaterThan(0));
            MechanicalNetworkGraph.Solve(source.Network, _ => 0, 60); Assert.That(consumer.Rpm, Is.Zero);
            consumer.Definition.Load = 11;
            MechanicalNetworkGraph.Solve(source.Network, _ => 1, 60); Assert.That(source.Rpm, Is.Zero);
            Assert.That(source.Network.Status, Is.EqualTo("过载"));
        }

        [Test]
        public void ConflictingGearLoop_StallsRatherThanCreatingEnergy()
        {
            var a = Node(1, 0, 0, "gear"); a.Definition.Power = 100;
            var b = Node(2, 1, 0, "gearbox"); b.Definition.Ports = "all"; b.RatioIndex = 2;
            var c = Node(3, 1, 1, "gear"); var d = Node(4, 0, 1, "gear");
            var graph = Graph(); graph.Rebuild(new[] { a, b, c, d });
            MechanicalNetworkGraph.Solve(a.Network, _ => 1, 60);
            Assert.That(a.Network.RatioConflict, Is.True); Assert.That(c.Rpm, Is.Zero);
        }
        #endregion

        #region 状态与事务测试
        [Test]
        public void StateRoundTrip_PreservesOrientationInventoryAndProgress()
        {
            var state = new MechanicalNodeState { Vertical = true, Engaged = false, RatioIndex = 2, ManualSeconds = 2.5f };
            state.Processing.Input.itemSlots[0].itemData = Item("Log", 3);
            state.Processing.Output.itemSlots[0].itemData = Item("DrilledLog", 2);
            state.Processing.Progress = 2; state.Processing.RecipeId = "drill";
            var copy = MemoryPackSerializer.Deserialize<MechanicalNodeState>(MemoryPackSerializer.Serialize(state));
            Assert.That(copy.Vertical, Is.True); Assert.That(copy.Engaged, Is.False);
            Assert.That(copy.Processing.Input.itemSlots[0].itemData.Stack.Amount, Is.EqualTo(3));
            Assert.That(copy.Processing.Output.itemSlots[0].itemData.IDName, Is.EqualTo("DrilledLog"));
            Assert.That(copy.Processing.Progress, Is.EqualTo(2));
            Assert.That(copy.ManualSeconds, Is.EqualTo(2.5f));
        }

        [Test]
        public void ProcessingInventory_RejectsUnknownMaterials_AndAcceptsRegisteredInputs()
        {
            using var processor = new MechanicalProcessor("hand_drill", new MechanicalProcessingState());
            Assert.That(processor.Input.CanAcceptQuickTransfer(new ItemSlot(0) { itemData = Item("Log", 1) }, processor.Input.Data.GetItemSlot(0)), Is.True);
            Assert.That(processor.Input.CanAcceptQuickTransfer(new ItemSlot(0) { itemData = Item("invalid_material", 1) }, processor.Input.Data.GetItemSlot(0)), Is.False);
        }

        [Test]
        public void FullOutputTransaction_DoesNotConsumeDrillingInput()
        {
            var input = new Inventory { Data = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = Item("Log", 2) } }, "输入") };
            var output = new Inventory { Data = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = Item("blocked", 100) } }, "输出") };
            var match = new CraftingRecipeMatch(new RuntimeRecipe { Id = "drill" }, false, new[] { new CraftingConsumption(0, 1) });
            Assert.That(CraftingTransaction.TryCreate(input, output, match, new[] { Item("DrilledLog", 1) }, false, out _, out _), Is.False);
            Assert.That(input.Data.GetItemSlot(0).itemData.Stack.Amount, Is.EqualTo(2));
            Assert.That(output.Data.GetItemSlot(0).itemData.IDName, Is.EqualTo("blocked"));
        }

        [Test]
        public void ArchiveRoundTrip_KeepsWorldsSeparate_AndMissingPayloadStartsEmpty()
        {
            var save = new GameSaveData();
            save.Mechanical.Worlds["world_A"] = new List<ItemData> { Item("Shaft_Wood", 1) };
            save.Mechanical.Worlds["world_B"] = new List<ItemData> { Item("Gear_Wood", 1) };
            var restored = new GameSaveData(); MechanicalWorld.RestoreArchive(restored, MechanicalWorld.CaptureArchive(save));
            Assert.That(restored.Mechanical.Worlds.Count, Is.EqualTo(2));
            Assert.That(restored.Mechanical.Worlds["world_B"][0].IDName, Is.EqualTo("Gear_Wood"));
            MechanicalWorld.RestoreArchive(restored, null); Assert.That(restored.Mechanical.Worlds, Is.Empty);
        }
        private static ItemData Item(string id, int amount) => new Data_GeneralItem { IDName = id, Stack = new ItemStack { Amount = amount, Volume = 1, Stackable = true } };
        #endregion
    }
}
