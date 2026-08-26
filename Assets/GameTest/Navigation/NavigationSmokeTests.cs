using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FlatWorld.WorldModel;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Navigation
{
    public sealed class NavigationSmokeTests
    {
        private const uint GroundPenalty = 1000u;


        [Test]
        [Category("Navigation.Smoke")]
        [Category("Smoke")]
        public void TopTileAndBuildingOccupancyJointlyControlWalkability()
        {
            WorldNavigationManager manager = CreateManagerForTest();
            GameObject buildingObject = new("NavigationOccupancyBuilding");
            Mod_Building building = null;
            using var world = new WorldRuntime("navigation-smoke", 1);
            try
            {
                Vector2Int cell = new(40, -12);
                var address = new FlatWorld.WorldModel.WorldAddress(
                    "surface", new Int2(cell.x, cell.y));
                var profile = new ChunkGenerationProfileSnapshot("navigation-smoke", 5, 1, 1);
                ChunkGenerationRequest request = world.BeginChunkGeneration(address, 17, profile);
                var terrain = new ChunkTerrainBuffer(1, 1);
                terrain.SetCell(0, 0, new TerrainCell(
                    1, 0, 0, 0, (short)GroundPenalty, TerrainCellFlags.Walkable));
                using var result = new ChunkGenerationResult(request, terrain);
                Assert.That(world.TryCommit(result, out string reason), Is.True, reason);
                ChunkRuntime chunk = world.Chunks[address];

                manager.RegisterChunkRuntime(chunk);
                Assert.That(manager.Grid.TryGetCell(cell, out WorldNavigationCell floorCell), Is.True);
                Assert.That(floorCell.Walkable, Is.True);

                building = buildingObject.AddComponent<Mod_Building>();
                building.Data.Role = BuildingRole.PlacedBuilding;
                building.Data.State = BuildingState.Installed;
                typeof(Mod_Building)
                    .GetField("_currentState", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(building, BuildingState.Installed);
                Assert.That(building.IsInstalled(), Is.True, "测试建筑必须处于有效占地状态。");
                BuildingOccupancyRegistry.Register(building, new[] { cell });
                manager.RegisterChunkRuntime(chunk);
                Assert.That(manager.Grid.TryGetCell(cell, out WorldNavigationCell occupied), Is.True);
                Assert.That(occupied.Walkable, Is.False);

                BuildingOccupancyRegistry.Unregister(building);
                manager.RegisterChunkRuntime(chunk);
                Assert.That(manager.Grid.TryGetCell(cell, out WorldNavigationCell unoccupied), Is.True);
                Assert.That(unoccupied.Walkable, Is.True);
            }
            finally
            {
                if (building != null)
                    BuildingOccupancyRegistry.Unregister(building);
                Object.DestroyImmediate(buildingObject);
                Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        [Category("Navigation.Debug")]
        public void RuntimePathOverlayCanBeToggledWithoutEditorGizmos()
        {
            try
            {
                WorldNavigationPathDebugOverlay.SetRoutesVisible(false);
                Assert.That(WorldNavigationPathDebugOverlay.RoutesVisible, Is.False);

                Assert.That(WorldNavigationPathDebugOverlay.ToggleRoutesVisible(), Is.True);
                Assert.That(WorldNavigationPathDebugOverlay.RoutesVisible, Is.True);

                Assert.That(WorldNavigationPathDebugOverlay.ToggleRoutesVisible(), Is.False);
                Assert.That(WorldNavigationPathDebugOverlay.RoutesVisible, Is.False);
            }
            finally
            {
                WorldNavigationPathDebugOverlay.SetRoutesVisible(false);
            }
        }

        [Test]
        [Category("Navigation.Debug")]
        public void RuntimePathOverlayBuildsOneMeshForActiveAgentRoutes()
        {
            WorldNavigationManager manager = CreateManagerForTest();
            GameObject actor = new("NavigationDebugRoute_Test");
            WorldNavigationPathDebugOverlay overlay = null;
            try
            {
                FillWalkableRect(manager.Grid, new RectInt(0, 0, 12, 4));

                Rigidbody2D body = actor.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                body.position = new Vector2(1.5f, 1.5f);

                WorldNavigationAgent agent = actor.AddComponent<WorldNavigationAgent>();
                agent.Bind(body, manager);
                agent.SetDestination(new Vector2(9.5f, 1.5f), forceRepath: true);
                agent.Tick(0.02f);
                Assert.That(manager.ProcessPathRequests(1), Is.EqualTo(1));
                DispatchCompletions(manager, 1);
                Assert.That(agent.HasPath, Is.True);

                WorldNavigationPathDebugOverlay.SetRoutesVisible(true);
                overlay = WorldNavigationPathDebugOverlay.ActiveInstance;
                Assert.That(overlay, Is.Not.Null);

                MethodInfo rebuild = typeof(WorldNavigationPathDebugOverlay).GetMethod(
                    "RebuildRouteMesh",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(rebuild, Is.Not.Null);
                rebuild.Invoke(overlay, null);

                MeshFilter filter = overlay.GetComponent<MeshFilter>();
                Assert.That(overlay.DrawnAgentCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(filter, Is.Not.Null);
                Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0));
            }
            finally
            {
                WorldNavigationPathDebugOverlay.SetRoutesVisible(false);
                if (overlay != null)
                    Object.DestroyImmediate(overlay.gameObject);
                Object.DestroyImmediate(actor);
                Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        [Category("Navigation.Grid")]
        public void SparseGridRoutesAcrossLargeWorldCoordinatesAndWallGap()
        {
            WorldNavigationGrid grid = new();
            Vector2Int origin = new(1_000_000, -1_000_000);
            FillWalkableRect(grid, new RectInt(origin.x, origin.y, 81, 21));

            int wallX = origin.x + 40;
            int gapY = origin.y + 13;
            for (int y = origin.y; y < origin.y + 21; y++)
                grid.SetCell(
                    new Vector2Int(wallX, y),
                    y == gapY ? GroundPenalty : 0u,
                    y == gapY);

            Vector2Int start = new(origin.x + 2, origin.y + 3);
            Vector2Int goal = new(origin.x + 78, origin.y + 4);
            List<Vector2Int> path = new();

            Assert.That(grid.TryFindPath(start, goal, path), Is.True);
            Assert.That(path[0], Is.EqualTo(start));
            Assert.That(path[^1], Is.EqualTo(goal));
            Assert.That(path.Exists(cell => cell.y >= gapY), Is.True);
            AssertPathSegmentsWalkable(grid, path);
        }

        [Test]
        [Category("Navigation.Grid")]
        public void DiagonalMovementCannotCutBlockedCorner()
        {
            WorldNavigationGrid grid = new();
            FillWalkableRect(grid, new RectInt(0, 0, 2, 2));
            grid.SetCell(new Vector2Int(1, 0), 0u, false);
            grid.SetCell(new Vector2Int(0, 1), 0u, false);

            List<Vector2Int> path = new();
            Assert.That(grid.TryFindPath(Vector2Int.zero, Vector2Int.one, path), Is.False);
            Assert.That(grid.HasLineOfSight(Vector2Int.zero, Vector2Int.one), Is.False);
        }

        [Test]
        [Category("Navigation.Grid")]
        public void PathAvoidsExpensiveTerrainWhenAReasonableDetourExists()
        {
            WorldNavigationGrid grid = new();
            FillWalkableRect(grid, new RectInt(0, 0, 9, 5));
            for (int x = 2; x <= 6; x++)
                grid.SetCell(new Vector2Int(x, 2), 10000u, true);

            List<Vector2Int> path = new();
            Assert.That(grid.TryFindPath(new Vector2Int(0, 2), new Vector2Int(8, 2), path), Is.True);
            Assert.That(path.Exists(cell => cell.y != 2), Is.True);
            AssertPathSegmentsWalkable(grid, path);
        }

        [Test]
        [Category("Navigation.Grid")]
        public void PathCrossesExpensiveTerrainWhenItIsTheOnlyRoute()
        {
            WorldNavigationGrid grid = new();
            for (int x = 0; x < 7; x++)
                grid.SetCell(new Vector2Int(x, 0), GroundPenalty, true);
            grid.SetCell(new Vector2Int(3, 0), 5000u, true);

            List<Vector2Int> path = new();
            Assert.That(grid.TryFindPath(Vector2Int.zero, new Vector2Int(6, 0), path), Is.True);
            Assert.That(path, Does.Contain(new Vector2Int(3, 0)));
            AssertPathSegmentsWalkable(grid, path);
        }

        [Test]
        [Category("Navigation.Obstacle")]
        public void DynamicObstacleBlocksAndRestoresCellsWithoutChangingTerrainData()
        {
            WorldNavigationGrid grid = new();
            FillWalkableRect(grid, new RectInt(-4, -4, 9, 9));
            Vector2Int blocked = new(0, 0);

            grid.RegisterBlocker(17, new[] { blocked });
            Assert.That(grid.TryGetCell(blocked, out WorldNavigationCell covered), Is.True);
            Assert.That(covered.Walkable, Is.False);

            List<Vector2Int> detour = new();
            Assert.That(grid.TryFindPath(new Vector2Int(-2, 0), new Vector2Int(2, 0), detour), Is.True);
            Assert.That(detour.Contains(blocked), Is.False);

            grid.UnregisterBlocker(17);
            Assert.That(grid.TryGetCell(blocked, out WorldNavigationCell restored), Is.True);
            Assert.That(restored.Walkable, Is.True);
        }

        [Test]
        [Category("Navigation.Streaming")]
        public void LoadedChunkCellsPublishOneBatchedGridRevision()
        {
            WorldNavigationGrid grid = new();

            grid.BeginBatchUpdate();
            try
            {
                FillWalkableRect(grid, new RectInt(0, 0, 16, 16));
            }
            finally
            {
                grid.EndBatchUpdate();
            }

            Assert.That(grid.CellCount, Is.EqualTo(256));
            Assert.That(grid.Revision, Is.EqualTo(1),
                "One loaded chunk must not publish one route revision per tile.");
        }

        [Test]
        [Category("Navigation.Streaming")]
        public void LoadingCellsDoesNotInvalidateRoutesButUnloadingWalkableCellsDoes()
        {
            WorldNavigationGrid grid = new();
            grid.BeginBatchUpdate();
            try
            {
                FillWalkableRect(grid, new RectInt(0, 0, 16, 16));
            }
            finally
            {
                grid.EndBatchUpdate();
            }

            int loadedRevision = grid.Revision;
            int invalidationRevision = grid.PathInvalidationRevision;

            grid.BeginBatchUpdate();
            try
            {
                FillWalkableRect(grid, new RectInt(16, 0, 16, 16));
            }
            finally
            {
                grid.EndBatchUpdate();
            }

            Assert.That(grid.Revision, Is.EqualTo(loadedRevision + 1));
            Assert.That(grid.PathInvalidationRevision, Is.EqualTo(invalidationRevision),
                "Opening a streamed chunk cannot break a route already being followed.");

            grid.BeginBatchUpdate();
            try
            {
                grid.RemoveRegion(new RectInt(0, 0, 16, 16));
            }
            finally
            {
                grid.EndBatchUpdate();
            }

            Assert.That(grid.PathInvalidationRevision, Is.EqualTo(invalidationRevision + 1),
                "Unloading walkable cells must notify agents whose route may cross that chunk.");
        }

        [Test]
        [Category("Navigation.Streaming")]
        public void LargeStreamingUpdateUsesBoundedChangeJournal()
        {
            WorldNavigationGrid grid = new();
            grid.BeginBatchUpdate();
            try
            {
                FillWalkableRect(grid, new RectInt(0, 0, 65, 33));
            }
            finally
            {
                grid.EndBatchUpdate();
            }

            List<Vector2Int> changes = new();
            grid.ConsumeChanges(changes, out bool fullReset);

            Assert.That(fullReset, Is.True,
                "A large streaming wave should become one cache reset instead of an unbounded tile journal.");
            Assert.That(changes, Is.Empty);
            Assert.That(grid.Revision, Is.EqualTo(1));
            Assert.That(grid.PathInvalidationRevision, Is.Zero);
        }

        [Test]
        [Category("Navigation.Streaming")]
        public void ManagerPublishesOneRevisionForBulkRegionUpdate()
        {
            WorldNavigationManager manager = CreateManagerForTest();
            try
            {
                manager.SetNavigationRegion(
                    null,
                    new RectInt(0, 0, 16, 16),
                    GroundPenalty,
                    true);

                Assert.That(manager.RegisteredCellCount, Is.EqualTo(256));
                Assert.That(manager.GridRevision, Is.EqualTo(1));
                Assert.That(manager.PathInvalidationRevision, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        [Category("Navigation.Agent")]
        public void ChangingDestinationKeepsCurrentPathMovingUntilReplacementArrives()
        {
            WorldNavigationManager manager = CreateManagerForTest();
            GameObject actor = new("NavigationAgent_Test");
            try
            {
                FillWalkableRect(manager.Grid, new RectInt(0, 0, 16, 4));

                Rigidbody2D body = actor.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                body.position = new Vector2(1.5f, 1.5f);

                WorldNavigationAgent agent = actor.AddComponent<WorldNavigationAgent>();
                agent.Bind(body, manager);
                agent.Configure(0.1f, 0.5f, 10f, 0.01f);
                agent.MaxSpeed = 2f;
                agent.SetDestination(new Vector2(8.5f, 1.5f), forceRepath: true);
                agent.Tick(0.02f);

                Assert.That(agent.PathPending, Is.True);
                Assert.That(manager.ProcessPathRequests(1), Is.EqualTo(1));
                DispatchCompletions(manager, 1);
                Assert.That(agent.HasPath, Is.True);

                agent.Tick(0.02f);
                Assert.That(agent.Velocity.sqrMagnitude, Is.GreaterThan(0f));

                agent.SetDestination(new Vector2(10.5f, 1.5f));

                Assert.That(agent.HasPath, Is.True,
                    "A changed target should queue a replacement without discarding the active route.");
                agent.Tick(0.02f);
                Assert.That(agent.Velocity.sqrMagnitude, Is.GreaterThan(0f),
                    "The actor must keep moving while its replacement route is pending.");
            }
            finally
            {
                Object.DestroyImmediate(actor);
                Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        [Category("Navigation.Agent")]
        public void WanderTargetOffsetRespectsMinimumDistance()
        {
            Vector2 offset = AI_WanderUtility.PickSaferOffset(
                Vector3.zero,
                new Vector2(0.1f, 0f),
                4f,
                enableAvoidDanger: false,
                minimumDistance: 1.5f);

            Assert.That(offset.magnitude, Is.GreaterThanOrEqualTo(1.499f));
            Assert.That(offset.magnitude, Is.LessThanOrEqualTo(4.001f));
        }

        [Test]
        [Category("Navigation.Scheduler")]
        public void CancelledRequestsDoNotKeepSearchesAliveOrBreakCacheLimit()
        {
            WorldNavigationManager manager = CreateManagerForTest();
            try
            {
                FillWalkableRect(manager.Grid, new RectInt(-32, -32, 64, 64));
                for (int y = -32; y < 32; y++)
                    manager.Grid.SetCell(new Vector2Int(0, y), 0u, false);

                Vector2 start = new(-20.5f, 0.5f);
                for (int i = 0; i < 1000; i++)
                {
                    int x = 1 + i % 30;
                    int y = -30 + (i / 30) % 60;
                    int request = manager.RequestPath(start, new Vector2(x + 0.5f, y + 0.5f), null);
                    manager.CancelPath(request);
                }

                Assert.That(manager.PendingPathCount, Is.Zero);
                Assert.That(manager.QueuedAdmissionCount, Is.Zero);
                Assert.That(manager.CachedGoalCount, Is.LessThanOrEqualTo(64));
                Assert.That(manager.ProcessPathRequests(10000), Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        [Category("Navigation.Scheduler")]
        public void CancelledCompletedRequestReleasesBufferedPathImmediately()
        {
            WorldNavigationManager manager = CreateManagerForTest();
            try
            {
                FillWalkableRect(manager.Grid, new RectInt(-4, -4, 9, 9));
                int request = manager.RequestPath(new Vector2(-2.5f, 0.5f), new Vector2(2.5f, 0.5f), null);

                Assert.That(manager.ProcessPathRequests(1), Is.EqualTo(1));
                Assert.That(manager.BufferedCompletionCount, Is.EqualTo(1));

                manager.CancelPath(request);
                Assert.That(manager.BufferedCompletionCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        [Category("Navigation.Scheduler")]
        public void ManyAgentsWithOneGoalShareOneReverseSearch()
        {
            WorldNavigationManager manager = CreateManagerForTest();
            List<int> requests = new();
            try
            {
                FillWalkableRect(manager.Grid, new RectInt(-32, -32, 64, 64));
                for (int y = -32; y < 32; y++)
                    manager.Grid.SetCell(new Vector2Int(0, y), 0u, false);

                for (int i = 0; i < 500; i++)
                {
                    Vector2 start = new(-20.5f, -25.5f + i % 50);
                    requests.Add(manager.RequestPath(start, new Vector2(20.5f, 0.5f), null));
                }

                Assert.That(manager.PendingPathCount, Is.EqualTo(500));
                Assert.That(manager.QueuedAdmissionCount, Is.EqualTo(500));
                Assert.That(manager.ProcessPathRequests(1), Is.EqualTo(1));
                Assert.That(manager.CachedGoalCount, Is.EqualTo(1));

                for (int i = 0; i < requests.Count; i++)
                    manager.CancelPath(requests[i]);
                Assert.That(manager.ProcessPathRequests(10000), Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        [Category("Navigation.Scheduler")]
        public void LongLoadedCorridorDoesNotFailAtLegacySearchLimit()
        {
            WorldNavigationManager manager = CreateManagerForTest();
            try
            {
                const int corridorLength = 70_000;
                for (int x = 0; x < corridorLength; x++)
                    manager.Grid.SetCell(new Vector2Int(x, 0), GroundPenalty, true);

                bool callbackInvoked = false;
                WorldNavigationPathResult result = default;
                manager.RequestPath(
                    new Vector2(0.5f, 0.5f),
                    new Vector2(corridorLength - 0.5f, 0.5f),
                    value =>
                    {
                        callbackInvoked = true;
                        result = value;
                    });

                Assert.That(manager.ProcessPathRequests(corridorLength + 16), Is.GreaterThan(65_536));
                Assert.That(manager.BufferedCompletionCount, Is.EqualTo(1));

                MethodInfo dispatch = typeof(WorldNavigationManager).GetMethod(
                    "DispatchCompletions",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(dispatch, Is.Not.Null);
                dispatch.Invoke(manager, new object[] { 1, double.PositiveInfinity });

                Assert.That(callbackInvoked, Is.True);
                Assert.That(result.Success, Is.True);
                Assert.That(result.ReachesDestination, Is.False);
                Assert.That(result.Waypoints, Is.Not.Empty);
                Assert.That(result.Waypoints.Length, Is.LessThanOrEqualTo(64));
                Assert.That(result.Waypoints[^1].x, Is.LessThan(result.ResolvedDestination.x));
            }
            finally
            {
                Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        [Category("Navigation.Migration")]
        public void RuntimePrefabsUseOnlyTheNewNavigationComponents()
        {
            GameObject worldManager = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Core/Managers/WorldManager.prefab");
            Assert.That(worldManager, Is.Not.Null);
            Assert.That(worldManager.GetComponentsInChildren<WorldNavigationManager>(true), Has.Length.EqualTo(1));

            string[] actors =
            {
                "Chicken", "Chicken_Tree", "WildBoar", "WildBoar_Tree", "Wolf", "Ghost"
            };
            for (int i = 0; i < actors.Length; i++)
            {
                GameObject actor = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/2_Prefabs/Gameplay/AI/{actors[i]}.prefab");
                Assert.That(actor, Is.Not.Null, actors[i]);
                Assert.That(actor.GetComponent<WorldNavigationAgent>(), Is.Not.Null, actors[i]);
            }

            GameObject mineShell = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Gameplay/Items/Common/MineResource.prefab");
            Assert.That(mineShell, Is.Not.Null);
            Assert.That(mineShell.GetComponent<WorldNavigationObstacle>(), Is.Not.Null,
                "通用矿物外壳必须登记动态导航障碍。");

            Dictionary<string, ItemDefinitionDto> itemDefinitions = ItemDefinitionCatalogLoader
                .LoadBuiltInDefinitions()
                .ToDictionary(definition => definition.Id, System.StringComparer.OrdinalIgnoreCase);
            string[] mines = { "Mine_Coal", "Mine_Copper", "Mine_Iron", "Mine_Stone", "Mine_Tin" };
            for (int i = 0; i < mines.Length; i++)
            {
                Assert.That(itemDefinitions[mines[i]].ShellPrefab, Is.EqualTo("MineResource"), mines[i]);
            }
        }

        [Test]
        [Category("Navigation.Migration")]
        public void GameplayRuntimeHasNoAstarPluginDependency()
        {
            string assemblyDefinition = File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/GamePlay.asmdef");
            Assert.That(assemblyDefinition, Does.Not.Contain("efa45043feb7e4147a305b73b5cea642"));

            string[] runtimeRoots =
            {
                "Assets/5_Scripts/5-3_GamePlay",
                "Assets/TheKiwiCoder/BehaviourTree/Scripts"
            };
            for (int rootIndex = 0; rootIndex < runtimeRoots.Length; rootIndex++)
            {
                string[] files = Directory.GetFiles(runtimeRoots[rootIndex], "*.cs", SearchOption.AllDirectories);
                for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
                {
                    string source = File.ReadAllText(files[fileIndex]);
                    Assert.That(source, Does.Not.Contain("using Pathfinding"), files[fileIndex]);
                    Assert.That(source, Does.Not.Contain("IAstarAI"), files[fileIndex]);
                }
            }

            string worldManagerYaml = File.ReadAllText(
                "Assets/2_Prefabs/Core/Managers/WorldManager.prefab");
            Assert.That(worldManagerYaml, Does.Not.Contain("78396926cbbfc4ac3b48fc5fc34a87d1"));
        }

        private static WorldNavigationManager CreateManagerForTest()
        {
            GameObject gameObject = new("WorldNavigationManager_Test");
            WorldNavigationManager manager = gameObject.AddComponent<WorldNavigationManager>();
            FieldInfo initField = typeof(WorldNavigationManager).GetField(
                "<Init>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(initField, Is.Not.Null);
            initField.SetValue(manager, true);
            return manager;
        }

        private static void DispatchCompletions(WorldNavigationManager manager, int maxCount)
        {
            MethodInfo dispatch = typeof(WorldNavigationManager).GetMethod(
                "DispatchCompletions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(dispatch, Is.Not.Null);
            dispatch.Invoke(manager, new object[] { maxCount, double.PositiveInfinity });
        }

        private static void FillWalkableRect(WorldNavigationGrid grid, RectInt rect)
        {
            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                for (int x = rect.xMin; x < rect.xMax; x++)
                    grid.SetCell(new Vector2Int(x, y), GroundPenalty, true);
            }
        }

        private static void AssertPathSegmentsWalkable(
            WorldNavigationGrid grid,
            IReadOnlyList<Vector2Int> path)
        {
            for (int i = 1; i < path.Count; i++)
                Assert.That(grid.HasLineOfSight(path[i - 1], path[i]), Is.True, $"segment {i - 1}->{i}");
        }
    }
}
