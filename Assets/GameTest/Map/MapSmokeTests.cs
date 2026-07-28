using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.Map
{
    /// <summary>地图基础冒烟测试：保护 Chunk、Map 与地图资源入口。</summary>
    public sealed class MapSmokeTests
    {
        [Test]
        [Category("Map.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/ChunkMgr.cs", "ChunkMgr");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Chunk/Chunk.cs", "Chunk");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Map/Base/Map.cs", "Map");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/Map", "t:Prefab");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Config/StructureCatalog_Default.asset");
        }
    }
}
