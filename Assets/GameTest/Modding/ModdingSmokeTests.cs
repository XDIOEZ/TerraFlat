using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.Modding
{
    /// <summary>MOD 基础冒烟测试：保护运行时、manifest、Lua 与模板入口。</summary>
    public sealed class ModdingSmokeTests
    {
        [Test]
        [Category("Modding.Smoke")]
        [Category("Smoke")]
        public void RequiredEntryPointsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/ModRuntimeManager.cs", "ModRuntimeManager");
            GameTestAssertions.AssertAssetExists("Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/ModManifest.cs");
            GameTestAssertions.AssertAssetExists("Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/ModLuaRuntime.cs");
            GameTestAssertions.AssertAssetExists("Assets/5_Scripts/5-2_Editor/Mods/ModTemplateCreator.cs");
        }
    }
}
