using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// 向 GamePlayMCP 暴露经过明确白名单筛选的 GM 命令。
    /// 命令只能操作当前本地玩家的正式管理员 API，并要求先取得 GamePlayMCP 控制租约；
    /// 不提供任意反射、任意 Console 或私有字段写入入口，避免把调试工具变成通用后门。
    /// </summary>
    [McpForUnityTool(
        "gameplay_gm",
        Description = "Execute one explicitly whitelisted FlatWorld GM command for the current local player. Call gameplay_capabilities first for the command list. To enable invincibility directly without opening the GM UI, use command=enable_invincibility; it enables administrator mode if needed and then enables runtime invincibility. Requires an active game world and the GamePlayMCP control lease. This is not arbitrary reflection or console execution.",
        Group = "core")]
    public static class GameplayGmTool
    {
        #region 工具参数与执行

        /// <summary>GamePlayMCP GM 命令参数。</summary>
        public sealed class Parameters
        {
            [ToolParameter("GM command. Call gameplay_capabilities first for the current registered command list.", Required = true)]
            public string command { get; set; }

            [ToolParameter("Exact BuffDefinition id for commands that require one, for example core:night_vision.", Required = false)]
            public string buffId { get; set; }
        }

        /// <summary>在当前控制租约下执行一个已注册的 GM 命令。</summary>
        public static object HandleCommand(JObject parameters)
        {
            string rawCommand = parameters?["command"]?.ToString()?.Trim();
            string command = GameplayMcpGmCommandRegistry.NormalizeCommand(rawCommand);
            const string SelfBuffPrefix = "apply_self_buff:";
            if (command.StartsWith(SelfBuffPrefix, StringComparison.Ordinal))
            {
                int separatorIndex = rawCommand?.IndexOf(':') ?? -1;
                string buffId = separatorIndex >= 0 && separatorIndex + 1 < rawCommand.Length
                    ? rawCommand.Substring(separatorIndex + 1).Trim()
                    : string.Empty;
                parameters ??= new JObject();
                parameters["buffId"] = buffId;
                command = "apply_self_buff";
            }

            if (string.IsNullOrEmpty(command))
                return new ErrorResponse(
                    "missing_gm_command: gameplay_gm 需要 command；请先调用 gameplay_capabilities 查看 gmCommands。 ");

            if (!GameplayMcpGmCommandRegistry.Contains(command))
            {
                string supported = string.Join(", ", GameplayMcpGmCommandRegistry.CommandNames);
                return new ErrorResponse(
                    $"unknown_gm_command: 不支持 GM 命令 '{command}'。可用命令：{supported}。 ");
            }

            if (!GameplayMcpRuntime.TryEnsureControl(
                    out Player player,
                    out _,
                    out _,
                    out string controlError))
            {
                return new ErrorResponse(controlError);
            }

            if (!GameplayMcpGmCommandRegistry.TryExecute(
                    command,
                    player,
                    parameters,
                    out JObject result,
                    out string executionError))
            {
                return new ErrorResponse($"gm_command_failed: {executionError}");
            }

            return new SuccessResponse("FlatWorld GM command completed.", result);
        }

        #endregion
    }

    #region GM 命令注册表

    /// <summary>标记一个可被 GamePlayMCP 自动发现的 GM 命令。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    internal sealed class GameplayMcpGmCommandAttribute : Attribute
    {
        /// <summary>创建 GM 命令描述。</summary>
        public GameplayMcpGmCommandAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }

        /// <summary>命令稳定名称。</summary>
        public string Name { get; }

        /// <summary>供 Agent 理解用途的命令说明。</summary>
        public string Description { get; }
    }

    /// <summary>一个只操作当前本地玩家的 GamePlayMCP GM 命令。</summary>
    internal interface IGameplayMcpGmCommand
    {
        /// <summary>执行命令并返回结构化结果。</summary>
        bool TryExecute(Player player, JObject parameters, out JObject result, out string error);
    }

    /// <summary>
    /// GamePlayMCP GM 命令注册表。
    /// 使用 TypeCache 自动发现命令类，新增一个明确职责的 GM 命令时无需修改中心分发 switch。
    /// </summary>
    internal static class GameplayMcpGmCommandRegistry
    {
        #region 注册表生命周期

        private sealed class Registration
        {
            public string Name;
            public string Description;
            public IGameplayMcpGmCommand Handler;
        }

        private static Dictionary<string, Registration> registrations;

        /// <summary>读取当前已注册的 GM 命令名称。</summary>
        public static IReadOnlyList<string> CommandNames =>
            GetRegistrations().Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        /// <summary>判断命令是否存在。</summary>
        public static bool Contains(string name)
        {
            return !string.IsNullOrEmpty(name) && GetRegistrations().ContainsKey(name);
        }

        /// <summary>构造供 gameplay_capabilities 返回的 GM 命令说明对象。</summary>
        public static JObject BuildCapabilityObject()
        {
            var result = new JObject();
            foreach (Registration registration in GetRegistrations().Values.OrderBy(
                         entry => entry.Name,
                         StringComparer.Ordinal))
            {
                result[registration.Name] = registration.Description ?? string.Empty;
            }

            return result;
        }

        /// <summary>规范化命令名称，保持接口对大小写和空白稳定。</summary>
        public static string NormalizeCommand(string value)
        {
            return value?.Trim().ToLowerInvariant() ?? string.Empty;
        }

        /// <summary>执行指定 GM 命令并保留生产 API 返回的失败原因。</summary>
        public static bool TryExecute(
            string name,
            Player player,
            JObject parameters,
            out JObject result,
            out string error)
        {
            result = null;
            error = string.Empty;
            if (!GetRegistrations().TryGetValue(name, out Registration registration))
            {
                error = $"未注册 GM 命令：{name}。";
                return false;
            }

            try
            {
                return registration.Handler.TryExecute(player, parameters, out result, out error);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                error = exception.Message;
                result = null;
                return false;
            }
        }

        /// <summary>首次使用时通过 TypeCache 构建命令注册表。</summary>
        private static Dictionary<string, Registration> GetRegistrations()
        {
            if (registrations != null)
                return registrations;

            registrations = new Dictionary<string, Registration>(StringComparer.OrdinalIgnoreCase);
            foreach (Type type in TypeCache.GetTypesWithAttribute<GameplayMcpGmCommandAttribute>())
            {
                if (type.IsAbstract || !typeof(IGameplayMcpGmCommand).IsAssignableFrom(type))
                    continue;

                GameplayMcpGmCommandAttribute attribute =
                    (GameplayMcpGmCommandAttribute)Attribute.GetCustomAttribute(
                        type,
                        typeof(GameplayMcpGmCommandAttribute));
                if (attribute == null || string.IsNullOrWhiteSpace(attribute.Name))
                    continue;

                if (Activator.CreateInstance(type) is not IGameplayMcpGmCommand handler)
                    continue;

                string name = NormalizeCommand(attribute.Name);
                if (registrations.ContainsKey(name))
                    throw new InvalidOperationException($"GamePlayMCP GM 命令重复注册：{name}");

                registrations.Add(name, new Registration
                {
                    Name = name,
                    Description = attribute.Description ?? string.Empty,
                    Handler = handler
                });
            }

            return registrations;
        }

        #endregion

        #region 管理员上下文

        /// <summary>解析当前本地玩家的管理员控制器。</summary>
        internal static bool TryGetAdminController(
            Player player,
            out PlayerAdminController controller,
            out string error)
        {
            controller = player?.GetComponentInChildren<PlayerAdminController>(true);
            if (player == null)
            {
                error = "当前本地玩家不可用。";
                return false;
            }

            if (controller == null)
            {
                error = "当前本地玩家缺少 PlayerAdminController。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>构造管理员与无敌状态，便于命令执行后立即验证。</summary>
        internal static JObject BuildStatus(
            string command,
            Player player,
            PlayerAdminController controller)
        {
            return new JObject
            {
                ["command"] = command,
                ["player_name"] = player?.Data?.Name_User ?? string.Empty,
                ["administrator"] = controller?.IsAdministrator ?? false,
                ["invincibility_enabled"] = controller?.IsAdminInvincibilityEnabled ?? false,
                ["runtime_only"] = true,
                ["hint"] = "Use command=disable_invincibility to turn it off; the state is not written to the player save."
            };
        }

        #endregion
    }

    #endregion

    #region 具体 GM 命令

    /// <summary>读取当前本地玩家管理员状态，不改变游戏状态。</summary>
    [GameplayMcpGmCommand(
        "status",
        "Read the current local player's administrator and runtime invincibility state.")]
    internal sealed class GameplayMcpGmStatusCommand : IGameplayMcpGmCommand
    {
        /// <summary>返回当前管理员状态。</summary>
        public bool TryExecute(Player player, JObject parameters, out JObject result, out string error)
        {
            if (!GameplayMcpGmCommandRegistry.TryGetAdminController(
                    player,
                    out PlayerAdminController controller,
                    out error))
            {
                result = null;
                return false;
            }

            result = GameplayMcpGmCommandRegistry.BuildStatus("status", player, controller);
            return true;
        }
    }

    /// <summary>启用当前本地玩家的管理员身份，但不自动打开无敌。</summary>
    [GameplayMcpGmCommand(
        "enable_admin",
        "Enable administrator mode for the current local player. This does not enable invincibility.")]
    internal sealed class GameplayMcpGmEnableAdminCommand : IGameplayMcpGmCommand
    {
        /// <summary>调用正式管理员入口。</summary>
        public bool TryExecute(Player player, JObject parameters, out JObject result, out string error)
        {
            if (!GameplayMcpGmCommandRegistry.TryGetAdminController(
                    player,
                    out PlayerAdminController controller,
                    out error))
            {
                result = null;
                return false;
            }

            if (!controller.TryEnableAdministrator())
            {
                result = null;
                error = "正式管理员入口拒绝了当前玩家。";
                return false;
            }

            result = GameplayMcpGmCommandRegistry.BuildStatus("enable_admin", player, controller);
            return true;
        }
    }

    /// <summary>一步启用管理员并开启运行时无敌，是 AI 直接启动无敌的主命令。</summary>
    [GameplayMcpGmCommand(
        "enable_invincibility",
        "Enable administrator mode if needed, then enable runtime administrator invincibility. Direct replacement for opening the GM UI.")]
    internal sealed class GameplayMcpGmEnableInvincibilityCommand : IGameplayMcpGmCommand
    {
        /// <summary>先满足管理员权限，再调用正式无敌入口。</summary>
        public bool TryExecute(Player player, JObject parameters, out JObject result, out string error)
        {
            if (!GameplayMcpGmCommandRegistry.TryGetAdminController(
                    player,
                    out PlayerAdminController controller,
                    out error))
            {
                result = null;
                return false;
            }

            if (!controller.IsAdministrator && !controller.TryEnableAdministrator())
            {
                result = null;
                error = "无法为当前玩家启用管理员身份，因此不能开启无敌。";
                return false;
            }

            if (!controller.TrySetAdminInvincibilityEnabled(true))
            {
                result = null;
                error = "正式管理员无敌入口拒绝了当前玩家。";
                return false;
            }

            result = GameplayMcpGmCommandRegistry.BuildStatus(
                "enable_invincibility",
                player,
                controller);
            return true;
        }
    }

    /// <summary>关闭当前管理员玩家的运行时无敌，保留管理员身份。</summary>
    [GameplayMcpGmCommand(
        "disable_invincibility",
        "Disable runtime administrator invincibility. Administrator mode remains enabled.")]
    internal sealed class GameplayMcpGmDisableInvincibilityCommand : IGameplayMcpGmCommand
    {
        /// <summary>调用正式无敌关闭入口。</summary>
        public bool TryExecute(Player player, JObject parameters, out JObject result, out string error)
        {
            if (!GameplayMcpGmCommandRegistry.TryGetAdminController(
                    player,
                    out PlayerAdminController controller,
                    out error))
            {
                result = null;
                return false;
            }

            if (!controller.IsAdministrator)
            {
                result = null;
                error = "当前玩家不是管理员；请先使用 command=enable_admin，或直接使用 command=enable_invincibility。";
                return false;
            }

            if (!controller.TrySetAdminInvincibilityEnabled(false))
            {
                result = null;
                error = "正式管理员无敌入口拒绝了当前玩家。";
                return false;
            }

            result = GameplayMcpGmCommandRegistry.BuildStatus(
                "disable_invincibility",
                player,
                controller);
            return true;
        }
    }

    /// <summary>切换当前管理员玩家的运行时无敌状态。</summary>
    [GameplayMcpGmCommand(
        "toggle_invincibility",
        "Toggle runtime administrator invincibility. Requires administrator mode; use enable_invincibility for one-step enable.")]
    internal sealed class GameplayMcpGmToggleInvincibilityCommand : IGameplayMcpGmCommand
    {
        /// <summary>调用正式无敌切换入口。</summary>
        public bool TryExecute(Player player, JObject parameters, out JObject result, out string error)
        {
            if (!GameplayMcpGmCommandRegistry.TryGetAdminController(
                    player,
                    out PlayerAdminController controller,
                    out error))
            {
                result = null;
                return false;
            }

            if (!controller.IsAdministrator)
            {
                result = null;
                error = "当前玩家不是管理员；请先使用 command=enable_admin，或直接使用 command=enable_invincibility。";
                return false;
            }

            if (!controller.TryToggleAdminInvincibility(out _))
            {
                result = null;
                error = "正式管理员无敌入口拒绝了当前玩家。";
                return false;
            }

            result = GameplayMcpGmCommandRegistry.BuildStatus(
                "toggle_invincibility",
                player,
                controller);
            return true;
        }
    }

    /// <summary>通过玩家现有管理员入口初始化创造背包，供受控调试流程取得测试物品。</summary>
    [GameplayMcpGmCommand(
        "initialize_creative_inventory",
        "Enable administrator mode if needed, then populate the current local player's creative inventory through Mod_PlayerTraits.InitializeCreativeInventoryForAdmin().")]
    internal sealed class GameplayMcpGmInitializeCreativeInventoryCommand : IGameplayMcpGmCommand
    {
        /// <summary>启用管理员后调用玩家模块已有的创造背包入口，不直接写库存槽位。</summary>
        public bool TryExecute(Player player, JObject parameters, out JObject result, out string error)
        {
            if (!GameplayMcpGmCommandRegistry.TryGetAdminController(
                    player,
                    out PlayerAdminController controller,
                    out error))
            {
                result = null;
                return false;
            }

            if (!controller.IsAdministrator && !controller.TryEnableAdministrator())
            {
                result = null;
                error = "无法为当前玩家启用管理员身份，因此不能初始化创造背包。";
                return false;
            }

            Mod_PlayerTraits playerTraits =
                player.itemMods?.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
            if (playerTraits == null)
            {
                result = null;
                error = "当前玩家缺少 Mod_PlayerTraits，无法初始化创造背包。";
                return false;
            }

            string summary = playerTraits.InitializeCreativeInventoryForAdmin();
            result = GameplayMcpGmCommandRegistry.BuildStatus(
                "initialize_creative_inventory",
                player,
                controller);
            result["summary"] = summary ?? string.Empty;
            return true;
        }
    }

    /// <summary>向当前受控玩家施加一个已注册 Buff，复用正式 BuffManager 生命周期。</summary>
    [GameplayMcpGmCommand(
        "apply_self_buff",
        "Apply one exact registered BuffDefinition id to the current controlled local player through BuffManager.AddBuff. Invoke as command=apply_self_buff:<buffId>.")]
    internal sealed class GameplayMcpGmApplySelfBuffCommand : IGameplayMcpGmCommand
    {
        /// <summary>校验目录与玩家模块后，通过正式 BuffManager 施加状态。</summary>
        public bool TryExecute(Player player, JObject parameters, out JObject result, out string error)
        {
            string buffId = parameters?["buffId"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(buffId))
            {
                result = null;
                error = "apply_self_buff 需要 buffId。";
                return false;
            }

            BuffDefinition definition = GameRes.ExistingInstance?.GetBuffDefinition(buffId);
            if (definition == null)
            {
                result = null;
                error = $"当前 GameRes 未注册 Buff：{buffId}。";
                return false;
            }

            BuffManager manager = player?.itemMods?.GetMod_ByID<BuffManager>(ModText.BuffManager);
            if (manager == null)
            {
                result = null;
                error = "当前玩家缺少 BuffManager。";
                return false;
            }

            bool applied = manager.AddBuff(definition.Id);
            bool active = manager.ActiveBuffs != null &&
                          manager.ActiveBuffs.ContainsKey(definition.Id);
            if (!applied && !active)
            {
                result = null;
                error = $"BuffManager 拒绝施加 Buff：{definition.Id}。";
                return false;
            }

            result = new JObject
            {
                ["command"] = "apply_self_buff",
                ["buff_id"] = definition.Id,
                ["display_name"] = definition.DisplayName ?? definition.Id,
                ["applied"] = applied,
                ["active"] = active,
                ["permanent"] = definition.IsPermanent
            };
            error = null;
            return true;
        }
    }

    #endregion
}
