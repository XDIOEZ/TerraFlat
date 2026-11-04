using System;
using FlatWorld.Audio;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using UnityEngine;

/// <summary>冻结声源的规范坐标，目标死亡后不再依赖目标 Transform。</summary>
public struct CombatAudioLocation
{
    #region 世界与空间坐标
    public string WorldKey, ShipId;
    public bool InSpace;
    public SpaceSectorPosition Position;
    public Vector2 ShipLocalPosition;

    public static CombatAudioLocation Capture(Vector2 point, string worldKey, Item coordinateOwner = null)
    {
        worldKey ??= GameplayWorldRegistry.ExecutionWorldKey;
        var result = new CombatAudioLocation { WorldKey = worldKey };
        SpaceSession session = SpaceSession.Current;
        if (worldKey?.StartsWith("ship:", StringComparison.Ordinal) == true && session != null)
        {
            result.ShipId = worldKey.Substring(5);
            result.ShipLocalPosition = point;
            ShipState ship = session.GetShip(result.ShipId);
            if (ship != null)
            {
                ShipGeometry.LocalToWorld(ship, point.x, point.y, out double x, out double y);
                ShipFlightState flight = session.GetFlight(ship.ShipId);
                result.InSpace = flight?.Phase == ShipFlightPhase.Cruising;
                result.WorldKey = result.InSpace ? SpaceSectorCoordinates.SceneKey(ship.Sector) : flight?.SurfaceWorldKey ?? worldKey;
                result.Position = new SpaceSectorPosition(ship.Sector, new SpaceVector2(x, y));
                return result;
            }
        }
        if (coordinateOwner != null && SpaceSession.TryGetCombatFrame(coordinateOwner, out _, out _) &&
            SpaceSession.TryGetCombatPosition(coordinateOwner, point, out SpaceSectorPosition canonical))
        {
            result.InSpace = true;
            result.Position = canonical;
        }
        else result.Position = new SpaceSectorPosition(default, SpaceVector2.FromVector2(WorldLocalPresentation.ToLogical(point)));
        return result;
    }
    #endregion
}

/// <summary>玩法发布已解析短音，联网适配器只复制事件，不参与伤害与选音。</summary>
public static class CombatAudioFeedback
{
    #region 权威事件与本地表现
    public static event Action<string, CombatAudioLocation> Published;

    public static void Publish(string cueId, CombatAudioLocation location)
    {
        if (!GameNetwork.HasStateAuthority || string.IsNullOrWhiteSpace(cueId)) return;
        Published?.Invoke(cueId, location);
        PlayLocally(cueId, location);
    }

    public static void PlayLocally(string cueId, CombatAudioLocation location)
    {
        string listenerWorld = GameplayWorldRegistry.LocalPresentationWorldKey;
        SpaceSession session = SpaceSession.Current;
        Vector2 point;
        string sourceWorld = location.WorldKey;
        if (!string.IsNullOrEmpty(location.ShipId) && listenerWorld == "ship:" + location.ShipId)
        {
            point = location.ShipLocalPosition;
            sourceWorld = listenerWorld;
        }
        else if (location.InSpace)
        {
            Player listener = ItemMgr.Instance?.User_Player;
            if (session == null || listener == null ||
                !session.TryGetNetworkObserver(listener.ProfileName, out SpaceNetworkObserver observer) || !observer.InSpace) return;
            if (!string.IsNullOrEmpty(observer.ShipId) && listenerWorld == "ship:" + observer.ShipId)
            {
                ShipState ship = session.GetShip(observer.ShipId);
                if (ship == null) return;
                ShipGeometry.WorldToLocal(ship, location.Position.Sector, location.Position.LocalMeters.X,
                    location.Position.LocalMeters.Y, out double x, out double y);
                point = new Vector2((float)x, (float)y);
            }
            else point = session.ProjectPosition(location.Position);
            sourceWorld = listenerWorld;
        }
        else point = WorldLocalPresentation.ProjectPosition(location.Position.LocalMeters.ToVector2());
        AudioPlayOptions options = AudioPlayOptions.At(point);
        options.SourceWorldKey = sourceWorld;
        AudioService.Instance.Play(cueId, options);
    }
    #endregion
}
