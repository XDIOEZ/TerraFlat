using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayTestSystemProbe
{
    #region 建筑背风区规则采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterWindShelterRules() => Register("environment", "wind_shelter", WindShelter);

    private struct WindShelterProbeQuery : IWindShelterQuery
    {
        internal HashSet<Vector2Int> Walls;
        internal HashSet<Vector2Int> Unknown;
        internal WorldTopologyDomain Topology;
        internal int Calls;

        public bool TryGetWindBlocking(Vector2Int cell, out bool blocking)
        {
            Calls++;
            cell = new Vector2Int(Topology.NormalizeX(cell.x), Topology.NormalizeY(cell.y));
            blocking = Walls?.Contains(cell) == true;
            return Unknown?.Contains(cell) != true;
        }
    }

    // 夹具只提供离散占地，反风射线与遮挡距离完全调用正式规则。
    private static JObject WindShelter(JObject args)
    {
        var wall = new Vector2Int(0, 0);
        var query = new WindShelterProbeQuery { Walls = new HashSet<Vector2Int> { wall } };
        Vector2 east = new(1f, 0f);
        var result = new JObject
        {
            ["distance"] = WindShelterRules.ShelterDistance,
            ["behind"] = WindShelterRules.IsSheltered(new Vector2(3.5f, .5f), east, ref query),
            ["upwind"] = WindShelterRules.IsSheltered(new Vector2(-2.5f, .5f), east, ref query),
            ["side"] = WindShelterRules.IsSheltered(new Vector2(3.5f, 1.5f), east, ref query),
            ["oldSideAfterReverse"] = WindShelterRules.IsSheltered(new Vector2(3.5f, .5f), -east, ref query),
            ["newSideAfterReverse"] = WindShelterRules.IsSheltered(new Vector2(-2.5f, .5f), -east, ref query),
            ["insideDistance"] = WindShelterRules.IsSheltered(new Vector2(12.999f, .5f), east, ref query),
            ["outsideDistance"] = WindShelterRules.IsSheltered(new Vector2(13.001f, .5f), east, ref query),
            ["windMagnitudeIndependent"] = WindShelterRules.IsSheltered(new Vector2(3.5f, .5f), east * .06f, ref query)
        };
        query.Walls.Clear();
        query.Walls.Add(new Vector2Int(-4, -2));
        result["negativePosition"] = WindShelterRules.IsSheltered(new Vector2(-.5f, -1.5f), east, ref query);
        query.Walls.Clear();
        query.Walls.Add(new Vector2Int(2, 0));
        result["integerBoundary"] = WindShelterRules.IsSheltered(new Vector2(3f, .5f), east, ref query);
        query.Walls.Clear();
        query.Walls.Add(wall);
        Vector2 diagonal = new(1f, 1f);
        result["diagonal"] = WindShelterRules.IsSheltered(new Vector2(3.5f, 3.5f), diagonal, ref query);
        query.Walls.Clear();
        query.Walls.Add(new Vector2Int(2, 3));
        result["diagonalCorner"] = WindShelterRules.IsSheltered(new Vector2(3.5f, 3.5f), diagonal, ref query);
        query.Walls.Clear();
        query.Walls.Add(new Vector2Int(3, 1));
        result["diagonalSide"] = WindShelterRules.IsSheltered(new Vector2(3.5f, 3.5f), diagonal, ref query);
        query.Walls.Clear();
        query.Walls.Add(new Vector2Int(2, 3));
        query.Unknown = new HashSet<Vector2Int> { new(3, 2) };
        result["cornerKnownHorizontalWall"] = WindShelterRules.IsSheltered(new Vector2(3.5f, 3.5f), diagonal, ref query);
        query.Walls.Clear();
        query.Walls.Add(new Vector2Int(3, 2));
        query.Unknown.Clear();
        query.Unknown.Add(new Vector2Int(2, 3));
        result["cornerKnownVerticalWall"] = WindShelterRules.IsSheltered(new Vector2(3.5f, 3.5f), diagonal, ref query);

        query.Walls.Clear();
        query.Walls.Add(wall);
        query.Unknown = new HashSet<Vector2Int> { new(2, 0) };
        result["unknownBeforeWall"] = WindShelterRules.IsSheltered(new Vector2(4.5f, .5f), east, ref query);
        query.Walls.Add(new Vector2Int(3, 0));
        result["wallBeforeUnknown"] = WindShelterRules.IsSheltered(new Vector2(4.5f, .5f), east, ref query);
        query.Unknown = null;
        query.Calls = 0;
        result["calm"] = WindShelterRules.IsSheltered(new Vector2(4.5f, .5f), Vector2.zero, ref query);
        result["invalidWind"] = WindShelterRules.IsSheltered(new Vector2(4.5f, .5f), new Vector2(float.NaN, 0f), ref query);
        result["invalidPosition"] = WindShelterRules.IsSheltered(new Vector2(float.PositiveInfinity, .5f), east, ref query);
        result["invalidQueryCalls"] = query.Calls;

        query.Walls.Clear();
        query.Walls.Add(new Vector2Int(3, 0));
        query.Topology = new WorldTopologyDomain(new int2(-4, -4), new int2(8, 8), true);
        result["wrapped"] = WindShelterRules.IsSheltered(new Vector2(-2.5f, .5f), east, ref query);
        result["wrappedEquivalent"] = WindShelterRules.IsSheltered(new Vector2(5.5f, .5f), east, ref query);
        result["buildings"] = WindShelterBuildingSamples();
        result["terrain"] = WindShelterTerrainSamples();
        result["machines"] = WindShelterMachineSamples();
        return result;
    }

    private struct WindShelterRegistryProbeQuery : IWindShelterQuery
    {
        internal BuildingOccupancyRegistry.WindBlockingQuery Buildings;
        public bool TryGetWindBlocking(Vector2Int cell, out bool blocking)
        {
            blocking = Buildings.IsBlocked(cell);
            return true;
        }
    }

    private static Scene WindShelterProbeScene(string name)
    {
#if UNITY_EDITOR
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        scene.name = name;
        return scene;
#else
        return SceneManager.CreateScene(name);
#endif
    }

    private static void CloseWindShelterProbeScene(Scene scene)
    {
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
#else
        SceneManager.UnloadSceneAsync(scene);
#endif
    }

    private static Mod_Building WindShelterProbeBuilding(Scene scene, bool blocksWind)
    {
        var root = new GameObject("[WindShelterProbe]") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        Mod_Building building = root.AddComponent<Mod_Building>();
        building.BlocksWindWhenPlaced = blocksWind;
        var state = new Ex_ModData();
        state.WriteData(new Mod_Building.Building_Data
        {
            Version = Mod_Building.CurrentBuildingDataVersion,
            Role = BuildingRole.PlacedBuilding, State = BuildingState.Installed,
            BuildingPrefabId = "test:wind_shelter", SummonerPrefabId = "test:wind_shelter_summoner"
        });
        building.ApplyNetworkData(state);
        return building;
    }

    // 正式建筑状态与占地索引在独立场景中开关，不能改动用户当前世界。
    private static JObject WindShelterBuildingSamples()
    {
        string key = "wind_shelter_probe_" + Guid.NewGuid().ToString("N");
        Scene firstScene = WindShelterProbeScene(key);
        Scene secondScene = WindShelterProbeScene(key + "_other");
        var buildings = new List<Mod_Building>();
        try
        {
            Mod_Building first = WindShelterProbeBuilding(firstScene, true);
            buildings.Add(first);
            Vector2Int cell = Vector2Int.zero;
            var cells = new[] { cell, new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(1, 1) };
            BuildingOccupancyRegistry.Register(first, cells);
            var query = new WindShelterRegistryProbeQuery
            { Buildings = BuildingOccupancyRegistry.CaptureWindBlockingQuery(firstScene.name) };
            var other = BuildingOccupancyRegistry.CaptureWindBlockingQuery(secondScene.name);
            var result = new JObject { ["installed"] = first.IsInstalled(), ["otherWorldClear"] = !other.IsBlocked(cell) };
            var footprint = new JArray();
            foreach (Vector2Int occupied in cells) footprint.Add(query.Buildings.IsBlocked(occupied));
            result["footprint"] = footprint;
            Vector2 position = new(4.5f, 1.5f);
            Vector2 wind = new(1f, 0f);
            result["closed"] = WindShelterRules.IsSheltered(position, wind, ref query);
            BuildingOccupancyRegistry.SetPassable(first, true);
            result["open"] = WindShelterRules.IsSheltered(position, wind, ref query);
            BuildingOccupancyRegistry.SetPassable(first, false);
            result["closedAgain"] = WindShelterRules.IsSheltered(position, wind, ref query);
            BuildingOccupancyRegistry.Unregister(first);
            result["removed"] = WindShelterRules.IsSheltered(position, wind, ref query);
            BuildingOccupancyRegistry.Register(first, cells);
            result["replaced"] = WindShelterRules.IsSheltered(position, wind, ref query);

            Mod_Building stacked = WindShelterProbeBuilding(firstScene, true);
            buildings.Add(stacked);
            BuildingOccupancyRegistry.Register(stacked, cell);
            BuildingOccupancyRegistry.SetPassable(first, true);
            result["stackedStillBlocked"] = query.Buildings.IsBlocked(cell);
            BuildingOccupancyRegistry.Unregister(stacked);
            result["lastBlockerRemoved"] = query.Buildings.IsBlocked(cell);
            Mod_Building facility = WindShelterProbeBuilding(firstScene, false);
            buildings.Add(facility);
            BuildingOccupancyRegistry.Register(facility, cell);
            result["ordinaryFacility"] = query.Buildings.IsBlocked(cell);
            BuildingOccupancyRegistry.SetPassable(first, false);
            BuildingOccupancyRegistry.Register(facility, cell);
            result["facilityKeepsOtherBlocker"] = query.Buildings.IsBlocked(cell);
            BuildingOccupancyRegistry.SetPassable(first, true);

            Mod_Building second = WindShelterProbeBuilding(secondScene, true);
            buildings.Add(second);
            BuildingOccupancyRegistry.Register(second, cell);
            other = BuildingOccupancyRegistry.CaptureWindBlockingQuery(secondScene.name);
            result["secondWorldBlocked"] = other.IsBlocked(cell);
            result["firstWorldStillClear"] = !query.Buildings.IsBlocked(cell);
            BuildingOccupancyRegistry.Unregister(second);
            UnityEngine.Object.DestroyImmediate(second.gameObject);
            buildings.Remove(second);
            BuildingOccupancyRegistry.ReleaseWorld(secondScene.name);
            result["releasedWorldClear"] = !BuildingOccupancyRegistry.CaptureWindBlockingQuery(secondScene.name).IsBlocked(cell);
            return result;
        }
        finally
        {
            foreach (Mod_Building building in buildings)
            {
                BuildingOccupancyRegistry.Unregister(building);
                UnityEngine.Object.DestroyImmediate(building.gameObject);
            }
            BuildingOccupancyRegistry.ReleaseWorld(firstScene.name);
            BuildingOccupancyRegistry.ReleaseWorld(secondScene.name);
            CloseWindShelterProbeScene(secondScene);
            CloseWindShelterProbeScene(firstScene);
        }
    }

    private struct WindShelterTerrainProbeQuery : IWindShelterQuery
    {
        internal ChunkMgr.RuntimeTerrainPresenceQuery Terrain;
        internal WorldTopologyDomain Topology;
        public bool TryGetWindBlocking(Vector2Int cell, out bool blocking)
            => Terrain.TryGetTerrainWindBlockingNormalized(
                new Vector2Int(Topology.NormalizeX(cell.x), Topology.NormalizeY(cell.y)), out blocking);
    }

    private static JObject WindShelterTerrainSamples()
    {
        const string dimension = "wind_shelter_terrain";
        using var world = new WorldRuntime(dimension, 1);
        ChunkRuntime chunk = SightProbeChunk(world, Vector2Int.zero, dimension);
        var query = new WindShelterTerrainProbeQuery
        { Terrain = new ChunkMgr.RuntimeTerrainPresenceQuery(world, default, dimension, 4, 4) };
        Vector2 position = new(3.5f, .5f);
        Vector2 wind = new(1f, 0f);
        var result = new JObject();
        result["floorReady"] = query.TryGetWindBlocking(new Vector2Int(1, 0), out bool blocked);
        result["floorBlocked"] = blocked;
        chunk.Terrain.SetDynamicSightBlockingCell(1, 0, true);
        result["dynamicOnly"] = WindShelterRules.IsSheltered(position, wind, ref query);
        chunk.Terrain.SetCell(1, 0, SightProbeCell(true));
        result["wall"] = WindShelterRules.IsSheltered(position, wind, ref query);
        chunk.Terrain.SetCell(1, 0, new TerrainCell(1, 0, 1, 1, 1, TerrainCellFlags.Walkable));
        result["nonSolidTile"] = WindShelterRules.IsSheltered(position, wind, ref query);
        chunk.Terrain.SetCell(1, 0, SightProbeCell(false));
        result["removed"] = WindShelterRules.IsSheltered(position, wind, ref query);
        int count = world.Chunks.Count;
        result["missingReady"] = query.TryGetWindBlocking(new Vector2Int(8, 0), out blocked);
        result["missingBlocked"] = blocked;
        result["missingCreatedChunk"] = world.Chunks.Count != count;
        chunk.Terrain.SetCell(1, 0, SightProbeCell(true));
        chunk = SightProbeChunk(world, Vector2Int.zero, dimension);
        result["replacement"] = WindShelterRules.IsSheltered(position, wind, ref query);
        ChunkRuntime negative = SightProbeChunk(world, new Vector2Int(-4, -4), dimension);
        negative.Terrain.SetCell(0, 1, SightProbeCell(true));
        result["negativeWall"] = WindShelterRules.IsSheltered(new Vector2(-.5f, -2.5f), wind, ref query);
        SightProbeChunk(world, new Vector2Int(-4, 0), dimension);
        chunk.Terrain.SetCell(3, 0, SightProbeCell(true));
        query.Topology = new WorldTopologyDomain(new int2(-4, -4), new int2(8, 8), true);
        query.Terrain = new ChunkMgr.RuntimeTerrainPresenceQuery(world, query.Topology, dimension, 4, 4);
        result["wrappedWall"] = WindShelterRules.IsSheltered(new Vector2(-2.5f, .5f), wind, ref query);
        world.BeginNewEpoch(2);
        result["oldEpochReady"] = query.TryGetWindBlocking(new Vector2Int(3, 0), out blocked);
        return result;
    }

    private static JArray WindShelterMachineSamples()
    {
        var root = new GameObject("[WindShelterMachineProbe]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            Mod_Building building = root.AddComponent<Mod_Building>();
            var graph = new MechanicalNetworkGraph(new Vector2Int(16, 16), Vector2Int.zero, default(WorldTopologyDomain));
            var cell = new Vector2Int(3, 4);
            var samples = new JArray();
            foreach (int layer in new[] { 0, (int)BuildingPlacementLayer.Ground, Mod_ShipPart.GroundFloorLayer, 15 })
            foreach (bool movement in new[] { false, true })
            foreach (bool declared in new[] { false, true })
            {
                var parameters = new JObject
                {
                    [nameof(Mod_Building.PlacementLayer)] = layer,
                    [nameof(Mod_Building.BlocksMovementWhenPlaced)] = movement,
                    [nameof(Mod_Building.BlocksWindWhenPlaced)] = declared
                };
                var configuration = new MachineModuleConfiguration(new RuntimeItemModuleDefinition(
                    "building", ModText.Building, "test:wind_building", parameters.ToString(), true), building);
                var definition = new MachineDefinition { Id = "test:wind_machine", Ports = "none" };
                BuildingTraversalRules.ApplyToMachine(definition, configuration);
                graph.Rebuild(new[] { new MachineEntity { Id = 1, Cell = cell, Definition = definition } });
                samples.Add(new JObject
                {
                    ["layer"] = layer, ["movement"] = movement, ["declared"] = declared,
                    ["compiledLayer"] = definition.Layer, ["compiledDeclaration"] = definition.BlocksWindWhenPlaced,
                    ["compiledBlocking"] = definition.BlocksWind, ["indexedBlocking"] = graph.IsWindBlockedNormalized(cell)
                });
            }
            return samples;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    #endregion
}
