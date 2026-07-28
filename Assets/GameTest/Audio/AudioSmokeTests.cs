using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.Audio
{
    /// <summary>音频基础冒烟测试：保护服务、Cue 与 Resources 配置入口。</summary>
    public sealed class AudioSmokeTests
    {
        [Test]
        [Category("Audio.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-6_Audio/Runtime/AudioService.cs", "AudioService");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-6_Audio/Runtime/AudioCue.cs", "AudioCue");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Audio/AudioRuntimeConfig.asset");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Audio/AudioCatalog.asset");
        }
    }
}
