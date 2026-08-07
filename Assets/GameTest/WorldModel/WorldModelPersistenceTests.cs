using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace FlatWorld.GameTest.WorldModel
{
    [Category("WorldModel.Persistence")]
    public sealed class WorldModelPersistenceTests
    {
        private static readonly ChunkGenerationProfileSnapshot Profile =
            new("persistence", 5, 8, 8, new Dictionary<string, double>
            {
                ["terrain.groundTileId"] = 1,
                ["terrain.waterTileId"] = 2,
                ["terrain.waterThreshold"] = 0.5,
                ["terrain.biomeCount"] = 4,
                ["grass.density"] = 1
            });

        [Test]
        public void SnapshotDuringGenerationExcludesHalfGeneratedChunk()
        {
            using var world = new WorldRuntime("save", 1);
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
            world.BeginChunkGeneration(address, 10, Profile);
            Assert.That(world.CaptureSnapshot().Chunks.Count, Is.Zero);
        }

        [Test]
        public void ChunkViewCanBindUnbindAndRebindWithoutDuplicateLeases()
        {
            using var world = CreateCommittedWorld(10);
            ChunkRuntime chunk = null;
            foreach (ChunkRuntime value in world.Chunks.Values)
                chunk = value;
            Assert.That(chunk, Is.Not.Null);

            GameObject viewObject = new("ChunkView_BindingTest");
            try
            {
                ChunkView view = viewObject.AddComponent<ChunkView>();
                for (int i = 0; i < 3; i++)
                {
                    view.Bind(world, chunk);
                    view.Bind(world, chunk);
                    Assert.That(chunk.PresentationLeaseCount, Is.EqualTo(1));
                    Assert.That(chunk.NavigationLeaseCount, Is.EqualTo(1));
                    Assert.That(chunk.PresentationStatus, Is.EqualTo(ChunkPresentationStatus.Bound));
                    view.Unbind();
                    Assert.That(chunk.PresentationLeaseCount, Is.Zero);
                    Assert.That(chunk.NavigationLeaseCount, Is.Zero);
                    Assert.That(chunk.PresentationStatus, Is.EqualTo(ChunkPresentationStatus.Unbound));
                }
            }
            finally
            {
                Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void DimensionCatalogMapsProfilesAndPureChunkViews()
        {
            DimensionCatalogSO catalog = AssetDatabase.LoadAssetAtPath<DimensionCatalogSO>(
                "Assets/Resources/Config/DimensionCatalog_Default.asset");
            Assert.That(catalog, Is.Not.Null);
            foreach (string dimensionId in new[] { "surface", "cave" })
            {
                DimensionDefinition definition = catalog.Find(dimensionId);
                Assert.That(definition, Is.Not.Null, dimensionId);
                Assert.That(definition.GenerationProfile, Is.Not.Null, dimensionId);
                Assert.That(definition.GenerationProfile.GenerationSignature, Is.EqualTo(6));
                Assert.That(definition.ChunkViewPrefab, Is.Not.Null, dimensionId);
                Assert.That(definition.ChunkViewPrefab.GetComponent<global::Map>(), Is.Null,
                    "ChunkView prefab must not carry the legacy Map authority.");
            }
        }

        [Test]
        public void ChunkViewPrefabRepeatedBindingRendersAndClearsEveryModelLayer()
        {
            ChunkView prefab = AssetDatabase.LoadAssetAtPath<ChunkView>(
                "Assets/2_Prefabs/WorldModel/ChunkView.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<ChunkTilemapRenderer>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<ChunkEnvironmentTilemapRenderer>(), Is.Not.Null);
            ChunkGrassRenderer grassRenderer = prefab.GetComponent<ChunkGrassRenderer>();
            Assert.That(grassRenderer, Is.Not.Null);
            Assert.That(grassRenderer.IsConfigured, Is.True,
                "Chunk grass must use the dedicated detail atlas, not the ground tile.");
            Assert.That(prefab.GetComponent<ChunkCollisionRenderer>(), Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Tilemap>(true).Length, Is.EqualTo(4));

            using var world = CreateCommittedWorld(44);
            ChunkRuntime chunk = null;
            foreach (ChunkRuntime value in world.Chunks.Values)
                chunk = value;
            ChunkView view = Object.Instantiate(prefab);
            try
            {
                ulong hash = chunk.Terrain.ComputeStableHash();
                for (int i = 0; i < 3; i++)
                {
                    view.Bind(world, chunk, includeNavigation: false);
                    view.Bind(world, chunk, includeNavigation: false);
                    Tilemap ground = view.transform.Find("Ground").GetComponent<Tilemap>();
                    Tilemap grass = view.transform.Find("Grass").GetComponent<Tilemap>();
                    Assert.That(ground.GetUsedTilesCount(), Is.GreaterThan(0));
                    Assert.That(grass.GetUsedTilesCount(), Is.GreaterThan(1),
                        "Visible grass must render multiple atlas variants.");
                    Assert.That(chunk.PresentationLeaseCount, Is.EqualTo(1));
                    Assert.That(chunk.NavigationLeaseCount, Is.Zero);
                    Assert.That(chunk.Terrain.ComputeStableHash(), Is.EqualTo(hash));
                    view.Unbind();
                    foreach (Tilemap tilemap in view.GetComponentsInChildren<Tilemap>(true))
                        Assert.That(tilemap.GetUsedTilesCount(), Is.Zero);
                    Assert.That(view.GetComponentInChildren<TilemapCollider2D>(true).enabled, Is.False);
                    Assert.That(chunk.PresentationLeaseCount, Is.Zero);
                }
            }
            finally
            {
                Object.DestroyImmediate(view.gameObject);
            }
        }

        [Test]
        public void DefaultSurfaceProfileProducesRiversAndGrassAcrossLocalWindow()
        {
            ChunkGenerationProfileSO profileAsset =
                AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(
                    "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset");
            Assert.That(profileAsset, Is.Not.Null);
            ChunkGenerationProfileSnapshot profile = profileAsset.CreateSnapshot();
            Assert.That(profile.Settings.RiverWidth, Is.EqualTo(0.085d).Within(0.000001d));
            Assert.That(profile.Settings.RiverScale, Is.EqualTo(0.006d).Within(0.000001d));

            using var world = new WorldRuntime("surface-coverage", 1);
            var generator = new DeterministicChunkGenerator();
            const int chunkRadius = 4;
            for (int chunkY = -chunkRadius; chunkY <= chunkRadius; chunkY++)
            for (int chunkX = -chunkRadius; chunkX <= chunkRadius; chunkX++)
            {
                var address = new FlatWorld.WorldModel.WorldAddress(
                    "surface",
                    new Int2(chunkX * profile.Width, chunkY * profile.Height));
                ChunkGenerationRequest request = world.BeginChunkGeneration(address, 424242, profile);
                using ChunkGenerationResult result = generator.Generate(request, CancellationToken.None);
                Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            }

            int riverCells = 0;
            int grassCells = 0;
            foreach (ChunkRuntime chunk in world.Chunks.Values)
            {
                ChunkTerrainData terrain = chunk.Terrain;
                for (int y = 0; y < terrain.Height; y++)
                for (int x = 0; x < terrain.Width; x++)
                {
                    if (terrain.TryGetEnvironmentValue("riverDepth", x, y, out float depth) &&
                        depth > 0f)
                        riverCells++;
                    if (terrain.GetGrass(x, y) == 2)
                        grassCells++;
                }
            }

            Assert.That(riverCells, Is.GreaterThan(100),
                "The default local window must contain clearly visible fresh-water coverage.");
            Assert.That(grassCells, Is.GreaterThan(500),
                "The default local window must contain visible grass detail coverage.");
        }

        [Test]
        public void DefaultSmallWrappedRuntimeTraversalContainsFreshWaterRivers()
        {
            ChunkGenerationProfileSO profileAsset =
                AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(
                    "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset");
            Assert.That(profileAsset, Is.Not.Null);
            ChunkGenerationProfileSnapshot profile = profileAsset.CreateSnapshot();
            var topology = new ChunkGenerationTopologySnapshot(
                new Int2(-64, -64), new Int2(128, 128));
            var addresses = new HashSet<FlatWorld.WorldModel.WorldAddress>();

            int startOriginX = FloorToChunkOrigin(topology.NormalizeX(362), profile.Width);
            int startOriginY = FloorToChunkOrigin(topology.NormalizeY(-109), profile.Height);
            for (int offsetY = -2; offsetY <= 2; offsetY++)
            for (int offsetX = -2; offsetX <= 2; offsetX++)
            {
                addresses.Add(new FlatWorld.WorldModel.WorldAddress(
                    "surface",
                    new Int2(
                        topology.NormalizeX(startOriginX + offsetX * profile.Width),
                        topology.NormalizeY(startOriginY + offsetY * profile.Height))));
            }

            float routeStep = profile.Width / Mathf.Sqrt(2f);
            for (int waypoint = 1; waypoint <= 8; waypoint++)
            {
                int originX = FloorToChunkOrigin(
                    topology.NormalizeX(Mathf.FloorToInt(362.5f - routeStep * waypoint)),
                    profile.Width);
                int originY = FloorToChunkOrigin(
                    topology.NormalizeY(Mathf.FloorToInt(-108.5f - routeStep * waypoint)),
                    profile.Height);
                addresses.Add(new FlatWorld.WorldModel.WorldAddress(
                    "surface", new Int2(originX, originY)));
            }

            using var world = new WorldRuntime("small-wrapped-river-coverage", 1);
            var generator = new DeterministicChunkGenerator();
            foreach (FlatWorld.WorldModel.WorldAddress address in addresses)
            {
                // GameManager stores the stable FNV-1a value of the user-facing seed "424242".
                ChunkGenerationRequest request = world.BeginChunkGeneration(
                    address, -780190301, profile, topology);
                using ChunkGenerationResult result = generator.Generate(
                    request, CancellationToken.None);
                Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            }

            int riverCells = 0;
            int totalCells = 0;
            foreach (ChunkRuntime chunk in world.Chunks.Values)
            {
                ChunkTerrainData terrain = chunk.Terrain;
                totalCells += terrain.CellCount;
                for (int y = 0; y < terrain.Height; y++)
                for (int x = 0; x < terrain.Width; x++)
                {
                    if (terrain.TryGetEnvironmentValue("riverDepth", x, y, out float depth) &&
                        depth > 0f)
                        riverCells++;
                }
            }

            Assert.That(riverCells, Is.GreaterThan(0),
                "The default small wrapped runtime traversal must not stay completely dry.");
            Assert.That(riverCells, Is.LessThan(totalCells / 3),
                "Fresh-water channels must not flood most of the streamed traversal window.");
        }

        [Test]
        public void DefaultWrappedTraversalDoesNotCrossACompletelyDryRegion()
        {
            ChunkGenerationProfileSO profileAsset =
                AssetDatabase.LoadAssetAtPath<ChunkGenerationProfileSO>(
                    "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset");
            ChunkGenerationProfileSnapshot profile = profileAsset.CreateSnapshot();
            var topology = new ChunkGenerationTopologySnapshot(
                new Int2(-512, -512), new Int2(1024, 1024));
            var addresses = new HashSet<FlatWorld.WorldModel.WorldAddress>();

            for (int offsetY = -2; offsetY <= 2; offsetY++)
            for (int offsetX = -2; offsetX <= 2; offsetX++)
            {
                addresses.Add(new FlatWorld.WorldModel.WorldAddress(
                    "surface", new Int2(352 + offsetX * 16, -112 + offsetY * 16)));
            }

            float routeStep = 24f / Mathf.Sqrt(2f);
            for (int waypoint = 1; waypoint <= 12; waypoint++)
            {
                int originX = Mathf.FloorToInt((362.5f - routeStep * waypoint) / 16f) * 16;
                int originY = Mathf.FloorToInt((-108.5f - routeStep * waypoint) / 16f) * 16;
                addresses.Add(new FlatWorld.WorldModel.WorldAddress(
                    "surface",
                    new Int2(topology.NormalizeX(originX), topology.NormalizeY(originY))));
            }

            using var world = new WorldRuntime("wrapped-coverage", 1);
            var generator = new DeterministicChunkGenerator();
            foreach (FlatWorld.WorldModel.WorldAddress address in addresses)
            {
                ChunkGenerationRequest request = world.BeginChunkGeneration(
                    address, 424242, profile, topology);
                using ChunkGenerationResult result = generator.Generate(
                    request, CancellationToken.None);
                Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            }

            int riverCells = 0;
            int grassCells = 0;
            foreach (ChunkRuntime chunk in world.Chunks.Values)
            {
                ChunkTerrainData terrain = chunk.Terrain;
                for (int y = 0; y < terrain.Height; y++)
                for (int x = 0; x < terrain.Width; x++)
                {
                    if (terrain.TryGetEnvironmentValue("riverDepth", x, y, out float depth) &&
                        depth > 0f)
                        riverCells++;
                    if (terrain.GetGrass(x, y) == 2)
                        grassCells++;
                }
            }

            Assert.That(riverCells, Is.GreaterThan(100),
                "The deterministic wrapped traversal must encounter visible fresh water.");
            Assert.That(grassCells, Is.GreaterThan(500),
                "The deterministic wrapped traversal must retain grass detail coverage.");
        }

        private static WorldRuntime CreateCommittedWorld(int seed)
        {
            var world = new WorldRuntime("save", 1);
            var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(0, 0));
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, seed, Profile);
            using ChunkGenerationResult result = new DeterministicChunkGenerator().Generate(
                request, CancellationToken.None);
            Assert.That(world.TryCommit(result, out string rejection), Is.True, rejection);
            return world;
        }

        private static int FloorToChunkOrigin(int value, int chunkSize)
        {
            return Mathf.FloorToInt(value / (float)chunkSize) * chunkSize;
        }
    }
}
