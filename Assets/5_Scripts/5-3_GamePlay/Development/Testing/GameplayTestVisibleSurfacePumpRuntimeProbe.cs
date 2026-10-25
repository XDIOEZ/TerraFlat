using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>保留到结束截图的真实水池、电泵、铜线、液管与储罐现场。</summary>
internal static class GameplayTestVisibleSurfacePumpRuntimeProbe
{
    #region 可见地表抽水跨帧入口
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterVisibleSurfacePumpOperations()
    {
        GameplayTestRuntimeProbe.RegisterAsync("industrial_fluids", "visible_surface_pump", CaptureVisibleSurfacePump);
    }

    private static IEnumerator CaptureVisibleSurfacePump(Player player, JObject args, Action<JObject> complete)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var fixture = new VisibleSurfacePumpFixture(player);
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        fixture.Build();
        float deadline = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            fixture.ObserveSharedWaterSearch();
            if (fixture.PresentationReady && fixture.SharedWaterSearchReady) break;
            yield return null;
        }
        if (!fixture.PresentationReady || !fixture.SharedWaterSearchReady)
            throw new InvalidOperationException("水池、正式机器表现或跨区块共享寻源未在八秒内就绪：" + fixture.PresentationDiagnostics());
        float initialTime = Time.time;
        while (Time.time - initialTime < .35f) yield return null;
        int unpoweredLevel = fixture.TotalLevel;
        bool run = args.Value<bool?>("after") == true;
        if (run)
        {
            fixture.ChargeBatteries();
            deadline = Time.realtimeSinceStartup + 40f;
            while (fixture.TotalLevel > 20 && Time.realtimeSinceStartup < deadline)
            {
                fixture.ObserveElectricalTick();
                yield return null;
            }
            fixture.ObserveElectricalTick();
            // 只切断真实电源保留抽后现场，绝不改写水量或储罐库存来凑图片。
            fixture.DisconnectPower();
            initialTime = Time.time;
            while (Time.time - initialTime < .35f) yield return null;
        }
        yield return null;
        complete(fixture.Sample(unpoweredLevel, run));
    }
    #endregion

    #region 真实地形快照和设备租约
    private sealed class TerrainCellSnapshot
    {
        private static readonly string[] layers =
        {
            TerrainSurfaceHeightLayer.ExcavatedLevelsLayerId,
            TerrainSurfaceHeightLayer.CoverLevelsLayerId,
            TerrainSurfaceHeightLayer.NaturalDepthLevelsLayerId
        };
        public readonly RuntimeTerrainTileSample Sample;
        private readonly TerrainCell cell;
        private readonly string liquid;
        private readonly int level;
        private readonly float[] values = new float[layers.Length];
        public TerrainCellSnapshot(RuntimeTerrainTileSample sample)
        {
            Sample = sample; cell = sample.Cell; liquid = sample.LiquidId; level = sample.LiquidLevel;
            for (int i = 0; i < layers.Length; i++)
                sample.Terrain.TryGetEnvironmentValue(layers[i], sample.LocalCell.x, sample.LocalCell.y, out values[i]);
        }
        public void Prepare(bool water)
        {
            var terrain = Sample.Terrain; int x = Sample.LocalCell.x, y = Sample.LocalCell.y;
            terrain.SetCell(x, y, new TerrainCell(cell.GroundTileId, 0, 0, cell.BiomeId,
                cell.NavigationCost, TerrainCellFlags.Walkable));
            terrain.SetEnvironmentValue(layers[0], x, y, water ? 1 : 0);
            terrain.SetEnvironmentValue(layers[1], x, y, 0);
            terrain.SetEnvironmentValue(layers[2], x, y, 0);
            WorldLiquidSystem.TrySet(Sample, "core:dirty_water", water ? 10 : 0);
        }
        public void Restore()
        {
            if (Sample.Terrain == null || Sample.Terrain.IsDisposed) return;
            var terrain = Sample.Terrain; int x = Sample.LocalCell.x, y = Sample.LocalCell.y;
            terrain.SetCell(x, y, cell);
            for (int i = 0; i < layers.Length; i++) terrain.SetEnvironmentValue(layers[i], x, y, values[i]);
            WorldLiquidSystem.TrySet(Sample, liquid, level);
        }
        public int Level => Sample.Terrain.GetLiquidLevel(Sample.LocalCell.x, Sample.LocalCell.y);
    }

    private sealed class VisibleSurfacePumpFixture : IDisposable
    {
        private readonly Player player;
        private readonly Vector3 playerPosition;
        private readonly Mod_Cam cameraModule;
        private readonly float originalCameraSize;
        private readonly List<TerrainCellSnapshot> terrain = new();
        private readonly List<TerrainCellSnapshot> water = new();
        private readonly List<MachineEntity> machines = new();
        private readonly List<MachineEntity> pumps = new(), tanks = new(), batteries = new();
        private readonly string worldKey;
        private readonly IDisposable windShield;
        private GameObject cameraTarget;
        private Vector2Int origin;
        private bool disposed, boundaryRegistered;
        private bool sharedSearch;
        private JObject searchDiagnostics;
        private readonly bool[] powered = new bool[2];
        private readonly float[] minimumEnergy = { 60000f, 60000f };

        public VisibleSurfacePumpFixture(Player player)
        {
            this.player = player; playerPosition = player.transform.position; worldKey = player.gameObject.scene.name;
            cameraModule = player.GetComponentInChildren<Mod_Cam>(true) ??
                throw new InvalidOperationException("抽水现场缺少正式玩家镜头模块。");
            if (cameraModule.HasFollowOverride) throw new InvalidOperationException("抽水现场不能覆盖已有镜头租约。");
            originalCameraSize = cameraModule.CurrentOrthographicSize;
            windShield = player.GetComponentInChildren<Mod_Mover>(true)?.SuppressWindMotion();
        }

        public void Build()
        {
            using var scope = MachineWorld.UseScope(worldKey);
            origin = FindLoadedUnoccupiedRectangle();
            var boundary = new List<Vector2Int>();
            for (int x = -1; x <= 5; x++)
            {
                boundary.Add(origin + new Vector2Int(x, -1));
                boundary.Add(origin + new Vector2Int(x, 4));
            }
            for (int y = 0; y < 4; y++)
            {
                boundary.Add(origin + new Vector2Int(-1, y));
                boundary.Add(origin + new Vector2Int(5, y));
            }
            WorldLiquidFlowObstacles.Register(this, boundary); boundaryRegistered = true;
            // 自有矩形只暂改真实地形层，水池周边封闭以免消耗地图原有水域。
            for (int y = -2; y <= 5; y++)
            for (int x = -1; x <= 9; x++)
            {
                ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile((Vector2)(origin + new Vector2Int(x, y)) + Vector2.one * .5f, out var sample);
                var snapshot = new TerrainCellSnapshot(sample); terrain.Add(snapshot);
                bool isWater = x >= 0 && x < 5 && y >= 0 && y < 4;
                if (isWater) water.Add(snapshot);
                snapshot.Prepare(isWater);
            }
            for (int branch = 0; branch < 2; branch++)
            {
                int y = branch == 0 ? 0 : 3;
                pumps.Add(Place("LiquidPump_Electric", 5, y));
                Place("LiquidPipe", 6, y); Place("LiquidPipe", 7, y);
                tanks.Add(Place("LiquidStorageTank", 8, y));
                int wireY = branch == 0 ? -1 : 4;
                batteries.Add(Place("Battery_Basic", 7, wireY));
                Place("Wire_Copper", 5, y); Place("Wire_Copper", 5, wireY);
                Place("Wire_Copper", 6, wireY); Place("Wire_Copper", 7, wireY);
            }
            player.transform.position = WorldLocalPresentation.ProjectPosition((Vector2)origin + new Vector2(2.5f, -1.5f));
            cameraTarget = new GameObject("[VisibleSurfacePumpCameraTarget]");
            SceneManager.MoveGameObjectToScene(cameraTarget, player.gameObject.scene);
            cameraTarget.transform.position = WorldLocalPresentation.ProjectPosition((Vector2)origin + new Vector2(4.5f, 1.5f));
            cameraModule.SetFollowOverride(cameraTarget.transform, followMode: Mod_Cam.FollowMode.PresentationLocked);
            cameraModule.SetOrthographicSize(5.5f);
        }

        private Vector2Int FindLoadedUnoccupiedRectangle()
        {
            Vector2Int center = Vector2Int.FloorToInt(WorldLocalPresentation.ToLogical(playerPosition));
            for (int radius = 0; radius <= 12; radius += 2)
            for (int dx = -radius; dx <= radius; dx += 2)
            for (int dy = -radius; dy <= radius; dy += 2)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue;
                Vector2Int candidate = center + new Vector2Int(dx - 4, dy - 1);
                bool free = true;
                for (int y = -2; y <= 5 && free; y++)
                for (int x = -1; x <= 9; x++)
                {
                    Vector2Int cell = candidate + new Vector2Int(x, y);
                    if (!ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile((Vector2)cell + Vector2.one * .5f, out var sample) ||
                        sample.Cell.GroundTileId == 0 || sample.LiquidLevel != 0 ||
                        TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) != 0 ||
                        MachineWorld.IsOccupied(cell, 0) || MachineWorld.IsOccupied(cell, 2) || MachineWorld.IsOccupied(cell, 3) ||
                        BuildingOccupancyRegistry.IsOccupied(cell, layer: 0)) { free = false; break; }
                }
                if (free) return candidate;
            }
            throw new InvalidOperationException("玩家附近没有已加载且无建筑的 11×8 抽水现场。");
        }

        private MachineEntity Place(string id, int x, int y)
        {
            if (!GameRes.ExistingInstance.TryGetItemDefinition(id, out RuntimeItemDefinition definition))
                throw new InvalidOperationException("抽水现场缺少真实物品：" + id);
            ItemData data = definition.CreateItemData(); data.Guid = ItemMgr.GetInstance().GenerateGuid();
            data.transform ??= new ItemTransform();
            data.transform.position = new Vector3(origin.x + x + .5f, origin.y + y + .5f);
            data.transform.rotation = Quaternion.identity; data.transform.scale = Vector3.one; data.inHand = false;
            Mod_Building.SetInstalledDataState(data);
            MachineWorld.WriteMachineState(data, new MachineState());
            MachineEntity node = MachineWorld.Place(data); machines.Add(node); return node;
        }

        public int TotalLevel => water.Sum(cell => cell.Level);
        public bool SharedWaterSearchReady => sharedSearch;
        public void ObserveSharedWaterSearch()
        {
            if (sharedSearch) return;
            // 跨区块水域由正常世界帧预算逐步合并，供电前持续读取直到两入口复用同一候选。
            var chunks = ChunkMgr.ExistingInstance;
            var first = chunks.SelectConnectedLiquidPumpSource((Vector2)(origin + new Vector2Int(4, 0)) + Vector2.one * .5f, out var firstSource);
            int groups = chunks.SharedLiquidPumpSearchGroupCount;
            long builds = chunks.SharedLiquidPumpTopologyBuildCount;
            var second = chunks.SelectConnectedLiquidPumpSource((Vector2)(origin + new Vector2Int(4, 3)) + Vector2.one * .5f, out var secondSource);
            sharedSearch = first == WorldLiquidPumpSourceSelection.Surplus && second == WorldLiquidPumpSourceSelection.Surplus &&
                firstSource.WorldCell == secondSource.WorldCell &&
                groups == chunks.SharedLiquidPumpSearchGroupCount && builds == chunks.SharedLiquidPumpTopologyBuildCount;
            searchDiagnostics = new JObject
            {
                ["first"] = first.ToString(), ["second"] = second.ToString(),
                ["firstSource"] = firstSource.WorldCell.ToString(), ["secondSource"] = secondSource.WorldCell.ToString(),
                ["groupsBeforeSecond"] = groups, ["groupsAfterSecond"] = chunks.SharedLiquidPumpSearchGroupCount,
                ["buildsBeforeSecond"] = builds, ["buildsAfterSecond"] = chunks.SharedLiquidPumpTopologyBuildCount,
                ["shared"] = sharedSearch
            };
        }
        public void ChargeBatteries()
        {
            foreach (var battery in batteries)
            {
                battery.ElectricalStoredJoules = battery.Definition.Electrical.CapacityJoules;
                MachineWorld.StateChanged(battery);
            }
        }
        public void ObserveElectricalTick()
        {
            for (int i = 0; i < pumps.Count; i++)
            {
                powered[i] |= pumps[i].ElectricalSuppliedWatts > 0f &&
                    ReferenceEquals(pumps[i].ElectricalNetwork, batteries[i].ElectricalNetwork);
                minimumEnergy[i] = Mathf.Min(minimumEnergy[i], batteries[i].ElectricalStoredJoules);
            }
        }
        public void DisconnectPower()
        {
            foreach (var battery in batteries)
            {
                battery.ElectricalStoredJoules = 0f; MachineWorld.StateChanged(battery);
            }
        }

        private int VisibleMachineBridgeCount()
        {
            var bridges = Resources.FindObjectsOfTypeAll<MechanicalDepthVisual>();
            int count = 0;
            foreach (var node in machines)
            {
                if (bridges.Any(bridge => bridge.gameObject.scene == player.gameObject.scene &&
                    bridge.HasSubmittedVisiblePartsForGameplayTestMachine(node))) count++;
            }
            return count;
        }
        private Vector3 ViewportPosition(Vector2 position)
        {
            Camera camera = cameraModule.ControllerCamera;
            return camera == null ? new Vector3(float.NaN, float.NaN, float.NaN) :
                camera.WorldToViewportPoint(WorldLocalPresentation.ProjectPosition(position));
        }
        private bool InViewport(Vector2 position)
        {
            Vector3 viewport = ViewportPosition(position);
            return viewport.z > 0 && viewport.x > .06f && viewport.x < .94f && viewport.y > .12f && viewport.y < .91f;
        }
        public JObject PresentationDiagnostics()
        {
            // 正式桥使用 DontSave，普通 FindObjectsByType 会跳过，必须枚举后再按真实归属筛选。
            var bridges = Resources.FindObjectsOfTypeAll<MechanicalDepthVisual>();
            var lower = ViewportPosition((Vector2)origin + new Vector2(-.5f, -1.5f));
            var upper = ViewportPosition((Vector2)origin + new Vector2(9f, 4.5f));
            return new JObject
            {
                ["machineCount"] = machines.Count, ["bridgeCount"] = bridges.Length,
                ["dontSaveBridgeCount"] = bridges.Count(bridge => (bridge.gameObject.hideFlags & HideFlags.DontSave) != 0),
                ["visibleMachineBridges"] = VisibleMachineBridgeCount(),
                ["sharedSearch"] = searchDiagnostics?.DeepClone(),
                ["lowerViewport"] = new JArray(lower.x, lower.y, lower.z),
                ["upperViewport"] = new JArray(upper.x, upper.y, upper.z),
                ["missingMachines"] = new JArray(machines.Where(node => !bridges.Any(bridge =>
                    bridge.gameObject.scene == player.gameObject.scene && bridge.HasSubmittedVisiblePartsForGameplayTestMachine(node)))
                    .Select(node => node.Id + ":" + node.Definition.Id + "@" + node.Cell))
            };
        }
        public bool PresentationReady => cameraTarget != null && machines.Count == 18 && VisibleMachineBridgeCount() == machines.Count &&
            InViewport((Vector2)origin + new Vector2(-.5f, -1.5f)) && InViewport((Vector2)origin + new Vector2(9f, 4.5f));
        public JObject Sample(int unpoweredLevel, bool after)
        {
            double pumpLiters = pumps.Sum(node => MachineWorld.GetFluidInventory(node).GetLiquidLiters());
            var tankAmounts = new JArray(tanks.Select(node => MachineWorld.GetFluidInventory(node).GetLiquidLiters()));
            return new JObject
            {
                ["after"] = after, ["initialLevel"] = 200, ["initialLiters"] = 50,
                ["unpoweredLevel"] = unpoweredLevel, ["remainingLevel"] = TotalLevel,
                ["waterCells"] = water.Count, ["wetCells"] = water.Count(cell => cell.Level > 0),
                ["pumpCount"] = pumps.Count, ["tankCount"] = tanks.Count,
                ["pipeCount"] = machines.Count(node => node.Definition.Id == "LiquidPipe"),
                ["wireCount"] = machines.Count(node => node.Definition.Id == "Wire_Copper"),
                ["machineCount"] = machines.Count, ["visibleMachineBridges"] = VisibleMachineBridgeCount(),
                ["presentation"] = PresentationDiagnostics(),
                ["inViewport"] = PresentationReady, ["sharedWaterSearch"] = sharedSearch,
                ["pumpLiters"] = pumpLiters, ["tankLiters"] = tankAmounts,
                ["poweredFirst"] = powered[0], ["poweredSecond"] = powered[1],
                ["firstEnergySpent"] = 60000f - minimumEnergy[0], ["secondEnergySpent"] = 60000f - minimumEnergy[1]
            };
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            var failures = new List<Exception>();
            using (MachineWorld.UseScope(worldKey))
                for (int i = machines.Count - 1; i >= 0; i--)
                    try { MachineWorld.DiscardPlacementCandidate(machines[i].Id); } catch (Exception error) { failures.Add(error); }
            foreach (var snapshot in terrain)
                try { snapshot.Restore(); } catch (Exception error) { failures.Add(error); }
            if (boundaryRegistered) WorldLiquidFlowObstacles.Unregister(this);
            if (cameraModule != null && cameraTarget != null)
            {
                cameraModule.ClearFollowOverride(cameraTarget.transform); cameraModule.SetOrthographicSize(originalCameraSize);
            }
            if (cameraTarget != null) UnityEngine.Object.Destroy(cameraTarget);
            if (player != null) player.transform.position = playerPosition;
            windShield?.Dispose();
            if (failures.Count > 0) throw new AggregateException("真实抽水现场恢复失败。", failures);
        }
    }
    #endregion
}

public sealed partial class MechanicalDepthVisual
{
    #region 真实抽水现场只读表现采样
    // 测试按权威交互身份核对已提交部件，避免把同格电线当成电泵或重复数同一视觉桥。
    internal bool HasSubmittedVisiblePartsForGameplayTestMachine(MachineEntity node)
    {
        if (disposed || owner == null || !owner.IsBatchPresentationRegistered || !isActiveAndEnabled ||
            !ReferenceEquals(interactionTarget, MachineWorld.GetOrCreateInteractionTarget(node))) return false;
        foreach (PartVisual part in parts)
            if (part != null && part.Visible && part.Sprite != null && part.SubmittedRenderer != null && !part.SubmittedRenderer.IsDisposed)
                return true;
        foreach (PartVisual part in facilityParts)
            if (part != null && part.Visible && part.Sprite != null && part.SubmittedRenderer != null && !part.SubmittedRenderer.IsDisposed)
                return true;
        return false;
    }
    #endregion
}
