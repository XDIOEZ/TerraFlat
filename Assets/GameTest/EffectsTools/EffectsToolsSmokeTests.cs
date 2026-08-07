using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.EffectsTools
{
    /// <summary>特效与工具基础冒烟测试：保护视觉入口和粒子资源。</summary>
    public sealed class EffectsToolsSmokeTests
    {
        [Test]
        [Category("EffectsTools.Smoke")]
        [Category("Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Core/Manager/VisualEffectManager.cs", "VisualEffectManager");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/SpecialEffects/DamageTextEffect.cs", "DamageTextEffect");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/ParticleEffect", "t:Prefab");
            GameTestAssertions.AssertFolderContainsAsset("Assets/Shaders", "t:Shader");
        }
    }
}
