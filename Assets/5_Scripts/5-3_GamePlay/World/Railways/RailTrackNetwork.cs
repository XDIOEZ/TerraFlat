using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

/// <summary>轨道位置保留连续正向，反向驾驶和跨弯不会随单格作者朝向翻转。</summary>
public readonly struct RailPosition
{
    #region 连续轨道坐标
    internal readonly Mod_RailTrack Track;
    internal readonly float Progress;
    internal readonly int Sign;
    internal readonly Vector2 Center;
    internal readonly ConveyorPath Route;
    internal RailPosition(Mod_RailTrack track, float progress, int sign, Vector2 center)
    { Track = track; Progress = progress; Sign = sign; Center = center; Route = track.Path; }
    public bool IsValid => Track != null && Track.IsAvailable && Track.Path.Same(Route);
    public Vector2 Position => Center + RailPathGeometry.Evaluate(Route, Progress);
    public Vector2 Tangent => RailPathGeometry.Tangent(Route, Progress) * Sign;
    public float Length => Route.Length;
    #endregion
}

/// <summary>铁路只复用传送带几何，不参与机械、输送或掉落物规则。</summary>
public static class RailPathGeometry
{
    #region 正式轨道几何
    public static Vector2 Direction(int port) => (port & 3) switch
    { 0 => Vector2.right, 1 => Vector2.up, 2 => Vector2.left, _ => Vector2.down };
    public static Vector2 Evaluate(ConveyorPath route, float progress)
    {
        route.TryMove(Direction(route.Input) * .5f, Mathf.Clamp(progress, 0f, route.Length), 1f, out Vector2 point);
        return point;
    }
    public static Vector2 Tangent(ConveyorPath route, float progress)
    {
        float canonical = route.CanonicalSign > 0 ? progress : route.Length - progress;
        Vector2 tangent = route.Curved ? new Vector2(Mathf.Cos(canonical * 2f), Mathf.Sin(canonical * 2f)) : Vector2.right;
        return ConveyorPath.Rotate(tangent * route.CanonicalSign, route.Rotation);
    }
    public static float Project(ConveyorPath route, Vector2 offset)
    {
        Vector2 local = ConveyorPath.Rotate(offset, -route.Rotation);
        float canonical = route.Curved
            ? Mathf.Clamp(Mathf.Atan2(local.x + .5f, .5f - local.y), 0f, Mathf.PI * .5f) * .5f
            : Mathf.Clamp01(local.x + .5f);
        return route.CanonicalSign > 0 ? canonical : route.Length - canonical;
    }
    #endregion
}

