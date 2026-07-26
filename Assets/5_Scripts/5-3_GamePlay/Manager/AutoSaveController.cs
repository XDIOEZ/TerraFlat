// AI-Context: 自动保存的全局偏好与运行时调度器；偏好写入 PlayerPrefs，只有进入游戏世界后才按未缩放时间触发正式存档。
using System;
using UnityEngine;

public static class AutoSavePreferences
{
    public const int DefaultIntervalMinutes = 10;
    public const int MinIntervalMinutes = 1;
    public const int MaxIntervalMinutes = 1440;

    private const string EnabledKey = "FlatWorld.AutoSave.Enabled.v1";
    private const string IntervalMinutesKey = "FlatWorld.AutoSave.IntervalMinutes.v1";

    public static event Action Changed;

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
}

[DisallowMultipleComponent]
public sealed class AutoSaveController : MonoBehaviour
{
    private GameManager gameManager;
    private double nextSaveTime = -1d;
    private bool isSaving;

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
        ScheduleFromNow();

        try
        {
            gameManager.SaveGame();
            Debug.Log($"[AutoSave] 自动保存完成，下次将在 {AutoSavePreferences.IntervalMinutes} 分钟后触发。");
        }
        catch (Exception exception)
        {
            Debug.LogException(new InvalidOperationException(
                "[AutoSave] 自动保存失败，已保留下一个保存周期。",
                exception));
        }
        finally
        {
            isSaving = false;
        }
    }
}
