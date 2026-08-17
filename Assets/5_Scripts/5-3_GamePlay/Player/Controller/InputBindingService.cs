using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum InputBindingDeviceGroup
{
    KeyboardMouse,
    Gamepad
}

public enum InputRebindStatus
{
    Completed,
    Canceled,
    Conflict,
    Failed
}

public struct InputRebindResult
{
    public InputRebindStatus Status;
    public InputBindingEntry ConflictingEntry;
    public Exception Exception;
}

public sealed class InputBindingEntry
{
    public string DisplayName { get; }
    public InputAction Action { get; }
    public int BindingIndex { get; }
    public InputBindingDeviceGroup DeviceGroup { get; }
    public string BindingGroup { get; }
    public string ExpectedControlLayout { get; }

    public InputBindingEntry(
        string displayName,
        InputAction action,
        int bindingIndex,
        InputBindingDeviceGroup deviceGroup,
        string bindingGroup,
        string expectedControlLayout)
    {
        DisplayName = displayName;
        Action = action;
        BindingIndex = bindingIndex;
        DeviceGroup = deviceGroup;
        BindingGroup = bindingGroup;
        ExpectedControlLayout = expectedControlLayout;
    }
}

public interface IInputBindingStore
{
    string Load();
    void Save(string json);
    void Clear();
}

public sealed class PlayerPrefsInputBindingStore : IInputBindingStore
{
    private const string PreferencesKey = "FlatWorld.InputBindings.v1";

    public string Load()
    {
        return PlayerPrefs.GetString(PreferencesKey, string.Empty);
    }

    public void Save(string json)
    {
        PlayerPrefs.SetString(PreferencesKey, json ?? string.Empty);
        PlayerPrefs.Save();
    }

    public void Clear()
    {
        PlayerPrefs.DeleteKey(PreferencesKey);
        PlayerPrefs.Save();
    }
}

/// <summary>
/// Owns runtime binding overrides for one PlayerInputActions instance.
/// UI code only consumes the public entry/rebind API and does not know how overrides are stored.
/// </summary>
public sealed class InputBindingService : IDisposable
{
    private const string ActionMapName = "Win10";

    private sealed class BindingSpec
    {
        public readonly string ActionName;
        public readonly string PartName;
        public readonly string DisplayName;
        public readonly InputBindingDeviceGroup DeviceGroup;
        public readonly string BindingGroup;
        public readonly string ExpectedControlLayout;
        public readonly int BindingOrdinal;

        public BindingSpec(
            string actionName,
            string partName,
            string displayName,
            InputBindingDeviceGroup deviceGroup,
            string bindingGroup,
            string expectedControlLayout,
            int bindingOrdinal = 0)
        {
            ActionName = actionName;
            PartName = partName;
            DisplayName = displayName;
            DeviceGroup = deviceGroup;
            BindingGroup = bindingGroup;
            ExpectedControlLayout = expectedControlLayout;
            BindingOrdinal = bindingOrdinal;
        }
    }

    #region 绑定覆盖持久化

    /// <summary>与 Unity Input System 的按键覆盖 JSON 保持兼容，用于加载前过滤已经从输入资产移除的绑定。</summary>
    [Serializable]
    private sealed class SavedBindingOverrideList
    {
        public SavedBindingOverride[] bindings;
    }

    /// <summary>保存单项绑定覆盖所需的字段；path 为空字符串时表示用户主动清除了绑定。</summary>
    [Serializable]
    private sealed class SavedBindingOverride
    {
        public string action;
        public string id;
        public string path;
        public string interactions;
        public string processors;
    }

    #endregion

