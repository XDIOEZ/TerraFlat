using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.Navigation
{
    /// <summary>导航基础冒烟测试：保护 A*、动态占地和 AI 移动入口。</summary>
    public sealed class NavigationSmokeTests
    {
        [Test]
        [Category("Navigation.Smoke")]
        public void RequiredEntryPointsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/PathFinding/AstarGameManager.cs", "AstarGameManager");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Building/BuildingOccupancyRegistry.cs", "BuildingOccupancyRegistry");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Move/Mover_AI.cs", "Mover_AI");
        }
    }
}
