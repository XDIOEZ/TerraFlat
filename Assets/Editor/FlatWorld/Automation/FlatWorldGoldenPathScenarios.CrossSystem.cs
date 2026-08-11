using System;
using System.Collections.Generic;
using System.Linq;
using FastCloner.Code;
using FlatWorld.Audio;
using FlatWorld.Dialogue;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Automation
{
    /// <summary>
    /// 黄金路径跨系统真实操作。所有操作只调用生产公开 API，并在退出世界前恢复玩家、环境、
    /// UI、音频和临时实体；关键数值固定，避免随机输入与不受控等待造成偶发失败。
    /// </summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        #region 背包与制作

        private const string GoldenCraftRecipeId = "core:打制石器";
        private const string GoldenCraftMaterialId = "Ore_Stone";
        private const string GoldenCraftOutputId = "ChippedTool";
        private static Mod_Inventory crossSystemBagModule;
        private static Inventory crossSystemBag;
        private static Inventory_Data crossSystemOriginalBagData;
        private static Inventory crossSystemCraftInput;
        private static Inventory crossSystemCraftOutput;
        private static bool crossSystemCraftingCompleted;
        private static bool crossSystemInventoryRestored;

        private static IFlatWorldGoldenPathOperation CreateInventoryCraftingOperation() =>
            new FlatWorldGoldenPathOperation(
                "inventory.crafting", "inventory-crafting",
                reset: ResetInventoryCraftingOperation,
                onWorldReady: RunInventoryCraftingOperation,
                beforeWorldExit: _ => AssertInventoryCraftingOperationCompleted(),
                cleanup: _ => CleanupInventoryCraftingOperation());

        /// <summary>重置真实背包与制作事务场景。</summary>
        private static void ResetInventoryCraftingOperation()
        {
            crossSystemBagModule = null;
            crossSystemBag = null;
            crossSystemOriginalBagData = null;
            crossSystemCraftInput = null;
            crossSystemCraftOutput = null;
            crossSystemCraftingCompleted = false;
            crossSystemInventoryRestored = false;
        }

        /// <summary>使用正式配方目录制作物品，再把产物放入真实玩家背包。</summary>
        private static void RunInventoryCraftingOperation(FlatWorldGoldenPathScenarioContext context)
        {
            if (context.Player == null || GameRes.Instance == null)
                throw new InvalidOperationException("背包制作操作缺少真实玩家或 GameRes。");

            crossSystemBagModule = context.Player.itemMods?.
                GetMod_ByID<Mod_Inventory>(ModText.Bag);
            crossSystemBag = crossSystemBagModule?.inventory;
            if (crossSystemBag?.Data?.itemSlots == null || crossSystemBag.Data.itemSlots.Count == 0)
                throw new InvalidOperationException("真实玩家背包模块或槽位未初始化。");

            crossSystemOriginalBagData =
                FastCloner.FastCloner.DeepClone(crossSystemBag.Data);
            crossSystemInventoryRestored = false;

            RuntimeRecipe recipe = GameRes.Instance.GetRecipe(GoldenCraftRecipeId);
            if (recipe == null)
                throw new InvalidOperationException($"正式配方目录缺少 {GoldenCraftRecipeId}。");

            crossSystemCraftInput = new Inventory
            {
                Data = CreateGoldenCraftInventoryData("GoldenPath.Crafting.Input", 4)
            };
            crossSystemCraftOutput = new Inventory
            {
                Data = CreateGoldenCraftInventoryData("GoldenPath.Crafting.Output", 2)
            };

            ItemData firstStone = GameRes.Instance.CreateItemData(GoldenCraftMaterialId);
            ItemData secondStone = GameRes.Instance.CreateItemData(GoldenCraftMaterialId);
            if (firstStone?.Stack == null || secondStone?.Stack == null)
                throw new InvalidOperationException($"无法创建制作材料 {GoldenCraftMaterialId}。");
            firstStone.Stack.Amount = 1f;
            secondStone.Stack.Amount = 1f;
            crossSystemCraftInput.Data.itemSlots[0].itemData = firstStone;
            crossSystemCraftInput.Data.itemSlots[3].itemData = secondStone;

            var capabilities = new CraftingCapabilities
            {
                RecipeType = RecipeType.Crafting,
                InputSlotLimit = 4,
                MaxRecipeWidth = 2,
                MaxRecipeHeight = 2,
                AllowCompactGrid = false,
                AllowOutputIntoInput = false
            };
            CraftingResult result = CraftingService.Craft(
                crossSystemCraftInput,
                crossSystemCraftOutput,
                capabilities,
                context.Player);
            if (!result.Success || result.Recipe == null ||
                !string.Equals(result.Recipe.Id, GoldenCraftRecipeId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"正式制作事务失败：reason={result.FailureReason}, message={result.Message}, " +
                    $"recipe={result.Recipe?.Id ?? "<null>"}。");
            }

            ItemData crafted = crossSystemCraftOutput.Data.itemSlots
                .Select(slot => slot?.itemData)
                .FirstOrDefault(item => item != null &&
                                        string.Equals(item.IDName, GoldenCraftOutputId,
                                            StringComparison.Ordinal));
            if (crafted == null)
                throw new InvalidOperationException($"制作成功后输出库存缺少 {GoldenCraftOutputId}。");

            // 隔离存档允许临时腾出末槽；退出前会按深拷贝完整恢复原背包。
            int lastIndex = crossSystemBag.Data.itemSlots.Count - 1;
            crossSystemBag.Data.itemSlots[lastIndex].itemData = null;
            ItemData playerCopy = FastCloner.FastCloner.DeepClone(crafted);
            if (!crossSystemBag.Data.TryAddItem(playerCopy, true) ||
                !crossSystemBag.Data.itemSlots.Any(slot =>
                    string.Equals(slot?.itemData?.IDName, GoldenCraftOutputId,
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("制作产物无法进入真实玩家背包。");
            }

            crossSystemBag.RefreshUI();
            crossSystemCraftingCompleted = true;
            Debug.Log(
                $"[GoldenPath][InventoryCrafting] 正式配方制作并写入玩家背包通过：" +
                $"recipe={GoldenCraftRecipeId}, output={GoldenCraftOutputId}。");
        }

        private static Inventory_Data CreateGoldenCraftInventoryData(string name, int slotCount)
        {
            var slots = new List<ItemSlot>(slotCount);
            for (int index = 0; index < slotCount; index++)
                slots.Add(new ItemSlot(index) { SlotMaxVolume = 100f });
            return new Inventory_Data(slots, name);
        }

        private static void AssertInventoryCraftingOperationCompleted()
        {
            if (!crossSystemCraftingCompleted)
                throw new InvalidOperationException("背包制作操作未完成。");
            RestoreInventoryCraftingPlayerState();
        }

        private static void CleanupInventoryCraftingOperation()
        {
            RestoreInventoryCraftingPlayerState();
            crossSystemCraftInput = null;
            crossSystemCraftOutput = null;
        }

        private static void RestoreInventoryCraftingPlayerState()
        {
            if (crossSystemInventoryRestored || crossSystemOriginalBagData == null ||
                crossSystemBag == null)
                return;

            crossSystemBag.Data = crossSystemOriginalBagData;
            if (crossSystemBagModule?.Data?.Data != null)
                crossSystemBagModule.Data.Data[crossSystemOriginalBagData.Name] =
                    crossSystemOriginalBagData;
            crossSystemBag.RefreshUI();
            crossSystemInventoryRestored = true;
        }

        #endregion

        #region 战斗目标

        private static Item crossSystemCombatTarget;
        private static bool crossSystemCombatCompleted;

        private static IFlatWorldGoldenPathOperation CreateCombatTargetDamageOperation() =>
            new FlatWorldGoldenPathOperation(
                "combat.target-damage", "combat",
                reset: ResetCombatTargetDamageOperation,
                onWorldReady: RunCombatTargetDamageOperation,
                beforeWorldExit: _ => AssertCombatTargetDamageOperationCompleted(),
                cleanup: _ => CleanupCombatTargetDamageOperation());

        private static void ResetCombatTargetDamageOperation()
        {
            crossSystemCombatTarget = null;
            crossSystemCombatCompleted = false;
        }

        /// <summary>生成正式生物并通过 DamageReceiver 完成一次可恢复伤害。</summary>
        private static void RunCombatTargetDamageOperation(FlatWorldGoldenPathScenarioContext context)
        {
            if (ItemMgr.Instance == null || context.Player == null)
                throw new InvalidOperationException("战斗目标操作缺少 ItemMgr 或玩家。");

            Vector3 position = context.Player.transform.position + Vector3.right * 2f;
            crossSystemCombatTarget = ItemMgr.Instance.InstantiateItem(
                "Chicken", position, Quaternion.identity, Vector3.one);
            if (crossSystemCombatTarget == null)
                throw new InvalidOperationException("无法生成战斗验证 Chicken。");
            crossSystemCombatTarget.Load();

            DamageReceiver receiver = crossSystemCombatTarget.GetComponentInChildren<DamageReceiver>(true);
            if (receiver == null || receiver.Hp <= 1f)
                throw new InvalidOperationException("战斗验证 Chicken 缺少可用 DamageReceiver。");

            float originalHp = receiver.Hp;
            float remaining = receiver.ForceHurt(1f);
            if (remaining >= originalHp || receiver.Hp >= originalHp)
                throw new InvalidOperationException("DamageReceiver 未应用真实伤害。");
            receiver.Heal(originalHp - receiver.Hp, context.Player);
            if (Mathf.Abs(receiver.Hp - originalHp) > 0.01f)
                throw new InvalidOperationException("战斗目标伤害后未通过生产治疗入口恢复。");

            crossSystemCombatCompleted = true;
            Debug.Log("[GoldenPath][Combat] 正式生物生成、受伤事件与治疗恢复通过。");
        }

        private static void AssertCombatTargetDamageOperationCompleted()
        {
            if (!crossSystemCombatCompleted)
                throw new InvalidOperationException("战斗目标操作未完成。");
        }

        private static void CleanupCombatTargetDamageOperation()
        {
            if (crossSystemCombatTarget != null && ItemMgr.Instance != null)
                ItemMgr.Instance.DespawnItem(crossSystemCombatTarget);
            crossSystemCombatTarget = null;
        }

        #endregion

        #region 背包 UI

        private static Inventory crossSystemInventoryPanelBag;
        private static GameController crossSystemInventoryPanelController;
        private static bool crossSystemInventoryPanelCompleted;

        private static IFlatWorldGoldenPathOperation CreateInventoryPanelOperation() =>
            new FlatWorldGoldenPathOperation(
                "ui.inventory-panel", "ui",
                reset: ResetInventoryPanelOperation,
                onWorldReady: RunInventoryPanelOperation,
                beforeWorldExit: _ =>
                {
                    if (!crossSystemInventoryPanelCompleted)
                        throw new InvalidOperationException("背包 UI 操作未完成。");
                },
                cleanup: _ => CleanupInventoryPanelOperation());

        /// <summary>重置背包 UI 与输入锁测试状态。</summary>
        private static void ResetInventoryPanelOperation()
        {
            crossSystemInventoryPanelBag = null;
            crossSystemInventoryPanelController = null;
            crossSystemInventoryPanelCompleted = false;
        }

        /// <summary>通过真实背包入口开关面板，并恢复测试前状态。</summary>
        private static void RunInventoryPanelOperation(FlatWorldGoldenPathScenarioContext context)
        {
            Mod_Inventory bagModule = context.Player?.itemMods?.
                GetMod_ByID<Mod_Inventory>(ModText.Bag);
            Inventory bag = bagModule?.inventory;
            GameController controller = context.Player?.itemMods?.
                GetMod_ByID<GameController>(ModText.Controller);
            if (bag == null || controller == null)
                throw new InvalidOperationException("真实玩家背包或输入控制器不存在。");

            crossSystemInventoryPanelBag = bag;
            crossSystemInventoryPanelController = controller;
            if (bag.basePanel != null && bag.basePanel.IsOpen())
                bag.basePanel.Close();
            if (controller.IsGameplayInputLocked)
                throw new InvalidOperationException(
                    $"背包 UI 测试前玩家输入已被锁定：{controller.DescribeGameplayInputLockState()}");

            bag.SwitchUI();
            if (bag.basePanel == null)
                throw new InvalidOperationException("真实玩家背包面板无法创建。");
            if (!bag.basePanel.IsOpen() || !controller.IsGameplayInputLocked)
                throw new InvalidOperationException("背包面板打开后未正确锁定玩家输入。");

            bag.SwitchUI();
            if (bag.basePanel.IsOpen() || controller.IsGameplayInputLocked)
                throw new InvalidOperationException("背包面板关闭后未正确释放玩家输入。");

            crossSystemInventoryPanelCompleted = true;
            Debug.Log("[GoldenPath][UI] 真实玩家背包面板开关与输入锁释放通过。");
        }

        /// <summary>无论断言是否失败，都关闭面板并释放本背包的输入锁。</summary>
        private static void CleanupInventoryPanelOperation()
        {
            if (crossSystemInventoryPanelBag?.basePanel != null &&
                crossSystemInventoryPanelBag.basePanel.IsOpen())
            {
                crossSystemInventoryPanelBag.basePanel.Close();
            }

            crossSystemInventoryPanelController?.ReleaseGameplayInputLock(
                crossSystemInventoryPanelBag);
            crossSystemInventoryPanelBag = null;
            crossSystemInventoryPanelController = null;
        }

        #endregion

        #region 音频

        private static AudioHandle crossSystemAudioHandle;
        private static bool crossSystemAudioCompleted;

        private static IFlatWorldGoldenPathOperation CreateAudioPlaybackOperation() =>
            new FlatWorldGoldenPathOperation(
                "audio.cue-playback", "audio",
                reset: ResetAudioPlaybackOperation,
                onWorldReady: RunAudioPlaybackOperation,
                beforeWorldExit: _ =>
                {
                    if (!crossSystemAudioCompleted)
                        throw new InvalidOperationException("音频播放操作未完成。");
                },
                cleanup: _ => CleanupAudioPlaybackOperation());

        private static void ResetAudioPlaybackOperation()
        {
            crossSystemAudioHandle = AudioHandle.Invalid;
            crossSystemAudioCompleted = false;
        }

        /// <summary>通过 AudioService 播放并停止正式 UI Cue，验证句柄与声源池链路。</summary>
        private static void RunAudioPlaybackOperation(FlatWorldGoldenPathScenarioContext context)
        {
            AudioService service = AudioService.Instance;
            if (service == null || !service.TryGetCue(AudioEventIds.UiClick, out AudioCue cue) ||
                cue == null)
                throw new InvalidOperationException("AudioService 缺少 ui.click Cue。");

            crossSystemAudioHandle = service.Play(cue, AudioPlayOptions.Global());
            if (!crossSystemAudioHandle.IsValid ||
                !AudioService.IsHandlePlaying(crossSystemAudioHandle))
                throw new InvalidOperationException("AudioService 未创建有效的 UI Cue 播放句柄。");

            service.Stop(crossSystemAudioHandle);
            if (AudioService.IsHandlePlaying(crossSystemAudioHandle))
                throw new InvalidOperationException("AudioService 停止后播放句柄仍处于活动状态。");

            crossSystemAudioCompleted = true;
            Debug.Log("[GoldenPath][Audio] UI Cue 路由、播放句柄与声源回收通过。");
        }

        private static void CleanupAudioPlaybackOperation()
        {
            if (crossSystemAudioHandle.IsValid && AudioService.HasInstance)
                AudioService.Instance.Stop(crossSystemAudioHandle);
            crossSystemAudioHandle = AudioHandle.Invalid;
        }

        #endregion

        #region 对话

        private static bool crossSystemDialogueCompleted;

        private static IFlatWorldGoldenPathOperation CreateDialogueSpeechOperation() =>
            new FlatWorldGoldenPathOperation(
                "dialogue.player-speech", "dialogue",
                reset: () => crossSystemDialogueCompleted = false,
                onWorldReady: RunDialogueSpeechOperation,
                beforeWorldExit: _ =>
                {
                    if (!crossSystemDialogueCompleted)
                        throw new InvalidOperationException("角色对话操作未完成。");
                });

        /// <summary>通过正式自言自语调度入口显示一次确定性测试气泡。</summary>
        private static void RunDialogueSpeechOperation(FlatWorldGoldenPathScenarioContext context)
        {
            CharacterSoliloquyController controller = context.Player?
                .GetComponentInChildren<CharacterSoliloquyController>(true);
            if (controller == null)
            {
                controller = UnityEngine.Object.FindObjectsByType<CharacterSoliloquyController>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(candidate => candidate != null &&
                                                 candidate.gameObject.scene.IsValid());
            }
            if (controller == null)
                throw new InvalidOperationException("运行时世界缺少 CharacterSoliloquyController。");

            bool shown = false;
            void HandleSpeech(CharacterSpeechRequest _) => shown = true;
            controller.SpeechShown += HandleSpeech;
            try
            {
                if (!controller.Say(
                        "黄金路径：角色气泡系统运行正常。",
                        CharacterSpeechPriority.Critical,
                        0.5f,
                        "golden-path") || !shown)
                {
                    throw new InvalidOperationException(
                        "CharacterSoliloquyController 未通过 Presenter 显示测试气泡。");
                }
            }
            finally
            {
                controller.SpeechShown -= HandleSpeech;
            }

            crossSystemDialogueCompleted = true;
            Debug.Log("[GoldenPath][Dialogue] 角色发言请求、Presenter 与显示事件通过。");
        }

        #endregion

        #region 环境时间与天气

        private static bool crossSystemEnvironmentCompleted;

        private static IFlatWorldGoldenPathOperation CreateEnvironmentTimeWeatherOperation() =>
            new FlatWorldGoldenPathOperation(
                "environment.time-weather", "environment",
                reset: () => crossSystemEnvironmentCompleted = false,
                onWorldReady: RunEnvironmentTimeWeatherOperation,
                beforeWorldExit: _ =>
                {
                    if (!crossSystemEnvironmentCompleted)
                        throw new InvalidOperationException("环境时间与天气操作未完成。");
                });

        /// <summary>推进真实世界时间并切换雨天，完成断言后恢复原环境。</summary>
        private static void RunEnvironmentTimeWeatherOperation(
            FlatWorldGoldenPathScenarioContext context)
        {
            DayTimeSystem dayTime = DayTimeSystem.Instance;
            WeatherMgr weather = WeatherMgr.Instance;
            string sceneName = SceneManager.GetActiveScene().name;
            if (dayTime == null || weather == null ||
                !dayTime.TryGetResolvedTimeData(sceneName, out _, out _))
                throw new InvalidOperationException("环境操作缺少 DayTimeSystem、WeatherMgr 或场景时间数据。");

            float originalTime = dayTime.GetCurrentTime(sceneName);
            WeatherType originalWeather = weather.CurrentWeather;
            float originalIntensity = weather.CurrentWeatherIntensity;
            try
            {
                dayTime.AdvanceTime(sceneName, 60f);
                float advanced = dayTime.GetCurrentTime(sceneName);
                if (Mathf.Approximately(advanced, originalTime))
                    throw new InvalidOperationException("DayTimeSystem.AdvanceTime 未推进世界时间。");

                weather.SetRain(0.75f);
                if (!weather.IsRaining() || weather.CurrentWeatherIntensity <= 0f)
                    throw new InvalidOperationException("WeatherMgr.SetRain 未进入有效雨天状态。");
            }
            finally
            {
                dayTime.JumpToTime(sceneName, originalTime);
                weather.SetWeather(originalWeather, originalIntensity);
            }

            crossSystemEnvironmentCompleted = true;
            Debug.Log("[GoldenPath][Environment] 世界时间推进、雨天切换与环境恢复通过。");
        }

        #endregion

        #region 导航

        private static WorldNavigationManager crossSystemNavigationManager;
        private static int crossSystemNavigationRequestId;
        private static bool crossSystemNavigationCompleted;
        private static WorldNavigationPathResult crossSystemNavigationResult;

        private static IFlatWorldGoldenPathOperation CreateNavigationLoadedGridOperation() =>
            new FlatWorldGoldenPathOperation(
                "navigation.loaded-grid", "navigation",
                reset: ResetNavigationLoadedGridOperation,
                onWorldReady: BeginNavigationLoadedGridOperation,
                tickWorldReady: _ => TickNavigationLoadedGridOperation(),
                beforeWorldExit: _ => AssertNavigationLoadedGridOperationCompleted(),
                cleanup: _ => CleanupNavigationLoadedGridOperation());

        private static void ResetNavigationLoadedGridOperation()
        {
            crossSystemNavigationManager = null;
            crossSystemNavigationRequestId = 0;
            crossSystemNavigationCompleted = false;
            crossSystemNavigationResult = default;
        }

        /// <summary>在已加载真实导航网格中请求一条玩家周边路径。</summary>
        private static void BeginNavigationLoadedGridOperation(
            FlatWorldGoldenPathScenarioContext context)
        {
            crossSystemNavigationManager = WorldNavigationManager.ExistingInstance;
            if (crossSystemNavigationManager == null ||
                !crossSystemNavigationManager.IsNavigationReady || context.Player == null)
                throw new InvalidOperationException("真实世界导航管理器或网格尚未就绪。");

            Vector2 start = context.Player.transform.position;
            Vector2Int startCell = WorldNavigationGrid.WorldToCell(start);
            Vector2Int goalCell = default;
            bool found = false;
            for (int radius = 4; radius <= 10 && !found; radius++)
            {
                Vector2Int[] candidates =
                {
                    startCell + new Vector2Int(radius, 0),
                    startCell + new Vector2Int(-radius, 0),
                    startCell + new Vector2Int(0, radius),
                    startCell + new Vector2Int(0, -radius)
                };
                foreach (Vector2Int candidate in candidates)
                {
                    if (!crossSystemNavigationManager.Grid.IsWalkable(candidate))
                        continue;
                    goalCell = candidate;
                    found = true;
                    break;
                }
            }
            if (!found)
                throw new InvalidOperationException("玩家周边十格内找不到可走导航目标。");

            crossSystemNavigationRequestId = crossSystemNavigationManager.RequestPath(
                start,
                WorldNavigationGrid.CellCenter(goalCell),
                result =>
                {
                    crossSystemNavigationResult = result;
                    crossSystemNavigationCompleted = true;
                    crossSystemNavigationRequestId = 0;
                });
            if (crossSystemNavigationRequestId <= 0)
                throw new InvalidOperationException("WorldNavigationManager 未接受路径请求。");
        }

        private static bool TickNavigationLoadedGridOperation()
        {
            if (!crossSystemNavigationCompleted)
                return false;
            if (!crossSystemNavigationResult.Success ||
                !crossSystemNavigationResult.ReachesDestination ||
                crossSystemNavigationResult.Waypoints == null ||
                crossSystemNavigationResult.Waypoints.Length == 0)
            {
                throw new InvalidOperationException(
                    $"真实导航路径失败：success={crossSystemNavigationResult.Success}, " +
                    $"reaches={crossSystemNavigationResult.ReachesDestination}, " +
                    $"waypoints={crossSystemNavigationResult.Waypoints?.Length ?? 0}。");
            }

            Debug.Log(
                $"[GoldenPath][Navigation] 已加载网格路径通过：" +
                $"waypoints={crossSystemNavigationResult.Waypoints.Length}, " +
                $"revision={crossSystemNavigationResult.GridRevision}。");
            return true;
        }

        private static void AssertNavigationLoadedGridOperationCompleted()
        {
            if (!crossSystemNavigationCompleted || !crossSystemNavigationResult.Success)
                throw new InvalidOperationException("真实导航路径操作未完成。");
        }

        private static void CleanupNavigationLoadedGridOperation()
        {
            if (crossSystemNavigationRequestId > 0 && crossSystemNavigationManager != null)
                crossSystemNavigationManager.CancelPath(crossSystemNavigationRequestId);
            crossSystemNavigationRequestId = 0;
            crossSystemNavigationManager = null;
        }

        #endregion
    }
}
