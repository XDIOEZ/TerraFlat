using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using FlatWorld.Gameplay.Events;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GM 面板的斜杠命令模式。
/// 所有文本命令都来自显式注册表，只调用既有 GM / 玩法入口，不暴露任意 C#、反射类型名或私有字段写入能力。
/// </summary>
public sealed partial class GMReflectionConsole
{
    private readonly struct GmCommandResult
    {
        public readonly bool Success;
        public readonly string Message;

        private GmCommandResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        /// <summary>构造成功结果。</summary>
        public static GmCommandResult Ok(string message) => new(true, message);

        /// <summary>构造失败结果。</summary>
        public static GmCommandResult Fail(string message) => new(false, message);
    }

    private sealed class GmSlashCommand
    {
        public string Name;
        public string[] Aliases;
        public string Usage;
        public string Description;
        public Func<IReadOnlyList<string>, GmCommandResult> Execute;

        /// <summary>按命令名或别名匹配，不区分大小写。</summary>
        public bool Matches(string value)
        {
            if (string.Equals(Name, value, StringComparison.OrdinalIgnoreCase))
                return true;

            return Aliases != null && Aliases.Any(alias =>
                string.Equals(alias, value, StringComparison.OrdinalIgnoreCase));
        }
    }

    private readonly List<GmSlashCommand> gmSlashCommands = new();

    #region 命令注册与入口

    /// <summary>初始化斜杠命令注册表；命令只能绑定到显式注册的安全 GM 操作。</summary>
    private void EnsureSlashCommands()
    {
        if (gmSlashCommands.Count > 0)
            return;

        RegisterSlashCommand("help", new[] { "?", "commands", "帮助" }, "/help [command]", "查看命令列表或某条命令的用法。", ExecuteHelpCommand);
        RegisterSlashCommand("admin", new[] { "op", "管理员" }, "/admin", "将本地玩家设为管理员。", ExecuteAdminCommand);
        RegisterSlashCommand("weather", new[] { "天气" }, "/weather <clear|rain>", "强制切换晴天或雨天。", ExecuteWeatherCommand);
        RegisterSlashCommand("time", new[] { "timescale", "时间" }, "/time <faster|slower|add|reset> [value]", "调整游戏时间倍率。", ExecuteTimeCommand);
        RegisterSlashCommand("speed", new[] { "速度" }, "/speed <multiplier>", "设置管理员玩家移动速度倍率。", ExecutePlayerSpeedCommand);
        RegisterSlashCommand("chunkspeed", new[] { "区块速度" }, "/chunkspeed <multiplier|unlimited|reset>", "设置区块加载速度倍率。", ExecuteChunkSpeedCommand);
        RegisterSlashCommand("drop", new[] { "item", "物品" }, "/drop <itemId> [amount]", "在玩家附近直接生成物品。", ExecuteDropCommand);
        RegisterSlashCommand("summon", new[] { "召唤" }, "/summon <actorId> [amount]", "在玩家附近召唤 AI 生物。", ExecuteSummonCommand);
        RegisterSlashCommand("tp", new[] { "teleport", "传送" }, "/tp <x> <y> | /tp mouse", "把本地玩家传送到世界坐标或鼠标位置。", ExecuteTeleportCommand);
        RegisterSlashCommand("event", new[] { "事件" }, "/event <start|stop|toggle|reload> [eventId]", "强制控制全局游戏事件。", ExecuteGameEventCommand);
    }

    /// <summary>注册一条命令定义。</summary>
    private void RegisterSlashCommand(
        string name,
        string[] aliases,
        string usage,
        string description,
        Func<IReadOnlyList<string>, GmCommandResult> execute)
    {
        gmSlashCommands.Add(new GmSlashCommand
        {
            Name = name,
            Aliases = aliases ?? Array.Empty<string>(),
            Usage = usage,
            Description = description,
            Execute = execute
        });
    }

