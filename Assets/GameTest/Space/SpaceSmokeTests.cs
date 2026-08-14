using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.Space
{
    /// <summary>太空基础冒烟测试：保护太空管理、飞行模块、场景与星体资源。</summary>
    public sealed class SpaceSmokeTests
    {
        [Test]
        [Category("Space.Smoke")]
        [Category("Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/World/Space/SpaceMgr.cs", "SpaceMgr");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/World/Space/Module_Fly.cs", "Module_Fly");
            GameTestAssertions.AssertAssetExists("Assets/3_Scenes/SpaceScene.unity");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/World/Space", "t:Prefab");
        }
    }
}
