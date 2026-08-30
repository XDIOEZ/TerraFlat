using System.Collections.Generic;
using NUnit.Framework;

namespace FlatWorld.GameTest.InventoryCrafting
{
    /// <summary>制作公共核心守恒测试。</summary>
    public sealed class CraftingCoreTests
    {
        [Test]
        [Category("InventoryCrafting.Core")]
        public void Transaction_MissingMaterials_DoesNotMutateInventory()
        {
            Inventory input = CreateInventory("输入", 10f, CreateItem("wood", 1f));
            Inventory output = CreateInventory("输出", 10f, (ItemData)null);
            CraftingRecipeMatch match = CreateMatch(new CraftingConsumption(0, 2f));

            bool prepared = CraftingTransaction.TryCreate(
                input,
                output,
                match,
                new[] { CreateItem("plank", 1f) },
                false,
                out _,
                out CraftingResult failure);

            Assert.That(prepared, Is.False);
            Assert.That(failure.FailureReason, Is.EqualTo(CraftingFailureReason.MissingMaterials));
            Assert.That(input.Data.itemSlots[0].itemData.IDName, Is.EqualTo("wood"));
            Assert.That(input.Data.itemSlots[0].itemData.Stack.Amount, Is.EqualTo(1f));
            Assert.That(output.Data.itemSlots[0].itemData, Is.Null);
        }

        [Test]
        [Category("InventoryCrafting.Core")]
        public void Transaction_MultipleOutputsInsufficientSpace_DoesNotPartiallyProduce()
        {
            Inventory input = CreateInventory("输入", 10f, CreateItem("wood", 2f));
            Inventory output = CreateInventory("输出", 1f, (ItemData)null);
            CraftingRecipeMatch match = CreateMatch(new CraftingConsumption(0, 1f));

            bool prepared = CraftingTransaction.TryCreate(
                input,
                output,
                match,
                new[] { CreateItem("plank", 1f), CreateItem("sawdust", 1f) },
                false,
                out _,
                out CraftingResult failure);

            Assert.That(prepared, Is.False);
            Assert.That(failure.FailureReason, Is.EqualTo(CraftingFailureReason.OutputSpaceInsufficient));
            Assert.That(input.Data.itemSlots[0].itemData.Stack.Amount, Is.EqualTo(2f));
            Assert.That(output.Data.itemSlots[0].itemData, Is.Null);
        }

        [Test]
        [Category("InventoryCrafting.Core")]
        public void Transaction_OutputFallback_UsesReleasedInputSlotAtomically()
        {
            Inventory input = CreateInventory("输入", 10f, CreateItem("wood", 1f));
            Inventory output = CreateInventory("输出", 1f, CreateItem("blocked", 1f));
            CraftingRecipeMatch match = CreateMatch(new CraftingConsumption(0, 1f));

            bool prepared = CraftingTransaction.TryCreate(
                input,
                output,
                match,
                new[] { CreateItem("plank", 1f) },
                true,
                out CraftingTransaction transaction,
                out _);

            Assert.That(prepared, Is.True);
            Assert.That(transaction.Commit(out CraftingResult failure), Is.True, failure?.Message);
            transaction.Complete();
            Assert.That(input.Data.itemSlots[0].itemData.IDName, Is.EqualTo("plank"));
            Assert.That(output.Data.itemSlots[0].itemData.IDName, Is.EqualTo("blocked"));
        }

        [Test]
        [Category("InventoryCrafting.Core")]
        public void Matcher_CraftingRecipe_IgnoresSlotPositionsAndReturnsConsumptionPlan()
        {
            Inventory input = CreateInventory(
                "输入",
                10f,
                CreateItem("iron", 1f, "metal"),
                CreateItem("wood", 1f),
                null,
                CreateItem("cloth", 1f),
                CreateItem("stone", 1f),
                null,
                null,
                null,
                null);
            RuntimeRecipe recipe = new RuntimeRecipe
            {
                Id = "test_compact_mirror",
                enableMirrorCrafting = true,
                inputs = new RuntimeRecipeInput
                {
                    GridWidth = 2,
                    GridHeight = 2,
                    inputOrder = RecipeInputRule.规则合成,
                    RowItems_List = new List<RuntimeRecipeIngredient>
                    {
                        Exact("wood"),
                        Tag("metal"),
                        Exact("stone"),
                        Exact("cloth")
                    }
                }
            };
            var capabilities = new CraftingCapabilities
            {
                RecipeType = RecipeType.Crafting,
                AllowCompactGrid = true
            };

            bool matched = CraftingRecipeMatcher.TryMatchRecipe(input, recipe, capabilities, out CraftingRecipeMatch match);

            Assert.That(matched, Is.True);
            Assert.That(match.Mirrored, Is.False);
            Assert.That(match.Consumptions, Has.Count.EqualTo(4));
            Assert.That(match.Consumptions[0].SlotIndex, Is.EqualTo(0));
            Assert.That(match.Consumptions[1].SlotIndex, Is.EqualTo(1));
            Assert.That(match.Consumptions[2].SlotIndex, Is.EqualTo(3));
            Assert.That(match.Consumptions[3].SlotIndex, Is.EqualTo(4));
        }

