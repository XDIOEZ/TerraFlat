using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.Building
{
    /// <summary>建筑基础冒烟测试：保护放置、占地和建筑资源入口。</summary>
    public sealed class BuildingSmokeTests
    {
        [Test]
        [Category("Building.Smoke")]
        [Category("Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/World/Building/Mod_Building.cs", "Mod_Building");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/World/Building/BuildingOccupancyRegistry.cs", "BuildingOccupancyRegistry");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/BuildingShadow.prefab");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/MineEntrance.prefab");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/Building/Summoners/MineEntrance_Summoner.prefab");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Config/StructureCatalog_Default.asset");
        }
    }
}
