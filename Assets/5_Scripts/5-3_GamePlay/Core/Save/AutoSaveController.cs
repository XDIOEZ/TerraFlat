// AI-Context: 自动保存的全局偏好与运行时调度器；偏好写入 PlayerPrefs，只有进入游戏世界后才按未缩放时间触发正式存档。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlatWorld.Settings;
using UnityEngine;

public static class AutoSavePreferences
{
    public const int DefaultIntervalMinutes = 10;
    public const int MinIntervalMinutes = 1;
    public const int MaxIntervalMinutes = 1440;

    public const string SettingsProviderId = "auto-save";
    public const string IntervalSettingKey = "autoSave.interval";

    private const string EnabledKey = "FlatWorld.AutoSave.Enabled.v1";
    private const string IntervalMinutesKey = "FlatWorld.AutoSave.IntervalMinutes.v1";

    public static event Action Changed;

    private static readonly ISettingsProvider settingsProvider =
        CreateSettingsProvider();

    /// <summary>供设置 UI 使用的自动保存下拉列表契约。</summary>
    public static ISettingsProvider SettingsProvider => RegisterSettingsProvider();

    public static bool Enabled => PlayerPrefs.GetInt(EnabledKey, 1) != 0;

    public static int IntervalMinutes =>
        Mathf.Clamp(
            PlayerPrefs.GetInt(IntervalMinutesKey, DefaultIntervalMinutes),
            MinIntervalMinutes,
            MaxIntervalMinutes);

    public static void Disable()
    {
        Save(false, IntervalMinutes);
    }

    public static void Enable(int intervalMinutes)
    {
        Save(true, Mathf.Clamp(intervalMinutes, MinIntervalMinutes, MaxIntervalMinutes));
    }

    private static void Save(bool enabled, int intervalMinutes)
    {
        bool changed = Enabled != enabled || IntervalMinutes != intervalMinutes;
        PlayerPrefs.SetInt(EnabledKey, enabled ? 1 : 0);
        PlayerPrefs.SetInt(IntervalMinutesKey, intervalMinutes);
        PlayerPrefs.Save();

        if (changed)
            Changed?.Invoke();
    }

    #region 设置提供者

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSettingsProviderOnLoad()
    {
        RegisterSettingsProvider();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        Changed = null;
    }

    private static ISettingsProvider RegisterSettingsProvider()
    {
        SettingsProviderRegistry.Register(settingsProvider);
        return settingsProvider;
    }

    private static ISettingsProvider CreateSettingsProvider()
    {
        return new AutoSaveSettingsProvider();
    }

    private sealed class AutoSaveSettingsProvider : ISettingsProvider
    {
        private static readonly IReadOnlyList<SettingOption> Options =
            new SettingOption[]
            {
                new SettingOption("disabled", "永远不自动保存"),
                new SettingOption("1-minute", "每 1 分钟"),
                new SettingOption("5-minutes", "每 5 分钟"),
                new SettingOption("10-minutes", "每 10 分钟"),
                new SettingOption("15-minutes", "每 15 分钟"),
                new SettingOption("30-minutes", "每 30 分钟"),
                new SettingOption("custom", "自定义间隔")
            };

        private static readonly int[] PresetMinutes = { 0, 1, 5, 10, 15, 30, -1 };
        private readonly IReadOnlyList<ISettingsDropdown> dropdowns;

        public AutoSaveSettingsProvider()
        {
            dropdowns = new ISettingsDropdown[]
            {
                new SettingsDropdown(
                    new SettingDescriptor(
                        IntervalSettingKey,
                        "自动保存间隔",
                        SettingControlType.Dropdown,
                        "save",
                        order: 0),
                    Options,
                    ResolveCurrentOptionIndex,
                    TrySetPreset)
            };
        }

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "自动保存";
        public int Order => 50;
        public IReadOnlyList<ISettingsToggle> ToggleSettings =>
            Array.Empty<ISettingsToggle>();
        public IReadOnlyList<ISettingsSlider> SliderSettings =>
            Array.Empty<ISettingsSlider>();
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => dropdowns;
        public IReadOnlyList<ISettingsSwitch> SwitchSettings =>
            Array.Empty<ISettingsSwitch>();

        public void ResetToDefaults() => Enable(DefaultIntervalMinutes);

        private static int ResolveCurrentOptionIndex()
        {
            if (!Enabled)
                return 0;

            for (int i = 1; i < PresetMinutes.Length - 1; i++)
            {
                if (PresetMinutes[i] == IntervalMinutes)
                    return i;
            }

            return PresetMinutes.Length - 1;
        }

        private static string TrySetPreset(int index)
        {
            if (index < 0 || index >= PresetMinutes.Length)
                return "自动保存选项无效。";
            if (index == PresetMinutes.Length - 1)
                return "自定义间隔需要先输入分钟数。";

            if (index == 0)
                Disable();
            else
                Enable(PresetMinutes[index]);
            return null;
        }
    }

    #endregion
}