public static class RailTrackNetwork
{
    #region 世界轨道登记
    private sealed class WorldTracks
    {
        public WorldTopologyDomain Topology;
        public readonly Dictionary<Vector2Int, Mod_RailTrack> Tracks = new();
        public Vector2Int Normalize(Vector2Int cell)
        { int2 value = Topology.Normalize(new int2(cell.x, cell.y)); return new Vector2Int(value.x, value.y); }
    }
    private static readonly Dictionary<int, WorldTracks> worlds = new();
    private static readonly Dictionary<Mod_RailTrack, (int scene, Vector2Int cell)> owners = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { worlds.Clear(); owners.Clear(); }
    internal static void Register(Mod_RailTrack track)
    {
        Unregister(track);
        int scene = track.item.gameObject.scene.handle;
        if (!worlds.TryGetValue(scene, out WorldTracks world))
        {
            world = new WorldTracks();
            string key = track.item.gameObject.scene.name;
            bool knownWorld = GameplayWorldRegistry.TryGet(key, out _);
            using var context = knownWorld ? GameplayWorldRegistry.UseWorld(key) : default;
            if ((knownWorld || UnityEngine.SceneManagement.SceneManager.GetActiveScene() == track.item.gameObject.scene) &&
                WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds)) world.Topology = bounds.ToDomain();
            worlds.Add(scene, world);
        }
        Vector2 position = track.item.transform.position;
        Vector2Int cell = world.Normalize(new Vector2Int(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)));
        if (world.Tracks.TryGetValue(cell, out Mod_RailTrack existing) && existing != null && existing != track)
            throw new InvalidOperationException("同格不能重复登记铁路。");
        world.Tracks[cell] = track; owners.Add(track, (scene, cell));
        Refresh(world, cell);
    }
    internal static void Unregister(Mod_RailTrack track)
    {
        if (!owners.TryGetValue(track, out var owner)) return;
        owners.Remove(track);
        if (!worlds.TryGetValue(owner.scene, out WorldTracks world)) return;
        world.Tracks.Remove(owner.cell); Refresh(world, owner.cell);
        if (world.Tracks.Count == 0) worlds.Remove(owner.scene);
    }
    private static void Refresh(WorldTracks world, Vector2Int cell)
    {
        RefreshCell(world, cell);
        for (int port = 0; port < 4; port++) RefreshCell(world, cell + Vector2Int.RoundToInt(RailPathGeometry.Direction(port)));
    }
    private static void RefreshCell(WorldTracks world, Vector2Int cell)
    {
        cell = world.Normalize(cell);
        if (!world.Tracks.TryGetValue(cell, out Mod_RailTrack track) || track == null) return;
        int neighbors = 0;
        for (int port = 0; port < 4; port++)
            if (world.Tracks.TryGetValue(world.Normalize(cell + Vector2Int.RoundToInt(RailPathGeometry.Direction(port))), out Mod_RailTrack neighbor) && neighbor != null)
                neighbors |= 1 << port;
        track.SetPath(ConveyorPath.Select(neighbors, track.RotationQuarterTurns));
    }
    #endregion

    #region 投影与弧长推进
    public static RailPosition Reverse(RailPosition position)
        => position.IsValid ? new RailPosition(position.Track, position.Progress, -position.Sign, position.Center) : default;
    public static bool TryProject(Item host, Vector2 position, out RailPosition result)
    {
        result = default;
        if (host == null || !float.IsFinite(position.x) || !float.IsFinite(position.y) || !worlds.TryGetValue(host.gameObject.scene.handle, out WorldTracks world)) return false;
        Vector2Int origin = new(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y));
        float nearest = .36f;
        for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
        {
            Vector2Int cell = origin + new Vector2Int(x, y);
            if (!world.Tracks.TryGetValue(world.Normalize(cell), out Mod_RailTrack track) || track == null || !track.IsAvailable) continue;
            Vector2 center = (Vector2)cell + Vector2.one * .5f;
            float progress = RailPathGeometry.Project(track.Path, position - center);
            float sqrDistance = (center + RailPathGeometry.Evaluate(track.Path, progress) - position).sqrMagnitude;
            if (sqrDistance >= nearest) continue;
            nearest = sqrDistance; result = new RailPosition(track, progress, 1, center);
        }
        return result.IsValid;
    }
    public static bool TryAdvance(Item host, RailPosition start, float signedDistance, out RailPosition result, out float travelled)
    {
        result = start; travelled = 0f;
        if (host == null || !start.IsValid || !float.IsFinite(signedDistance) || host.gameObject.scene != start.Track.item.gameObject.scene ||
            !worlds.TryGetValue(host.gameObject.scene.handle, out WorldTracks world)) return false;
        float remaining = Mathf.Abs(signedDistance);
        int travelSign = signedDistance < 0f ? -1 : 1;
        int guard = 0;
        while (remaining > .000001f && guard++ < 4096)
        {
            int progressSign = result.Sign * travelSign;
            float room = progressSign > 0 ? result.Route.Length - result.Progress : result.Progress;
            float step = Mathf.Min(room, remaining);
            result = new RailPosition(result.Track, result.Progress + step * progressSign, result.Sign, result.Center);
            remaining -= step; travelled += step * travelSign;
            if (remaining <= .000001f) return true;
            int exit = progressSign > 0 ? result.Route.Output : result.Route.Input;
            Vector2Int cell = owners[result.Track].cell;
            Vector2Int nextCell = world.Normalize(cell + Vector2Int.RoundToInt(RailPathGeometry.Direction(exit)));
            if (!world.Tracks.TryGetValue(nextCell, out Mod_RailTrack next) || next == null || !next.IsAvailable || !next.Path.HasPathPort(exit + 2)) return false;
            int arrival = (exit + 2) & 3;
            bool entersInput = next.Path.Input == arrival;
            int nextProgressSign = entersInput ? 1 : -1;
            result = new RailPosition(next, entersInput ? 0f : next.Path.Length, nextProgressSign * travelSign,
                result.Center + RailPathGeometry.Direction(exit));
        }
        return remaining <= .000001f;
    }
    public static bool TrySignedDistance(Item host, RailPosition from, RailPosition to, float maximumDistance, out float distance)
    {
        distance = 0f;
        if (!from.IsValid || !to.IsValid || !(maximumDistance >= 0f) || !float.IsFinite(maximumDistance)) return false;
        for (int sign = 1; sign >= -1; sign -= 2)
        {
            RailPosition probe = from;
            float walked = 0f;
            for (int guard = 0; walked <= maximumDistance && guard < 4096; guard++)
            {
                if (probe.Track == to.Track)
                {
                    float local = (to.Progress - probe.Progress) * probe.Sign * sign;
                    if (local >= -.00001f && walked + local <= maximumDistance)
                    { distance = (walked + Mathf.Max(0f, local)) * sign; return true; }
                }
                float edge = (probe.Sign * sign > 0 ? probe.Route.Length - probe.Progress : probe.Progress) + .0001f;
                if (!TryAdvance(host, probe, edge * sign, out RailPosition next, out float moved)) break;
                walked += Mathf.Abs(moved); probe = next;
            }
        }
        return false;
    }
    #endregion
}
