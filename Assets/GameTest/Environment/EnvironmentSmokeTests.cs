using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.Environment
{
    /// <summary>环境基础冒烟测试：保护时间、天气、温度与雨效入口。</summary>
    public sealed class EnvironmentSmokeTests
    {
        [Test]
        [Category("Environment.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Time/DayTimeSystem.cs", "DayTimeSystem");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/WeatherMgr.cs", "WeatherMgr");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/TemperatureMgr.cs", "TemperatureMgr");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Weather/RainEffect.prefab");
        }
    }
}
