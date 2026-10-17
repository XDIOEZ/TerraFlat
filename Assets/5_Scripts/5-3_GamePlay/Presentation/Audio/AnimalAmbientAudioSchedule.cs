using UnityEngine;

/// <summary>行为模块只报告当前是否允许环境发声，音频表现不识别具体物种或状态名。</summary>
public interface IAnimalAmbientAudioActivity
{
    bool AllowsAmbientAudio { get; }
}

/// <summary>每只动物独立的短音时钟，拒绝播放和过期 Tick 都只安排下一次机会。</summary>
public sealed class AnimalAmbientAudioSchedule
{
    #region 独立随机时钟
    private uint randomState;
    private float intervalMinSeconds;
    private float intervalMaxSeconds;
    public bool IsInitialized { get; private set; }
    public float RemainingSeconds { get; private set; }

    public void Reset(uint seed, float intervalMinSeconds, float intervalMaxSeconds,
        float initialDelayMinSeconds, float initialDelayMaxSeconds)
    {
        randomState = seed ^ 0x9E3779B9u;
        randomState ^= randomState >> 16;
        randomState *= 0x7FEB352Du;
        randomState ^= randomState >> 15;
        randomState *= 0x846CA68Bu;
        randomState ^= randomState >> 16;
        if (randomState == 0) randomState = 0xA341316Cu;
        this.intervalMinSeconds = intervalMinSeconds;
        this.intervalMaxSeconds = intervalMaxSeconds;
        RemainingSeconds = SampleDelay(initialDelayMinSeconds, initialDelayMaxSeconds);
        IsInitialized = true;
    }

    public bool Advance(float deltaTime, bool eligible, bool alreadyPlaying)
    {
        if (!IsInitialized || !float.IsFinite(deltaTime) || deltaTime <= 0f)
            return false;
        RemainingSeconds = Mathf.Max(0f, RemainingSeconds - deltaTime);
        if (RemainingSeconds > 0f)
            return false;

        // 先排下次时间再请求声源，范围外或并发拒绝也不会逐帧重试。
        RemainingSeconds = SampleDelay(intervalMinSeconds, intervalMaxSeconds);
        return eligible && !alreadyPlaying;
    }

    public void Clear()
    {
        IsInitialized = false;
        RemainingSeconds = 0f;
    }

    private float SampleDelay(float minSeconds, float maxSeconds)
    {
        float min = Mathf.Max(0.25f, float.IsFinite(minSeconds) ? minSeconds : 1f);
        float max = Mathf.Max(min, float.IsFinite(maxSeconds) ? maxSeconds : min);
        randomState ^= randomState << 13;
        randomState ^= randomState >> 17;
        randomState ^= randomState << 5;
        float unit = (randomState >> 8) * (1f / 16777216f);
        return Mathf.Lerp(min, max, unit);
    }
    #endregion
}
