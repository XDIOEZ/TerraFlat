using System;
using System.Collections.Generic;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestRuntimeProbe
{
    #region 本机库存待确认期间的业务拒绝采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterPendingInventoryRuntime()
    {
        Register("inventory", "pending_pickup_guards", CapturePendingPickupGuards);
        Register("inventory", "player_carry_capacity_persistence", CapturePlayerCarryCapacityPersistence);
    }

    // 采样真实玩家初始化容量与独立实例的复制、存档和公开快照。
    private static JObject CapturePlayerCarryCapacityPersistence(Item player, JObject args)
    {
        Inventory_Data liveBag = player.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag)?.inventory?.Data;
        ItemData copy = ItemInstanceDataFactory.CloneRuntime(player.itemData);
        ActorModuleState.SetMaxCarryWeight(copy, 0f);
        ActorModuleState.SetMaxCarryVolume(copy, 137f);
        var bag = (Inventory_ModuleData)System.Linq.Enumerable.First(copy.ModuleDataDic.Values,
            module => module.ModuleId == ModText.Bag);
        ItemData cloned = ItemInstanceDataFactory.CloneRuntime(copy);
        byte[] bytes = ItemSnapshotSerialization.SerializePayload(ItemSnapshotSerialization.Capture(copy));
        ItemData restored = MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(bytes).CreateColdData();
        bool beforeValid = ItemRuntimeStateFingerprint.TryCalculateModule(bag, false, new HashSet<ItemData>(), out ulong before);
        ActorModuleState.SetMaxCarryVolume(copy, 138f);
        bool afterValid = ItemRuntimeStateFingerprint.TryCalculateModule(bag, false, new HashSet<ItemData>(), out ulong after);
        var publicTarget = new Inventory_ModuleData
        { StableName = bag.StableName, ModuleId = bag.ModuleId, MaxCarryWeight = 9f, MaxCarryVolume = 11f };
        ModuleInstanceSnapshot.Capture(bag, true).RestoreTo(publicTarget);
        return new JObject
        {
            ["initialWeight"] = liveBag?.MaxCarryWeight, ["initialVolume"] = liveBag?.MaxCarryVolume,
            ["configuredWeight"] = ActorModuleState.GetMaxCarryWeight(player.itemData),
            ["configuredVolume"] = ActorModuleState.GetMaxCarryVolume(player.itemData),
            ["cloneWeight"] = ActorModuleState.GetMaxCarryWeight(cloned),
            ["cloneVolume"] = ActorModuleState.GetMaxCarryVolume(cloned),
            ["restoredWeight"] = ActorModuleState.GetMaxCarryWeight(restored),
            ["restoredVolume"] = ActorModuleState.GetMaxCarryVolume(restored),
            ["fingerprintChanged"] = beforeValid && afterValid && before != after,
            ["publicPreserved"] = publicTarget.MaxCarryWeight == 9f && publicTarget.MaxCarryVolume == 11f
        };
    }

    private sealed class PendingPickupUseCounter : IFoodMechanic, IFoodUseRule
    {
        public string MechanicId => "test.pending-pickup-use";
        public int Priority => 0;
        public int Uses;
        public void OnFoodUse(FoodUseContext context) => Uses++;
    }

    private sealed class PendingPickupDropProvider : IDroppedItemSpawnProvider
    {
        public Item Owner;
        public Transform Root;
        public int Calls;
        public float GeneratedAmount;
        public bool CanHandle(in DroppedItemSpawnContext context)
            => ReferenceEquals(context.SourceItem, Owner) && context.Reason == DroppedItemSpawnReason.PlayerDiscard;
        public DroppedItemHandle SpawnDrop(ItemData source, in DroppedItemSpawnContext context, float rotation, Vector3 scale)
        {
            Calls++;
            var instance = new GameObject("[LuaPendingDiscardResult]");
            instance.transform.SetParent(Root, false);
            GameItem item = instance.AddComponent<GameItem>();
            ItemData data = ItemInstanceDataFactory.CloneRuntime(source);
            data.Guid = Guid.NewGuid().GetHashCode();
            if (data.Guid == 0) data.Guid = 1;
            item.BindData(data);
            GeneratedAmount += data.Stack.Amount;
            return new DroppedItemHandle(data.Guid, DroppedItemService.Epoch, item);
        }
    }

    // 借用当前玩家身份，临时库存、生成接管和网络桥在同步调用后恢复。
    private static JObject CapturePendingPickupGuards(Item player, JObject args)
    {
        if (!ActorProfileRegistry.IsLocallyControlled(player))
            throw new InvalidOperationException("待确认库存回归需要本地受控玩家。");
        var root = new GameObject("[LuaPendingPickupGuards]");
        // 模拟丢弃的回滚句柄需要归属于隔离玩家的真实世界场景。
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, player.gameObject.scene);
        root.SetActive(false);
        Func<bool> previousPending = ItemNetworkStateSerialization.IsLocalInventoryTransactionPending;
        IDisposable spawnLease = null;
        int completedFoodEvents = 0;
        string completedFoodId = string.Empty;
        Item owner = player;
        void HandleFoodCompleted(FoodConsumeResult result)
        {
            if (!ReferenceEquals(result.Consumer, owner)) return;
            completedFoodEvents++;
            completedFoodId = result.ConsumedItemId;
        }

        FoodConsumptionService.ConsumptionCompleted += HandleFoodCompleted;
        try
        {
            bool pending = true;
            ItemNetworkStateSerialization.IsLocalInventoryTransactionPending = () => pending;

            var foodObject = new GameObject("[LuaPendingFood]");
            foodObject.transform.SetParent(root.transform, false);
            GameItem foodItem = foodObject.AddComponent<GameItem>();
            foodItem.BindData(new Data_GeneralItem
            {
                IDName = "test:pending_pickup_food", Stack = new ItemStack { Amount = 3f, CanBePickedUp = false }
            });
            foodItem.Owner = owner;
            var foodSlot = new ItemSlot(0) { itemData = foodItem.itemData };
            var foodInventory = new Inventory_Data(new List<ItemSlot> { foodSlot }, "test.pending-food");
            var gateway = new InventoryFoodItemOperationGateway();
            gateway.BindInventoryContext(foodInventory, foodSlot, 0);
            var sourceData = new ModData_FoodData
            {
                FoodData = new Food
                {
                    Max_EatingProgress = 2f,
                    nutrition = new Nutrition { Carbohydrates = 5f, Fat = 0f, Protein = 0f, Water = 0f, Vitamins = 0f }
                }
            };
            var consumerData = new ModData_FoodData
            {
                FoodData = new Food
                {
                    nutrition = new Nutrition { Carbohydrates = 0f, Fat = 0f, Protein = 0f, Water = 0f, Vitamins = 0f }
                }
            };
            var source = new FoodRuntimeContext(foodItem, sourceData, () => true, null, null, null, null, gateway);
            var consumer = new FoodRuntimeContext(owner, consumerData, () => false, null, null, null, null);
            using var pipeline = new FoodRulePipeline(source);
            var useCounter = new PendingPickupUseCounter();
            pipeline.Add(useCounter);
            pipeline.Initialize();
            var nutrition = new FoodNutritionService(consumer);
            bool pendingFoodAccepted = FoodConsumptionService.TryConsume(source, consumer, pipeline, nutrition);
            var result = new JObject
            {
                ["pendingFoodAccepted"] = pendingFoodAccepted,
                ["pendingFoodAmount"] = foodSlot.itemData?.Stack.Amount ?? 0f,
                ["pendingFoodProgress"] = source.EatingProgress,
                ["pendingFoodUses"] = useCounter.Uses,
                ["pendingNutrition"] = consumer.Data.nutrition.Carbohydrates,
                ["pendingFoodCompletedEvents"] = completedFoodEvents
            };

            ItemData logs = GameRes.ExistingInstance.CreateItemData("Log");
            logs.Stack.Amount = 3f;
            logs.Stack.CanBePickedUp = false;
            var dropSlot = new ItemSlot(0) { itemData = logs };
            Mod_DiscardItem discard = root.AddComponent<Mod_DiscardItem>();
            discard.ModuleInit(owner, new Ex_ModData { Name = "test.pending-discard", ID = "test.pending-discard", Enabled = true });
            var provider = new PendingPickupDropProvider { Owner = owner, Root = root.transform };
            spawnLease = DroppedItemService.RegisterSpawnProvider(provider);
            int worldBefore = DroppedItemService.Count;
            discard.DropItemByCountAtWorldPosition(dropSlot, 1, Vector2.zero);
            result["pendingDiscardAmount"] = dropSlot.itemData?.Stack.Amount ?? 0f;
            result["pendingDropCalls"] = provider.Calls;
            result["pendingGeneratedAmount"] = provider.GeneratedAmount;
            result["pendingWorldUnchanged"] = DroppedItemService.Count == worldBefore;

            pending = false;
            result["releasedFoodAccepted"] = FoodConsumptionService.TryConsume(source, consumer, pipeline, nutrition);
            result["releasedFoodAmount"] = foodSlot.itemData?.Stack.Amount ?? 0f;
            result["releasedFoodUses"] = useCounter.Uses;
            result["releasedNutrition"] = consumer.Data.nutrition.Carbohydrates;
            result["partialFoodProgress"] = source.EatingProgress;
            result["partialFoodCompletedEvents"] = completedFoodEvents;
            bool completedFoodAccepted = FoodConsumptionService.TryConsume(source, consumer, pipeline, nutrition);
            result["completedFoodAccepted"] = completedFoodAccepted;
            result["completedFoodEvents"] = completedFoodEvents;
            result["completedFoodId"] = completedFoodId;
            result["completedFoodAmount"] = foodSlot.itemData?.Stack.Amount ?? 0f;
            result["completedNutrition"] = consumer.Data.nutrition.Carbohydrates;
            result["completedFoodProgress"] = source.EatingProgress;
            discard.DropItemByCountAtWorldPosition(dropSlot, 1, Vector2.zero);
            result["releasedDiscardAmount"] = dropSlot.itemData?.Stack.Amount ?? 0f;
            result["releasedDropCalls"] = provider.Calls;
            result["releasedGeneratedAmount"] = provider.GeneratedAmount;
            return result;
        }
        finally
        {
            FoodConsumptionService.ConsumptionCompleted -= HandleFoodCompleted;
            ItemNetworkStateSerialization.IsLocalInventoryTransactionPending = previousPending;
            try { spawnLease?.Dispose(); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    #endregion
}
