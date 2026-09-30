using System.Collections.Generic;
using UnityEngine;

/// <summary>蜂巢领地以整格范围持续观测玩家，并把同一份警戒进度广播给仍处于领地内的成员。</summary>
public sealed partial class Mod_HiveColony
{
    #region 领地警戒
    private readonly Dictionary<Player, float> playerStay = new(); // 每名玩家的连续停留时间。
    private readonly HashSet<Player> observedPlayers = new(); // 本轮仍在领地的玩家。
    private readonly List<Player> expiredPlayers = new(); // 离开领地的玩家。

    /// <summary>静止玩家也能推进蜂巢警戒；只有仍在领地内的蜜蜂接收共享愤怒进度。</summary>
    private void TickTerritoryAlarm(float deltaTime)
    {
        observedPlayers.Clear();
        ItemMgr manager = itemManager;
        if (manager == null)
            return;
        float highestStay = 0f;
        foreach (Player player in manager.Player_DIC.Values)
        {
            if (player == null || !player.gameObject.activeInHierarchy || player.DestructionHandled)
                continue;
            if (!IsInsideTerritory(player.transform.position))
                continue;
            observedPlayers.Add(player);
            playerStay.TryGetValue(player, out float seconds);
            seconds = Mathf.Min(AlarmSeconds, seconds + deltaTime);
            playerStay[player] = seconds;
            highestStay = Mathf.Max(highestStay, seconds);
        }

        expiredPlayers.Clear();
        foreach (Player player in playerStay.Keys)
            if (!observedPlayers.Contains(player))
                expiredPlayers.Add(player);
        foreach (Player player in expiredPlayers)
            playerStay.Remove(player);

        ApplyTerritoryAlert(highestStay);
    }

    /// <summary>领地内成员共享玩家的最高停留进度；飞出领地的成员立即移除这一路警戒源。</summary>
    private void ApplyTerritoryAlert(float staySeconds)
    {
        foreach (Item resident in residents.Values)
        {
            Mod_BeeBehavior bee = resident?.itemMods?.GetMod_ByID<Mod_BeeBehavior>(Mod_BeeBehavior.ModuleId);
            if (bee == null)
                continue;
            float residentStay = IsInsideTerritory(resident.transform.position) ? staySeconds : 0f;
            bee.SetColonyTerritoryAlert(residentStay, AlarmSeconds);
        }
    }

    /// <summary>领地按蜂巢所在整格向外扩展，半径随当前成员数增长；循环世界使用最近镜像距离。</summary>
    private bool IsInsideTerritory(Vector2 worldPosition)
    {
        Vector2 home = CellCenter(HomePosition);
        Vector2 target = CellCenter(WorldTopologyRuntime.NormalizePosition(worldPosition));
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(home, target);
        return Mathf.Abs(delta.x) <= TerritoryRadiusCells && Mathf.Abs(delta.y) <= TerritoryRadiusCells;
    }

    /// <summary>对成员行为公开与警戒完全相同的领地判定，避免巡逻边界另算一套。</summary>
    public bool ContainsTerritoryPosition(Vector2 worldPosition) => IsInsideTerritory(worldPosition);

    /// <summary>在领地随机整格内加入少量格内偏移，让巡逻自然且保证不会选出边界。</summary>
    public Vector2 GetRandomTerritoryPatrolPosition()
    {
        Vector2 center = CellCenter(HomePosition);
        int radius = TerritoryRadiusCells;
        int offsetX = UnityEngine.Random.Range(-radius, radius + 1);
        int offsetY = UnityEngine.Random.Range(-radius, radius + 1);
        Vector2 jitter = new(
            UnityEngine.Random.Range(-0.4f, 0.4f),
            UnityEngine.Random.Range(-0.4f, 0.4f));
        return WorldTopologyRuntime.NormalizePosition(center + new Vector2(offsetX, offsetY) + jitter);
    }

    /// <summary>重载或卸载时清除本轮玩家停留记录。</summary>
    private void ClearTerritoryAlarm()
    {
        playerStay.Clear();
        observedPlayers.Clear();
        expiredPlayers.Clear();
    }

    /// <summary>领地按整格中心判断，避免蜂巢放置偏移改变覆盖范围。</summary>
    private static Vector2 CellCenter(Vector2 position) =>
        new(Mathf.Floor(position.x) + 0.5f, Mathf.Floor(position.y) + 0.5f);

#if UNITY_EDITOR
    /// <summary>开启蜂巢信息后在 Scene 视图框出实时领地，尺寸与运行时整格判定完全一致。</summary>
    private void OnDrawGizmos()
    {
        if (!HiveColonyDebugOverlay.Visible || TerritoryBaseRadiusCells < 0)
            return;

        Vector2 source = item != null
            ? (Vector2)item.transform.position
            : (Vector2)(transform.parent != null ? transform.parent.position : transform.position);
        Vector2 center = CellCenter(source);
        float size = TerritorySizeCells;
        Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.75f);
        Gizmos.DrawWireCube(new Vector3(center.x, center.y, transform.position.z), new Vector3(size, size, 0f));
    }
#endif
    #endregion
}
