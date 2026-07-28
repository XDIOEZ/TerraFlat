using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.DataSave
{
    /// <summary>数据存档基础冒烟测试：保护存档入口与权威数据链脚本。</summary>
    public sealed class DataSaveSmokeTests
    {
        [Test]
        [Category("DataSave.Smoke")]
        public void RequiredEntryPointsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/SaveDataMgr.cs", "SaveDataMgr");
            GameTestAssertions.AssertAssetExists("Assets/5_Scripts/5-3_GamePlay/Map/Data/GameSaveData.cs");
            GameTestAssertions.AssertAssetExists("Assets/5_Scripts/5-1_Data/ItemData/ItemData.cs");
            GameTestAssertions.AssertAssetExists("Assets/5_Scripts/5-1_Data/ModData/ModuleData.cs");
        }
    }
}
