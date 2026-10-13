using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XLua;

/// <summary>Lua 只编排测试步骤，玩家操作始终通过正式玩法接口逐帧执行。</summary>
public sealed class GameplayTestLuaRunner : IDisposable
{
    #region 测试上下文与限制

    private const int MaximumSteps = 256;
    private const int MaximumSourceCharacters = 1024 * 1024;
    private const float MaximumStepSeconds = 30f;
    private const float MaximumTotalSeconds = 180f;
    private Player player;
    private readonly Func<string, bool> check;
    private readonly Action<string, string, bool, string> record;
    private Mod_GameController controller;
    private Mod_Mover mover;
    private Mod_GameMCP_LLM navigation;
    private bool previousRunState;
    private bool controlAcquired;
    private bool running;
    private bool disposed;
    private Mod_PlayerAdminController admin;
    private float initialAdminSpeed;
    private float initialHealth;
    private bool initialInvincibility;
    private bool gmSpeedChanged;
    private bool gmHealthChanged;
    private bool gmInvincibilityChanged;
    private readonly HashSet<string> createdBuffs = new(StringComparer.OrdinalIgnoreCase);
    private LuaEnv luaEnvironment;
    private LuaFunction executeLua;
    private readonly List<LuaFunction> callbacks = new();

    private sealed class Command
    {
        public string Name;
        public string Label;
        public string Text;
        public string Argument;
        public LuaFunction Validator;
        public float X;
        public float Y;
        public float Seconds;
        public float Tolerance;
        public int Index;
        public bool Success;
        public string Message = string.Empty;
    }

    public bool Succeeded { get; private set; }

    public GameplayTestLuaRunner(Player player, Func<string, bool> check,
        Action<string, string, bool, string> record)
    {
        this.player = player;
        this.check = check ?? throw new ArgumentNullException(nameof(check));
        this.record = record ?? throw new ArgumentNullException(nameof(record));
    }

    /// <summary>EditMode 共用正式 Lua 解析和限额检查，但不获取玩家也不执行任何玩法动作。</summary>
    public static void ValidateScript(string source, string chunkName)
    {
        string failure = null;
        using var validator = new GameplayTestLuaRunner(null, _ => true,
            (_, _, success, message) => { if (!success) failure = message; });
        if (!validator.TryReadCommands(source, chunkName, out _))
            throw new InvalidOperationException("测试 Lua 预检失败：" + (failure ?? chunkName));
    }

    #endregion

    #region Lua 有界流程编排

