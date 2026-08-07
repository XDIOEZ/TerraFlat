using FlatWorld.GameTest.Shared;
using FlatWorld.Audio;
using NUnit.Framework;
using UnityEditor;

namespace FlatWorld.GameTest.Audio
{
    /// <summary>音频基础冒烟测试：保护服务、Cue 与 Resources 配置入口。</summary>
    public sealed class AudioSmokeTests
    {
        [Test]
        [Category("Audio.Smoke")]
        [Category("Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-6_Audio/Runtime/AudioService.cs", "AudioService");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-6_Audio/Runtime/AudioCue.cs", "AudioCue");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Audio/AudioRuntimeConfig.asset");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Audio/AudioCatalog.asset");
            GameTestAssertions.AssertAssetExists("Assets/Audio/Generated/weather.rain.loop__01.wav");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Audio/Cues/weather.rain.loop.asset");

            AudioCatalog catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(
                "Assets/Resources/Audio/AudioCatalog.asset");
            Assert.That(catalog.TryGet(AudioEventIds.WeatherRainLoop, out AudioCue cue), Is.True);
            Assert.That(cue.Loop, Is.True);
            Assert.That(cue.Bus, Is.EqualTo(AudioBus.Ambient));
        }
    }
}
