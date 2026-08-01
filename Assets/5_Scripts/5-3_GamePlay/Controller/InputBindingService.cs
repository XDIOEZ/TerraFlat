using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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

    public InputBindingEntry(string displayName, InputAction action, int bindingIndex)
    {
        DisplayName = displayName;
        Action = action;
        BindingIndex = bindingIndex;
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
        public readonly int BindingOrdinal;

        public BindingSpec(
            string actionName,
            string partName,
            string displayName,
            int bindingOrdinal = 0)
        {
            ActionName = actionName;
            PartName = partName;
            DisplayName = displayName;
            BindingOrdinal = bindingOrdinal;
        }
    }

    private static readonly BindingSpec[] EditableBindingSpecs =
    {
        new BindingSpec("Move_Player", "up", "向上移动"),
        new BindingSpec("Move_Player", "down", "向下移动"),
        new BindingSpec("Move_Player", "left", "向左移动"),
        new BindingSpec("Move_Player", "right", "向右移动"),
        new BindingSpec("LeftClick", null, "主要操作"),
        new BindingSpec("RightClick", null, "次要操作"),
        new BindingSpec("E", null, "交互"),
        new BindingSpec("Shift", null, "奔跑"),
        new BindingSpec("Tab", null, "营养面板"),
        new BindingSpec("CtrlMouse", "Modifier", "镜头缩放修饰键"),
        new BindingSpec("OpenChat", null, "打开聊天框"),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 1", 0),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 2", 1),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 3", 2),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 4", 3),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 5", 4),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 6", 5),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 7", 6),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 8", 7),
        new BindingSpec("SwitchHotBar_Player", null, "快捷栏 9", 8),
        new BindingSpec("ESC", null, "打开设置")
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

        restoreMapAfterRebind = actionMap.enabled;
        if (restoreMapAfterRebind)
            actionMap.Disable();

        activeCallback = onFinished;

        try
        {
            activeRebind = action.PerformInteractiveRebinding(bindingIndex)
                .WithExpectedControlType<ButtonControl>()
                .WithCancelingThrough("<Keyboard>/escape")
                .WithCancelingThrough("<Gamepad>/buttonEast")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .OnMatchWaitForAnother(0.1f)
                .OnCancel(_ => FinishRebind(new InputRebindResult
                {
                    Status = InputRebindStatus.Canceled
                }))
                .OnComplete(_ =>
                {
                    InputBindingEntry conflict = FindConflict(entry);
                    if (conflict != null)
                    {
                        RestoreOverride(action, bindingIndex, previousOverridePath);
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
            RestoreOverride(action, bindingIndex, previousOverridePath);
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
            inputAsset.LoadBindingOverridesFromJson(json);
        }
        catch (Exception exception)
        {
            inputAsset.RemoveAllBindingOverrides();
            store.Clear();
            Debug.LogWarning($"[InputBindingService] 已忽略损坏的按键配置：{exception.Message}");
        }
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
                spec.BindingOrdinal);
            if (bindingIndex < 0)
            {
                Debug.LogWarning(
                    $"[InputBindingService] 找不到可编辑绑定：{spec.ActionName}/{spec.PartName}");
                continue;
            }

            entries.Add(new InputBindingEntry(spec.DisplayName, action, bindingIndex));
        }
    }

    private static int FindBindingIndex(
        InputAction action,
        string partName,
        int bindingOrdinal)
    {
        int matchingBindingOrdinal = 0;
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
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

            string path = binding.path;
            if (path != null &&
                (path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase)))
            {
                if (matchingBindingOrdinal == bindingOrdinal)
                    return i;

                matchingBindingOrdinal++;
            }
        }

        return -1;
    }

    private InputBindingEntry FindConflict(InputBindingEntry changedEntry)
    {
        string changedPath = changedEntry.Action.bindings[changedEntry.BindingIndex].effectivePath;
        if (string.IsNullOrWhiteSpace(changedPath))
            return null;

        for (int i = 0; i < entries.Count; i++)
        {
            InputBindingEntry candidate = entries[i];
            if (ReferenceEquals(candidate, changedEntry))
                continue;

            string candidatePath =
                candidate.Action.bindings[candidate.BindingIndex].effectivePath;
            if (string.Equals(changedPath, candidatePath, StringComparison.OrdinalIgnoreCase))
                return candidate;
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

    private static void RestoreOverride(
        InputAction action,
        int bindingIndex,
        string previousOverridePath)
    {
        if (string.IsNullOrEmpty(previousOverridePath))
            action.RemoveBindingOverride(bindingIndex);
        else
            action.ApplyBindingOverride(bindingIndex, previousOverridePath);
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