    // 预算钩子只在宿主侧可见，脚本不能关闭钩子或吞掉预算错误。
    private const string Bootstrap = @"
        local commands = {}
        local totalSeconds = 0
        local instructionCount = 0
        local function finite(value, name)
            assert(type(value) == 'number' and value == value
                and value ~= math.huge and value ~= -math.huge,
                name .. ' 必须是有限数字')
            return value
        end
        local function text(value, name)
            assert(type(value) == 'string' and #value > 0 and #value <= 4096,
                name .. ' 必须是 1 至 4096 字节的文字')
            return value
        end
        local function duration(value)
            finite(value, 'seconds')
            assert(value >= 0 and value <= 30, '单步时间必须在 0 至 30 秒之间')
            totalSeconds = totalSeconds + value
            assert(totalSeconds <= 180, '测试累计时间超过 180 秒')
            return value
        end
        local function enqueue(command)
            assert(#commands < 256, '测试流程超过 256 步')
            command.label = command.label or command.name
            commands[#commands + 1] = command
        end
        local test = {}
        function test.check(name, label)
            enqueue({name='check', text=text(name, 'check name'),
                label=label == nil and name or text(label, 'label')})
        end
        function test.log(message)
            enqueue({name='log', text=text(message, 'log text')})
        end
        function test.wait(seconds)
            enqueue({name='wait', seconds=duration(seconds)})
        end
        function test.move(x, y, seconds)
            finite(x, 'x'); finite(y, 'y')
            assert(x ~= 0 or y ~= 0, 'move 需要非零方向')
            assert(seconds ~= nil and seconds > 0, 'move 时间必须大于零')
            enqueue({name='move', x=x, y=y, seconds=duration(seconds)})
        end
        function test.move_to(x, y, timeout, tolerance)
            timeout = timeout == nil and 10 or timeout
            tolerance = tolerance == nil and 0.25 or tolerance
            finite(tolerance, 'tolerance')
            assert(tolerance >= 0.05 and tolerance <= 2,
                'move_to 到达距离必须在 0.05 至 2 之间')
            assert(timeout > 0, 'move_to 超时必须大于零')
            enqueue({name='move_to', x=finite(x, 'x'), y=finite(y, 'y'),
                seconds=duration(timeout), tolerance=tolerance})
        end
        function test.look_at(x, y)
            enqueue({name='look_at', x=finite(x, 'x'), y=finite(y, 'y')})
        end
        function test.interact() enqueue({name='interact'}) end
        function test.select_hotbar(index)
            finite(index, 'index')
            assert(index >= 0 and index == math.floor(index),
                '快捷栏下标必须是从零开始的整数')
            enqueue({name='select_hotbar', index=index})
        end
        function test.use() enqueue({name='use'}) end
        function test.fluid(scenario, label)
            scenario = text(scenario, 'fluid scenario')
            enqueue({name='fluid', text=scenario,
                label=label == nil and scenario or text(label, 'label')})
        end
        function test.gm(command, argument)
            command = text(command, 'GM command')
            if argument ~= nil then argument = text(argument, 'GM argument') end
            enqueue({name='gm', text=command, argument=argument, label='GM: ' .. command})
        end
        function test.space(operation, validator, label, argument)
            operation = text(operation, 'space operation')
            assert(type(validator) == 'function', 'space 必须提供 Lua 断言回调')
            if argument ~= nil then argument = text(argument, 'space argument') end
            enqueue({name='space', text=operation, argument=argument, validator=validator,
                label=label == nil and operation or text(label, 'label')})
        end
        local function copy(source)
            local target = {}; for key, value in pairs(source or {}) do target[key] = value end
            return target
        end
        local scope = {test=test, assert=assert, error=error, ipairs=ipairs, pairs=pairs,
            next=next, select=select, tonumber=tonumber, tostring=tostring, type=type,
            math=copy(math), string=copy(string), table=copy(table), utf8=copy(utf8)}
        scope._G = scope
        local function execute(chunk, ...)
            local arguments = {...}
            instructionCount = 0
            debug.sethook(function()
                instructionCount = instructionCount + 1000
                if instructionCount >= 1000000 then error('测试 Lua 超过 100 万条指令预算', 0) end
            end, '', 1000)
            local ok, result = xpcall(function() return chunk(table.unpack(arguments)) end, function(message)
                debug.sethook()
                return debug.traceback(tostring(message), 2)
            end)
            debug.sethook()
            return ok, result
        end
        return scope, commands, execute
    ";

    private bool TryReadCommands(string source, string chunkName, out List<Command> commands)
    {
        commands = new List<Command>();
        try
        {
            if (string.IsNullOrWhiteSpace(source) || source.Length > MaximumSourceCharacters)
                throw new InvalidOperationException("测试 Lua 不能为空，且不能超过 1MB 字符。");

            ReleaseLua();
            luaEnvironment = new LuaEnv();
            object[] bootstrap = luaEnvironment.DoString(Bootstrap, "GameplayTestBootstrap");
            using LuaTable scope = (LuaTable)bootstrap[0];
            using LuaTable queue = (LuaTable)bootstrap[1];
            executeLua = (LuaFunction)bootstrap[2];
            using LuaFunction script = luaEnvironment.LoadString(source,
                string.IsNullOrWhiteSpace(chunkName) ? "@GameplayTest.lua" : chunkName, scope);
            object[] result = executeLua.Call(script);
            if (result.Length == 0 || result[0] is not bool success || !success)
                throw new InvalidOperationException(result.Length > 1
                    ? result[1]?.ToString() : "测试 Lua 未返回执行状态。");

            float totalSeconds = 0f;
            for (int index = 1; index <= MaximumSteps + 1; index++)
            {
                queue.Get(index, out LuaTable queuedStep);
                using LuaTable step = queuedStep;
                if (step == null) break;
                if (index > MaximumSteps) throw new InvalidOperationException("测试流程超过 256 步。");
                Command command = new()
                {
                    Name = step.Get<string>("name"),
                    Label = step.Get<string>("label"),
                    Text = step.Get<string>("text"),
                    Argument = step.Get<string>("argument"),
                    X = ReadNumber(step, "x"),
                    Y = ReadNumber(step, "y"),
                    Seconds = ReadNumber(step, "seconds"),
                    Tolerance = ReadNumber(step, "tolerance"),
                    Index = checked((int)ReadNumber(step, "index"))
                };
                totalSeconds += command.Seconds;
                if (command.Seconds < 0f || command.Seconds > MaximumStepSeconds ||
                    totalSeconds > MaximumTotalSeconds)
                    throw new InvalidOperationException("测试时间超过单步 30 秒或累计 180 秒限制。");
                if (command.Name == "gm" &&
                    !GameplayTestGm.TryValidate(command.Text, command.Argument, out string gmError))
                    throw new InvalidOperationException(gmError);
                if (command.Name == "fluid" && !GameplayTestFluids.IsScenario(command.Text))
                    throw new InvalidOperationException("未知流体测试案例：" + command.Text);
                if (command.Name == "space")
                {
                    if (!GameplayTestSpaceScenario.TryValidate(command.Text, command.Argument, out string spaceError))
                        throw new InvalidOperationException(spaceError);
                    command.Validator = step.Get<LuaFunction>("validator") ??
                        throw new InvalidOperationException("太空测试缺少 Lua 断言回调。");
                    callbacks.Add(command.Validator);
                }
                commands.Add(command);
            }
            if (commands.Count == 0) throw new InvalidOperationException("测试 Lua 没有安排任何步骤。");
            return true;
        }
        catch (Exception exception)
        {
            record("lua", "读取测试 Lua", false, exception.ToString());
            return false;
        }
    }

    private static float ReadNumber(LuaTable table, string name)
    {
        object value = table.Get<object>(name);
        if (value == null) return 0f;
        float number = Convert.ToSingle(value, CultureInfo.InvariantCulture);
        if (float.IsNaN(number) || float.IsInfinity(number))
            throw new InvalidOperationException($"测试参数 {name} 超出有限数字范围。");
        return number;
    }

    #endregion

    #region 流程逐帧执行

    public IEnumerator Run(string source, string chunkName)
    {
        if (disposed || running)
        {
            record("runner", "开始测试", false, disposed ? "执行器已释放。" : "执行器正在运行。");
            yield break;
        }

        running = true;
        Succeeded = false;
        try
        {
            if (!TryReadCommands(source, chunkName, out List<Command> commands)) yield break;
            if (!TryAcquireControl(out string error))
            {
                record("control", "接管测试玩家", false, error);
                yield break;
            }

            bool fluidFailed = false;
            foreach (Command command in commands)
            {
                if (!TryEnsureContext(out error))
                {
                    record(command.Name, command.Label, false, error);
                    yield break;
                }

                IEnumerator operation = ExecuteCommand(command);
                try
                {
                    while (true)
                    {
                        bool hasNext = false;
                        object current = null;
                        try
                        {
                            hasNext = operation.MoveNext();
                            if (hasNext) current = operation.Current;
                        }
                        catch (Exception exception)
                        {
                            command.Success = false;
                            command.Message = exception.ToString();
                        }
                        if (!hasNext) break;
                        yield return current;
                    }
                }
                finally { (operation as IDisposable)?.Dispose(); }

                record(command.Name, command.Label, command.Success, command.Message);
                // 流体子案例各自隔离并清理，失败后继续收集同组报告，但不能把整组改成通过。
                if (!command.Success)
                {
                    if (command.Name != "fluid") yield break;
                    fluidFailed = true;
                }
                yield return null;
            }
            Succeeded = !fluidFailed;
        }
        finally
        {
            ReleaseControl();
            ReleaseLua();
            running = false;
        }
    }

    private IEnumerator ExecuteCommand(Command command)
    {
        switch (command.Name)
        {
            case "check":
                command.Success = check(command.Text);
                command.Message = command.Success ? $"检查通过：{command.Text}" : $"检查失败：{command.Text}";
                break;
            case "fluid":
                command.Success = GameplayTestFluids.Run(command.Text, record, out command.Message);
                break;
            case "gm":
                // 用例失败或中断时仍可还原 GM 制造的临时条件。
                gmSpeedChanged |= command.Text == "set_move_speed";
                gmHealthChanged |= command.Text == "set_health_ratio";
                gmInvincibilityChanged |= command.Text == "enable_invincibility" ||
                                         command.Text == "disable_invincibility";
                if (command.Text == "apply_self_buff" &&
                    player.itemMods?.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager)?.HasBuff(command.Argument) == false)
                    createdBuffs.Add(command.Argument);
                command.Success = GameplayTestGm.TryExecute(player, command.Text,
                    command.Argument, out command.Message);
                break;
            case "space":
                IEnumerator probe = RunSpaceCommand(command);
                try { while (probe.MoveNext()) yield return probe.Current; }
                finally { (probe as IDisposable)?.Dispose(); }
                break;
            case "log":
                Debug.Log($"[GameplayTest Lua] {command.Text}");
                command.Success = true;
                command.Message = command.Text;
                break;
            case "wait":
            case "move":
                bool moving = command.Name == "move";
                if (moving && !controller.TrySetExternalMoveInput(this,
                        Vector2.ClampMagnitude(new Vector2(command.X, command.Y), 1f)))
                {
                    command.Message = "玩家拒绝了移动输入。";
                    yield break;
                }
                double deadline = Time.realtimeSinceStartupAsDouble + command.Seconds;
                try
                {
                    while (Time.realtimeSinceStartupAsDouble < deadline)
                    {
                        if (!TryEnsureContext(out command.Message)) yield break;
                        yield return null;
                    }
                    command.Success = TryEnsureContext(out command.Message);
                    if (command.Success) command.Message = $"完成 {command.Seconds:0.###} 秒{(moving ? "移动" : "等待")}。";
                }
                finally
                {
                    if (moving && controller != null && controller.IsExternalGameplayControlOwner(this))
                        controller.TrySetExternalMoveInput(this, Vector2.zero);
                }
                break;
            case "move_to":
                IEnumerator moveTo = MoveTo(command);
                try { while (moveTo.MoveNext()) yield return moveTo.Current; }
                finally { (moveTo as IDisposable)?.Dispose(); }
                break;
            case "look_at":
                command.Success = controller.TrySetExternalAimWorldPosition(this,
                    new Vector3(command.X, command.Y, 0f));
                command.Message = command.Success ? "已设置瞄准位置。" : "玩家拒绝了瞄准位置。";
                break;
            case "interact":
                Mod_InteractSender sender = player.GetComponentInChildren<Mod_InteractSender>(true);
                command.Success = sender != null && sender.TryInteractAtCurrentPosition();
                command.Message = command.Success ? "已执行正式交互。" : "当前位置没有可以交互的目标，或玩家拒绝交互。";
                break;
            case "select_hotbar":
                Mod_HotBar hotbar = player.GetComponentInChildren<Mod_HotBar>(true);
                command.Success = hotbar != null && hotbar.TrySelectSlot(command.Index);
                command.Message = command.Success ? $"已选择快捷栏 {command.Index}。" : $"无法选择快捷栏 {command.Index}。";
                break;
            case "use":
                Item held = player.GetComponentInChildren<Mod_HotBar>(true)?.CurentSelectItem;
                if (held == null)
                {
                    command.Message = "当前没有可使用的手持物品。";
                    break;
                }
                held.Act();
                command.Success = true;
                command.Message = $"已调用手持物品的正式使用入口：{held.itemData?.IDName}";
                break;
            default:
                command.Message = $"未知测试命令：{command.Name}";
                break;
        }
    }

    #region 太空数据与 Lua 断言
    private IEnumerator RunSpaceCommand(Command command)
    {
        JObject sample = null;
        if (GameplayTestUniverseProbe.Supports(command.Text))
            sample = GameplayTestUniverseProbe.Capture(command.Text);
        else if (GameplayTestShipProbe.Supports(command.Text))
            sample = GameplayTestShipProbe.Capture(command.Text);
        else
        {
            // 迁移会重建玩家，先交还旧租约，再从正式会话取得新玩家。
            ReleaseControl();
            using var scenario = new GameplayTestSpaceScenario();
            yield return scenario.Execute(command.Text, command.Argument, value => sample = value);
            player = ItemMgr.Instance?.User_Player;
            if (!TryAcquireControl(out string error)) throw new InvalidOperationException(error);
        }
        if (sample == null) throw new InvalidOperationException("太空操作没有返回真实结果。");
        record("space_sample", command.Label + "（实际采样）", true,
            sample.ToString(Newtonsoft.Json.Formatting.None));
        var tables = new List<LuaTable>();
        try
        {
            LuaTable data = (LuaTable)ToLuaValue(sample, tables);
            object[] result = executeLua.Call(command.Validator, data);
            command.Success = result.Length > 0 && result[0] is bool passed && passed;
            command.Message = result.Length > 1 && result[1] != null ? result[1].ToString() :
                command.Success ? "Lua 太空断言通过。" : "Lua 太空断言失败。";
            foreach (object value in result) (value as IDisposable)?.Dispose();
        }
        finally
        {
            for (int index = tables.Count - 1; index >= 0; index--) tables[index].Dispose();
        }
    }

    private object ToLuaValue(JToken value, List<LuaTable> tables)
    {
        if (value is JObject obj)
        {
            LuaTable table = luaEnvironment.NewTable();
            tables.Add(table);
            foreach (JProperty property in obj.Properties()) table.Set(property.Name, ToLuaValue(property.Value, tables));
            return table;
        }
        if (value is JArray array)
        {
            LuaTable table = luaEnvironment.NewTable();
            tables.Add(table);
            for (int index = 0; index < array.Count; index++) table.Set(index + 1, ToLuaValue(array[index], tables));
            return table;
        }
        if (value.Type == JTokenType.Null) return null;
        if (value.Type == JTokenType.Boolean) return value.Value<bool>();
        if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) return value.Value<double>();
        return value.Value<string>();
    }