        [Test]
        [Category("InventoryCrafting.Core")]
        public void Matcher_UnorderedRecipe_RejectsExtraOccupiedSlots()
        {
            Inventory input = CreateInventory("输入", 10f, CreateItem("wood", 1f), CreateItem("stone", 1f));
            RuntimeRecipe recipe = new RuntimeRecipe
            {
                Id = "test_unordered_extra",
                inputs = new RuntimeRecipeInput
                {
                    GridWidth = 1,
                    GridHeight = 1,
                    inputOrder = RecipeInputRule.无规则合成,
                    RowItems_List = new List<RuntimeRecipeIngredient> { Exact("wood") }
                }
            };

            bool matched = CraftingRecipeMatcher.TryMatchRecipe(
                input,
                recipe,
                new CraftingCapabilities { RecipeType = RecipeType.Crafting },
                out _);

            Assert.That(matched, Is.False);
        }

        [Test]
        [Category("InventoryCrafting.Core")]
        public void Matcher_PositionlessRecipe_AllowsSameMaterialAcrossSlots()
        {
            Inventory input = CreateInventory(
                "输入",
                10f,
                CreateItem("stone", 1f),
                CreateItem("stone", 2f));
            RuntimeRecipe recipe = new RuntimeRecipe
            {
                Id = "test_positionless_split_stack",
                inputs = new RuntimeRecipeInput
                {
                    GridWidth = 1,
                    GridHeight = 1,
                    inputOrder = RecipeInputRule.无规则合成,
                    RowItems_List = new List<RuntimeRecipeIngredient> { Exact("stone", 3) }
                }
            };

            bool matched = CraftingRecipeMatcher.TryMatchRecipe(
                input,
                recipe,
                new CraftingCapabilities { RecipeType = RecipeType.Crafting },
                out CraftingRecipeMatch match);

            Assert.That(matched, Is.True);
            Assert.That(match.Consumptions, Has.Count.EqualTo(2));
            Assert.That(match.Consumptions[0].Amount, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(match.Consumptions[1].Amount, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        [Category("InventoryCrafting.Core")]
        public void Matcher_UnorderedOverlappingTags_FindsGlobalConsumptionPlan()
        {
            Inventory input = CreateInventory(
                "输入",
                20f,
                CreateItem("shared", 5f, "a", "b"),
                CreateItem("a_only", 4f, "a"),
                CreateItem("b_only", 5f, "b"));
            RuntimeRecipe recipe = new RuntimeRecipe
            {
                Id = "test_unordered_overlapping_tags",
                inputs = new RuntimeRecipeInput
                {
                    GridWidth = 3,
                    GridHeight = 1,
                    inputOrder = RecipeInputRule.无规则合成,
                    RowItems_List = new List<RuntimeRecipeIngredient>
                    {
                        Tag("b", 9),
                        Tag("a", 5),
                        Tag("a", 0)
                    }
                }
            };

            bool matched = CraftingRecipeMatcher.TryMatchRecipe(
                input,
                recipe,
                new CraftingCapabilities { RecipeType = RecipeType.Crafting },
                out CraftingRecipeMatch match);

            Assert.That(matched, Is.True);
            Assert.That(match.Consumptions, Has.Count.EqualTo(3));
            Assert.That(match.Consumptions[0].Amount, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(match.Consumptions[1].Amount, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(match.Consumptions[2].Amount, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        [Category("InventoryCrafting.Core")]
        public void Transaction_MultipleNonStackableOutputs_RequireOneSlotPerUnit()
        {
            Inventory input = CreateInventory("输入", 10f, CreateItem("wood", 1f));
            Inventory output = CreateInventory("输出", 5f, (ItemData)null);
            ItemData heavyOutput = CreateItem("anvil", 2f);
            heavyOutput.Stack.Volume = 5f;

            bool prepared = CraftingTransaction.TryCreate(
                input,
                output,
                CreateMatch(new CraftingConsumption(0, 1f)),
                new[] { heavyOutput },
                false,
                out _,
                out CraftingResult failure);

            Assert.That(prepared, Is.False);
            Assert.That(failure.FailureReason, Is.EqualTo(CraftingFailureReason.OutputSpaceInsufficient));
            Assert.That(input.Data.itemSlots[0].itemData.Stack.Amount, Is.EqualTo(1f));
            Assert.That(output.Data.itemSlots[0].itemData, Is.Null);
        }

        private static CraftingRecipeMatch CreateMatch(params CraftingConsumption[] consumptions)
        {
            return new CraftingRecipeMatch(new RuntimeRecipe { Id = "test_recipe" }, false, consumptions);
        }

        private static Inventory CreateInventory(string name, float slotMaxVolume, params ItemData[] items)
        {
            var slots = new List<ItemSlot>(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                slots.Add(new ItemSlot(i)
                {
                    SlotMaxVolume = slotMaxVolume,
                    itemData = items[i]
                });
            }

            return new Inventory
            {
                Data = new Inventory_Data(slots, name)
            };
        }

        private static ItemData CreateItem(string id, float amount, params string[] tags)
        {
            return new Data_GeneralItem
            {
                IDName = id,
                Tags = new List<string>(tags),
                Stack = new ItemStack
                {
                    Amount = amount,
                    Volume = 1f
                }
            };
        }

        private static RuntimeRecipeIngredient Exact(string id, int amount = 1)
        {
            return new RuntimeRecipeIngredient
            {
                matchMode = MatchMode.ExactItem,
                ItemName = id,
                amount = amount
            };
        }

        private static RuntimeRecipeIngredient Tag(string tag, int amount = 1)
        {
            return new RuntimeRecipeIngredient
            {
                matchMode = MatchMode.ByTag,
                Tag = tag,
                amount = amount
            };
        }
    }
}
