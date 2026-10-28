using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class SpaceMgr
{
    #region 单机已就绪星区表现缓存
    public const int SectorPresentationCacheCapacity = 2;
    public bool SectorPresentationCachingEnabled { get; set; } = true;
    private readonly Dictionary<SpaceSectorId, SectorView> retainedSectors = new();
    private readonly List<SpaceSectorId> invalidRetainedSectors = new(SectorPresentationCacheCapacity);
    private long sectorCacheOrder;
    public int SectorPresentationCacheHits { get; private set; }
    public int SectorPresentationCreatedCount { get; private set; }
    public int RetainedSectorPresentationCount => retainedSectors.Count;

    public void ReleaseRetainedPresentationCache() => ClearRetainedSectors();

    // 只读取活动星区完整装配状态，不推进加载或改变缓存。
    public bool IsSectorPresentationReady(SpaceSectorId sector) => sectors.TryGetValue(sector, out SectorView view) &&
        view.Root != null && view.PresentationReady && !view.Unloading;

    public bool TryGetSectorPresentationDebugState(SpaceSectorId sector,
        out int rootInstanceId, out bool retained, out bool active)
    {
        rootInstanceId = 0;
        active = false;
        retained = retainedSectors.TryGetValue(sector, out SectorView view);
        if (!retained && !sectors.TryGetValue(sector, out view)) return false;
        if (view.Root == null) return false;
        rootInstanceId = view.Root.GetInstanceID();
        active = view.Root.gameObject.activeInHierarchy;
        return true;
    }

    private bool IsSectorPresentationComplete(SectorView view)
    {
        if (view.Root == null || view.Data?.Bodies == null || view.Planets.Count != view.Data.Bodies.Count)
            return false;
        foreach (BodyState body in view.Data.Bodies)
        {
            if (SaveDataMgr.Instance?.SaveData?.PlanetData_Dict.TryGetValue(body.PlanetId, out _) != true)
                return false;
            Planet found = null;
            foreach (Planet planet in view.Planets)
                if (planet != null && planet.planetData?.BodyId == body.BodyId) { found = planet; break; }
            if (found == null) return false;
        }
        return true;
    }

    private bool IsRetainedSectorValid(SectorView view, SpaceSession session) =>
        SectorPresentationCachingEnabled && !GameNetwork.IsOnline && session?.State != null &&
        ReferenceEquals(view.SessionState, session.State) &&
        view.ResourceOwner != null && view.ResourceOwner == GameRes.ExistingInstance &&
        view.ResourceOwner.isLoadFinish && view.ResourceRevision == view.ResourceOwner.ItemDefinitionRevision &&
        ReferenceEquals(view.Data, session.GetUniverse(view.Sector).State) && IsSectorPresentationComplete(view);

    private bool TryRetainSector(SectorView view)
    {
        if (GameManager.Instance?.IsInGameWorld != true || !view.PresentationReady ||
            !IsRetainedSectorValid(view, SpaceSession.Current)) return false;
        // 只缓存星体；船、散物等仍按自己的生命周期移交或随原场景释放。
        var planetRoots = new HashSet<Transform>();
        foreach (Planet planet in view.Planets) planetRoots.Add(planet.transform);
        for (int index = view.Root.childCount - 1; index >= 0; index--)
        {
            Transform child = view.Root.GetChild(index);
            if (!planetRoots.Contains(child)) child.SetParent(null, true);
        }
        view.Root.gameObject.SetActive(false);
        view.Root.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(view.Root.gameObject, gameObject.scene);
        view.Root.SetParent(transform, true);
        view.Wanted = false;
        view.LastCached = ++sectorCacheOrder;
        retainedSectors[view.Sector] = view;
        while (retainedSectors.Count > SectorPresentationCacheCapacity)
        {
            SectorView oldest = null;
            foreach (SectorView candidate in retainedSectors.Values)
                if (oldest == null || candidate.LastCached < oldest.LastCached) oldest = candidate;
            retainedSectors.Remove(oldest.Sector);
            DestroyRetainedSector(oldest);
        }
        return true;
    }

    private bool TryRestoreRetainedSector(SpaceSession session, SpaceSectorId sector, Scene scene, out SectorView view)
    {
        view = null;
        if (!retainedSectors.Remove(sector, out SectorView cached)) return false;
        if (!IsRetainedSectorValid(cached, session))
        {
            DestroyRetainedSector(cached);
            return false;
        }
        // 缓存对象只在旧场景卸载完成后归还，避免 Wanted 与 Unloading 相互覆盖。
        cached.Root.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(cached.Root.gameObject, scene);
        cached.Scene = scene;
        cached.Wanted = true;
        cached.Unloading = false;
        cached.PresentationReady = true;
        cached.Loading = null;
        try
        {
            foreach (Planet planet in cached.Planets)
            {
                BodyState body = session.GetBody(planet.planetData.BodyId);
                if (!SaveDataMgr.Instance.SaveData.PlanetData_Dict.TryGetValue(body.PlanetId, out PlanetData data))
                    throw new InvalidOperationException("缓存星体失去当前会话数据：" + body.PlanetId);
                planet.planetData = data;
                RuntimePlanets.Add(planet);
                runtimePlanetDict[data.BodyId] = planet;
                planet.ApplyUniverse(session, body);
            }
            EnsureSpaceSceneTime(scene.name);
        }
        catch
        {
            foreach (Planet planet in cached.Planets)
                if (planet != null) { RuntimePlanets.Remove(planet); runtimePlanetDict.Remove(planet.planetData.BodyId); }
            DestroyRetainedSector(cached);
            throw;
        }
        SectorPresentationCacheHits++;
        view = cached;
        return true;
    }

    private void DestroyRetainedSector(SectorView view)
    {
        if (view.Root != null)
        {
            if (Application.isPlaying) Destroy(view.Root.gameObject);
            else DestroyImmediate(view.Root.gameObject);
        }
        view.Root = null;
        view.Planets.Clear();
    }

    private void ClearRetainedSectors()
    {
        foreach (SectorView view in retainedSectors.Values) DestroyRetainedSector(view);
        retainedSectors.Clear();
    }

    private void PruneRetainedSectors(SpaceSession session)
    {
        invalidRetainedSectors.Clear();
        foreach (KeyValuePair<SpaceSectorId, SectorView> entry in retainedSectors)
            if (!IsRetainedSectorValid(entry.Value, session)) invalidRetainedSectors.Add(entry.Key);
        foreach (SpaceSectorId sector in invalidRetainedSectors)
            if (retainedSectors.Remove(sector, out SectorView view)) DestroyRetainedSector(view);
        invalidRetainedSectors.Clear();
    }
    #endregion
}