    private static readonly BindingSpec[] EditableBindingSpecs =
    {
        new BindingSpec("Move_Player", "up", "向上移动", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("Move_Player", "down", "向下移动", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("Move_Player", "left", "向左移动", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("Move_Player", "right", "向右移动", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("LeftClick", null, "主要操作", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("RightClick", null, "次要操作", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("E", null, "交互", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("F", null, "丢弃", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("B", null, "背包", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("P", null, "装备面板", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("H", null, "手工制作", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("ToggleRun", null, "切换奔跑", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("Shift", null, "长按奔跑", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("Tab", null, "角色参数面板", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("CtrlMouse", "Modifier", "镜头缩放修饰键", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("OpenChat", null, "打开聊天框", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 1", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button", 0),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 2", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button", 1),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 3", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button", 2),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 4", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button", 3),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 5", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button", 4),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 6", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button", 5),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 7", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button", 6),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 8", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button", 7),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 9", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button", 8),
        new BindingSpec("ESC", null, "关闭面板 / 打开设置", InputBindingDeviceGroup.KeyboardMouse, "Keyboard&Mouse", "Button"),
        new BindingSpec("Move_Player", null, "角色移动", InputBindingDeviceGroup.Gamepad, "Gamepad", "Vector2"),
        new BindingSpec("GamepadCursor", null, "虚拟光标", InputBindingDeviceGroup.Gamepad, "Gamepad", "Vector2"),
        new BindingSpec("LeftClick", null, "主要操作", InputBindingDeviceGroup.Gamepad, "Gamepad", "Button"),
        new BindingSpec("RightClick", null, "次要操作", InputBindingDeviceGroup.Gamepad, "Gamepad", "Button"),
        new BindingSpec("E", null, "交互", InputBindingDeviceGroup.Gamepad, "Gamepad", "Button"),
        new BindingSpec("F", null, "丢弃", InputBindingDeviceGroup.Gamepad, "Gamepad", "Button"),
        new BindingSpec("P", null, "装备面板", InputBindingDeviceGroup.Gamepad, "Gamepad", "Button"),
        new BindingSpec("H", null, "手工制作", InputBindingDeviceGroup.Gamepad, "Gamepad", "Button"),
        new BindingSpec("ToggleRun", null, "切换奔跑", InputBindingDeviceGroup.Gamepad, "Gamepad", "Button"),
        new BindingSpec("Shift", null, "长按奔跑", InputBindingDeviceGroup.Gamepad, "Gamepad", "Button"),
        new BindingSpec("Tab", null, "营养面板", InputBindingDeviceGroup.Gamepad, "Gamepad", "Button"),
        new BindingSpec("HotbarPrevious", null, "快捷栏上一格", InputBindingDeviceGroup.Gamepad, "Gamepad", "Button"),
        new BindingSpec("HotbarNext", null, "快捷栏下一格", InputBindingDeviceGroup.Gamepad, "Gamepad", "Button"),
        new BindingSpec("OpenChat", null, "打开聊天框", InputBindingDeviceGroup.Gamepad, "Gamepad", "Button"),
        new BindingSpec("ESC", null, "关闭面板 / 打开设置", InputBindingDeviceGroup.Gamepad, "Gamepad", "Button")
    };

    private readonly InputActionAsset inputAsset;
    private readonly InputActionMap actionMap;
    private readonly IInputBindingStore store;
    private readonly List<InputBindingEntry> entries = new List<InputBindingEntry>();

    private InputActionRebindingExtensions.RebindingOperation activeRebind;
    private Action<InputRebindResult> activeCallback;
    private bool restoreMapAfterRebind;
    private bool restoreMapAfterSuspension;
    private int suspensionDepth;
    private bool disposed;

    public event Action BindingsChanged;

    public IReadOnlyList<InputBindingEntry> Entries => entries;
    public bool IsRebinding => activeRebind != null;

    public IReadOnlyList<InputBindingEntry> GetEntries(InputBindingDeviceGroup deviceGroup)
    {
        List<InputBindingEntry> result = new List<InputBindingEntry>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].DeviceGroup == deviceGroup)
                result.Add(entries[i]);
        }

        return result;
    }

    public InputBindingService(InputActionAsset inputAsset, IInputBindingStore store = null)
    {
        this.inputAsset = inputAsset != null
            ? inputAsset
            : throw new ArgumentNullException(nameof(inputAsset));
        this.store = store ?? new PlayerPrefsInputBindingStore();
        actionMap = inputAsset.FindActionMap(ActionMapName, true);

        LoadSavedOverrides();
        BuildEditableEntries();
    }

    public string GetBindingDisplayString(InputBindingEntry entry)
    {
        if (entry == null || entry.Action == null || entry.BindingIndex < 0 ||
            entry.BindingIndex >= entry.Action.bindings.Count)
        {
            return "未绑定";
        }

        if (string.IsNullOrEmpty(entry.Action.bindings[entry.BindingIndex].effectivePath))
            return "未绑定";

        string display = entry.Action.GetBindingDisplayString(
            entry.BindingIndex,
            InputBinding.DisplayStringOptions.DontIncludeInteractions);
        return string.IsNullOrWhiteSpace(display) ? "未绑定" : display;
    }

    public void SuspendGameplayInput()
    {
        ThrowIfDisposed();

        if (suspensionDepth == 0)
        {
            restoreMapAfterSuspension = actionMap.enabled;
            if (restoreMapAfterSuspension)
                actionMap.Disable();
        }

        suspensionDepth++;
    }

    public void ResumeGameplayInput()
    {
        if (disposed || suspensionDepth == 0)
            return;

        suspensionDepth--;
        if (suspensionDepth == 0 && restoreMapAfterSuspension)
        {
            restoreMapAfterSuspension = false;
            actionMap.Enable();
        }
    }

    public void BeginInteractiveRebind(
        InputBindingEntry entry,
        Action<InputRebindResult> onFinished)
    {
        ThrowIfDisposed();
        CancelActiveRebind();

        if (!IsValidEntry(entry))
        {
            onFinished?.Invoke(new InputRebindResult
            {
                Status = InputRebindStatus.Failed,
                Exception = new ArgumentException("Binding entry is no longer valid.", nameof(entry))
            });
            return;
        }

        InputAction action = entry.Action;
        int bindingIndex = entry.BindingIndex;
        string previousOverridePath = action.bindings[bindingIndex].overridePath;
        bool hadPreviousOverride = previousOverridePath != null;

        restoreMapAfterRebind = actionMap.enabled;
        if (restoreMapAfterRebind)
            actionMap.Disable();

        activeCallback = onFinished;

        try
        {
            string keyboardCancelPath = IsEffectivePath(entry, "<Keyboard>/escape")
                ? "<Keyboard>/backspace"
                : "<Keyboard>/escape";
            string gamepadCancelPath = IsEffectivePath(entry, "<Gamepad>/buttonEast")
                ? "<Gamepad>/start"
                : "<Gamepad>/buttonEast";

            activeRebind = action.PerformInteractiveRebinding(bindingIndex)
                .WithExpectedControlType(entry.ExpectedControlLayout)
                .WithCancelingThrough(keyboardCancelPath)
                .WithCancelingThrough(gamepadCancelPath)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .OnMatchWaitForAnother(0.1f);

            if (entry.DeviceGroup == InputBindingDeviceGroup.Gamepad)
            {
                activeRebind.WithControlsHavingToMatchPath("<Gamepad>");
            }
            else
            {
                activeRebind
                    .WithControlsHavingToMatchPath("<Keyboard>")
                    .WithControlsHavingToMatchPath("<Mouse>");
            }

            activeRebind
                .OnCancel(_ => FinishRebind(new InputRebindResult
                {
                    Status = InputRebindStatus.Canceled
                }))
                .OnComplete(_ =>
                {
                    InputBindingEntry conflict = FindConflict(entry);
                    if (conflict != null)
                    {
                        RestoreOverride(
                            action,
                            bindingIndex,
                            previousOverridePath,
                            hadPreviousOverride);
                        FinishRebind(new InputRebindResult
                        {
                            Status = InputRebindStatus.Conflict,
                            ConflictingEntry = conflict
                        });
                        return;
                    }

                    SaveOverrides();
                    BindingsChanged?.Invoke();
                    FinishRebind(new InputRebindResult
                    {
                        Status = InputRebindStatus.Completed
                    });
                });

            activeRebind.Start();
        }
        catch (Exception exception)
        {
            RestoreOverride(
                action,
                bindingIndex,
                previousOverridePath,
                hadPreviousOverride);
            FinishRebind(new InputRebindResult
            {
                Status = InputRebindStatus.Failed,
                Exception = exception
            });
        }
    }

    public void CancelActiveRebind()
    {
        activeRebind?.Cancel();
    }

    public void ResetToDefaults()
    {
        ThrowIfDisposed();
        CancelActiveRebind();
        inputAsset.RemoveAllBindingOverrides();
        store.Clear();
        BindingsChanged?.Invoke();
    }

    public void ResetToDefaults(InputBindingDeviceGroup deviceGroup)
    {
        ThrowIfDisposed();
        CancelActiveRebind();

        for (int i = 0; i < entries.Count; i++)
        {
            InputBindingEntry entry = entries[i];
            if (entry.DeviceGroup == deviceGroup)
                entry.Action.RemoveBindingOverride(entry.BindingIndex);
        }

        SaveOverrides();
        BindingsChanged?.Invoke();
    }

    #region 单项绑定清除

    /// <summary>清除单个绑定；使用空覆盖路径禁用默认绑定，并立即持久化当前设备组。</summary>
    public bool ClearBinding(InputBindingEntry entry)
    {
        ThrowIfDisposed();
        CancelActiveRebind();

        if (!IsValidEntry(entry))
            return false;

        entry.Action.ApplyBindingOverride(
            entry.BindingIndex,
            new InputBinding
            {
                overridePath = string.Empty
            });
        SaveOverrides();
        BindingsChanged?.Invoke();
        return true;
    }

    #endregion

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (activeRebind != null)
        {
            activeRebind.Dispose();
            activeRebind = null;
        }

        activeCallback = null;
        BindingsChanged = null;
        suspensionDepth = 0;
        restoreMapAfterRebind = false;
        restoreMapAfterSuspension = false;
    }

    private void LoadSavedOverrides()
    {
        string json = store.Load();
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            SavedBindingOverrideList savedOverrides =
                JsonUtility.FromJson<SavedBindingOverrideList>(json);
            if (savedOverrides == null || savedOverrides.bindings == null)
                throw new FormatException("按键覆盖配置缺少 bindings 字段。");

            inputAsset.RemoveAllBindingOverrides();
            int removedCount = 0;
            for (int i = 0; i < savedOverrides.bindings.Length; i++)
            {
                SavedBindingOverride savedOverride = savedOverrides.bindings[i];
                InputAction action;
                int bindingIndex = FindBindingIndexById(
                    savedOverride != null ? savedOverride.id : null,
                    out action);
                if (bindingIndex < 0)
                {
                    removedCount++;
                    continue;
                }

                action.ApplyBindingOverride(
                    bindingIndex,
                    new InputBinding
                    {
                        overridePath = FromSavedOverrideValue(savedOverride.path),
                        overrideInteractions = FromSavedOverrideValue(savedOverride.interactions),
                        overrideProcessors = FromSavedOverrideValue(savedOverride.processors)
                    });
            }

            if (removedCount > 0)
                SaveOverrides();
        }
        catch (Exception exception)
        {
            inputAsset.RemoveAllBindingOverrides();
            store.Clear();
            Debug.LogWarning($"[InputBindingService] 已忽略损坏的按键配置：{exception.Message}");
        }
    }

    /// <summary>按稳定绑定 ID 查找当前输入资产中的动作与索引。</summary>
    private int FindBindingIndexById(string bindingId, out InputAction action)
    {
        action = null;
        if (string.IsNullOrEmpty(bindingId))
            return -1;

        for (int mapIndex = 0; mapIndex < inputAsset.actionMaps.Count; mapIndex++)
        {
            InputActionMap map = inputAsset.actionMaps[mapIndex];
            for (int actionIndex = 0; actionIndex < map.actions.Count; actionIndex++)
            {
                InputAction candidate = map.actions[actionIndex];
                for (int bindingIndex = 0; bindingIndex < candidate.bindings.Count; bindingIndex++)
                {
                    if (!string.Equals(
                            candidate.bindings[bindingIndex].id.ToString(),
                            bindingId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    action = candidate;
                    return bindingIndex;
                }
            }
        }

        return -1;
    }

    /// <summary>还原 Unity 按键覆盖 JSON 中用字符串 null 表示的空覆盖值。</summary>
    private static string FromSavedOverrideValue(string value)
    {
        return string.Equals(value, "null", StringComparison.Ordinal)
            ? null
            : value;
    }

    private void BuildEditableEntries()
    {
        entries.Clear();
        for (int i = 0; i < EditableBindingSpecs.Length; i++)
        {
            BindingSpec spec = EditableBindingSpecs[i];
            InputAction action = actionMap.FindAction(spec.ActionName, false);
            if (action == null)
            {
                Debug.LogWarning($"[InputBindingService] 找不到输入动作：{spec.ActionName}");
                continue;
            }

            int bindingIndex = FindBindingIndex(
                action,
                spec.PartName,
                spec.BindingGroup,
                spec.BindingOrdinal);
            if (bindingIndex < 0)
            {
                Debug.LogWarning(
                    $"[InputBindingService] 找不到可编辑绑定：{spec.ActionName}/{spec.PartName}");
                continue;
            }

            entries.Add(new InputBindingEntry(
                spec.DisplayName,
                action,
                bindingIndex,
                spec.DeviceGroup,
                spec.BindingGroup,
                spec.ExpectedControlLayout));
        }
    }

    private static int FindBindingIndex(
        InputAction action,
        string partName,
        string bindingGroup,
        int bindingOrdinal)
    {
        int matchingBindingOrdinal = 0;
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (!BelongsToGroup(binding, bindingGroup))
                continue;

            if (!string.IsNullOrEmpty(partName))
            {
                if (binding.isPartOfComposite &&
                    string.Equals(binding.name, partName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }

                continue;
            }

            if (binding.isComposite || binding.isPartOfComposite)
                continue;

            if (matchingBindingOrdinal == bindingOrdinal)
                return i;

            matchingBindingOrdinal++;
        }

        return -1;
    }

    private InputBindingEntry FindConflict(InputBindingEntry changedEntry)
    {
        string changedPath = changedEntry.Action.bindings[changedEntry.BindingIndex].effectivePath;
        if (string.IsNullOrWhiteSpace(changedPath))
            return null;

        foreach (InputAction action in actionMap.actions)
        {
            for (int bindingIndex = 0; bindingIndex < action.bindings.Count; bindingIndex++)
            {
                if (ReferenceEquals(action, changedEntry.Action) &&
                    bindingIndex == changedEntry.BindingIndex)
                {
                    continue;
                }

                InputBinding binding = action.bindings[bindingIndex];
                if (!BelongsToGroup(binding, changedEntry.BindingGroup) ||
                    !string.Equals(changedPath, binding.effectivePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    InputBindingEntry candidate = entries[i];
                    if (candidate.DeviceGroup == changedEntry.DeviceGroup &&
                        ReferenceEquals(candidate.Action, action) &&
                        candidate.BindingIndex == bindingIndex)
                    {
                        return candidate;
                    }
                }

                return new InputBindingEntry(
                    action.name,
                    action,
                    bindingIndex,
                    changedEntry.DeviceGroup,
                    changedEntry.BindingGroup,
                    action.expectedControlType);
            }
        }

        return null;
    }

    private bool IsValidEntry(InputBindingEntry entry)
    {
        return entry != null &&
               entries.Contains(entry) &&
               entry.Action != null &&
               entry.BindingIndex >= 0 &&
               entry.BindingIndex < entry.Action.bindings.Count;
    }

    private static bool IsEffectivePath(InputBindingEntry entry, string path)
    {
        return string.Equals(
            entry.Action.bindings[entry.BindingIndex].effectivePath,
            path,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool BelongsToGroup(InputBinding binding, string bindingGroup)
    {
        if (string.IsNullOrEmpty(binding.groups) || string.IsNullOrEmpty(bindingGroup))
            return false;

        string[] groups = binding.groups.Split(InputBinding.Separator);
        for (int i = 0; i < groups.Length; i++)
        {
            if (string.Equals(groups[i], bindingGroup, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void RestoreOverride(
        InputAction action,
        int bindingIndex,
        string previousOverridePath,
        bool hadPreviousOverride)
    {
        if (!hadPreviousOverride)
            action.RemoveBindingOverride(bindingIndex);
        else
            action.ApplyBindingOverride(
                bindingIndex,
                previousOverridePath ?? string.Empty);
    }

    private void SaveOverrides()
    {
        store.Save(inputAsset.SaveBindingOverridesAsJson());
    }

    private void FinishRebind(InputRebindResult result)
    {
        InputActionRebindingExtensions.RebindingOperation operation = activeRebind;
        Action<InputRebindResult> callback = activeCallback;
        activeRebind = null;
        activeCallback = null;

        operation?.Dispose();

        if (restoreMapAfterRebind)
        {
            restoreMapAfterRebind = false;
            actionMap.Enable();
        }

        callback?.Invoke(result);
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(InputBindingService));
    }
}
