using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class MachineWorld
{
    #region 无人世界的纯数据调度
    private static readonly List<string> backgroundWorldKeys = new();
    private static readonly HashSet<string> backgroundWorldSet = new(StringComparer.Ordinal);

    // 只消费本次游戏传入的时间，恢复档案不会补算退出游戏后的时间。
    public static void TickBackgroundWorlds(float seconds)
    {
        if (!GameNetwork.HasStateAuthority || !MachineDefinition.Positive(seconds)) return;
        GameSaveData save = SaveDataMgr.Instance?.SaveData;
        if (save?.Mechanical?.Worlds == null || save.PlanetData_Dict == null) return;
        string activeWorld = SceneManager.GetActiveScene().name;
        backgroundWorldKeys.Clear(); backgroundWorldSet.Clear();
        foreach (string key in save.Mechanical.Worlds.Keys) RegisterBackgroundWorld(save, key, activeWorld);
        foreach (string key in scopes.Keys) RegisterBackgroundWorld(save, key, activeWorld);
        // 先复制世界键，爆炸与加工捕获修改档案时不会破坏当前枚举。
        foreach (string key in backgroundWorldKeys)
        {
            bool loaded = GameplayWorldRegistry.TryGet(key, out _);
            using var worldScope = loaded ? GameplayWorldRegistry.UseWorld(key) : null;
            using var lease = UseScope(key);
            ScopeState background = scope;
            background.BackgroundSimulation = true;
            try
            {
                if (!loaded) DroppedItemService.TickColdWorld(key, seconds);
                Tick(seconds);
            }
            finally { background.BackgroundSimulation = false; }
        }
    }

    private static void RegisterBackgroundWorld(GameSaveData save, string key, string activeWorld)
    {
        if (string.IsNullOrWhiteSpace(key) || key == activeWorld || key.StartsWith("ship:", StringComparison.Ordinal) ||
            !save.PlanetData_Dict.ContainsKey(key) || !backgroundWorldSet.Add(key)) return;
        backgroundWorldKeys.Add(key);
    }

    public static bool CanPublishLocalEnvironment(MachineEntity node)
        => node != null && node.ScopeKey == SceneManager.GetActiveScene().name;

    public static float SampleAmbientTemperature(MachineEntity node)
    {
        if (node == null) return TemperatureMgr.DefaultAmbientTemperature;
        using var lease = UseNodeScope(node);
        return TryGetFluidAmbientTemperature(node, out float temperature) ? temperature : TemperatureMgr.DefaultAmbientTemperature;
    }
    #endregion
}
