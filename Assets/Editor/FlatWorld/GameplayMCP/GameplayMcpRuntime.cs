using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// GamePlayMCP 的 Editor 运行时适配器。
    /// 只编排公开生产 API，并把高频世界状态压缩成结构化数据，避免视觉模型参与常规测试循环。
    /// </summary>
    internal static class GameplayMcpRuntime
    {
        public const string ProtocolVersion = "0.8.1";
        public const string ExtensionPath = "Assets/Editor/FlatWorld/GameplayMCP/";

        private static readonly object ControlOwner = new GameplayMcpControlOwner();

        private sealed class GameplayMcpControlOwner
        {
        }

        #region 玩家与控制租约

        /// <summary>尝试解析当前真实本地玩家及核心控制模块。</summary>
        public static bool TryGetPlayerContext(
            out Player player,
            out GameController controller,
            out Mover mover,
            out string error)
        {
            player = null;
            controller = null;
            mover = null;
            error = string.Empty;

            if (!Application.isPlaying)
            {
                error = "not_playing: Unity 必须处于 Play Mode。";
                return false;
            }

            GameManager gameManager = GameManager.Instance;
            if (gameManager == null || !gameManager.IsInGameWorld || !gameManager.IsGameplayReady)
            {
                error = "world_not_ready: 当前尚未进入可玩的游戏世界。";
                return false;
            }

            player = ItemMgr.Instance?.User_Player;
            if (player == null || player.itemMods == null)
            {
                error = "player_not_ready: 当前本地玩家尚未创建或模块未加载。";
                return false;
            }

            controller = player.itemMods.GetMod_ByID<GameController>(ModText.Controller);
            mover = player.itemMods.GetMod_ByID<Mover>(ModText.Mover);
            if (controller == null || mover == null || mover.rb == null)
            {
                error = "player_control_missing: 玩家缺少 GameController 或可用 Mover。";
                return false;
            }

            return true;
        }

        /// <summary>确保 GamePlayMCP 持有玩家的唯一外部控制租约。</summary>
        public static bool TryEnsureControl(
            out Player player,
            out GameController controller,
            out Mover mover,
            out string error)
        {
            if (!TryGetPlayerContext(out player, out controller, out mover, out error))
                return false;

            if (controller.IsExternalGameplayControlOwner(ControlOwner))
                return true;

            if (!controller.TryAcquireExternalGameplayControl(ControlOwner))
            {
                error = $"control_busy: 外部控制租约已被 {controller.ExternalGameplayControlOwnerName} 持有。";
                return false;
            }

            return true;
        }

        /// <summary>释放 GamePlayMCP 持有的外部控制租约。</summary>
        public static bool TryReleaseControl(out string error)
        {
            error = string.Empty;
            if (!TryGetPlayerContext(out _, out GameController controller, out _, out error))
                return false;

            if (!controller.IsExternalGameplayControlOwner(ControlOwner))
                return true;

            controller.TrySetExternalMoveInput(ControlOwner, Vector2.zero);
            controller.TrySetExternalAttackHeld(ControlOwner, false);
            controller.ReleaseExternalGameplayControl(ControlOwner);
            return true;
        }

        /// <summary>判断 GamePlayMCP 是否拥有当前玩家控制租约。</summary>
        public static bool OwnsControl(GameController controller)
        {
            return controller != null && controller.IsExternalGameplayControlOwner(ControlOwner);
        }

        #endregion

        #region 会话启动

        /// <summary>列出本机现有正式存档，供 Agent 选择自动测试基线。</summary>
        public static JArray ListSaves()
        {
            string saveDirectory = SaveDataMgr.GetDefaultSavePath();
            if (!Directory.Exists(saveDirectory))
                return new JArray();

            string[] files = Directory.GetFiles(saveDirectory, "*.bytes", SearchOption.TopDirectoryOnly);
            var ordered = files
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ToArray();

            var result = new JArray();
            for (int i = 0; i < ordered.Length; i++)
            {
                FileInfo info = ordered[i];
                result.Add(new JObject
                {
                    ["name"] = Path.GetFileNameWithoutExtension(info.Name),
                    ["lastWriteUtc"] = info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture),
                    ["bytes"] = info.Length
                });
            }

            return result;
        }

        /// <summary>
        /// 从指定或最近存档启动世界；默认复制到 Library 隔离目录，避免 Agent 游玩污染玩家正式存档。
        /// </summary>
        public static async Task<JObject> ContinueSaveAsync(string saveName, string playerName, bool isolated, float timeoutSeconds)
        {
            if (!Application.isPlaying)
                return BuildActionError("not_playing", "请先通过 Unity MCP 进入 Play Mode。", false);

            GameManager gameManager = GameManager.Instance;
            SaveDataMgr saveDataMgr = SaveDataMgr.Instance;
            if (gameManager == null || saveDataMgr == null)
                return BuildActionError("lifecycle_not_ready", "GameManager 或 SaveDataMgr 尚未就绪。", false);

            if (gameManager.IsInGameWorld)
            {
                return new JObject
                {
                    ["ok"] = true,
                    ["alreadyInWorld"] = true,
                    ["save"] = saveDataMgr.SaveData?.saveName ?? string.Empty,
                    ["player"] = saveDataMgr.CurrentContrrolPlayerName ?? string.Empty
                };
            }

            string isolatedDirectory = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Library",
                "FlatWorldGameplayMCP",
                "Saves");
            string existingIsolatedPath = isolated && !string.IsNullOrWhiteSpace(saveName)
                ? Path.Combine(isolatedDirectory, saveName.Trim() + ".bytes")
                : null;
            bool resumeExistingIsolated = !string.IsNullOrEmpty(existingIsolatedPath) &&
                                          File.Exists(existingIsolatedPath);
            string sourcePath = resumeExistingIsolated ? existingIsolatedPath : ResolveSavePath(saveName);
            if (string.IsNullOrEmpty(sourcePath))
                return BuildActionError("save_not_found", "没有找到可用于 GamePlayMCP 的存档。", false);

            string loadPath = sourcePath;
            string defaultSaveDirectory = SaveDataMgr.GetDefaultSavePath();
            if (isolated)
            {
                Directory.CreateDirectory(isolatedDirectory);
                if (!resumeExistingIsolated)
                {
                    string isolatedName = $"GameplayMCP_{Path.GetFileNameWithoutExtension(sourcePath)}";
                    loadPath = Path.Combine(isolatedDirectory, isolatedName + ".bytes");
                    File.Copy(sourcePath, loadPath, true);
                    CopyIfExists(sourcePath + ".bak", loadPath + ".bak");
                }
                saveDataMgr.UserSavePath = isolatedDirectory + Path.DirectorySeparatorChar;
            }
            else
            {
                // 同一 Play Mode 内允许从隔离会话切回正式路径时，必须显式恢复存档根目录。
                saveDataMgr.UserSavePath = defaultSaveDirectory;
            }

            saveDataMgr.LoadSaveByDisk(loadPath);
            if (saveDataMgr.SaveData == null)
                return BuildActionError("save_load_failed", $"存档加载失败：{loadPath}", false);

            if (isolated)
                saveDataMgr.SaveData.saveName = Path.GetFileNameWithoutExtension(loadPath);

            string resolvedPlayerName = ResolvePlayerName(saveDataMgr.SaveData, playerName);
            if (string.IsNullOrEmpty(resolvedPlayerName))
                return BuildActionError("player_not_found", "存档中没有可控制玩家。", false);

            gameManager.ContinueGame(resolvedPlayerName);
            // MCPForUnity stdio bridge 单次命令约 30 秒超时；会话动作必须提前结束并把控制权还给 Agent。
            timeoutSeconds = Mathf.Clamp(timeoutSeconds, 2f, 20f);
            bool ready = await WaitUntilAsync(
                () => gameManager != null &&
                      gameManager.IsInGameWorld &&
                      gameManager.IsGameplayReady &&
                      ItemMgr.Instance?.User_Player != null,
                timeoutSeconds);

            return new JObject
            {
                ["ok"] = ready,
                ["save"] = saveDataMgr.SaveData?.saveName ?? string.Empty,
                ["player"] = resolvedPlayerName,
                ["isolated"] = isolated,
                ["resumedIsolated"] = resumeExistingIsolated,
                ["ready"] = ready,
                ["code"] = ready ? "ready" : "world_entry_timeout"
            };
        }

        /// <summary>通过正式 GameManager 生命周期创建一个新世界并等待玩家进入可玩态。</summary>
        public static async Task<JObject> CreateWorldAsync(
            string saveName,
            string playerName,
            string seed,
            string worldName,
            string topology,
            int radius,
            float noiseScale,
            bool isolated,
            float timeoutSeconds)
        {
            if (!Application.isPlaying)
                return BuildActionError("not_playing", "请先通过 Unity MCP 进入 Play Mode。", false);

            GameManager gameManager = GameManager.Instance;
            SaveDataMgr saveDataMgr = SaveDataMgr.Instance;
            if (gameManager == null || saveDataMgr == null)
                return BuildActionError("lifecycle_not_ready", "GameManager 或 SaveDataMgr 尚未就绪。", false);
            if (gameManager.IsInGameWorld)
                return BuildActionError("already_in_world", "当前已经处于游戏世界中，请先保存退出。", false);
            if (gameManager.IsWorldEntryInProgress)
                return BuildActionError("world_entry_in_progress", "世界仍在加载，请查询会话状态，不要重复创建。", false);

            timeoutSeconds = Mathf.Clamp(timeoutSeconds, 2f, 20f);
            double deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
            // 主菜单允许延迟创建 GameRes；显式走正式单例入口启动会话，不能只等待 ExistingInstance。
            GameRes resources = GameRes.Instance;
            if (resources == null)
                return BuildActionError("resources_unavailable", "游戏资源管理器不可用。", false);
            await WaitUntilAsync(
                () => resources == null || resources.isLoadFinish ||
                      resources.LoadState == ResourceLoadState.Failed ||
                      resources.LoadState == ResourceLoadState.Disposed,
                timeoutSeconds);
            if (resources == null || resources.LoadState == ResourceLoadState.Disposed)
                return BuildActionError("resources_unavailable", "资源会话已结束，请重新启动播放。", false);
            if (resources.LoadState == ResourceLoadState.Failed)
                return BuildActionError("resource_load_failed", resources.LastLoadError, false);
            if (!resources.isLoadFinish)
                return BuildActionError("resources_not_ready", "游戏资源未在限定时间内完成加载。", false);

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string resolvedSaveName = string.IsNullOrWhiteSpace(saveName)
                ? $"GameplayMCP_{timestamp}"
                : saveName.Trim();
            string resolvedPlayerName = string.IsNullOrWhiteSpace(playerName)
                ? "AgentPlayer"
                : playerName.Trim();
            string resolvedWorldName = string.IsNullOrWhiteSpace(worldName)
                ? resolvedSaveName
                : worldName.Trim();

            WorldTopologyMode topologyMode = string.Equals(
                topology?.Trim(),
                "infinite",
                StringComparison.OrdinalIgnoreCase)
                ? WorldTopologyMode.Infinite
                : WorldTopologyMode.Wrapped;
            var planetData = new PlanetData
            {
                Name = resolvedWorldName,
                Radius = Mathf.Max(1, radius),
                NoiseScale = PlanetData.NormalizeNoiseScale(noiseScale),
                TopologyMode = topologyMode
            };
            TimeData timeData = gameManager.ReadyTimeData?.CreateRuntimeCopy() ?? new TimeData();
            var request = new NewWorldCreationRequest(
                resolvedSaveName,
                resolvedPlayerName,
                seed,
                planetData,
                timeData,
                GameDifficultyId.Simple,
                null,
                GameRes.ExistingInstance?.TextLibraries);
            if (!request.TryValidate(out string validationError))
                return BuildActionError("invalid_world_request", validationError, false);

            // 新建与续档遵守同一个隔离开关；首个存档及后续自动保存都必须写到选定目录。
            string saveDirectory = isolated
                ? Path.Combine(Directory.GetCurrentDirectory(), "Library", "FlatWorldGameplayMCP", "Saves")
                : SaveDataMgr.GetDefaultSavePath();
            Directory.CreateDirectory(saveDirectory);
            string previousSavePath = saveDataMgr.UserSavePath;
            saveDataMgr.UserSavePath = saveDirectory + Path.DirectorySeparatorChar;
            if (!gameManager.CreateNewWorld(request))
            {
                saveDataMgr.UserSavePath = previousSavePath;
                return BuildActionError("create_world_rejected", "GameManager 拒绝了新世界请求。", false);
            }

            bool ready = await WaitUntilAsync(
                () => gameManager != null &&
                      gameManager.IsInGameWorld &&
                      gameManager.IsGameplayReady &&
                      ItemMgr.Instance?.User_Player != null,
                Mathf.Max(0f, (float)(deadline - Time.realtimeSinceStartupAsDouble)));

            return new JObject
            {
                ["ok"] = ready,
                ["ready"] = ready,
                ["code"] = ready ? "ready" : "world_entry_timeout",
                ["save"] = saveDataMgr.SaveData?.saveName ?? resolvedSaveName,
                ["player"] = saveDataMgr.CurrentContrrolPlayerName ?? resolvedPlayerName,
                ["world"] = resolvedWorldName,
                ["isolated"] = isolated,
                ["topology"] = topologyMode.ToString(),
                ["seed"] = saveDataMgr.SaveData?.SaveSeed ?? seed ?? string.Empty
            };
        }

        /// <summary>调用正式退出协程保存当前世界并返回 GameStartScene。</summary>
        public static async Task<JObject> SaveAndExitAsync(float timeoutSeconds)
        {
            if (!Application.isPlaying)
                return BuildActionError("not_playing", "Unity 当前不在 Play Mode。", false);

            GameManager gameManager = GameManager.Instance;
            Player player = ItemMgr.Instance?.User_Player;
            if (gameManager == null || player == null || !gameManager.IsInGameWorld)
                return BuildActionError("world_not_ready", "当前没有可保存退出的游戏世界。", false);

            string saveName = SaveDataMgr.Instance?.SaveData?.saveName ?? string.Empty;
            if (TryGetPlayerContext(out _, out GameController controller, out Mover mover, out _))
            {
                if (controller.IsExternalGameplayControlOwner(ControlOwner))
                {
                    controller.TrySetExternalMoveInput(ControlOwner, Vector2.zero);
                    controller.TrySetExternalAttackHeld(ControlOwner, false);
                    mover?.SetRunState(false);
                    controller.ReleaseExternalGameplayControl(ControlOwner);
                }
            }

            bool completed = false;
            gameManager.StartCoroutine(gameManager.BackToHelloScene_Coroutine(
                player,
                () => completed = true,
                saveCurrentGame: true));

            timeoutSeconds = Mathf.Clamp(timeoutSeconds, 2f, 20f);
            bool finished = await WaitUntilAsync(
                () => completed ||
                      (!gameManager.IsInGameWorld &&
                       string.Equals(SceneManager.GetActiveScene().name, "GameStartScene", StringComparison.Ordinal)),
                timeoutSeconds);

            return new JObject
            {
                ["ok"] = finished,
                ["saved"] = finished,
                ["save"] = saveName,
                ["scene"] = SceneManager.GetActiveScene().name,
                ["code"] = finished ? "saved_and_exited" : "exit_timeout"
            };
        }

        /// <summary>解析指定存档；未指定时使用最近修改的正式存档。</summary>
        private static string ResolveSavePath(string saveName)
        {
            string directory = SaveDataMgr.GetDefaultSavePath();
            if (!Directory.Exists(directory))
                return null;

            if (!string.IsNullOrWhiteSpace(saveName))
            {
                string path = Path.Combine(directory, saveName.Trim() + ".bytes");
                return File.Exists(path) ? path : null;
            }

            return Directory.GetFiles(directory, "*.bytes", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        /// <summary>优先使用调用方指定玩家，否则选择存档中的第一个稳定档案键。</summary>
        private static string ResolvePlayerName(GameSaveData saveData, string playerName)
        {
            if (saveData?.PlayerData_Dict == null || saveData.PlayerData_Dict.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(playerName) && saveData.PlayerData_Dict.ContainsKey(playerName))
                return playerName;

            return saveData.PlayerData_Dict.Keys.OrderBy(name => name, StringComparer.Ordinal).FirstOrDefault();
        }

        /// <summary>复制存在的伴随文件。</summary>
        private static void CopyIfExists(string source, string target)
        {
            if (File.Exists(source))
                File.Copy(source, target, true);
        }

        #endregion

        #region 世界观察

        /// <summary>构造供 Agent 高频消费的紧凑结构化观察结果。</summary>
        public static JObject BuildObservation(float radius, int maxEntities, bool includeInventory)
        {
            radius = Mathf.Clamp(radius, 1f, 64f);
            maxEntities = Mathf.Clamp(maxEntities, 1, 128);

            var root = new JObject
            {
                ["protocol"] = ProtocolVersion,
                ["playing"] = Application.isPlaying,
                ["frame"] = Time.frameCount,
                ["timeScale"] = Round(Time.timeScale),
                ["scene"] = SceneManager.GetActiveScene().name
            };

            if (!TryGetPlayerContext(out Player player, out GameController controller, out Mover mover, out string error))
            {
                root["ready"] = false;
                root["reason"] = error;
                return root;
            }

            root["ready"] = true;
            root["player"] = BuildPlayerObservation(player, controller, mover, includeInventory);
            root["terrain"] = BuildTerrainGridObservation(player.transform.position);
            root["drops"] = BuildNearbyDroppedItemObservation(player, radius, Mathf.Min(maxEntities, 16));
            root["nearby"] = BuildNearbyObservation(player, radius, maxEntities);
            return root;
        }

        /// <summary>构造玩家核心状态、快捷栏和可选库存摘要。</summary>
        private static JObject BuildPlayerObservation(
            Player player,
            GameController controller,
            Mover mover,
            bool includeInventory)
        {
            DamageReceiver health = player.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
            Mod_Stamina stamina = player.itemMods.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
            Mod_Oxygen oxygen = player.itemMods.GetMod_ByID<Mod_Oxygen>(ModText.Oxygen);
            Mod_Food food = player.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);
            Inventory_HotBar hotbar = ResolveHotbar(player);
            Mod_Hand handModule = player.GetComponentInChildren<Mod_Hand>(true);
            Inventory_Hand handInventory = handModule?.HandInventory;
            ItemSlot handSlot = handInventory?.Data?.itemSlots != null &&
                                handInventory.Data.Index >= 0 &&
                                handInventory.Data.Index < handInventory.Data.itemSlots.Count
                ? handInventory.Data.itemSlots[handInventory.Data.Index]
                : null;
            ItemData handItemData = handSlot?.itemData;
            Mod_ColdWeapon handWeapon = handItemData == null
                ? null
                : player.GetComponentsInChildren<Mod_ColdWeapon>(true)
                    .FirstOrDefault(module => module != null && module.item != null &&
                                              module.item.Owner == player &&
                                              module.item.itemData != null &&
                                              module.item.itemData.Guid == handItemData.Guid);
            Nutrition nutrition = food?.Data?.nutrition;
            TileEffectReceiver tileReceiver = player.itemMods.GetMod_ByID<TileEffectReceiver>(ModText.TileEffectReceiver) ??
                                               player.GetComponentInChildren<TileEffectReceiver>(true);
            TileData_Water waterTile = tileReceiver?.currentTileData as TileData_Water;
            Mod_InteractSender interactSender = player.GetComponentInChildren<Mod_InteractSender>(true);

            var result = new JObject
            {
                ["guid"] = player.itemData?.Guid ?? 0,
                ["position"] = VectorToJson(player.transform.position),
                ["velocity"] = VectorToJson(mover.rb.velocity),
                ["moving"] = mover.IsMoving,
                ["running"] = mover.IsRunning,
                ["hp"] = health == null ? JValue.CreateNull() : new JArray(Round(health.Hp), Round(health.MaxHp)),
                ["stamina"] = stamina == null ? JValue.CreateNull() : new JArray(Round(stamina.CurrentValue), Round(stamina.MaxValue)),
                ["water"] = new JObject
                {
                    ["inWater"] = oxygen?.IsInWater ?? waterTile != null,
                    ["immersion"] = tileReceiver == null ? 0f : Round(tileReceiver.CurrentWaterImmersion),
                    ["tile"] = tileReceiver?.currentTileData?.Name ?? string.Empty,
                    ["depth"] = waterTile == null ? JValue.CreateNull() : Round(waterTile.LiquidDepth),
                    ["salt"] = waterTile == null ? JValue.CreateNull() : Round(waterTile.salt),
                    ["oxygen"] = oxygen == null ? JValue.CreateNull() : new JArray(Round(oxygen.CurrentValue), Round(oxygen.MaxValue)),
                    ["breathBlocked"] = oxygen?.IsBreathBlocked ?? false,
                    ["drowning"] = oxygen?.IsDrowning ?? false
                },
                ["hand"] = new JObject
                {
                    ["index"] = handInventory?.Data?.Index ?? -1,
                    ["held"] = handItemData?.IDName ?? string.Empty,
                    ["guid"] = handItemData == null ? JValue.CreateNull() : new JValue(handItemData.Guid),
                    ["amount"] = handItemData?.Stack == null ? 0f : Round(handItemData.Stack.Amount),
                    ["runtimeWeapon"] = handWeapon != null,
                    ["canAttack"] = handWeapon?.CanAttack ?? false,
                    ["attackState"] = handWeapon?.CurrentState.ToString() ?? string.Empty
                },
                ["nutrition"] = nutrition == null ? JValue.CreateNull() : new JObject
                {
                    ["carb"] = RatioPair(nutrition.Carbohydrates, nutrition.Max_Carbohydrates),
                    ["fat"] = RatioPair(nutrition.Fat, nutrition.Max_Fat),
                    ["protein"] = RatioPair(nutrition.Protein, nutrition.Max_Protein),
                    ["water"] = RatioPair(nutrition.Water, nutrition.Max_Water),
                    ["vitamins"] = RatioPair(nutrition.Vitamins, nutrition.Max_Vitamins)
                },
                ["inputLocked"] = controller.IsGameplayInputLocked,
                ["inputLock"] = controller.IsGameplayInputLocked ? controller.DescribeGameplayInputLockState() : string.Empty,
                ["agentControl"] = new JObject
                {
                    ["active"] = controller.HasExternalGameplayControl,
                    ["owned"] = OwnsControl(controller),
                    ["owner"] = controller.ExternalGameplayControlOwnerName
                },
                ["interaction"] = interactSender == null ? JValue.CreateNull() : new JObject
                {
                    ["currentType"] = interactSender.CurrentInteractionType,
                    ["currentGuid"] = interactSender.CurrentInteractionTargetGuid,
                    ["held"] = interactSender.HasHeldInteraction,
                    ["heldType"] = interactSender.HeldInteractionType,
                    ["heldGuid"] = interactSender.HeldInteractionTargetGuid
                }
            };

            if (hotbar != null)
            {
                var slots = new JArray();
                if (hotbar.Data?.itemSlots != null)
                {
                    for (int i = 0; i < hotbar.Data.itemSlots.Count; i++)
                    {
                        ItemData data = hotbar.Data.itemSlots[i]?.itemData;
                        if (data != null)
                            slots.Add(BuildSlotEntry(i, data));
                    }
                }

                result["hotbar"] = new JObject
                {
                    ["selected"] = hotbar.CurrentIndex,
                    ["held"] = hotbar.CurentSelectItem?.itemData?.IDName ?? string.Empty,
                    ["slots"] = slots
                };
            }

            if (includeInventory)
                result["inventory"] = BuildInventorySummary(player, hotbar);

            return result;
        }

        /// <summary>通过 ItemMgr 既有空间索引构造玩家半径内的实体摘要。</summary>
        private static JArray BuildNearbyObservation(Player player, float radius, int maxEntities)
        {
            ItemMgr itemMgr = ItemMgr.Instance;
            if (itemMgr == null)
                return new JArray();

            var candidates = new List<Item>(64);
            var dedupe = new HashSet<Item>();
            LayerMask allLayers = ~0;
            itemMgr.QueryItemsInCircleNonAlloc(
                player.transform.position,
                radius,
                allLayers,
                player,
                candidates,
                dedupe);

            var ordered = candidates
                .Where(item => item != null && item.itemData != null)
                .Select(item => new
                {
                    Item = item,
                    Distance = WorldTopologyRuntime.Distance(player.transform.position, item.transform.position)
                })
                .Where(entry => entry.Distance <= radius)
                .OrderBy(entry => entry.Distance)
                .Take(maxEntities)
                .ToArray();

            var result = new JArray();
            for (int i = 0; i < ordered.Length; i++)
                result.Add(BuildEntityObservation(player, ordered[i].Item, ordered[i].Distance));
            return result;
        }

        /// <summary>构造一个附近实体的紧凑摘要。</summary>
        private static JObject BuildEntityObservation(Player player, Item item, float distance)
        {
            DamageReceiver health = item.itemMods?.GetMod_ByID<DamageReceiver>(ModText.Hp);
            bool interactable = CanPlayerInteract(item, player);
            ItemData data = item.itemData;
            Mod_MechanicalNode mechanical = item.itemMods?.GetMod_ByID<Mod_MechanicalNode>(Mod_MechanicalNode.ModuleId) ??
                                            item.GetComponentInChildren<Mod_MechanicalNode>(true);
            var tags = new JArray();
            if (data.Tags != null)
            {
                for (int i = 0; i < Mathf.Min(8, data.Tags.Count); i++)
                    tags.Add(data.Tags[i]);
            }

            var result = new JObject
            {
                ["guid"] = data.Guid,
                ["id"] = data.IDName ?? string.Empty,
                ["name"] = data.GameName ?? string.Empty,
                ["position"] = VectorToJson(item.transform.position),
                ["distance"] = Round(distance),
                ["hp"] = health == null ? JValue.CreateNull() : new JArray(Round(health.Hp), Round(health.MaxHp)),
                ["interactable"] = interactable,
                ["pickup"] = data.Stack?.CanBePickedUp ?? false,
                ["faction"] = data.FactionId ?? string.Empty,
                ["tags"] = tags
            };

            if (mechanical != null)
            {
                MechanicalNode node = mechanical.Node;
                MechanicalNetwork network = node?.Network;
                MechanicalNodeState state = node?.State ?? mechanical.LocalState;
                result["mechanical"] = new JObject
                {
                    ["attached"] = node != null,
                    ["definition"] = (node?.Definition ?? mechanical.Definition)?.Id ?? string.Empty,
                    ["kind"] = (node?.Definition ?? mechanical.Definition)?.Kind ?? string.Empty,
                    ["source"] = (node?.Definition ?? mechanical.Definition)?.Source ?? string.Empty,
                    ["rpm"] = node == null ? 0f : Round(node.Rpm),
                    ["manualSeconds"] = state == null ? 0f : Round(state.ManualSeconds),
                    ["networkActive"] = network?.Active ?? false,
                    ["status"] = network?.Status ?? string.Empty,
                    ["supply"] = network == null ? 0f : Round(network.Supply),
                    ["demand"] = network == null ? 0f : Round(network.Demand),
                    ["networkNodes"] = network?.Nodes?.Count ?? 0
                };
            }

            return result;
        }

        /// <summary>按生产交互契约判断目标当前是否接受玩家交互，避免仅凭接口存在误报。</summary>
        internal static bool CanPlayerInteract(Item targetItem, Player player)
        {
            if (targetItem == null || player == null || targetItem == player || !targetItem.gameObject.activeInHierarchy)
                return false;

            Mod_InteractSender sender = player.GetComponentInChildren<Mod_InteractSender>(true);
            if (sender == null)
                return false;

            MonoBehaviour[] behaviours = targetItem.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IInteractable interactable &&
                    behaviours[i].gameObject.activeInHierarchy &&
                    sender.CanInteractTarget(interactable))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>聚合玩家普通库存与快捷栏，减少 Agent 为查询资源重复翻槽位。</summary>
        private static JArray BuildInventorySummary(Player player, Inventory_HotBar hotbar)
        {
            var totals = new Dictionary<string, float>(StringComparer.Ordinal);

            void Add(ItemData data)
            {
                if (data == null || string.IsNullOrEmpty(data.IDName))
                    return;

                float amount = data.Stack?.Amount ?? 1f;
                totals[data.IDName] = totals.TryGetValue(data.IDName, out float old) ? old + amount : amount;
            }

            if (hotbar?.Data?.itemSlots != null)
            {
                foreach (ItemSlot slot in hotbar.Data.itemSlots)
                    Add(slot?.itemData);
            }

            Mod_Inventory[] inventories = player.GetComponentsInChildren<Mod_Inventory>(true);
            foreach (Mod_Inventory inventoryModule in inventories)
            {
                if (inventoryModule?.InventoryInstances == null)
                    continue;

                foreach (Inventory inventory in inventoryModule.InventoryInstances)
                {
                    if (inventory?.Data?.itemSlots == null)
                        continue;
                    foreach (ItemSlot slot in inventory.Data.itemSlots)
                        Add(slot?.itemData);
                }
            }

            var result = new JArray();
            foreach (KeyValuePair<string, float> pair in totals.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                result.Add(new JObject { ["id"] = pair.Key, ["n"] = Round(pair.Value) });
            return result;
        }

        /// <summary>固定返回玩家脚下为中心的 3×3 权威地块，避免 Agent 丢失脚下环境上下文。</summary>
        private static JObject BuildTerrainGridObservation(Vector2 playerPosition)
        {
            Vector2 normalized = WorldTopologyRuntime.NormalizePosition(playerPosition);
            Vector2Int center = Vector2Int.FloorToInt(normalized);
            var cells = new JArray();

            for (int dy = 1; dy >= -1; dy--)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Vector2 samplePosition = new(center.x + dx + 0.5f, center.y + dy + 0.5f);
                    if (!TryBuildTerrainCellObservation(samplePosition, dx, dy, out JObject cell))
                    {
                        cells.Add(new JObject
                        {
                            ["offset"] = new JArray(dx, dy),
                            ["loaded"] = false
                        });
                        continue;
                    }

                    cells.Add(cell);
                }
            }

            return new JObject
            {
                ["grid"] = "3x3",
                ["center"] = new JArray(center.x, center.y),
                ["cells"] = cells
            };
        }

        /// <summary>把一个已加载权威地块压缩为身份、通行与水流信息。</summary>
        private static bool TryBuildTerrainCellObservation(
            Vector2 samplePosition,
            int offsetX,
            int offsetY,
            out JObject result)
        {
            result = null;
            ChunkMgr chunkMgr = ChunkMgr.ExistingInstance;
            if (chunkMgr == null ||
                !chunkMgr.TryGetRuntimeTerrainTile(samplePosition, out RuntimeTerrainTileSample sample))
            {
                return false;
            }

            TryResolveTerrainTileMetadata(
                chunkMgr,
                sample.TopTileId,
                out string blockId,
                out string displayName);

            bool water = (sample.Cell.Flags & TerrainCellFlags.Water) != 0;
            result = new JObject
            {
                ["offset"] = new JArray(offsetX, offsetY),
                ["loaded"] = true,
                ["cell"] = new JArray(sample.WorldCell.x, sample.WorldCell.y),
                ["tileId"] = sample.TopTileId,
                ["id"] = blockId,
                ["name"] = displayName,
                ["walkable"] = sample.Terrain.IsWalkable(sample.LocalCell.x, sample.LocalCell.y),
                ["water"] = water,
                ["biomeId"] = sample.Cell.BiomeId
            };

            if (water &&
                chunkMgr.TryGetRuntimeWaterCurrent(
                    new Vector2(sample.WorldCell.x + 0.5f, sample.WorldCell.y + 0.5f),
                    out RuntimeWaterCurrentSample current))
            {
                result["current"] = new JObject
                {
                    ["kind"] = current.Kind.ToString(),
                    ["direction"] = new JArray(Round(current.Direction.x), Round(current.Direction.y)),
                    ["flow"] = Round(current.Flow)
                };
            }

            return true;
        }

        /// <summary>读取附近 ECS 掉落物当前位置，与旧 ItemMgr nearby 分开返回。</summary>
        private static JArray BuildNearbyDroppedItemObservation(Player player, float radius, int maxDrops)
        {
            var candidates = new List<DroppedItemObservation>(Mathf.Max(4, maxDrops));
            DroppedItemService.QueryNearbyEntityDrops(player.transform.position, radius, candidates);
            var ordered = candidates
                .Select(drop => new
                {
                    Drop = drop,
                    Distance = WorldTopologyRuntime.Distance(player.transform.position, drop.Position)
                })
                .OrderBy(entry => entry.Distance)
                .ThenBy(entry => entry.Drop.Id)
                .Take(maxDrops)
                .ToArray();

            var result = new JArray();
            for (int i = 0; i < ordered.Length; i++)
            {
                DroppedItemObservation drop = ordered[i].Drop;
                var tags = new JArray();
                for (int tagIndex = 0; tagIndex < drop.Tags.Count; tagIndex++)
                    tags.Add(drop.Tags[tagIndex]);

                result.Add(new JObject
                {
                    ["guid"] = drop.Id,
                    ["id"] = drop.ItemId,
                    ["name"] = ResolveItemDisplayName(drop.ItemId, drop.GameName),
                    ["position"] = new JObject
                    {
                        ["x"] = Round(drop.Position.x),
                        ["y"] = Round(drop.Position.y)
                    },
                    ["distance"] = Round(ordered[i].Distance),
                    ["amount"] = Round(drop.Amount),
                    ["pickable"] = drop.Pickable,
                    ["tags"] = tags
                });
            }

            return result;
        }

        /// <summary>把数字地块 ID 映射到当前世界实际使用的 Tile_Block 身份。</summary>
        internal static bool TryResolveTerrainTileMetadata(
            ChunkMgr chunkMgr,
            int tileId,
            out string blockId,
            out string displayName)
        {
            blockId = string.Empty;
            displayName = string.Empty;
            if (chunkMgr == null || tileId == 0)
                return false;

            string parameterId = "tile.block." + tileId;
            if (!TryResolveTerrainBlockId(chunkMgr.ActiveGenerationProfile, parameterId, out blockId) &&
                !TryResolveTerrainBlockId(chunkMgr.RuntimeTileCatalogProfile, parameterId, out blockId))
            {
                return false;
            }

            RuntimeTileDefinition block = GameRes.ExistingInstance?.GetTileBlock(blockId);
            displayName = !string.IsNullOrWhiteSpace(block?.displayName)
                ? block.displayName
                : blockId;
            return true;
        }

        /// <summary>按稳定 ID、Tile_Block 名称或显示名精确解析可查询地块 ID。</summary>
        internal static HashSet<int> ResolveTerrainTileIds(string query, out JArray resolved)
        {
            resolved = new JArray();
            var matches = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(query))
                return matches;

            ChunkMgr chunkMgr = ChunkMgr.ExistingInstance;
            if (chunkMgr == null)
                return matches;

            var definitions = new Dictionary<int, string>();
            CollectTerrainTileDefinitions(chunkMgr.ActiveGenerationProfile, definitions);
            CollectTerrainTileDefinitions(chunkMgr.RuntimeTileCatalogProfile, definitions);
            string normalized = query.Trim();

            foreach (KeyValuePair<int, string> pair in definitions.OrderBy(pair => pair.Key))
            {
                RuntimeTileDefinition block = GameRes.ExistingInstance?.GetTileBlock(pair.Value);
                if (!TerrainTileNameEquals(normalized, pair.Key, pair.Value, block))
                    continue;

                matches.Add(pair.Key);
                resolved.Add(new JObject
                {
                    ["tileId"] = pair.Key,
                    ["id"] = pair.Value,
                    ["name"] = !string.IsNullOrWhiteSpace(block?.displayName)
                        ? block.displayName
                        : pair.Value
                });
            }

            return matches;
        }

        /// <summary>按地表/支撑层规则取得与 RuntimeTerrainTileSample.TopTileId 一致的有效顶层 ID。</summary>
        internal static int ResolveEffectiveTerrainTileId(ChunkTerrainData terrain, int x, int y)
        {
            TerrainCell effective = TerrainSupportLayer.GetSurfaceCell(terrain, x, y);
            int supportId = TerrainSupportLayer.GetTileId(terrain, x, y);
            return supportId != 0 && effective.BlockingTileId == 0 && effective.BackTileId == 0
                ? supportId
                : terrain.GetTopTileId(x, y);
        }

        /// <summary>当前语言物品名优先来自运行时定义，缺失时使用冷载荷名称。</summary>
        internal static string ResolveItemDisplayName(string itemId, string fallback)
        {
            GameRes gameRes = GameRes.ExistingInstance;
            return gameRes?.ItemDefinitions != null &&
                   gameRes.ItemDefinitions.TryGetValue(itemId ?? string.Empty, out RuntimeItemDefinition definition)
                ? definition.DisplayName
                : fallback ?? string.Empty;
        }

        private static bool TryResolveTerrainBlockId(
            ChunkGenerationProfileSnapshot profile,
            string parameterId,
            out string blockId)
        {
            blockId = null;
            return profile?.TextParameters != null &&
                   profile.TextParameters.TryGetValue(parameterId, out blockId) &&
                   !string.IsNullOrWhiteSpace(blockId);
        }

        private static void CollectTerrainTileDefinitions(
            ChunkGenerationProfileSnapshot profile,
            IDictionary<int, string> definitions)
        {
            if (profile?.TextParameters == null)
                return;

            const string prefix = "tile.block.";
            foreach (KeyValuePair<string, string> pair in profile.TextParameters)
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal) ||
                    !int.TryParse(pair.Key.Substring(prefix.Length), out int tileId) ||
                    tileId == 0 ||
                    string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                definitions[tileId] = pair.Value.Trim();
            }
        }

        private static bool TerrainTileNameEquals(
            string query,
            int tileId,
            string blockId,
            RuntimeTileDefinition block)
        {
            return string.Equals(query, tileId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(query, blockId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(query, block?.name, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(query, block?.tileItemName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(query, block?.displayName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(query, block?.tileDataTemplate?.ID, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(query, block?.tileDataTemplate?.Name, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region 动作执行

        /// <summary>执行一个已注册的 GamePlayMCP 动作。</summary>
        public static async Task<JObject> ExecuteActionAsync(JObject parameters)
        {
            string action = GetString(parameters, "action", string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
                return BuildActionError("missing_action", "缺少 action。", false);

            if (!TryEnsureControl(out Player player, out GameController controller, out Mover mover, out string error))
                return BuildActionError("control_unavailable", error, false);

            return await GameplayMcpActionRegistry.ExecuteAsync(
                action,
                new GameplayMcpActionContext(player, controller, mover),
                parameters);
        }

        /// <summary>按方向移动一段真实 Editor 时间，到期后停止输入并恢复原奔跑状态。</summary>
        internal static async Task<JObject> MoveForAsync(
            JObject parameters,
            Player player,
            GameController controller,
            Mover mover)
        {
            Vector2 direction = new Vector2(GetFloat(parameters, "x", 0f), GetFloat(parameters, "y", 0f));
            float seconds = Mathf.Clamp(GetFloat(parameters, "seconds", 0.5f), 0.02f, 20f);
            bool run = GetBool(parameters, "run", false);
            if (direction.sqrMagnitude <= 0.0001f)
                return BuildActionError("invalid_direction", "move 需要非零 x/y。", false);

            bool previousRun = mover.IsRunning;
            controller.TrySetExternalMoveInput(ControlOwner, Vector2.ClampMagnitude(direction, 1f));
            mover.SetRunState(run);
            try
            {
                bool completed = await WaitEditorSecondsAsync(seconds);
                if (!completed)
                    return BuildActionError("play_mode_ended", "移动期间 Play Mode 已结束。", false);
            }
            finally
            {
                if (controller != null && controller.IsExternalGameplayControlOwner(ControlOwner))
                    controller.TrySetExternalMoveInput(ControlOwner, Vector2.zero);
                if (mover != null)
                    mover.SetRunState(previousRun);
            }

            return BuildActionSuccess("move", player, new JObject
            {
                ["seconds"] = Round(seconds),
                ["run"] = run
            });
        }

        /// <summary>持续重算方向移动到目标点，使用现有输入链并设置有界超时。</summary>
        internal static async Task<JObject> MoveToAsync(
            JObject parameters,
            Player player,
            GameController controller,
            Mover mover)
        {
            Vector2 target = WorldTopologyRuntime.NormalizePosition(new Vector2(
                GetFloat(parameters, "x", player.transform.position.x),
                GetFloat(parameters, "y", player.transform.position.y)));
            float maxSeconds = Mathf.Clamp(GetFloat(parameters, "seconds", 5f), 0.05f, 20f);
            float tolerance = Mathf.Clamp(GetFloat(parameters, "tolerance", 0.25f), 0.05f, 2f);
            bool run = GetBool(parameters, "run", false);
            bool previousRun = mover.IsRunning;
            mover.SetRunState(run);

            double deadline = EditorApplication.timeSinceStartup + maxSeconds;
            try
            {
                bool reached = await RunUntilAsync(() =>
                {
                    if (player == null || controller == null ||
                        !controller.IsExternalGameplayControlOwner(ControlOwner))
                    {
                        return true;
                    }

                    Vector2 delta = WorldTopologyRuntime.ShortestDelta(player.transform.position, target);
                    if (delta.sqrMagnitude <= tolerance * tolerance)
                    {
                        controller.TrySetExternalMoveInput(ControlOwner, Vector2.zero);
                        return true;
                    }

                    controller.TrySetExternalMoveInput(ControlOwner, delta.normalized);
                    return EditorApplication.timeSinceStartup >= deadline;
                });

                Vector2 finalDelta = player == null
                    ? Vector2.one * float.PositiveInfinity
                    : WorldTopologyRuntime.ShortestDelta(player.transform.position, target);
                bool arrived = reached && finalDelta.sqrMagnitude <= tolerance * tolerance;
                return BuildActionSuccess("move_to", player, new JObject
                {
                    ["target"] = VectorToJson(target),
                    ["reached"] = arrived,
                    ["timedOut"] = !arrived
                });
            }
            finally
            {
                if (controller != null && controller.IsExternalGameplayControlOwner(ControlOwner))
                    controller.TrySetExternalMoveInput(ControlOwner, Vector2.zero);
                if (mover != null)
                    mover.SetRunState(previousRun);
            }
        }

        /// <summary>设置 Agent 世界瞄准点，可直接使用坐标或附近实体 Guid。</summary>
        internal static JObject LookAt(JObject parameters, Player player, GameController controller)
        {
            if (!TryResolveTargetPosition(parameters, player, out Vector2 target, out string error))
                return BuildActionError("target_not_found", error, false);

            if (!controller.TrySetExternalAimWorldPosition(ControlOwner, target))
                return BuildActionError("aim_rejected", "GameController 拒绝了外部瞄准点。", false);

            return BuildActionSuccess("look_at", player, new JObject { ["target"] = VectorToJson(target) });
        }

        /// <summary>按正式交互距离与目标有效性规则执行一次交互。</summary>
        internal static JObject Interact(JObject parameters, Player player)
        {
            Mod_InteractSender sender = player.GetComponentInChildren<Mod_InteractSender>(true);
            if (sender == null)
                return BuildActionError("interaction_missing", "玩家没有 Mod_InteractSender。", false);

            int targetGuid = GetInt(parameters, "targetGuid", 0);
            bool interacted;
            if (targetGuid == 0)
            {
                interacted = sender.TryInteractAtCurrentPosition();
            }
            else
            {
                Item targetItem = FindRuntimeItem(targetGuid);
                if (targetItem == null)
                    return BuildActionError("target_not_found", $"找不到运行时实体 Guid={targetGuid}。", false);

                interacted = false;
                MonoBehaviour[] behaviours = targetItem.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < behaviours.Length && !interacted; i++)
                {
                    if (behaviours[i] is IInteractable interactable)
                        interacted = sender.TryInteractTarget(interactable);
                }
            }

            return BuildActionSuccess("interact", player, new JObject
            {
                ["targetGuid"] = targetGuid == 0 ? JValue.CreateNull() : new JValue(targetGuid),
                ["interacted"] = interacted
            });
        }

        /// <summary>按真实交互链持续按住一个目标，供手摇轮等持续交互玩法复用。</summary>
        internal static async Task<JObject> InteractHoldAsync(JObject parameters, Player player)
        {
            Mod_InteractSender sender = player.GetComponentInChildren<Mod_InteractSender>(true);
            if (sender == null)
                return BuildActionError("interaction_missing", "玩家没有 Mod_InteractSender。", false);

            int targetGuid = GetInt(parameters, "targetGuid", 0);
            bool interacted = false;
            if (targetGuid == 0)
            {
                interacted = sender.TryBeginHeldInteraction();
            }
            else
            {
                Item targetItem = FindRuntimeItem(targetGuid);
                if (targetItem == null)
                    return BuildActionError("target_not_found", $"找不到运行时实体 Guid={targetGuid}。", false);

                MonoBehaviour[] behaviours = targetItem.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < behaviours.Length && !interacted; i++)
                {
                    if (behaviours[i] is IInteractable interactable)
                        interacted = sender.TryBeginHeldInteraction(interactable);
                }
            }

            if (!interacted)
            {
                return BuildActionSuccess("interact_hold", player, new JObject
                {
                    ["targetGuid"] = targetGuid == 0 ? JValue.CreateNull() : new JValue(targetGuid),
                    ["interacted"] = false,
                    ["seconds"] = 0f
                });
            }

            float seconds = Mathf.Clamp(GetFloat(parameters, "seconds", 1f), 0.02f, 20f);
            try
            {
                bool completed = await WaitEditorSecondsAsync(seconds);
                if (!completed)
                    return BuildActionError("play_mode_ended", "持续交互期间 Play Mode 已结束。", false);
            }
            finally
            {
                if (sender != null)
                    sender.EndHeldInteraction();
            }

            return BuildActionSuccess("interact_hold", player, new JObject
            {
                ["targetGuid"] = targetGuid == 0 ? JValue.CreateNull() : new JValue(targetGuid),
                ["interacted"] = true,
                ["seconds"] = Round(seconds)
            });
        }

        /// <summary>向现有攻击事件链提交一次有时长的攻击按压。</summary>
        internal static async Task<JObject> AttackAsync(JObject parameters, Player player, GameController controller)
        {
            int targetGuid = GetInt(parameters, "targetGuid", 0);
            if (targetGuid != 0)
            {
                Item target = FindRuntimeItem(targetGuid);
                if (target == null)
                    return BuildActionError("target_not_found", $"找不到运行时实体 Guid={targetGuid}。", false);
                controller.TrySetExternalAimWorldPosition(ControlOwner, target.transform.position);
                await WaitEditorSecondsAsync(0.02f);
            }

            float seconds = Mathf.Clamp(GetFloat(parameters, "seconds", 0.2f), 0.02f, 5f);
            controller.TrySetExternalAttackHeld(ControlOwner, true);
            try
            {
                bool completed = await WaitEditorSecondsAsync(seconds);
                if (!completed)
                    return BuildActionError("play_mode_ended", "攻击期间 Play Mode 已结束。", false);
            }
            finally
            {
                if (controller != null && controller.IsExternalGameplayControlOwner(ControlOwner))
                    controller.TrySetExternalAttackHeld(ControlOwner, false);
            }

            return BuildActionSuccess("attack", player, new JObject
            {
                ["targetGuid"] = targetGuid == 0 ? JValue.CreateNull() : new JValue(targetGuid),
                ["seconds"] = Round(seconds)
            });
        }

        /// <summary>调用当前真实手持物的 Act 入口。</summary>
        internal static JObject UseHeldItem(Player player)
        {
            Inventory_HotBar hotbar = ResolveHotbar(player);
            Item heldItem = hotbar?.CurentSelectItem;
            if (heldItem == null)
                return BuildActionError("no_held_item", "当前快捷栏没有可使用的手持物。", false);

            heldItem.Act();
            return BuildActionSuccess("use", player, new JObject
            {
                ["heldItem"] = heldItem.itemData?.IDName ?? heldItem.name
            });
        }

        /// <summary>通过快捷栏公开控制入口选择槽位。</summary>
        internal static JObject SelectHotbar(JObject parameters, Player player)
        {
            Inventory_HotBar hotbar = ResolveHotbar(player);
            if (hotbar == null)
                return BuildActionError("hotbar_missing", "玩家没有 Inventory_HotBar。", false);

            int index = GetInt(parameters, "index", -1);
            bool selected = hotbar.TrySelectSlot(index);
            return BuildActionSuccess("select_hotbar", player, new JObject
            {
                ["index"] = index,
                ["selected"] = selected,
                ["heldItem"] = hotbar.CurentSelectItem?.itemData?.IDName ?? string.Empty
            });
        }

        /// <summary>让真实游戏继续运行一小段时间，不注入额外输入。</summary>
        internal static async Task<JObject> WaitActionAsync(JObject parameters, Player player)
        {
            float seconds = Mathf.Clamp(GetFloat(parameters, "seconds", 0.5f), 0.02f, 20f);
            bool completed = await WaitEditorSecondsAsync(seconds);
            if (!completed)
                return BuildActionError("play_mode_ended", "等待期间 Play Mode 已结束。", false);
            return BuildActionSuccess("wait", player, new JObject { ["seconds"] = Round(seconds) });
        }

        /// <summary>停止外部持续输入并退出奔跑模式。</summary>
        internal static JObject Stop(Player player, GameController controller, Mover mover)
        {
            controller.TrySetExternalMoveInput(ControlOwner, Vector2.zero);
            controller.TrySetExternalAttackHeld(ControlOwner, false);
            mover.SetRunState(false);
            return BuildActionSuccess("stop", player, new JObject { ["stopped"] = true });
        }

        #endregion

        #region 通用辅助

        /// <summary>等待指定 Editor 实时时长；Play Mode 结束时立即取消。</summary>
        internal static Task<bool> WaitEditorSecondsAsync(float seconds)
        {
            double deadline = EditorApplication.timeSinceStartup + Math.Max(0.001, seconds);
            var completion = new TaskCompletionSource<bool>();
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                if (!Application.isPlaying)
                {
                    EditorApplication.update -= callback;
                    completion.TrySetResult(false);
                    return;
                }

                if (EditorApplication.timeSinceStartup < deadline)
                    return;

                EditorApplication.update -= callback;
                completion.TrySetResult(true);
            };
            EditorApplication.update += callback;
            return completion.Task;
        }

        /// <summary>等待条件成立或超时，整个过程不阻塞 Unity 主线程。</summary>
        private static Task<bool> WaitUntilAsync(Func<bool> condition, float timeoutSeconds)
        {
            double deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
            var completion = new TaskCompletionSource<bool>();
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                try
                {
                    if (!Application.isPlaying)
                    {
                        EditorApplication.update -= callback;
                        completion.TrySetResult(false);
                        return;
                    }

                    if (condition())
                    {
                        EditorApplication.update -= callback;
                        completion.TrySetResult(true);
                        return;
                    }

                    if (EditorApplication.timeSinceStartup < deadline)
                        return;

                    EditorApplication.update -= callback;
                    completion.TrySetResult(false);
                }
                catch (Exception exception)
                {
                    EditorApplication.update -= callback;
                    completion.TrySetException(exception);
                }
            };
            EditorApplication.update += callback;
            return completion.Task;
        }

        /// <summary>每个 Editor 更新执行一次条件 Tick，直到 Tick 请求结束。</summary>
        private static Task<bool> RunUntilAsync(Func<bool> tick)
        {
            var completion = new TaskCompletionSource<bool>();
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                try
                {
                    if (!Application.isPlaying)
                    {
                        EditorApplication.update -= callback;
                        completion.TrySetResult(false);
                        return;
                    }

                    if (!tick())
                        return;

                    EditorApplication.update -= callback;
                    completion.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    EditorApplication.update -= callback;
                    completion.TrySetException(exception);
                }
            };
            EditorApplication.update += callback;
            return completion.Task;
        }

        /// <summary>根据 targetGuid 或 x/y 解析世界目标位置。</summary>
        private static bool TryResolveTargetPosition(JObject parameters, Player player, out Vector2 target, out string error)
        {
            int targetGuid = GetInt(parameters, "targetGuid", 0);
            if (targetGuid != 0)
            {
                Item item = FindRuntimeItem(targetGuid);
                if (item == null)
                {
                    target = default;
                    error = $"找不到运行时实体 Guid={targetGuid}。";
                    return false;
                }

                target = item.transform.position;
                error = string.Empty;
                return true;
            }

            if (parameters?["x"] == null || parameters?["y"] == null)
            {
                target = player != null ? (Vector2)player.transform.position : Vector2.zero;
                error = "需要 targetGuid 或同时提供 x/y。";
                return false;
            }

            target = WorldTopologyRuntime.NormalizePosition(new Vector2(
                GetFloat(parameters, "x", 0f),
                GetFloat(parameters, "y", 0f)));
            error = string.Empty;
            return true;
        }

        /// <summary>按 Guid 获取当前世界仍注册的真实 Item。</summary>
        private static Item FindRuntimeItem(int guid)
        {
            ItemMgr itemMgr = ItemMgr.Instance;
            return itemMgr != null && itemMgr.WorldRunTimeItems.TryGetValue(guid, out Item item) ? item : null;
        }

        /// <summary>解析玩家快捷栏模块。</summary>
        private static Inventory_HotBar ResolveHotbar(Player player)
        {
            return player?.itemMods?.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar) ??
                   player?.GetComponentInChildren<Inventory_HotBar>(true);
        }

        /// <summary>构造成功动作响应并附带最终玩家位置，减少额外 observe 调用。</summary>
        internal static JObject BuildActionSuccess(string action, Player player, JObject data)
        {
            return new JObject
            {
                ["ok"] = true,
                ["action"] = action,
                ["position"] = player == null ? JValue.CreateNull() : VectorToJson(player.transform.position),
                ["data"] = data ?? new JObject()
            };
        }

        /// <summary>构造稳定错误令牌；能力缺口会显式提示 Agent 扩展协议。</summary>
        public static JObject BuildActionError(
            string code,
            string message,
            bool capabilityGap,
            string requestedAction = null)
        {
            var result = new JObject
            {
                ["ok"] = false,
                ["code"] = code,
                ["message"] = message,
                ["capabilityGap"] = capabilityGap
            };
            if (capabilityGap)
            {
                result["requestedAction"] = requestedAction ?? string.Empty;
                result["extensionPath"] = ExtensionPath;
            }
            return result;
        }

        /// <summary>构造单个快捷栏槽位摘要。</summary>
        private static JObject BuildSlotEntry(int index, ItemData data)
        {
            return new JObject
            {
                ["i"] = index,
                ["id"] = data.IDName ?? string.Empty,
                ["n"] = Round(data.Stack?.Amount ?? 1f),
                ["durability"] = data.MaxDurability > 0f ? Round(data.Durability / data.MaxDurability) : 0f
            };
        }

        /// <summary>把当前值与最大值压成二元数组。</summary>
        private static JArray RatioPair(float current, float maximum)
        {
            return new JArray(Round(current), Round(maximum));
        }

        /// <summary>把二维坐标压成二元数组。</summary>
        private static JArray VectorToJson(Vector2 value)
        {
            return new JArray(Round(value.x), Round(value.y));
        }

        /// <summary>把三维坐标压成二维世界坐标数组。</summary>
        private static JArray VectorToJson(Vector3 value)
        {
            return new JArray(Round(value.x), Round(value.y));
        }

        /// <summary>减少状态 JSON 的无效小数噪声。</summary>
        private static float Round(float value)
        {
            return (float)Math.Round(value, 3, MidpointRounding.AwayFromZero);
        }

        /// <summary>读取字符串参数。</summary>
        private static string GetString(JObject parameters, string key, string fallback)
        {
            JToken token = parameters?[key];
            return token == null || token.Type == JTokenType.Null ? fallback : token.ToString();
        }

        /// <summary>读取浮点参数。</summary>
        private static float GetFloat(JObject parameters, string key, float fallback)
        {
            string text = parameters?[key]?.ToString();
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
                   !float.IsNaN(value) && !float.IsInfinity(value)
                ? value
                : fallback;
        }

        /// <summary>读取整数参数。</summary>
        private static int GetInt(JObject parameters, string key, int fallback)
        {
            string text = parameters?[key]?.ToString();
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;
        }

        /// <summary>读取布尔参数。</summary>
        private static bool GetBool(JObject parameters, string key, bool fallback)
        {
            return bool.TryParse(parameters?[key]?.ToString(), out bool value) ? value : fallback;
        }

        #endregion
    }

    /// <summary>标记一个可被 GamePlayMCP 自动发现的玩法动作。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    internal sealed class GameplayMcpActionAttribute : Attribute
    {
        public GameplayMcpActionAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }
        public string Description { get; }
    }

    /// <summary>GamePlayMCP 动作上下文，统一提供当前真实玩家与控制模块。</summary>
    internal readonly struct GameplayMcpActionContext
    {
        public GameplayMcpActionContext(Player player, GameController controller, Mover mover)
        {
            Player = player;
            Controller = controller;
            Mover = mover;
        }

        public Player Player { get; }
        public GameController Controller { get; }
        public Mover Mover { get; }
    }

    /// <summary>一个可扩展的 GamePlayMCP 玩法动作。</summary>
    internal interface IGameplayMcpAction
    {
        /// <summary>通过真实生产 API 执行动作并返回结构化结果。</summary>
        Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters);
    }

    /// <summary>
    /// GamePlayMCP 动作注册表。
    /// 使用 Unity TypeCache 自动发现动作，新增协议只需新增带特性的 Action 类，不修改中心分发器。
    /// </summary>
    internal static class GameplayMcpActionRegistry
    {
        private static Dictionary<string, Registration> _registrations;

        private sealed class Registration
        {
            public string Name;
            public string Description;
            public IGameplayMcpAction Handler;
        }

        /// <summary>读取当前自动发现的动作名称。</summary>
        public static IReadOnlyList<string> ActionNames =>
            GetRegistrations().Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        /// <summary>读取当前动作及其用途，供 capability 工具报告。</summary>
        public static JObject BuildCapabilityObject()
        {
            var result = new JObject();
            foreach (Registration registration in GetRegistrations().Values.OrderBy(entry => entry.Name, StringComparer.Ordinal))
                result[registration.Name] = registration.Description;
            return result;
        }

        /// <summary>执行一个已注册动作；未知动作统一返回 capability_gap。</summary>
        public static Task<JObject> ExecuteAsync(string name, GameplayMcpActionContext context, JObject parameters)
        {
            if (!GetRegistrations().TryGetValue(name, out Registration registration))
            {
                return Task.FromResult(GameplayMcpRuntime.BuildActionError(
                    "capability_gap",
                    $"GamePlayMCP 尚未实现动作 '{name}'。",
                    true,
                    name));
            }

            return registration.Handler.ExecuteAsync(context, parameters);
        }

        /// <summary>首次使用时通过 TypeCache 构建注册表。</summary>
        private static Dictionary<string, Registration> GetRegistrations()
        {
            if (_registrations != null)
                return _registrations;

            _registrations = new Dictionary<string, Registration>(StringComparer.OrdinalIgnoreCase);
            foreach (Type type in TypeCache.GetTypesWithAttribute<GameplayMcpActionAttribute>())
            {
                if (type.IsAbstract || !typeof(IGameplayMcpAction).IsAssignableFrom(type))
                    continue;

                GameplayMcpActionAttribute attribute =
                    (GameplayMcpActionAttribute)Attribute.GetCustomAttribute(type, typeof(GameplayMcpActionAttribute));
                if (attribute == null || string.IsNullOrWhiteSpace(attribute.Name))
                    continue;

                if (Activator.CreateInstance(type) is not IGameplayMcpAction handler)
                    continue;

                if (_registrations.ContainsKey(attribute.Name))
                    throw new InvalidOperationException($"GamePlayMCP 动作重复注册：{attribute.Name}");

                _registrations.Add(attribute.Name, new Registration
                {
                    Name = attribute.Name,
                    Description = attribute.Description ?? string.Empty,
                    Handler = handler
                });
            }

            return _registrations;
        }
    }

    /// <summary>按二维方向移动一段时间。</summary>
    [GameplayMcpAction("move", "Move with a normalized 2D input for a bounded real-time duration.")]
    internal sealed class GameplayMcpMoveAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters) =>
            GameplayMcpRuntime.MoveForAsync(parameters, context.Player, context.Controller, context.Mover);
    }

    /// <summary>持续向世界坐标移动直到到达或超时。</summary>
    [GameplayMcpAction("move_to", "Move toward a world position through the normal Mover input chain until reached or timed out.")]
    internal sealed class GameplayMcpMoveToAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters) =>
            GameplayMcpRuntime.MoveToAsync(parameters, context.Player, context.Controller, context.Mover);
    }

    /// <summary>设置世界瞄准点。</summary>
    [GameplayMcpAction("look_at", "Aim at world x/y or a runtime targetGuid returned by gameplay_observe.")]
    internal sealed class GameplayMcpLookAtAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters) =>
            Task.FromResult(GameplayMcpRuntime.LookAt(parameters, context.Player, context.Controller));
    }

    /// <summary>执行正式交互。</summary>
    [GameplayMcpAction("interact", "Interact through Mod_InteractSender using normal range and target validity rules.")]
    internal sealed class GameplayMcpInteractAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters) =>
            Task.FromResult(GameplayMcpRuntime.Interact(parameters, context.Player));
    }

    /// <summary>持续执行正式交互，用于手摇轮等必须按住交互键的玩法。</summary>
    [GameplayMcpAction(
        "interact_hold",
        "Hold a production interaction target for bounded real seconds through Mod_InteractSender. Useful for hand cranks and other continuous interactions.")]
    internal sealed class GameplayMcpInteractHoldAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters) =>
            GameplayMcpRuntime.InteractHoldAsync(parameters, context.Player);
    }

    /// <summary>执行一次有界攻击按压。</summary>
    [GameplayMcpAction("attack", "Press and release the production AttackStarted/AttackEnded chain, optionally aimed at targetGuid.")]
    internal sealed class GameplayMcpAttackAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters) =>
            GameplayMcpRuntime.AttackAsync(parameters, context.Player, context.Controller);
    }

    /// <summary>使用当前真实手持物。</summary>
    [GameplayMcpAction("use", "Use the currently held item through Item.Act().")]
    internal sealed class GameplayMcpUseAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters) =>
            Task.FromResult(GameplayMcpRuntime.UseHeldItem(context.Player));
    }

    /// <summary>切换真实快捷栏槽位。</summary>
    [GameplayMcpAction("select_hotbar", "Select a zero-based hotbar slot through the production hotbar switch flow.")]
    internal sealed class GameplayMcpSelectHotbarAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters) =>
            Task.FromResult(GameplayMcpRuntime.SelectHotbar(parameters, context.Player));
    }

    /// <summary>停止 Agent 持续控制状态。</summary>
    [GameplayMcpAction("stop", "Stop external movement and attack input and leave running mode.")]
    internal sealed class GameplayMcpStopAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters) =>
            Task.FromResult(GameplayMcpRuntime.Stop(context.Player, context.Controller, context.Mover));
    }

    /// <summary>让真实世界继续运行一小段时间。</summary>
    [GameplayMcpAction("wait", "Let the live game advance for a bounded real-time duration without new input.")]
    internal sealed class GameplayMcpWaitAction : IGameplayMcpAction
    {
        public Task<JObject> ExecuteAsync(GameplayMcpActionContext context, JObject parameters) =>
            GameplayMcpRuntime.WaitActionAsync(parameters, context.Player);
    }
}