    private void ReleaseLua()
    {
        foreach (LuaFunction callback in callbacks) callback.Dispose();
        callbacks.Clear();
        executeLua?.Dispose();
        executeLua = null;
        luaEnvironment?.Dispose();
        luaEnvironment = null;
    }
    #endregion

    private IEnumerator MoveTo(Command command)
    {
        navigation = player.GetComponentInChildren<Mod_GameMCP_LLM>(true);
        if (navigation == null)
        {
            command.Message = "玩家缺少通用导航操作模块。";
            yield break;
        }
        Vector2 target = WorldTopologyRuntime.NormalizePosition(new Vector2(command.X, command.Y));
        if (!navigation.TryBeginMoveTo(this, target, command.Tolerance, out command.Message)) yield break;
        double deadline = Time.realtimeSinceStartupAsDouble + command.Seconds;
        try
        {
            while (true)
            {
                if (!TryEnsureContext(out command.Message)) yield break;
                if (!navigation.TryGetMoveToStatus(this, out Mod_GameMCP_LLM.MoveToStatus status))
                {
                    command.Message = "导航操作已丢失。";
                    yield break;
                }
                if (status.IsTerminal)
                {
                    float distance = WorldTopologyRuntime.ShortestDelta(player.transform.position, target).magnitude;
                    command.Success = status.State == Mod_GameMCP_LLM.OperationState.Reached &&
                                      distance <= command.Tolerance;
                    command.Message = $"导航结果：{status.StopReason}，剩余距离 {distance:0.###}。";
                    yield break;
                }
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                {
                    command.Message = $"导航超过 {command.Seconds:0.###} 秒仍未到达目标。";
                    yield break;
                }
                yield return null;
            }
        }
        finally
        {
            if (navigation != null) navigation.TryCancelMoveTo(this);
        }
    }

