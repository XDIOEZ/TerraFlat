using System;
using System.Collections.Generic;
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
        => Register("inventory", "pending_pickup_guards", CapturePendingPickupGuards);

    private sealed class PendingPickupUseCounter : IFoodMechanic, IFoodUseRule
    {
        public string MechanicId => "test.pending-pickup-use";
        public int Priority => 0;
        public int Uses;
        public void OnFoodUse(FoodUseContext context) => Uses++;
    }

    private sealed class PendingPickupDropProvider : IDroppedItemSpawnProvider
    {
        public Player Owner;
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

    // 桥与生成接管仅在自有对象的同步调用期间替换，finally 恢复，不驱动当前测试玩家。
    private static JObject CapturePendingPickupGuards(Player _, JObject args)
    {
        var root = new GameObject("[LuaPendingPickupGuards]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        Func<bool> previousPending = ItemNetworkStateSerialization.IsLocalInventoryTransactionPending;
        IDisposable spawnLease = null;
        try
        {
            Player owner = root.AddComponent<Player>();
            owner.BindData(new Data_Player
            {
                IDName = "test:pending_pickup_player", Stack = new ItemStack { CanBePickedUp = false }
            });
            owner.SetProfileContext(GameplayTestSystemProbe.Flag(args, "localProfile", true), false, "LuaPendingPickupGuards");
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
                    Max_EatingProgress = 1f,
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
                ["pendingNutrition"] = consumer.Data.nutrition.Carbohydrates
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
            discard.DropItemByCountAtWorldPosition(dropSlot, 1, Vector2.zero);
            result["releasedDiscardAmount"] = dropSlot.itemData?.Stack.Amount ?? 0f;
            result["releasedDropCalls"] = provider.Calls;
            result["releasedGeneratedAmount"] = provider.GeneratedAmount;
            return result;
        }
        finally
        {
            ItemNetworkStateSerialization.IsLocalInventoryTransactionPending = previousPending;
            try { spawnLease?.Dispose(); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    #endregion
}
