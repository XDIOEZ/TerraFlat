using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.Combat
{
    /// <summary>战斗基础冒烟测试：保护伤害、Buff 与技能系统入口。</summary>
    public sealed class CombatSmokeTests
    {
        [Test]
        [Category("Combat.Smoke")]
        public void RequiredEntryPointsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Combat/DamageReceiver.cs", "DamageReceiver");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Buff/BuffManager.cs", "BuffManager");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Skill/Mod_SkillManager.cs", "Mod_SkillManager");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/Weapon", "t:Prefab");
        }
    }
}
