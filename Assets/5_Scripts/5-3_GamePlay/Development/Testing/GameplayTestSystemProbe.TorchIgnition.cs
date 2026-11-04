using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 火把显式点火与火种库存回归

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterTorchIgnitionInventoryRules()
        => Register("item_runtime", "torch_ignition", CaptureTorchIgnitionInventoryRules);

    // 正式模块执行点火、熄灭和库存快照，Lua 独立核对状态与扣量。
    private static JObject CaptureTorchIgnitionInventoryRules(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().ToDictionary(value => value.Id);
        var configurations = new JArray();
        foreach (string id in new[] { "Torch_Summoner", "Torch_Building" })
        {
            var modules = definitions[id].Modules;
            JObject building = JObject.Parse(modules["建筑模块"].Data.Value<string>("BitData"));
            configurations.Add(new JObject
            {
                ["id"] = id,
                ["startsBurning"] = modules["combustion"].Parameters.Value<bool>("startBurning"),
                ["sharedModules"] = building["SharedModuleIds"].DeepClone()
            });
        }

        var root = new GameObject("Lua Torch Ignition") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var environment = new TorchIgnitionEnvironmentSource { World = root.scene.name, Allowed = true };
        ItemEnvironmentSources.Register(environment);
        Mod_FuelInteraction interaction = null;
        Mod_Combustion combustion = null;
        try
        {
            Item torch = root.AddComponent<GameItem>();
            torch.BindData(EntityProbeData("probe:torch", 1f));
            var actorRoot = new GameObject("Actor");
            actorRoot.transform.SetParent(root.transform, false);
            Item actor = actorRoot.AddComponent<GameItem>();
            actor.BindData(EntityProbeData("probe:torch-actor", 1f));
            var health = actorRoot.AddComponent<Mod_DamageReceiver>();
            health.modData = new Ex_ModData { ID = ModText.Hp, Name = "health" };
            health.Data.Hp = health.Data.MaxHp = 10f;
            actor.itemMods.AddMod(health);

            var fuel = root.AddComponent<Mod_Fuel>();
            fuel.ExData = new Ex_ModData_MemoryPackable { ID = ModText.Fuel, Name = "fuel" };
            fuel.ModuleInit(torch, fuel.ExData);
            fuel.Data.Fuel = new Vector2(100f, 100f);
            torch.itemMods.AddMod(fuel);
            combustion = root.AddComponent<Mod_Combustion>();
            combustion.ModData.Name = "combustion";
            combustion.ModuleInit(torch, combustion.ModData);
            combustion.startBurning = false;
            torch.itemMods.AddMod(combustion);
            combustion.BindModuleDependencies(torch.itemMods);
            combustion.Load();
            interaction = root.AddComponent<Mod_FuelInteraction>();
            interaction.ModData.Name = "fuelInteraction";
            interaction.ModuleInit(torch, interaction.ModData);
            interaction.BindModuleDependencies(torch.itemMods);
            interaction.Load();

            Inventory inventory = interaction.IgnitionInventory;
            bool emptyRejected = !interaction.TryIgniteFromStoredItem(actor);
            bool fuelRejected = !inventory.Data.TryAddItem(EntityProbeData("Log", 1f), false);
            inventory.Data.TryAddItem(EntityProbeData("FireSeed", 3f), true);
            bool insertionStayedOut = !interaction.IsBurning;
            bool ignited = interaction.TryIgniteFromStoredItem(actor);
            float afterIgnite = inventory.Data.GetItemSlot(0).itemData.Stack.Amount;
            bool duplicateRejected = !interaction.TryIgniteFromStoredItem(actor);
            bool extinguished = interaction.TryExtinguish(actor) && !interaction.IsBurning;
            float retainedFuel = fuel.Data.Fuel.x;
            environment.Allowed = false;
            bool oxygenRejected = !interaction.TryIgniteFromStoredItem(actor);
            float afterOxygenFailure = inventory.Data.GetItemSlot(0).itemData.Stack.Amount;
            interaction.Save();
            interaction.Unload();
            bool unloadedRejected = !interaction.TryIgniteFromStoredItem(actor) && !inventory.CanAccessRuntime;
            inventory.Data.GetItemSlot(0).itemData = null;
            interaction.Load();
            float restoredAmount = inventory.Data.GetItemSlot(0).itemData.Stack.Amount;

            inventory.Data.TryConsumeFromSlot(inventory.Data.GetItemSlot(0), 2, out _);
            ItemData iron = EntityProbeData("probe:hot-iron", 2f);
            iron.MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 499.9f };
            bool belowThresholdRejected = !inventory.Data.TryAddItem(iron, false);
            iron.MatterState.TemperatureCelsius = 500f;
            bool hotItemAccepted = inventory.Data.TryAddItem(iron, true);
            ItemData storedIron = inventory.Data.GetItemSlot(0).itemData;
            bool hotInsertionStayedOut = !interaction.IsBurning;
            environment.Allowed = true;
            storedIron.MatterState.TemperatureCelsius = 499.9f;
            bool cooledItemRejected = !interaction.TryIgniteFromStoredItem(actor);
            storedIron.MatterState.TemperatureCelsius = 500f;
            environment.Allowed = false;
            bool hotOxygenRejected = !interaction.TryIgniteFromStoredItem(actor);
            environment.Allowed = true;
            fuel.Data.Fuel.x = 0f;
            bool hotItemCannotSupplyFuel = !interaction.TryIgniteFromStoredItem(actor) && fuel.Data.Fuel.x == 0f;
            fuel.Data.Fuel.x = 100f;
            bool hotItemIgnited = interaction.TryIgniteFromStoredItem(actor);
            bool hotItemRetained = ReferenceEquals(storedIron, inventory.Data.GetItemSlot(0).itemData) &&
                storedIron.Stack.Amount == 2f && storedIron.MatterState.TemperatureCelsius == 500f;
            interaction.TryExtinguish(actor);
            bool hotItemReused = interaction.TryIgniteFromStoredItem(actor) && storedIron.Stack.Amount == 2f;

            return new JObject
            {
                ["configurations"] = configurations, ["emptyRejected"] = emptyRejected,
                ["fuelRejected"] = fuelRejected, ["insertionStayedOut"] = insertionStayedOut,
                ["ignited"] = ignited, ["afterIgnite"] = afterIgnite,
                ["duplicateRejected"] = duplicateRejected, ["extinguished"] = extinguished,
                ["retainedFuel"] = retainedFuel, ["oxygenRejected"] = oxygenRejected,
                ["afterOxygenFailure"] = afterOxygenFailure,
                ["unloadedRejected"] = unloadedRejected, ["restoredAmount"] = restoredAmount,
                ["belowThresholdRejected"] = belowThresholdRejected, ["hotItemAccepted"] = hotItemAccepted,
                ["hotInsertionStayedOut"] = hotInsertionStayedOut, ["cooledItemRejected"] = cooledItemRejected,
                ["hotOxygenRejected"] = hotOxygenRejected, ["hotItemCannotSupplyFuel"] = hotItemCannotSupplyFuel,
                ["hotItemIgnited"] = hotItemIgnited, ["hotItemRetained"] = hotItemRetained,
                ["hotItemReused"] = hotItemReused
            };
        }
        finally
        {
            interaction?.Unload();
            combustion?.Unload();
            ItemEnvironmentSources.Unregister(environment);
            Object.DestroyImmediate(root);
        }
    }

    private sealed class TorchIgnitionEnvironmentSource : IItemEnvironmentSource
    {
        public string World;
        public bool Allowed;
        public bool TrySample(Item item, out ItemEnvironmentSample sample) { sample = default; return false; }
        public float SupplyAmbientOxygen(Item item, float maximumAmount) => 0f;
        public bool TrySampleCombustion(string worldKey, Vector2 position, out bool canBurn)
        { canBurn = Allowed; return worldKey == World; }
    }

    #endregion
}