    #endregion

    #region 玩家控制租约与清理

    private bool TryAcquireControl(out string error)
    {
        error = string.Empty;
        if (player == null || ItemMgr.Instance?.User_Player != player)
        {
            error = "测试玩家不存在，或已经不是当前本地玩家。";
            return false;
        }
        controller = player.GetComponentInChildren<Mod_GameController>(true);
        mover = player.GetComponentInChildren<Mod_Mover>(true);
        if (controller == null || mover == null || mover.rb == null)
        {
            error = "测试玩家缺少控制模块或可用的移动模块。";
            return false;
        }
        if (!controller.TryAcquireExternalGameplayControl(this))
        {
            error = $"玩家控制已被占用：{controller.ExternalGameplayControlOwnerName}";
            return false;
        }
        controlAcquired = true;
        previousRunState = mover.IsRunning;
        admin = player.GetComponentInChildren<Mod_PlayerAdminController>(true);
        initialAdminSpeed = admin?.AdminMoveSpeedMultiplier ?? 1f;
        initialInvincibility = admin?.IsAdminInvincibilityEnabled ?? false;
        initialHealth = player.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp ?? 0f;
        mover.SetRunState(false);
        return true;
    }

    private bool TryEnsureContext(out string error)
    {
        error = string.Empty;
        if (disposed || !Application.isPlaying)
            error = "测试已停止，或播放模式已结束。";
        else if (player == null || ItemMgr.Instance?.User_Player != player ||
                 GameManager.Instance == null || !GameManager.Instance.IsGameplayReady)
            error = "测试期间玩家或游戏世界不再就绪。";
        else if (controller == null || !controller.IsExternalGameplayControlOwner(this))
            error = "测试期间玩家控制租约已丢失。";
        return string.IsNullOrEmpty(error);
    }

    private void ReleaseControl()
    {
        if (navigation != null) navigation.TryCancelMoveTo(this);
        if (player != null)
        {
            Mod_BuffManager buffs = player.itemMods?.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
            if (buffs != null)
                foreach (string id in createdBuffs) buffs.RemoveBuff(id);
            if (admin != null && admin.IsAdministrator)
            {
                if (gmInvincibilityChanged)
                    admin.TrySetAdminInvincibilityEnabled(initialInvincibility);
                if (gmSpeedChanged)
                    admin.TrySetAdminMoveSpeedMultiplier(initialAdminSpeed, out _);
            }
            if (gmHealthChanged)
            {
                Mod_DamageReceiver health = player.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
                if (health != null && initialHealth > 0f) health.Hp = initialHealth;
            }
        }
        createdBuffs.Clear();
        if (controller != null && controller.IsExternalGameplayControlOwner(this))
        {
            controller.TrySetExternalMoveInput(this, Vector2.zero);
            controller.TrySetExternalAttackHeld(this, false);
            controller.ReleaseExternalGameplayControl(this);
        }
        if (controlAcquired && mover != null) mover.SetRunState(previousRunState);
        controlAcquired = false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ReleaseControl();
        ReleaseLua();
    }

    #endregion
}
