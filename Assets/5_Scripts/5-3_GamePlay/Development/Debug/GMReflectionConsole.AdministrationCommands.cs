using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using FlatWorld.Localization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region Reflection command discovery

    private void CyclePlayerMoveSpeed()
    {
        Mod_PlayerAdminController controller = FindFirstComponent("Mod_PlayerAdminController") as Mod_PlayerAdminController;
        if (controller == null)
        {
            SetStatus("未找到本地玩家，无法调整移动速度。", Color.yellow);
            RefreshPlayerMoveSpeedButton();
            return;
        }

        if (!controller.TryCycleAdminMoveSpeedMultiplier(out float multiplier))
        {
            SetStatus("未找到玩家移动模块，移动速度调整失败。", Color.yellow);
            return;
        }

        GMConsolePreferences.SetPlayerMoveSpeed(multiplier);
        RefreshPlayerMoveSpeedButton();
        SetStatusFormat("玩家移动速度已调整为 {0:0.#} 倍。",
            multiplier > 1f ? GmAccentHover : GmTextSecondary, multiplier);
    }

    private void ApplyPlayerMoveSpeedInput()
    {
        if (playerMoveSpeedInput == null)
            return;

        string value = playerMoveSpeedInput.text?.Trim();
        bool parsed = float.TryParse(
                          value,
                          NumberStyles.Float,
                          CultureInfo.InvariantCulture,
                          out float requestedMultiplier) ||
                      float.TryParse(
                          value,
                          NumberStyles.Float,
                          CultureInfo.CurrentCulture,
                          out requestedMultiplier);
        if (!parsed || float.IsNaN(requestedMultiplier) || float.IsInfinity(requestedMultiplier))
        {
            SetStatus("请输入有效的移动速度倍率，例如 1、2.5 或 10。", Color.yellow);
            RefreshPlayerMoveSpeedButton();
            return;
        }

        Mod_PlayerAdminController controller = FindFirstComponent("Mod_PlayerAdminController") as Mod_PlayerAdminController;
        if (controller == null)
        {
            SetStatus("未找到本地玩家，无法调整移动速度。", Color.yellow);
            RefreshPlayerMoveSpeedButton();
            return;
        }

        if (!controller.TrySetAdminMoveSpeedMultiplier(requestedMultiplier, out float appliedMultiplier))
        {
            SetStatus("未找到玩家移动模块，移动速度调整失败。", Color.yellow);
            RefreshPlayerMoveSpeedButton();
            return;
        }

        playerMoveSpeedInput.SetTextWithoutNotify(
            appliedMultiplier.ToString("0.##", CultureInfo.InvariantCulture));
        GMConsolePreferences.SetPlayerMoveSpeed(appliedMultiplier);
        RefreshPlayerMoveSpeedButton();
        SetStatusFormat("玩家移动速度已调整为 {0:0.##} 倍。",
            appliedMultiplier > 1f ? GmAccentHover : GmTextSecondary, appliedMultiplier);
    }

    private void RefreshPlayerMoveSpeedButton()
    {
        Mod_PlayerAdminController controller = FindObjectOfType<Mod_PlayerAdminController>(true);
        float multiplier = controller != null ? controller.AdminMoveSpeedMultiplier : 1f;
        if (playerMoveSpeedInput != null && !playerMoveSpeedInput.isFocused)
            playerMoveSpeedInput.SetTextWithoutNotify(multiplier.ToString("0.##", CultureInfo.InvariantCulture));

        if (playerMoveSpeedButton != null)
        {
            TextMeshProUGUI label = playerMoveSpeedButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
                SetLocalizedFormat(label, "玩家移速：{0:0.##}x", multiplier);
        }

        Button statusButton = playerMoveSpeedApplyButton ?? playerMoveSpeedButton;
        SetGmButtonVisual(
            statusButton,
            multiplier > 1f ? GmSelection : GmSurfaceRaised,
            multiplier > 1f);
    }

    private void ApplyChunkLoadSpeedInput()
    {
        if (chunkLoadSpeedInput == null)
            return;

        string value = chunkLoadSpeedInput.text?.Trim();
        bool unlimitedRequested = IsUnlimitedChunkLoadValue(value);
        float requestedMultiplier = float.NaN;
        bool parsed = unlimitedRequested || float.TryParse(
                          value,
                          NumberStyles.Float,
                          CultureInfo.InvariantCulture,
                          out requestedMultiplier) ||
                      float.TryParse(
                          value,
                          NumberStyles.Float,
                          CultureInfo.CurrentCulture,
                          out requestedMultiplier);
        if (unlimitedRequested)
            requestedMultiplier = float.PositiveInfinity;

        if (!parsed || float.IsNaN(requestedMultiplier) || float.IsNegativeInfinity(requestedMultiplier))
        {
            SetStatus("请输入有效倍率，或输入‘无限’取消加载上限。", Color.yellow);
            RefreshChunkLoadSpeedControl();
            return;
        }

        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager == null ||
            !chunkManager.TrySetChunkLoadSpeedMultiplier(requestedMultiplier, out float appliedMultiplier))
        {
            SetStatus("未找到区块管理器，加载速度调整失败。", Color.yellow);
            RefreshChunkLoadSpeedControl();
            return;
        }

        GMConsolePreferences.SetChunkLoadSpeed(
            appliedMultiplier,
            chunkManager.IsChunkLoadSpeedUnlimited);
        RefreshChunkLoadSpeedControl();
        ShowChunkLoadSpeedStatus(chunkManager, appliedMultiplier);
    }

    private void ToggleUnlimitedChunkLoadSpeed()
    {
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager == null)
        {
            SetStatus("未找到区块管理器，加载速度调整失败。", Color.yellow);
            return;
        }

        float requestedMultiplier = chunkManager.IsChunkLoadSpeedUnlimited
            ? GMConsolePreferences.ChunkLoadSpeedMultiplier
            : float.PositiveInfinity;
        if (!chunkManager.TrySetChunkLoadSpeedMultiplier(
                requestedMultiplier,
                out float appliedMultiplier))
        {
            SetStatus("区块加载速度调整失败。", Color.yellow);
            return;
        }

        GMConsolePreferences.SetChunkLoadSpeed(
            appliedMultiplier,
            chunkManager.IsChunkLoadSpeedUnlimited);
        RefreshChunkLoadSpeedControl();
        ShowChunkLoadSpeedStatus(chunkManager, appliedMultiplier);
    }

    private void RefreshChunkLoadSpeedControl()
    {
        float multiplier = ChunkMgr.CurrentChunkLoadSpeedMultiplier;
        bool unlimited = ChunkMgr.CurrentChunkLoadSpeedUnlimited;
        if (chunkLoadSpeedInput != null && !chunkLoadSpeedInput.isFocused)
        {
            chunkLoadSpeedInput.SetTextWithoutNotify(
                unlimited ? "无限" : multiplier.ToString("0.##", CultureInfo.InvariantCulture));
        }

        bool accelerated = unlimited || multiplier > 1f;
        SetGmButtonVisual(
            chunkLoadSpeedApplyButton,
            accelerated ? GmSelection : GmSurfaceRaised,
            accelerated);

        if (chunkLoadSpeedUnlimitedButton == null)
            return;

        TextMeshProUGUI label =
            chunkLoadSpeedUnlimitedButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            SetLocalizedText(label, unlimited ? "恢复" : "无限");

        SetGmButtonVisual(
            chunkLoadSpeedUnlimitedButton,
            unlimited ? GmSelection : GmSurfaceRaised,
            unlimited);
    }

    private static bool IsUnlimitedChunkLoadValue(string value)
    {
        return string.Equals(value, "无限", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "无限制", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "inf", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "infinity", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "unlimited", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowChunkLoadSpeedStatus(ChunkMgr chunkManager, float appliedMultiplier)
    {
        if (chunkManager.IsChunkLoadSpeedUnlimited)
        {
            SetStatusFormat(
                "区块加载已设为自动最大；后台并发 {0}，仍保留主线程安全预算。",
                GmAccentHover, chunkManager.EffectiveBackgroundGenerationConcurrency);
            return;
        }

        SetStatusFormat("区块加载速度已调整为 {0:0.##} 倍。",
            appliedMultiplier > 1f ? GmAccentHover : GmTextSecondary, appliedMultiplier);
    }

    private void ToggleTeleportShortcut()
    {
        bool enabled = Mod_PlayerAdminController.ToggleTeleportToMouseShortcut();
        GMConsolePreferences.SetTeleportShortcut(enabled);
        RefreshTeleportShortcutButton();
        SetStatus(
            enabled ? "T 键鼠标传送已开启。" : "T 键鼠标传送已关闭。",
            enabled ? GmAccentHover : GmTextSecondary);
    }

    private void ToggleAdminInvincibility()
    {
        Mod_PlayerAdminController controller =
            FindFirstComponent("Mod_PlayerAdminController") as Mod_PlayerAdminController;
        if (controller == null || !controller.TryToggleAdminInvincibility(out bool enabled))
        {
            RefreshAdminInvincibilityButton();
            SetStatus("请先启用管理员模式，才能切换无敌。", Color.yellow);
            return;
        }

        RefreshAdminInvincibilityButton();
        SetStatus(
            enabled ? "管理员无敌已开启。" : "管理员无敌已关闭，生命与生存状态将正常结算。",
            enabled ? GmAccentHover : GmAccent);
    }

    private void RefreshAdminInvincibilityButton()
    {
        if (adminInvincibilityButton == null)
            return;

        Mod_PlayerAdminController controller = FindObjectOfType<Mod_PlayerAdminController>(true);
        bool canToggle = controller != null && controller.IsAdministrator;
        bool enabled = canToggle && controller.IsAdminInvincibilityEnabled;

        TextMeshProUGUI label = adminInvincibilityButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            SetLocalizedText(label, canToggle
                ? (enabled ? "管理员无敌：开" : "管理员无敌：关")
                : "管理员无敌：需权限");
        }

        adminInvincibilityButton.interactable = canToggle;
        SetGmButtonVisual(
            adminInvincibilityButton,
            !canToggle ? GmSurfaceLow : enabled ? GmSelection : GmSurfaceRaised,
            enabled);
    }

    private void RefreshTeleportShortcutButton()
    {
        if (teleportShortcutButton == null)
            return;

        bool enabled = Mod_PlayerAdminController.TeleportToMouseShortcutEnabled;
        TextMeshProUGUI label = teleportShortcutButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            SetLocalizedText(label, enabled ? "T 传送：开" : "T 传送：关");

        SetGmButtonVisual(
            teleportShortcutButton,
            enabled ? GmSelection : GmSurfaceRaised,
            enabled);
    }

    private void ToggleNavigationPathHints()
    {
        bool visible = WorldNavigationPathDebugOverlay.ToggleRoutesVisible();
        GMConsolePreferences.SetNavigationPathVisible(visible);
        RefreshNavigationPathButton();
        SetStatus(
            visible ? "AI 导航路线提示已开启。" : "AI 导航路线提示已关闭。",
            visible ? GmAccentHover : GmTextSecondary);
    }

    private void ToggleAnimalDebugOverlay()
    {
        bool visible = AI_DebugOverlay.Toggle();
        GMConsolePreferences.SetAnimalDebugOverlayVisible(visible);
        RefreshAnimalDebugOverlayButton();
        SetStatus(
            visible ? "动物头顶参数已开启。" : "动物头顶参数已关闭。",
            visible ? GmAccentHover : GmTextSecondary);
    }

    private void RefreshAnimalDebugOverlayButton()
    {
        if (animalDebugOverlayButton == null)
            return;

        bool visible = AI_DebugOverlay.Visible;
        TextMeshProUGUI label = animalDebugOverlayButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            SetLocalizedText(label, visible ? "动物参数：开" : "动物参数：关");

        SetGmButtonVisual(
            animalDebugOverlayButton,
            visible ? GmSelection : GmSurfaceRaised,
            visible);
    }

    private void ToggleHiveDebugOverlay()
    {
        bool visible = HiveColonyDebugOverlay.Toggle();
        GMConsolePreferences.SetHiveDebugOverlayVisible(visible);
        RefreshHiveDebugOverlayButton();
        SetStatus(
            visible ? "蜂巢头顶参数已开启。" : "蜂巢头顶参数已关闭。",
            visible ? GmAccentHover : GmTextSecondary);
    }

    private void RefreshHiveDebugOverlayButton()
    {
        if (hiveDebugOverlayButton == null)
            return;

        bool visible = HiveColonyDebugOverlay.Visible;
        TextMeshProUGUI label = hiveDebugOverlayButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            SetLocalizedText(label, visible ? "蜂巢参数：开" : "蜂巢参数：关");

        SetGmButtonVisual(
            hiveDebugOverlayButton,
            visible ? GmSelection : GmSurfaceRaised,
            visible);
    }

    private void RefreshNavigationPathButton()
    {
        if (navigationPathButton == null)
            return;

        bool visible = WorldNavigationPathDebugOverlay.RoutesVisible;
        TextMeshProUGUI label = navigationPathButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            SetLocalizedText(label, visible ? "AI 路线提示：开" : "AI 路线提示：关");

        SetGmButtonVisual(
            navigationPathButton,
            visible ? GmSelection : GmSurfaceRaised,
            visible);
    }

    private void RebuildReflectedCommands()
    {
        commands.Clear();

        // 场景组件只扫描一次，命名命令和自动命令共用这份快照。
        MonoBehaviour[] sceneBehaviours = FindObjectsOfType<MonoBehaviour>(true);
        Dictionary<string, Component> namedTargets = new(StringComparer.Ordinal);
        for (int i = 0; i < sceneBehaviours.Length; i++)
        {
            MonoBehaviour behaviour = sceneBehaviours[i];
            if (behaviour != null && behaviour.gameObject.scene.IsValid())
                namedTargets.TryAdd(behaviour.GetType().Name, behaviour);
        }

        AddNamedCommand(namedTargets, "环境", "晴天", "GameDebugManager", "SetClearWeather");
        AddNamedCommand(namedTargets, "环境", "下雨", "GameDebugManager", "SetRainWeather");
        AddNamedCommand(namedTargets, "环境", "环境信息", "GameDebugManager", "ToggleEnvironmentInfo");
        AddNamedCommand(namedTargets, "管理员", "刷新区块", "Mod_ChunkLoader", "RefreshChunksAroundPlayer");
        AddNamedCommand(namedTargets, "管理员", "手持 +9999", "Mod_PlayerAdminController", "AddAmountToCurrentHandItem", 9999f);
        AddNamedCommand(namedTargets, "管理员", "背包 +100", "Mod_PlayerAdminController", "AddAmountToAllBagItems", 100f);
        AddNamedCommand(namedTargets, "管理员", "时间恢复", "Mod_PlayerAdminController", "ResetTimeScale");
        AddNamedCommand(namedTargets, "管理员", "时间 +0.5", "Mod_PlayerAdminController", "TryUpdateTimeScale", 0.5f);
        AddNamedCommand(namedTargets, "管理员", "时间 -0.5", "Mod_PlayerAdminController", "TryUpdateTimeScale", -0.5f);

        Dictionary<Type, MethodInfo[]> safeMethodsByType = new();
        foreach (MonoBehaviour behaviour in sceneBehaviours)
        {
            if (commands.Count >= MaxDiscoveredCommands)
                break;
            if (behaviour == null || !behaviour.gameObject.scene.IsValid())
                continue;

            Type type = behaviour.GetType();
            if (!safeMethodsByType.TryGetValue(type, out MethodInfo[] methods))
            {
                methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(method => method.ReturnType == typeof(void) &&
                                     method.GetParameters().Length == 0 &&
                                     IsSafeAutoCommand(method))
                    .ToArray();
                safeMethodsByType.Add(type, methods);
            }

            for (int i = 0; i < methods.Length && commands.Count < MaxDiscoveredCommands; i++)
            {
                MethodInfo method = methods[i];
                AddCommand($"扫描/{type.Name}", method.Name, behaviour, method, Array.Empty<object>());
            }
        }

        RebuildCommandButtons();
    }

    private void AddNamedCommand(Dictionary<string, Component> namedTargets, string category, string label, string typeName, string methodName, params object[] arguments)
    {
        if (!namedTargets.TryGetValue(typeName, out Component target) || target == null)
            return;

        MethodInfo method = FindCompatibleMethod(target.GetType(), methodName, arguments);
        if (method != null)
            AddCommand(category, label, target, method, arguments ?? Array.Empty<object>());
    }

    private void AddCommand(string category, string label, Component target, MethodInfo method, object[] arguments)
    {
        if (target == null || method == null)
            return;

        string argumentKey = string.Join(",", arguments.Select(argument => argument?.ToString() ?? "<null>"));
        string identity = target.GetInstanceID() + "|" + method.Name + "|" + argumentKey;
        for (int i = 0; i < commands.Count; i++)
        {
            ReflectedCommand existing = commands[i];
            if (existing.Target != null &&
                existing.Target.GetInstanceID() + "|" + existing.Method.Name + "|" +
                string.Join(",", existing.Arguments.Select(argument => argument?.ToString() ?? "<null>")) == identity)
            {
                return;
            }
        }

        commands.Add(new ReflectedCommand
        {
            Category = category,
            Label = label,
            Target = target,
            Method = method,
            Arguments = arguments
        });
    }

    private void RebuildCommandButtons()
    {
        RebuildCommandPage();
    }

    private static bool IsSafeAutoCommand(MethodInfo method)
    {
        string name = method.Name;
        return name == "ToggleEnvironmentInfo" ||
               name == "SetClearWeather" ||
               name == "SetRainWeather" ||
               name == "SetClearWeatherDebug" ||
               name == "SetRainWeatherDebug" ||
               name == "ToggleDebugPanel" ||
               name == "RefreshChunksAroundPlayer" ||
               name == "RefreshConfiguredChunkWindow" ||
               name == "InitializeCreativeInventoryForAdmin" ||
               name == "TeleportToMousePosition" ||
               name == "CleanupNullItems" ||
               name == "LoadAllRuntimeItems";
    }

    private void InvokeCommand(ReflectedCommand command)
    {
        if (command.Target == null)
        {
            SetStatus("指令目标已失效，请重新扫描。", Color.yellow);
            return;
        }

        try
        {
            object result = command.Method.Invoke(command.Target, command.Arguments);
            string resultMessage = result as string;
            if (string.IsNullOrWhiteSpace(resultMessage))
                SetStatusProvider(() => FlatWorldLocalizationService.GetUiFormat(
                    "已执行：{0} / {1}",
                    FlatWorldLocalizationService.GetUiText(command.Category),
                    FlatWorldLocalizationService.GetUiText(command.Label)),
                    new Color(0.35f, 0.95f, 0.85f));
            else
                SetStatus(resultMessage, new Color(0.35f, 0.95f, 0.85f));
        }
        catch (TargetInvocationException exception)
        {
            SetStatusFormat("执行失败：{0}", new Color(1f, 0.42f, 0.38f),
                exception.InnerException?.Message ?? exception.Message);
            Debug.LogException(exception.InnerException ?? exception);
        }
        catch (Exception exception)
        {
            SetStatusFormat("执行失败：{0}", new Color(1f, 0.42f, 0.38f), exception.Message);
            Debug.LogException(exception);
        }
    }

    private void InvokeByTypeName(string typeName, string methodName, params object[] arguments)
    {
        Component target = FindFirstComponent(typeName);
        MethodInfo method = target != null ? FindCompatibleMethod(target.GetType(), methodName, arguments) : null;
        if (target == null || method == null)
        {
            SetStatusFormat("未找到可用指令：{0}.{1}", Color.yellow, typeName, methodName);
            return;
        }

        InvokeCommand(new ReflectedCommand
        {
            Category = typeName,
            Label = methodName,
            Target = target,
            Method = method,
            Arguments = arguments ?? Array.Empty<object>()
        });
    }

    #endregion

    #region 反射入口与管理命令

    private void SetAdministrator()
    {
        Mod_PlayerAdminController adminController =
            FindFirstComponent("Mod_PlayerAdminController") as Mod_PlayerAdminController;
        if (adminController != null && adminController.TryEnableAdministrator())
        {
            RefreshAdminInvincibilityButton();
            SetStatus("管理员已启用。", new Color(0.35f, 0.95f, 0.85f));
            return;
        }

        Component controller = adminController;
        object player = ReadMember(controller, "player") ?? FindFirstComponent("Player");
        object playerData = ReadMember(player, "Data");
        if (playerData == null || !WriteMember(playerData, "Name_User", AdministratorName))
        {
            SetStatus("未找到本地玩家数据，无法设置管理员。", Color.yellow);
            return;
        }

        RefreshAdminInvincibilityButton();
        SetStatus("管理员已启用。", new Color(0.35f, 0.95f, 0.85f));
    }

    private Transform GetLocalPlayerTransform()
    {
        Transform localPlayer = ItemMgr.GetInstance()?.UserPlayerTransform;
        if (localPlayer != null)
            return localPlayer;

        Component player = FindLocalPlayer() ?? FindObjectOfType<Player>(true);
        return player != null ? player.transform : null;
    }

    private static ChunkGenerator_Land FindLandGenerator()
    {
        Map[] maps = FindObjectsOfType<Map>(true);
        for (int i = 0; i < maps.Length; i++)
        {
            ChunkGenerator_Land generator = maps[i]?.LandGenerator;
            if (generator != null)
                return generator;
        }

        return null;
    }

    private static IEnumerable<MonoBehaviour> FindSceneBehaviours()
    {
        return FindObjectsOfType<MonoBehaviour>(true)
            .Where(behaviour => behaviour != null && behaviour.gameObject.scene.IsValid());
    }

    private static Component FindFirstComponent(string typeName)
    {
        return FindSceneBehaviours().FirstOrDefault(component => component.GetType().Name == typeName);
    }

    private static MethodInfo FindCompatibleMethod(Type type, string name, object[] arguments)
    {
        arguments = arguments ?? Array.Empty<object>();
        foreach (MethodInfo method in type.GetMethods(InstanceFlags))
        {
            if (method.Name != name)
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != arguments.Length)
                continue;

            bool isCompatible = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (arguments[i] != null && !parameters[i].ParameterType.IsInstanceOfType(arguments[i]) &&
                    !IsNumericType(parameters[i].ParameterType))
                {
                    isCompatible = false;
                    break;
                }
            }

            if (isCompatible)
                return method;
        }

        return null;
    }

    private static bool IsNumericType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) ||
               type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }

    private static object ReadMember(object target, string memberName)
    {
        return ReadMember(target, FindMember(target, memberName));
    }

    private static object ReadMember(object target, MemberInfo member)
    {
        if (target == null || member == null)
            return null;

        if (member is FieldInfo field)
            return field.GetValue(target);
        if (member is PropertyInfo property && property.CanRead)
            return property.GetValue(target);
        return null;
    }

    private static bool WriteMember(object target, string memberName, object value)
    {
        return WriteMember(target, FindMember(target, memberName), value);
    }

    private static bool WriteMember(object target, MemberInfo member, object value)
    {
        if (target == null || member == null)
            return false;

        if (member is FieldInfo field)
        {
            field.SetValue(target, value);
            return true;
        }

        if (member is PropertyInfo property && property.CanWrite)
        {
            property.SetValue(target, value);
            return true;
        }

        return false;
    }

    private static MemberInfo FindMember(object target, string memberName)
    {
        if (target == null)
            return null;

        Type type = target.GetType();
        return (MemberInfo)type.GetField(memberName, InstanceFlags) ?? type.GetProperty(memberName, InstanceFlags);
    }

    private static Type GetMemberType(object target, string memberName)
    {
        MemberInfo member = FindMember(target, memberName);
        if (member is FieldInfo field)
            return field.FieldType;
        if (member is PropertyInfo property)
            return property.PropertyType;
        return null;
    }

    private void SetStatus(string message, Color color)
    {
        if (statusText == null)
            return;

        SetLocalizedText(statusText, message);
        statusText.color = color;
    }

    private void SetStatusFormat(string template, Color color, params object[] arguments)
    {
        if (statusText == null)
            return;

        SetLocalizedFormat(statusText, template, arguments);
        statusText.color = color;
    }

    private void SetStatusProvider(Func<string> provider, Color color)
    {
        if (statusText == null)
            return;

        SetLocalizedProvider(statusText, provider);
        statusText.color = color;
    }

    #endregion
}
