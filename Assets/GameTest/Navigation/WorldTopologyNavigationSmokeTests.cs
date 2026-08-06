using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.GameTest.Navigation
{
    public sealed class WorldTopologyNavigationSmokeTests
    {
        [Test]
        [Category("Navigation.Smoke")]
        public void WrappedGridConnectsOppositeBoundaryCellsWhileInfiniteGridDoesNot()
        {
            SaveDataMgr manager = Object.FindObjectOfType<SaveDataMgr>();
            GameObject managerOwner = null;
            if (manager == null)
            {
                managerOwner = new GameObject("WrappedNavigationSaveDataMgr");
                manager = managerOwner.AddComponent<SaveDataMgr>();
            }

            GameSaveData previousSave = manager.SaveData;
            string sceneName = SceneManager.GetActiveScene().name;
            var planet = new PlanetData
            {
                Name = sceneName,
                Radius = 16,
                ChunkSize = new Vector2Int(16, 16),
                TopologyMode = WorldTopologyMode.Wrapped
            };
            manager.SaveData = new GameSaveData
            {
                PlanetData_Dict = new Dictionary<string, PlanetData> { [sceneName] = planet }
            };

            try
            {
                var grid = new WorldNavigationGrid();
                Vector2Int right = new Vector2Int(15, 0);
                Vector2Int left = new Vector2Int(-16, 0);
                grid.SetCell(right, 100u, true);
                grid.SetCell(left, 100u, true);

                Assert.That(grid.CanTraverse(right, left, out _), Is.True);
                Assert.That(grid.HasLineOfSight(right, left), Is.True);
                var path = new List<Vector2Int>();
                Assert.That(grid.TryFindPath(right, left, path), Is.True);
                Assert.That(path, Is.EqualTo(new[] { right, left }));

                planet.TopologyMode = WorldTopologyMode.Infinite;
                Assert.That(grid.CanTraverse(right, left, out _), Is.False);
            }
            finally
            {
                manager.SaveData = previousSave;
                if (managerOwner != null)
                    Object.DestroyImmediate(managerOwner);
            }
        }

        [Test]
        [Category("Navigation.Smoke")]
        public void PlayerWrapRefreshesCanonicalChunkAndNavigationWindow()
        {
            string controller = System.IO.File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Item/PlayerWorldWrapController.cs");
            string loader = System.IO.File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Chunk/Mod_ChunkLoader.cs");

            Assert.That(controller, Does.Contain("chunkLoader.RefreshAfterWorldWrap()"));
            Assert.That(loader, Does.Contain("RefreshChunksAroundPlayer()"));
            Assert.That(loader, Does.Contain("RefreshRuntimeWindow"));
            Assert.That(loader, Does.Not.Contain("LoadChunkCloseToPlayer"));
            Assert.That(loader, Does.Not.Contain("RequestNavMeshRefresh"));
        }
    }
}
