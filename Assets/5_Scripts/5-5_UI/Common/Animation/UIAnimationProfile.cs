using System;
using DG.Tweening;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 经校验的只读动画参数快照，可由多个相同 AnimationId 的面板安全共享。
/// Duration 为完整行程秒数；Offset 是关闭态相对 UI 坐标偏移，ClosedScale 是关闭态相对缩放。
/// 速度不进入配置：位移视觉由 Offset + Duration 自然决定，并交给 CanvasScaler 适配屏幕。
/// </summary>
public sealed class UIAnimationProfile
{
    #region 只读参数
    public float OpenDuration { get; }
    public float CloseDuration { get; }
    public Vector2 ClosedOffset { get; }
    public float ClosedScale { get; }
    public Ease OpenEase { get; }
    public Ease CloseEase { get; }

    internal UIAnimationProfile(UIAnimationData data)
    {
        ValidateRange(data.openDuration, 0f, 60f, nameof(data.openDuration));
        ValidateRange(data.closeDuration, 0f, 60f, nameof(data.closeDuration));
        ValidateRange(data.offsetX, -100000f, 100000f, nameof(data.offsetX));
        ValidateRange(data.offsetY, -100000f, 100000f, nameof(data.offsetY));
        ValidateRange(data.closedScale, 0f, 10f, nameof(data.closedScale));
        OpenDuration = data.openDuration;
        CloseDuration = data.closeDuration;
        ClosedOffset = new Vector2(data.offsetX, data.offsetY);
        ClosedScale = data.closedScale;
        OpenEase = ParseEase(data.openEase);
        CloseEase = ParseEase(data.closeEase);
    }

    /// <summary>拒绝非有限值和越界配置，不把错误参数传给 Tween。</summary>
    internal static void ValidateRange(float value, float min, float max, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
            throw new ArgumentOutOfRangeException(name, $"{name} 必须在 {min}～{max} 之间且为有限数值。");
    }

    private static Ease ParseEase(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !Enum.TryParse(name, true, out Ease ease) ||
            !Enum.IsDefined(typeof(Ease), ease) || ease == Ease.Unset ||
            ease.ToString().StartsWith("INTERNAL", StringComparison.Ordinal))
        {
            throw new ArgumentException($"无效的 UI 动画缓动名称：{name}");
        }

        return ease;
    }
    #endregion
}

#region JSON 传输结构
[JsonObject(MemberSerialization.OptIn)]
internal sealed class UIAnimationData
{
    [JsonProperty] public float openDuration = 0.2f;
    [JsonProperty] public float closeDuration = 0.15f;
    [JsonProperty] public float offsetX = 0f;
    [JsonProperty] public float offsetY = -48f;
    [JsonProperty] public float closedScale = 0.94f;
    [JsonProperty] public string openEase = "OutCubic";
    [JsonProperty] public string closeEase = "InCubic";
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class UIAnimationEntry
{
    [JsonProperty] public string id = null;
    [JsonProperty] public UIAnimationData data = null;
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class UIAnimationDocument
{
    [JsonProperty] public int version = 1;
    [JsonProperty] public string defaultId = UIAnimationManager.DefaultAnimationId;
    [JsonProperty] public UIAnimationEntry[] profiles = null;
}
#endregion