    /// <summary>
    /// 执行一条 GM 文本命令；这是 UI Enter、未来调试桥接和自动化可共用的正式入口。
    /// </summary>
    public bool TryExecuteCommand(string rawCommand)
    {
        EnsureSlashCommands();
        if (!TryTokenizeCommand(rawCommand, out List<string> tokens, out string parseError))
        {
            SetCommandResult(GmCommandResult.Fail(parseError), rawCommand);
            return false;
        }

        if (tokens.Count == 0)
        {
            SetCommandResult(GmCommandResult.Fail("请输入命令，例如 /help。"), rawCommand);
            return false;
        }

        string commandName = tokens[0].TrimStart('/');
        GmSlashCommand command = FindSlashCommand(commandName);
        if (command == null)
        {
            SetCommandResult(
                GmCommandResult.Fail($"未知命令：/{commandName}。输入 /help 查看可用命令。"),
                rawCommand);
            return false;
        }

        try
        {
            GmCommandResult result = command.Execute(tokens.Skip(1).ToArray());
            SetCommandResult(result, rawCommand);
            return result.Success;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SetCommandResult(
                GmCommandResult.Fail($"命令执行异常：{exception.Message}"),
                rawCommand);
            return false;
        }
    }

    /// <summary>查找命令定义。</summary>
    private GmSlashCommand FindSlashCommand(string name)
    {
        return gmSlashCommands.FirstOrDefault(command => command.Matches(name));
    }

    /// <summary>统一把命令结果写回 GM 状态栏和 Console。</summary>
    private void SetCommandResult(GmCommandResult result, string rawCommand)
    {
        Color color = result.Success ? GmAccentHover : GmDanger;
        SetStatus(result.Message, color);
        Debug.Log($"[GM Command] {rawCommand?.Trim()} => {(result.Success ? "OK" : "FAIL")} · {result.Message}");
    }

    #endregion

    #region 命令输入 UI

    /// <summary>搜索框 Enter：只有以 / 开头的文本才作为命令执行，普通搜索保持原行为。</summary>
    private void HandleGlobalSearchSubmit(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.TrimStart().StartsWith("/", StringComparison.Ordinal))
            return;

        TryExecuteCommand(value);
        if (gmSearchInput == null)
            return;

