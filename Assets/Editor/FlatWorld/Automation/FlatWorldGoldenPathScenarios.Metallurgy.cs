using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Gameplay.Progress;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>
    /// 首条铁器纵向路径：实例化真实高炉并执行完整熔炼事务得到粗铁，再使用正式制作事务产出粗铁镐；
    /// 两个里程碑写入真实 Data_Player，首次退出、磁盘保存及 ContinueGame 后再次断言，临时库存与背包均有界恢复。
    /// </summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        #region 常量与状态

        private const string GoldenRawIronRecipeId = "core:铁矿=粗铁锭";
        private const string GoldenRawIronPickaxeRecipeId = "core:粗铁镐";
        private const string GoldenIronOreId = "Ore_Iron";
        private const string GoldenRawIronId = "Ingot_RawIron";
        private const string GoldenRawIronPickaxeId = "Pickaxe_RawIron";

        private static Item metallurgyFurnaceItem;
        private static Inventory metallurgyCraftInput;
        private static Inventory metallurgyCraftOutput;
        private static Inventory metallurgyBag;
        private static Inventory_Data metallurgyOriginalBagData;
        private static string metallurgyOriginalItemSpecialData;
        private static Player metallurgyActor;
        private static int metallurgyActorGuid;
        private static bool metallurgySmeltingSignalObserved;
        private static bool metallurgyCraftingSignalObserved;
        private static bool metallurgyCompleted;
        private static bool metallurgyReentryVerified;
        private static bool metallurgyInventoryRestored;

        #endregion

        #region 操作注册

        private static IFlatWorldGoldenPathOperation CreateMetallurgyProgressionOperation() =>
            new FlatWorldGoldenPathOperation(
                "inventory.metallurgy-progression",
                "inventory-crafting",
                reset: ResetMetallurgyProgressionOperation,
                onWorldReady: RunMetallurgyProgressionOperation,
                beforeWorldExit: AssertMetallurgyProgressionBeforeWorldExit,
                onWorldReentered: AssertMetallurgyProgressionAfterWorldReentry,
                cleanup: CleanupMetallurgyProgressionOperation);

        private static void ResetMetallurgyProgressionOperation()
        {
            metallurgyFurnaceItem = null;
            metallurgyCraftInput = null;
            metallurgyCraftOutput = null;
            metallurgyBag = null;
            metallurgyOriginalBagData = null;
            metallurgyOriginalItemSpecialData = null;
            metallurgyActor = null;
            metallurgyActorGuid = 0;
            metallurgySmeltingSignalObserved = false;
            metallurgyCraftingSignalObserved = false;
            metallurgyCompleted = false;
            metallurgyReentryVerified = false;
            metallurgyInventoryRestored = false;
        }

        #endregion

        #region 场景执行

        private static void RunMetallurgyProgressionOperation(FlatWorldGoldenPathScenarioContext context)
        {
            if (context.Player?.Data == null || GameRes.Instance == null)
                throw new InvalidOperationException("首铁场景缺少真实玩家、玩家数据或 GameRes。");

            RuntimeRecipe smeltingRecipe = RequireMetallurgyRecipe(
                GoldenRawIronRecipeId,
                RecipeType.Smelting,
                GoldenRawIronId);
            RuntimeRecipe pickaxeRecipe = RequireMetallurgyRecipe(
                GoldenRawIronPickaxeRecipeId,
                RecipeType.Crafting,
                GoldenRawIronPickaxeId);

            metallurgyBag = context.Player.itemMods?.
                GetMod_ByID<Mod_Inventory>(ModText.Bag)?.inventory;
            if (metallurgyBag?.Data?.itemSlots == null || metallurgyBag.Data.itemSlots.Count == 0)
                throw new InvalidOperationException("首铁场景缺少已初始化的真实玩家背包。");

            metallurgyOriginalBagData = FastCloner.FastCloner.DeepClone(metallurgyBag.Data);
            metallurgyOriginalItemSpecialData = context.Player.Data.ItemSpecialData;
            metallurgyActor = context.Player;
            metallurgyActorGuid = context.Player.Data.Guid;
            metallurgyInventoryRestored = false;
            ClearMetallurgyProgress(context.Player.Data);

            GameplayProgressEvents.SmeltSucceeded += ObserveMetallurgySmeltingSignal;
            GameplayProgressEvents.CraftSucceeded += ObserveMetallurgyCraftingSignal;
            try
            {
                int requiredRawIron = pickaxeRecipe.inputs.RowItems_List
                    .Where(ingredient => ingredient != null &&
                                         string.Equals(
                                             ingredient.ItemName,
                                             GoldenRawIronId,
                                             StringComparison.Ordinal))
                    .Sum(ingredient => ingredient.amount);
                ItemData rawIronBatch = RunRawIronSmeltingTransaction(
                    context.Player,
                    smeltingRecipe,
                    requiredRawIron);
                RunRawIronPickaxeCraftingTransaction(
                    context.Player,
                    pickaxeRecipe,
                    rawIronBatch);
            }
            catch
            {
                RestoreMetallurgyPlayerState(context.Player);
                DespawnMetallurgyFurnace();
                throw;
            }
            finally
            {
                GameplayProgressEvents.SmeltSucceeded -= ObserveMetallurgySmeltingSignal;
                GameplayProgressEvents.CraftSucceeded -= ObserveMetallurgyCraftingSignal;
            }

            if (!metallurgySmeltingSignalObserved || !metallurgyCraftingSignalObserved)
            {
                RestoreMetallurgyPlayerState(context.Player);
                throw new InvalidOperationException("首铁场景没有观察到正式熔炼或制作成功信号。");
            }

            PlayerMetallurgyProgressStore progress = new(context.Player.Data);
            if (!progress.FirstRawIronSmelted || !progress.FirstMetalToolCrafted)
            {
                RestoreMetallurgyPlayerState(context.Player);
                throw new InvalidOperationException("首铁里程碑没有同步写入玩家 ItemSpecialData。");
            }

            ItemData craftedPickaxe = metallurgyCraftOutput.Data.itemSlots
                .Select(slot => slot?.itemData)
                .FirstOrDefault(item => string.Equals(
                    item?.IDName,
                    GoldenRawIronPickaxeId,
                    StringComparison.Ordinal));
            if (craftedPickaxe == null)
            {
                RestoreMetallurgyPlayerState(context.Player);
                throw new InvalidOperationException("正式制作成功后输出库存缺少粗铁镐。");
            }

            int lastIndex = metallurgyBag.Data.itemSlots.Count - 1;
            metallurgyBag.Data.itemSlots[lastIndex].itemData = null;
            if (!metallurgyBag.Data.TryAddItem(FastCloner.FastCloner.DeepClone(craftedPickaxe), true) ||
                !metallurgyBag.Data.itemSlots.Any(slot => string.Equals(
                    slot?.itemData?.IDName,
                    GoldenRawIronPickaxeId,
                    StringComparison.Ordinal)))
            {
                RestoreMetallurgyPlayerState(context.Player);
                throw new InvalidOperationException("粗铁镐无法进入真实玩家背包，不能验证存档边界。");
            }

            metallurgyBag.RefreshUI();
            metallurgyCompleted = true;
            Debug.Log(
                $"[GoldenPath][Metallurgy] 冶炼、粗铁镐制作、成功信号和进度写入通过：" +
                $"smelt={GoldenRawIronRecipeId}, craft={GoldenRawIronPickaxeRecipeId}。");
        }

        private static ItemData RunRawIronSmeltingTransaction(
            Player actor,
            RuntimeRecipe recipe,
            int requiredRawIron)
        {
            if (ItemMgr.Instance == null)
                throw new InvalidOperationException("首铁场景缺少 ItemMgr，无法实例化真实高炉。");
            if (requiredRawIron <= 0)
                throw new InvalidOperationException("粗铁镐配方没有声明粗铁锭消耗。");

            metallurgyFurnaceItem = ItemMgr.Instance.InstantiateItem(
                "BlastFurnace",
                actor.transform.position + Vector3.right * 2f,
                Quaternion.identity,
                Vector3.one);
            if (metallurgyFurnaceItem == null)
                throw new InvalidOperationException("真实高炉实例化失败。");

            metallurgyFurnaceItem.Load();
            Mod_Furnace furnace = metallurgyFurnaceItem.itemMods?.
                GetMod_ByID<Mod_Furnace>(ModText.Furnace);
            if (furnace?.InputInventory?.Data?.itemSlots == null ||
                furnace.OutputInventory?.Data?.itemSlots == null)
            {
                throw new InvalidOperationException("真实高炉缺少已初始化的熔炉模块或输入输出库存。");
            }

            ItemData ore = GameRes.Instance.CreateItemData(GoldenIronOreId);
            if (ore?.Stack == null)
                throw new InvalidOperationException($"无法创建冶炼材料 {GoldenIronOreId}。");
            ore.Stack.Amount = requiredRawIron;
            furnace.InputInventory.Data.itemSlots[0].itemData = ore;

            Inventory matchInventory = new()
            {
                Data = CreateGoldenCraftInventoryData("GoldenPath.Metallurgy.Smelting.Match", 1)
            };
            matchInventory.Data.itemSlots[0].itemData = FastCloner.FastCloner.DeepClone(ore);
            var capabilities = new CraftingCapabilities
            {
                RecipeType = RecipeType.Smelting,
                InputSlotLimit = 1,
                MaxRecipeWidth = 1,
                MaxRecipeHeight = 1,
                AllowCompactGrid = false,
                AllowOutputIntoInput = false
            };
            if (!CraftingRecipeMatcher.TryMatchRecipe(
                    matchInventory,
                    recipe,
                    capabilities,
                    out CraftingRecipeMatch match) ||
                match == null)
            {
                throw new InvalidOperationException("正式冶炼配方无法匹配真实高炉中的铁矿输入。");
            }

            furnace.Data.Temperature = recipe.Temperature;
            for (int index = 0; index < requiredRawIron; index++)
                furnace.CompleteSmelting(actor);

            bool inputConsumed = furnace.InputInventory.Data.itemSlots.All(slot =>
                !string.Equals(slot?.itemData?.IDName, GoldenIronOreId, StringComparison.Ordinal));
            List<ItemSlot> outputSlots = furnace.OutputInventory.Data.itemSlots
                .Where(slot => string.Equals(
                    slot?.itemData?.IDName,
                    GoldenRawIronId,
                    StringComparison.Ordinal))
                .ToList();
            ItemData rawIron = outputSlots
                .Select(slot => slot?.itemData)
                .FirstOrDefault();
            float outputAmount = outputSlots.Sum(slot => slot.itemData.Stack?.Amount ?? 0f);
            if (!inputConsumed || rawIron?.Stack == null || outputAmount < requiredRawIron)
                throw new InvalidOperationException("真实高炉事务没有原子扣除铁矿并产出粗铁锭。");

            ItemData transferredBatch = FastCloner.FastCloner.DeepClone(rawIron);
            transferredBatch.Stack.Amount = outputAmount;
            for (int index = 0; index < outputSlots.Count; index++)
                outputSlots[index].itemData = null;

            DespawnMetallurgyFurnace();
            return transferredBatch;
        }

        private static void RunRawIronPickaxeCraftingTransaction(
            Player actor,
            RuntimeRecipe recipe,
            ItemData rawIronBatch)
        {
            metallurgyCraftInput = new Inventory
            {
                Data = CreateGoldenCraftInventoryData("GoldenPath.Metallurgy.Crafting.Input", 9)
            };
            metallurgyCraftOutput = new Inventory
            {
                Data = CreateGoldenCraftInventoryData("GoldenPath.Metallurgy.Crafting.Output", 2)
            };

            for (int index = 0; index < recipe.inputs.RowItems_List.Count; index++)
            {
                RuntimeRecipeIngredient ingredient = recipe.inputs.RowItems_List[index];
                if (ingredient == null || ingredient.amount <= 0f || string.IsNullOrWhiteSpace(ingredient.ItemName))
                    continue;

                ItemData input;
                if (string.Equals(ingredient.ItemName, GoldenRawIronId, StringComparison.Ordinal))
                {
                    if (rawIronBatch?.Stack == null || rawIronBatch.Stack.Amount < ingredient.amount)
                        throw new InvalidOperationException("真实高炉产出的粗铁不足以制作粗铁镐。");

                    input = FastCloner.FastCloner.DeepClone(rawIronBatch);
                    input.Stack.Amount = ingredient.amount;
                    rawIronBatch.Stack.Amount -= ingredient.amount;
                }
                else
                {
                    input = GameRes.Instance.CreateItemData(ingredient.ItemName);
                }
                if (input?.Stack == null)
                    throw new InvalidOperationException($"无法创建粗铁镐材料 {ingredient.ItemName}。");
                if (!string.Equals(ingredient.ItemName, GoldenRawIronId, StringComparison.Ordinal))
                    input.Stack.Amount = ingredient.amount;
                metallurgyCraftInput.Data.itemSlots[index].itemData = input;
            }

            var capabilities = new CraftingCapabilities
            {
                RecipeType = RecipeType.Crafting,
                InputSlotLimit = 9,
                MaxRecipeWidth = 3,
                MaxRecipeHeight = 3,
                AllowCompactGrid = false,
                AllowOutputIntoInput = false
            };
            CraftingResult result = CraftingService.Craft(
                metallurgyCraftInput,
                metallurgyCraftOutput,
                capabilities,
                actor);
            if (!result.Success ||
                !string.Equals(result.Recipe?.Id, GoldenRawIronPickaxeRecipeId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"正式粗铁镐制作失败：reason={result.FailureReason}, message={result.Message}, " +
                    $"recipe={result.Recipe?.Id ?? "<null>"}。");
            }
        }

        #endregion

        #region 断言与清理

        private static void AssertMetallurgyProgressionBeforeWorldExit(
            FlatWorldGoldenPathScenarioContext context)
        {
            if (!metallurgyCompleted)
                throw new InvalidOperationException("首铁纵向场景没有完成。");
            if (metallurgyBag?.Data?.itemSlots == null ||
                !metallurgyBag.Data.itemSlots.Any(slot => string.Equals(
                    slot?.itemData?.IDName,
                    GoldenRawIronPickaxeId,
                    StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("退出世界前真实玩家背包缺少粗铁镐。");
            }
        }

        private static void AssertMetallurgyProgressionAfterWorldReentry(
            FlatWorldGoldenPathScenarioContext context)
        {
            PlayerMetallurgyProgressStore progress = new(context.Player.Data);
            if (!progress.FirstRawIronSmelted || !progress.FirstMetalToolCrafted)
                throw new InvalidOperationException("磁盘保存并 ContinueGame 后首铁里程碑丢失。");

            Mod_Inventory bagModule = context.Player.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag);
            Inventory bag = bagModule?.inventory;
            if (bag?.Data?.itemSlots == null ||
                !bag.Data.itemSlots.Any(slot => string.Equals(
                    slot?.itemData?.IDName,
                    GoldenRawIronPickaxeId,
                    StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("磁盘保存并 ContinueGame 后粗铁镐没有恢复到玩家背包。");
            }

            metallurgyReentryVerified = true;
            RestoreMetallurgyPlayerState(context.Player);
            Debug.Log("[GoldenPath][Metallurgy] 首铁里程碑与粗铁镐通过真实保存退出和 ContinueGame 重进。");
        }

        private static void CleanupMetallurgyProgressionOperation(
            FlatWorldGoldenPathScenarioContext context)
        {
            RestoreMetallurgyPlayerState(context.Player != null ? context.Player : metallurgyActor);
            DespawnMetallurgyFurnace();
            metallurgyCraftInput = null;
            metallurgyCraftOutput = null;
            if (metallurgyCompleted && !metallurgyReentryVerified)
                Debug.LogWarning("[GoldenPath][Metallurgy] 场景已完成，但重进断言尚未执行。");
        }

        /// <summary>移除仅供本场景使用的高炉，绝不写入区块或对象池存档。</summary>
        private static void DespawnMetallurgyFurnace()
        {
            if (metallurgyFurnaceItem != null && !metallurgyFurnaceItem.DestructionHandled &&
                ItemMgr.Instance != null)
            {
                ItemMgr.Instance.DespawnItem(
                    metallurgyFurnaceItem,
                    saveData: false,
                    detachFromChunk: false);
            }
            metallurgyFurnaceItem = null;
        }

        private static void RestoreMetallurgyPlayerState(Player player)
        {
            if (metallurgyInventoryRestored || metallurgyOriginalBagData == null || player?.Data == null)
                return;

            Mod_Inventory bagModule = player.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag);
            Inventory bag = bagModule?.inventory;
            if (bag == null)
                throw new InvalidOperationException("首铁场景清理时无法定位当前玩家背包。");

            Inventory_Data restoredBagData =
                FastCloner.FastCloner.DeepClone(metallurgyOriginalBagData);
            bag.Data = restoredBagData;
            if (bagModule.Data?.Data != null)
                bagModule.Data.Data[restoredBagData.Name] = restoredBagData;
            player.Data.ItemSpecialData = metallurgyOriginalItemSpecialData;
            bag.RefreshUI();
            metallurgyInventoryRestored = true;
        }

        private static RuntimeRecipe RequireMetallurgyRecipe(
            string recipeId,
            RecipeType recipeType,
            string outputItemId)
        {
            RuntimeRecipe recipe = GameRes.Instance.GetRecipe(recipeId);
            if (recipe?.inputs == null || recipe.inputs.recipeType != recipeType ||
                recipe.outputs?.results == null ||
                !recipe.outputs.results.Any(output => string.Equals(
                    output?.ItemName,
                    outputItemId,
                    StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"正式配方目录缺少有效的 {recipeId} → {outputItemId}。");
            }
            return recipe;
        }

        private static void ClearMetallurgyProgress(Data_Player playerData)
        {
            ItemSpecialDataJsonStore.WriteNamespace(
                playerData,
                PlayerMetallurgyProgressStore.NamespaceKey,
                new Newtonsoft.Json.Linq.JObject());
        }

        private static void ObserveMetallurgySmeltingSignal(Player actor, string outputItemId)
        {
            if (IsMetallurgyActor(actor) &&
                string.Equals(outputItemId, GoldenRawIronId, StringComparison.Ordinal))
                metallurgySmeltingSignalObserved = true;
        }

        private static void ObserveMetallurgyCraftingSignal(Player actor, string outputItemId)
        {
            if (IsMetallurgyActor(actor) &&
                string.Equals(outputItemId, GoldenRawIronPickaxeId, StringComparison.Ordinal))
                metallurgyCraftingSignalObserved = true;
        }

        /// <summary>只接受本场景真实玩家发出的进度，避免其他本地实体造成假阳性。</summary>
        private static bool IsMetallurgyActor(Player actor)
        {
            return actor != null &&
                   (ReferenceEquals(actor, metallurgyActor) ||
                    metallurgyActorGuid != 0 && actor.Data?.Guid == metallurgyActorGuid);
        }

        #endregion
    }
}