[DisallowMultipleComponent]
public sealed class AutoSaveController : MonoBehaviour
{
    private GameManager gameManager;
    private double nextSaveTime = -1d;
    private bool isSaving;
    private bool isCapturing;
    private Task<bool> pendingSaveWrite;

    public static AutoSaveController Ensure(GameManager manager)
    {
        if (manager == null)
            return null;

        AutoSaveController controller = manager.GetComponent<AutoSaveController>();
        if (controller == null)
            controller = manager.gameObject.AddComponent<AutoSaveController>();

        controller.Attach(manager);
        return controller;
    }

    private void Attach(GameManager manager)
    {
        if (gameManager == manager)
            return;

        Unsubscribe();
        gameManager = manager;
        Subscribe();
        ResetSchedule();
    }

    private void OnEnable()
    {
        Subscribe();
        AutoSavePreferences.Changed -= ResetSchedule;
        AutoSavePreferences.Changed += ResetSchedule;
        ResetSchedule();
    }

    private void OnDisable()
    {
        Unsubscribe();
        AutoSavePreferences.Changed -= ResetSchedule;
        nextSaveTime = -1d;
        // 协程在禁用时由 Unity 停止；后台写盘任务仍保持，重新启用后会继续轮询并清理状态。
        isCapturing = false;
    }

    private void Subscribe()
    {
        if (gameManager == null)
            return;

        gameManager.Event_GameWorldEnter -= ResetSchedule;
        gameManager.Event_GameWorldEnter += ResetSchedule;
        gameManager.Event_GameWorldExit -= StopSchedule;
        gameManager.Event_GameWorldExit += StopSchedule;
    }

    private void Unsubscribe()
    {
        if (gameManager == null)
            return;

        gameManager.Event_GameWorldEnter -= ResetSchedule;
        gameManager.Event_GameWorldExit -= StopSchedule;
    }

    private void Update()
    {
        // 后台写盘期间仍允许 Unity 正常处理输入、物理和实体 Tick；完成后再记录结果。
        if (isSaving)
        {
            if (isCapturing || !TryCompletePendingSave())
                return;
        }

        if (!CanRun())
        {
            nextSaveTime = -1d;
            return;
        }

        if (nextSaveTime < 0d)
        {
            ScheduleFromNow();
            return;
        }

        if (!isSaving && Time.realtimeSinceStartupAsDouble >= nextSaveTime)
            SaveNow();
    }

    private bool CanRun()
    {
        return gameManager != null &&
               gameManager.IsInGameWorld &&
               AutoSavePreferences.Enabled;
    }

    private void ResetSchedule()
    {
        if (CanRun())
            ScheduleFromNow();
        else
            nextSaveTime = -1d;
    }

    private void StopSchedule()
    {
        nextSaveTime = -1d;
    }

    private void ScheduleFromNow()
    {
        nextSaveTime =
            Time.realtimeSinceStartupAsDouble +
            AutoSavePreferences.IntervalMinutes * 60d;
    }

    private void SaveNow()
    {
        isSaving = true;
        isCapturing = true;
        ScheduleFromNow();
        gameManager.BeginSaveStatus();
        StartCoroutine(SaveNowCoroutine());
    }

    /// <summary>分帧采集世界快照；磁盘任务创建后由 Update 非阻塞轮询。</summary>
    private IEnumerator SaveNowCoroutine()
    {
        Task<bool> writeTask = null;
        yield return gameManager.SaveGameInBackgroundCoroutine(task => writeTask = task);

        isCapturing = false;
        if (writeTask != null)
        {
            pendingSaveWrite = writeTask;
            yield break;
        }

        pendingSaveWrite = Task.FromException<bool>(
            new InvalidOperationException("自动保存未创建后台写入任务。"));
    }

    /// <summary>轮询后台磁盘写入，绝不在主线程等待任务完成。</summary>
    private bool TryCompletePendingSave()
    {
        if (pendingSaveWrite == null)
        {
            isSaving = false;
            gameManager?.CompleteSaveStatus(false);
            return true;
        }

        if (!pendingSaveWrite.IsCompleted)
            return false;

        bool statusSucceeded = true;
        try
        {
            bool wroteToDisk = pendingSaveWrite.GetAwaiter().GetResult();
            if (wroteToDisk)
            {
                Debug.Log(
                    $"[AutoSave] 自动保存完成，下次将在 {AutoSavePreferences.IntervalMinutes} 分钟后触发。");
            }
            else
            {
                Debug.Log("[AutoSave] 自动保存已被较新的手动或退出保存取代。");
            }
        }
        catch (Exception exception)
        {
            statusSucceeded = false;
            Debug.LogException(new InvalidOperationException(
                "[AutoSave] 后台写盘失败，已保留下一个保存周期。",
                exception));
        }
        finally
        {
            pendingSaveWrite = null;
            isSaving = false;
            gameManager?.CompleteSaveStatus(statusSucceeded);
        }

        return true;
    }
}