        gmSearchInput.SetTextWithoutNotify(string.Empty);
        if (gmSearchResultsRoot != null)
            gmSearchResultsRoot.SetActive(false);
        if (gmSearchSummaryText != null)
            gmSearchSummaryText.text = "输入名称，或 /help";
        gmSearchInput.ActivateInputField();
    }

    /// <summary>命令模式下用同一个浮层展示命令补全，不再把 /xxx 当作普通功能搜索。</summary>
    private bool TryRebuildSlashCommandSuggestions(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || !query.StartsWith("/", StringComparison.Ordinal))
            return false;

        EnsureSlashCommands();
        string commandPrefix = query.Substring(1)
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        List<GmSlashCommand> matches = gmSlashCommands
            .Where(command => commandPrefix.Length == 0 ||
                              command.Name.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase) ||
                              command.Aliases.Any(alias => alias.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase)))
            .Take(MaxVisibleSearchResults)
            .ToList();

        gmSearchResultsRoot.SetActive(true);
        gmSearchResultsRoot.transform.SetAsLastSibling();
        float resultsHeight = Mathf.Clamp(matches.Count * 36f + 18f, 56f, 158f);
        ConfigureSearchResultsOverlay(gmSearchResultsRect, resultsHeight);
        gmSearchSummaryText.text = "命令模式 · Enter 执行";

        if (matches.Count == 0)
        {
            AddPageHint(gmSearchResultsContent, "没有匹配命令。输入 /help 查看全部命令。", 32f);
            return true;
        }

        for (int i = 0; i < matches.Count; i++)
        {
            GmSlashCommand command = matches[i];
            Button button = CreateButton(
                gmSearchResultsContent,
                $"{command.Usage}  —  {command.Description}",
                () => FillSlashCommand(command),
                0f,
                32f);
            button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true).alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(gmSearchResultsContent as RectTransform);
        return true;
    }

    /// <summary>把命令模板写回顶部输入框，继续填写参数。</summary>
    private void FillSlashCommand(GmSlashCommand command)
    {
        if (gmSearchInput == null || command == null)
            return;

        gmSearchInput.SetTextWithoutNotify($"/{command.Name} ");
        gmSearchInput.caretPosition = gmSearchInput.text.Length;
        gmSearchInput.ActivateInputField();
        RebuildGlobalSearchResults();
    }

    /// <summary>在“命令”页展示文本命令参考，点击即可把命令填到顶部输入框。</summary>
    private void BuildSlashCommandReference(Transform parent)
    {
        EnsureSlashCommands();
        AddPageHint(parent, "文本命令与按钮调用同一批 GM 能力；输入 /help <command> 可查看单条用法。", 30f);
        Transform grid = CreateActionGrid(parent, 2, 516f, 44f, gmSlashCommands.Count);
        for (int i = 0; i < gmSlashCommands.Count; i++)
        {
            GmSlashCommand command = gmSlashCommands[i];
            Button button = CreateButton(grid, command.Usage, () => FillSlashCommand(command), 0f, 42f);
            RegisterSearchEntry(
                GmPageId.Commands,
                command.Usage,
                $"{command.Description} {string.Join(" ", command.Aliases)} slash command 文本命令",
                button.transform as RectTransform);
        }
    }

    #endregion

    #region 命令实现

    /// <summary>显示命令列表或单条帮助。</summary>
    private GmCommandResult ExecuteHelpCommand(IReadOnlyList<string> args)
    {
        if (args.Count > 0)
        {
            GmSlashCommand target = FindSlashCommand(args[0].TrimStart('/'));
            return target == null
                ? GmCommandResult.Fail($"没有命令：{args[0]}。")
                : GmCommandResult.Ok($"{target.Usage} · {target.Description}");
        }

        string fullHelp = string.Join("\n", gmSlashCommands.Select(command => $"{command.Usage}  {command.Description}"));
        Debug.Log("[GM Command] 可用命令：\n" + fullHelp);
        return GmCommandResult.Ok("可用命令：/admin /weather /time /speed /chunkspeed /drop /summon /tp /event。输入 /help <命令> 查看参数。");
    }

    /// <summary>启用管理员身份。</summary>
    private GmCommandResult ExecuteAdminCommand(IReadOnlyList<string> args)
    {
        if (args.Count != 0)
            return GmCommandResult.Fail("用法：/admin");

        PlayerAdminController controller = FindFirstComponent("PlayerAdminController") as PlayerAdminController;
        if (controller == null || !controller.TryEnableAdministrator())
            return GmCommandResult.Fail("未找到本地玩家，无法启用管理员。 ");

        RefreshAdminInvincibilityButton();
        return GmCommandResult.Ok("管理员已启用。 ");
    }

    /// <summary>切换调试天气。</summary>
    private GmCommandResult ExecuteWeatherCommand(IReadOnlyList<string> args)
    {
        if (args.Count != 1)
            return GmCommandResult.Fail("用法：/weather <clear|rain>");

        GameDebugManager manager = FindFirstComponent("GameDebugManager") as GameDebugManager;
        if (manager == null || WeatherMgr.Instance == null)
            return GmCommandResult.Fail("天气系统尚未就绪。 ");

        switch (args[0].ToLowerInvariant())
        {
            case "clear":
            case "sun":
            case "晴":
            case "晴天":
                manager.SetClearWeather();
                return GmCommandResult.Ok("天气已切换为晴天。 ");
            case "rain":
            case "雨":
            case "下雨":
                manager.SetRainWeather();
                return GmCommandResult.Ok("天气已切换为雨天。 ");
            default:
                return GmCommandResult.Fail("天气只支持 clear 或 rain。 ");
        }
    }

    /// <summary>通过现有 PlayerAdminController 调整时间倍率。</summary>
    private GmCommandResult ExecuteTimeCommand(IReadOnlyList<string> args)
    {
        if (args.Count < 1 || args.Count > 2)
            return GmCommandResult.Fail("用法：/time <faster|slower|add|reset> [value]");

        string action = args[0].ToLowerInvariant();
        if (action == "reset" || action == "normal" || action == "重置")
        {
            if (!TryInvokeGmMethod("PlayerAdminController", "ResetTimeScale", Array.Empty<object>(), out _, out string resetError))
                return GmCommandResult.Fail(resetError);
            return GmCommandResult.Ok("时间倍率已重置为 1x。 ");
        }

        float delta = 0.5f;
        if (action == "add")
        {
            if (args.Count != 2 || !TryParseFiniteFloat(args[1], out delta))
                return GmCommandResult.Fail("用法：/time add <delta>");
        }
        else if (args.Count == 2)
        {
            if (!TryParseFiniteFloat(args[1], out float step) || step <= 0f)
                return GmCommandResult.Fail("时间步长必须是大于 0 的数字。 ");
            delta = step;
        }

        if (action == "slower" || action == "slow" || action == "减速")
            delta = -Mathf.Abs(delta);
        else if (action == "faster" || action == "fast" || action == "加速")
            delta = Mathf.Abs(delta);
        else if (action != "add")
            return GmCommandResult.Fail("时间操作只支持 faster、slower、add 或 reset。 ");

        if (!TryInvokeGmMethod("PlayerAdminController", "TryUpdateTimeScale", new object[] { delta }, out object result, out string error))
            return GmCommandResult.Fail(error);
        if (result is bool changed && !changed)
            return GmCommandResult.Fail("时间倍率已到允许范围边界，没有继续变化。 ");

        return GmCommandResult.Ok($"时间倍率已调整 {delta:+0.##;-0.##;0}。 ");
    }

    /// <summary>设置玩家移动速度倍率。</summary>
    private GmCommandResult ExecutePlayerSpeedCommand(IReadOnlyList<string> args)
    {
        if (args.Count != 1 || !TryParseFiniteFloat(args[0], out float multiplier))
            return GmCommandResult.Fail("用法：/speed <multiplier>");

        PlayerAdminController controller = FindFirstComponent("PlayerAdminController") as PlayerAdminController;
        if (controller == null || !controller.TrySetAdminMoveSpeedMultiplier(multiplier, out float appliedMultiplier))
            return GmCommandResult.Fail("玩家移动模块尚未就绪。 ");

        GMConsolePreferences.SetPlayerMoveSpeed(appliedMultiplier);
        RefreshPlayerMoveSpeedButton();
        return GmCommandResult.Ok($"玩家移动速度已设置为 {appliedMultiplier:0.##}x。 ");
    }

    /// <summary>设置区块加载速度。</summary>
    private GmCommandResult ExecuteChunkSpeedCommand(IReadOnlyList<string> args)
    {
        if (args.Count != 1)
            return GmCommandResult.Fail("用法：/chunkspeed <multiplier|unlimited|reset>");

        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null)
            return GmCommandResult.Fail("区块管理器尚未就绪。 ");

        string value = args[0].Trim();
        float requested;
        if (value.Equals("unlimited", StringComparison.OrdinalIgnoreCase) || value == "无限")
            requested = float.PositiveInfinity;
        else if (value.Equals("reset", StringComparison.OrdinalIgnoreCase) || value == "重置")
            requested = 1f;
        else if (!TryParseFiniteFloat(value, out requested))
            return GmCommandResult.Fail("区块速度必须是数字、unlimited 或 reset。 ");

        if (!manager.TrySetChunkLoadSpeedMultiplier(requested, out float appliedMultiplier))
            return GmCommandResult.Fail("区块加载速度调整失败。 ");

        GMConsolePreferences.SetChunkLoadSpeed(appliedMultiplier, manager.IsChunkLoadSpeedUnlimited);
        RefreshChunkLoadSpeedControl();
        return GmCommandResult.Ok(manager.IsChunkLoadSpeedUnlimited
            ? "区块加载速度已设为无限。 "
            : $"区块加载速度已设为 {appliedMultiplier:0.##}x。 ");
    }

    /// <summary>在玩家附近直接生成物品。</summary>
    private GmCommandResult ExecuteDropCommand(IReadOnlyList<string> args)
    {
        if (args.Count < 1 || args.Count > 2)
            return GmCommandResult.Fail("用法：/drop <itemId> [amount]");

        int amount = 1;
        if (args.Count == 2 && (!int.TryParse(args[1], out amount) || amount < 1 || amount > 9999))
            return GmCommandResult.Fail("物品数量必须在 1-9999 之间。 ");

        RefreshItemIds();
        AirdropItemEntry entry = availableAirdropItems.FirstOrDefault(item =>
            string.Equals(item.ItemId, args[0], StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return GmCommandResult.Fail($"没有可生成的物品 ID：{args[0]}。 ");

        Transform player = GetLocalPlayerTransform();
        if (player == null)
            return GmCommandResult.Fail("未找到本地玩家，无法确定生成位置。 ");

        Vector2 direction = UnityEngine.Random.insideUnitCircle;
        if (direction.sqrMagnitude < 0.001f)
            direction = Vector2.right;
        Vector3 position = player.position + (Vector3)(direction.normalized * 1.25f);
        position.z = player.position.z;

        return TrySpawnItemThroughReflection(entry.ItemId, amount, position, out string result)
            ? GmCommandResult.Ok(result)
            : GmCommandResult.Fail(result);
    }

    /// <summary>在玩家附近批量召唤 AI 生物。</summary>
    private GmCommandResult ExecuteSummonCommand(IReadOnlyList<string> args)
    {
        if (args.Count < 1 || args.Count > 2)
            return GmCommandResult.Fail("用法：/summon <actorId> [amount]");

        int amount = 1;
        if (args.Count == 2 && (!int.TryParse(args[1], out amount) || amount < 1 || amount > 20))
            return GmCommandResult.Fail("生物数量必须在 1-20 之间。 ");

        RefreshAiCreatureIds();
        AiCreatureEntry entry = availableAiCreatures.FirstOrDefault(creature =>
            string.Equals(creature.ItemId, args[0], StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return GmCommandResult.Fail($"没有可召唤的 AI 生物 ID：{args[0]}。 ");

        Transform player = GetLocalPlayerTransform();
        ItemMgr itemManager = ItemMgr.Instance;
        if (player == null || itemManager == null)
            return GmCommandResult.Fail("玩家或 ItemMgr 尚未就绪。 ");

        int spawnedCount = 0;
        string firstFailure = null;
        for (int i = 0; i < amount; i++)
        {
            Vector3 spawnPosition = GetAiCreatureSpawnPosition(player.position, i, amount);
            if (TrySpawnInitializedAiCreature(itemManager, entry.ItemId, spawnPosition, out _, out string spawnError))
                spawnedCount++;
            else if (firstFailure == null)
                firstFailure = spawnError;
        }

        if (spawnedCount == amount)
            return GmCommandResult.Ok($"召唤成功：{entry.DisplayName} × {spawnedCount}");
        return GmCommandResult.Fail(spawnedCount > 0
            ? $"部分召唤成功：{entry.DisplayName} {spawnedCount}/{amount}；{firstFailure}"
            : $"召唤失败：{firstFailure}");
    }

    /// <summary>传送玩家到鼠标或明确世界坐标。</summary>
    private GmCommandResult ExecuteTeleportCommand(IReadOnlyList<string> args)
    {
        if (args.Count == 1 && args[0].Equals("mouse", StringComparison.OrdinalIgnoreCase))
        {
            return TryInvokeGmMethod("Mod_PlayerTraits", "TeleportToMousePosition", Array.Empty<object>(), out _, out string mouseError)
                ? GmCommandResult.Ok("已传送到鼠标位置。 ")
                : GmCommandResult.Fail(mouseError);
        }

        if (args.Count != 2 ||
            !TryParseFiniteFloat(args[0], out float x) ||
            !TryParseFiniteFloat(args[1], out float y))
        {
            return GmCommandResult.Fail("用法：/tp <x> <y> 或 /tp mouse");
        }

        Transform playerTransform = GetLocalPlayerTransform();
        if (playerTransform == null)
            return GmCommandResult.Fail("未找到本地玩家。 ");

        Vector3 destination = new(x, y, playerTransform.position.z);
        Rigidbody2D body = playerTransform.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = destination;
        }

        playerTransform.position = destination;
        Player player = playerTransform.GetComponent<Player>() ?? playerTransform.GetComponentInParent<Player>();
        if (player?.Data != null)
            player.Data.transform.position = destination;

        ChunkMgr.Instance?.ResetChunkLoadQueue();
        Mod_ChunkLoader chunkLoader = playerTransform.GetComponentInChildren<Mod_ChunkLoader>(true) ??
                                      playerTransform.GetComponentInParent<Mod_ChunkLoader>();
        chunkLoader?.RefreshChunksAroundPlayer();
        return GmCommandResult.Ok($"已传送到 ({x:0.##}, {y:0.##})。 ");
    }

    /// <summary>控制全局游戏事件。</summary>
    private GmCommandResult ExecuteGameEventCommand(IReadOnlyList<string> args)
    {
        if (args.Count < 1 || args.Count > 2)
            return GmCommandResult.Fail("用法：/event <start|stop|toggle|reload> [eventId]");

        BindGameEventManager();
        GameEventManager manager = boundGameEventManager;
        if (manager == null)
            return GmCommandResult.Fail("游戏事件管理器尚未就绪。 ");

        string action = args[0].ToLowerInvariant();
        if (action == "reload" || action == "重载")
        {
            if (args.Count != 1)
                return GmCommandResult.Fail("用法：/event reload");
            GameEventConfigLoadResult result = manager.ReloadConfiguration();
            RebuildGameEventPage();
            return result.HasErrors
                ? GmCommandResult.Fail($"事件配置已重载，但发现 {result.Issues.Count} 个配置问题，请查看 Console。")
                : GmCommandResult.Ok($"事件配置已重载，共 {manager.Definitions.Count} 个事件。 ");
        }

        if (args.Count != 2)
            return GmCommandResult.Fail($"用法：/event {action} <eventId>");

        string eventId = args[1];
        bool active = manager.IsEventActive(eventId);
        bool success;
        switch (action)
        {
            case "start":
            case "触发":
                if (active)
                    return GmCommandResult.Ok($"事件已在运行：{eventId}");
                success = manager.TryForceTriggerNow(eventId);
                break;
            case "stop":
            case "结束":
                if (!active)
                    return GmCommandResult.Fail($"事件当前未运行：{eventId}");
                success = manager.CancelEvent(eventId);
                break;
            case "toggle":
            case "切换":
                success = active ? manager.CancelEvent(eventId) : manager.TryForceTriggerNow(eventId);
                break;
            default:
                return GmCommandResult.Fail("事件操作只支持 start、stop、toggle 或 reload。 ");
        }

        RequestGameEventPageRefresh();
        return success
            ? GmCommandResult.Ok($"事件操作成功：{action} {eventId}")
            : GmCommandResult.Fail($"事件操作失败：{eventId}。请检查事件 ID、世界状态和权限。 ");
    }

    #endregion

    #region 解析与安全调用

    /// <summary>解析 Minecraft 风格的空格参数，并支持单双引号包裹含空格文本。</summary>
    private static bool TryTokenizeCommand(string rawCommand, out List<string> tokens, out string error)
    {
        tokens = new List<string>();
        error = null;
        string input = rawCommand?.Trim();
        if (string.IsNullOrWhiteSpace(input))
            return true;
        if (input.StartsWith("/", StringComparison.Ordinal))
            input = input.Substring(1);

        var current = new StringBuilder();
        char quote = '\0';
        for (int i = 0; i < input.Length; i++)
        {
            char character = input[i];
            if (quote != '\0')
            {
                if (character == quote)
                    quote = '\0';
                else
                    current.Append(character);
                continue;
            }

            if (character == '"' || character == '\'')
            {
                quote = character;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (current.Length == 0)
                    continue;
                tokens.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (quote != '\0')
        {
            error = "命令参数存在未闭合的引号。 ";
            return false;
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());
        return true;
    }

    /// <summary>
    /// 仅供已注册命令适配既有 GM 私有方法；类型名与方法名由代码固定，用户文本不能控制反射目标。
    /// </summary>
    private static bool TryInvokeGmMethod(
        string typeName,
        string methodName,
        object[] arguments,
        out object result,
        out string error)
    {
        result = null;
        Component target = FindFirstComponent(typeName);
        MethodInfo method = target != null
            ? FindCompatibleMethod(target.GetType(), methodName, arguments)
            : null;
        if (target == null || method == null)
        {
            error = $"未找到可用指令：{typeName}.{methodName}";
            return false;
        }

        try
        {
            result = method.Invoke(target, arguments ?? Array.Empty<object>());
            error = null;
            return true;
        }
        catch (TargetInvocationException exception)
        {
            error = exception.InnerException?.Message ?? exception.Message;
            Debug.LogException(exception.InnerException ?? exception);
            return false;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            Debug.LogException(exception);
            return false;
        }
    }

    #endregion
}
