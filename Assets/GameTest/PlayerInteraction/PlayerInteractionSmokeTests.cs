using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.PlayerInteraction
{
    /// <summary>玩家交互基础冒烟测试：保护玩家、输入与交互入口。</summary>
    public sealed class PlayerInteractionSmokeTests
    {
        [Test]
        [Category("PlayerInteraction.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Item/Player.cs", "Player");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Controller/GameController.cs", "GameController");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Controller/InputBindingService.cs", "InputBindingService");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/Player", "t:Prefab");
        }
    }
}
