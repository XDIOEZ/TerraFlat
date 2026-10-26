using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class Program
{
    #region 正式化工反应与原子库存回归
    private static int checks;
    private static Dictionary<string, JObject> definitions;
    private static void Check(bool value, string label)
    { checks++; if (!value) throw new InvalidOperationException(label); }
    private static ItemData Material(string id, float amount, float temperature) => new()
    {
        IDName = id, Stack = new ItemStack { Amount = amount },
        MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = temperature }
    };
    private static Inventory Inventory(params ItemData[] items) => new()
    { Data = new Inventory_Data { itemSlots = items.Select((item, index) => new ItemSlot(index) { itemData = item }).ToList() } };
    private static float Amount(Inventory inventory, string id)
        => inventory.Data.itemSlots.Where(slot => slot.itemData?.IDName == id).Sum(slot => slot.itemData.Stack.Amount);
    private static string State(Inventory inventory) => JsonConvert.SerializeObject(inventory.Data.itemSlots);
    private static RuntimeItemReactionDefinition Reaction(string source)
        => ItemMatterReactionCompiler.CompileReactions(definitions[source]["reactions"].ToObject<List<ItemReactionDefinitionDto>>(), source).Single();

    private static void Main(string[] args)
    {
        JObject content = JObject.Parse(File.ReadAllText(Path.Combine(args[0], "Assets/StreamingAssets/GameConfig/Items/shells/petrochemicals.json")));
        definitions = content["items"].ToDictionary(item => item.Value<string>("id"), item => (JObject)item);
        TestReaction("CrudeOil", 349.99f, 4, 3, false, false);
        TestReaction("CrudeOil", 350, 4, 3, true, true);
        TestReaction("CrudeOil", 350, 3, 3, false, false);
        TestReaction("CrudeOil", 350, 8, 3, true, true, 2);
        TestReaction("CrudeOil", 350, 4, 2, true, false);
        TestReaction("Naphtha", 749.99f, 1, 2, false, false);
        TestReaction("Naphtha", 750, 1, 2, true, true);
        TestReaction("Naphtha", 750, 1, 1, true, false);
        CatalystCases();
        InvalidCatalystRecipes();
        LiquidPersistenceFailureCases();
        Console.WriteLine($"PASS {checks} checks: production reaction compiler/resolver/allocator, atomic inventory transaction, liquid storage and heat persistence rollback.");
    }

    private static void TestReaction(string source, float temperature, float amount, int outputSlots,
        bool expectedMatch, bool expectedCommit, int expectedBatches = 1)
    {
        RuntimeItemReactionDefinition reaction = Reaction(source);
        Inventory input = Inventory(Material(source, amount, temperature));
        Inventory output = Inventory(new ItemData[outputSlots]);
        string before = State(input), beforeOutput = State(output);
        bool matched = ItemReactionResolver.TryMatchBatch(input, reaction, out var match, out int batches);
        Check(matched == expectedMatch, source + " temperature/input boundary");
        bool committed = false;
        if (matched)
        {
            Check(batches == expectedBatches, source + " complete batches");
            ItemData[] products = reaction.Recipe.outputs.results.Select(result => Material(result.ItemName, result.amount * batches, temperature)).ToArray();
            if (CraftingTransaction.TryCreate(input, output, match, products, false, out var transaction, out _))
            {
                Check(State(input) == before && State(output) == beforeOutput, "preview changed actual inventories");
                committed = transaction.Commit(out _);
                if (committed)
                {
                    transaction.Complete();
                    Check(!transaction.Commit(out _), "duplicate commit accepted");
                    foreach (RuntimeRecipeResult result in reaction.Recipe.outputs.results)
                        Check(Amount(output, result.ItemName) == result.amount * batches, source + " incomplete byproducts");
                }
            }
        }
        Check(committed == expectedCommit, source + " atomic commit result");
        if (!committed) Check(before == State(input) && beforeOutput == State(output), source + " rejection lost material");
    }

    private static void CatalystCases()
    {
        RuntimeItemReactionDefinition reaction = Reaction("Ethylene");
        foreach (var sample in new (float temperature, float catalyst, float catalystTemperature, bool expected)[] { (200f, 1f, 200f, true),
                     (199.99f, 1f, 200f, false), (200f, 0f, 200f, false), (200f, 1f, 199.99f, false) })
        {
            Inventory input = Inventory(Material("Ethylene", 7, sample.temperature),
                sample.catalyst > 0 ? Material("PolymerizationCatalyst", sample.catalyst, sample.catalystTemperature) : null);
            Inventory output = Inventory(new ItemData[1]);
            bool matched = ItemReactionResolver.TryMatchBatch(input, reaction, out var match, out int batches);
            Check(matched == sample.expected, "catalyst presence or temperature boundary");
            if (!matched) continue;
            Check(batches == 7 && match.Consumptions.All(value => value.SlotIndex == 0), "catalyst incorrectly limits or enters consumption plan");
            Check(CraftingTransaction.TryCreate(input, output, match, new[] { Material("Plastic", batches, 200) }, false, out var transaction, out _), "polymerization prepare");
            Check(transaction.Commit(out _), "polymerization commit"); transaction.Complete();
            Check(Amount(input, "PolymerizationCatalyst") == 1 && Amount(input, "Ethylene") == 0 && Amount(output, "Plastic") == 7, "polymerization did not preserve catalyst");
            input.Data.itemSlots[0].itemData = Material("Ethylene", 2, 200);
            Check(ItemReactionResolver.TryMatchBatch(input, reaction, out _, out int repeated) && repeated == 2, "preserved catalyst cannot be reused");
        }
        Inventory dragged = Inventory(Material("Ethylene", 1, 200), Material("PolymerizationCatalyst", 1, 200));
        dragged.Dragged.Add(1);
        Check(!ItemReactionResolver.TryMatchBatch(dragged, reaction, out _, out _), "dragged catalyst participates");
        Inventory ghost = Inventory(Material("Ethylene", 1, 200), Material("PolymerizationCatalyst", 0, 200));
        Check(!ItemReactionResolver.TryMatchBatch(ghost, reaction, out _, out _), "zero-stack catalyst participates");
        Inventory blocked = Inventory(Material("Ethylene", 1, 200), Material("PolymerizationCatalyst", 1, 200));
        Check(!ItemReactionResolver.TryMatchBatch(blocked, reaction, out _, out _, new HashSet<int> { 1 }), "blocked catalyst participates");
    }

    private static void InvalidCatalystRecipes()
    {
        foreach (int amount in new[] { 0, -1 })
        {
            bool failed = false;
            try
            {
                ItemMatterReactionCompiler.CompileReactions(new[] { new ItemReactionDefinitionDto
                { Id = "invalid", Inputs = new() { new() { ItemId = "PolymerizationCatalyst", Amount = amount } },
                    Outputs = new() { new() { ItemId = "Plastic", Amount = 1 } } } }, "invalid");
            }
            catch (InvalidDataException) { failed = true; }
            Check(failed, "no-consumption or negative-input reaction accepted");
        }
    }

    private static void LiquidPersistenceFailureCases()
    {
        GameRes.ExistingInstance.Liquids["core:crude_oil"] = new LiquidDefinition
        {
            HeatProcess = new LiquidHeatProcess { Mode = LiquidHeatProcessMode.ConsumeServing,
                MinimumTemperature = 100, Seconds = 3, ConsumeAmount = 4, OutputAmount = 1, OutputItemId = "CrudeOil" }
        };
        foreach (var sample in new (int failures, bool writeThenFail)[] { (1, false), (1, true), (2, true) })
        {
            var liquid = new LiquidContainerState { Temperature = 100 };
            MixedLiquidContents.Add(liquid, "core:crude_oil", 4, 100);
            LiquidContainerState saved = MixedLiquidContents.Clone(liquid);
            Inventory output = Inventory(new ItemData[1]);
            int calls = 0;
            var failures = new List<Exception>();
            Exception caught = null;
            CraftingTransaction Prepare(string id, int amount)
                => CraftingTransaction.TryCreateGrant(output, new[] { Material(id, amount, 100) }, out var transaction, out _) ? transaction : null;
            void Persist()
            {
                calls++;
                if (calls <= sample.failures)
                {
                    if (sample.writeThenFail) saved = MixedLiquidContents.Clone(liquid);
                    var error = new InvalidOperationException("injected liquid persistence failure " + calls);
                    failures.Add(error); throw error;
                }
                saved = MixedLiquidContents.Clone(liquid);
            }
            try { PetrochemicalHeatTestEntry.Process(liquid, Prepare, Persist); }
            catch (Exception error) { caught = error; }
            Check(caught != null && calls == 2, "liquid persist failure did not run restoration write");
            Check(Amount(output, "CrudeOil") == 0 && MixedLiquidContents.GetAmount(liquid, "core:crude_oil") == 4,
                "liquid persist failure left products or lost liquid");
            Check(MixedLiquidContents.GetAmount(saved, "core:crude_oil") == 4, "liquid restore did not repair a partially written state");
            if (sample.failures == 1) Check(ReferenceEquals(caught, failures[0]), "original persistence exception was lost");
            else Check(caught is AggregateException aggregate && aggregate.InnerExceptions.Contains(failures[0]) &&
                aggregate.InnerExceptions.Contains(failures[1]), "restoration error hid the original persistence error");
            Check(PetrochemicalHeatTestEntry.Process(liquid, Prepare, () => saved = MixedLiquidContents.Clone(liquid)), "restored liquid cannot retry");
            Check(Amount(output, "CrudeOil") == 1 && liquid.Amount == 0 && saved.Amount == 0, "retry duplicated product or liquid");
        }
    }
    #endregion
}
