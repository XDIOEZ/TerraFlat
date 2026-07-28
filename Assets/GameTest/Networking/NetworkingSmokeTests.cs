using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.Networking
{
    /// <summary>联机基础冒烟测试：保护启动器、网络管理器、玩家 Prefab 与测试场景。</summary>
    public sealed class NetworkingSmokeTests
    {
        [Test]
        [Category("Networking.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-4_Networking/Gameplay/NetworkGameBootstrap.cs", "NetworkGameBootstrap");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-4_Networking/Gameplay/FlatWorldGameNetworkManager.cs", "FlatWorldGameNetworkManager");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab");
            GameTestAssertions.AssertAssetExists("Assets/3_Scenes/NetworkTest.unity");
        }
    }
}
