using FlatWorld.Audio;
using UnityEngine;

/// <summary>所有燃烧物共用的空间循环；宿主提供真实燃烧状态并负责结束播放。</summary>
public struct CombustionAudioLoop
{
    #region 共享燃烧声音

    public const string CueId = "fire.burning.loop";
    private AudioHandle handle;
    public bool IsPlaying => handle.IsPlaying;

    public void RefreshBurningAudio(Transform source, bool burning)
    {
        if (!burning)
        {
            StopBurningAudio();
            return;
        }
        if (handle.IsPlaying || !Application.isPlaying || !AudioService.HasInstance)
            return;

        var options = AudioPlayOptions.Attached(source);
        options.FadeIn = 0.3f;
        handle = AudioService.Instance.Play(CueId, options);
    }

    public void StopBurningAudio(float fadeOut = 0.3f)
    {
        handle.Stop(fadeOut);
        handle = AudioHandle.Invalid;
    }

    #endregion
}
