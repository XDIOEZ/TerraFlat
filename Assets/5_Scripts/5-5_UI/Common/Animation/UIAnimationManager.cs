using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// UI 动画的惰性服务：注册当前启用的动画组件、缓存解析后的只读 JSON 配置并管理统一倍率。
/// 无 Update、无场景对象，不依赖 UIManager，也不修改 DOTween.timeScale 或 Time.timeScale。
/// 默认配置来自 Resources/Config/UIAnimations；MOD 可通过 TryReloadJson 原子替换配置。
/// </summary>
public sealed class UIAnimationManager
{
    #region 单例与状态
    public const string DefaultAnimationId = "panel.default";
    public const string ConfigurationResourcePath = "Config/UIAnimations";
    private static UIAnimationManager instance;
    private readonly HashSet<BaseUIAnimation> animations = new HashSet<BaseUIAnimation>();
    private Dictionary<string, UIAnimationProfile> profiles;
    private string defaultId = DefaultAnimationId;
    private float playbackSpeed = 1f;
    private bool animationsEnabled = true;

    public static UIAnimationManager Instance => instance ?? (instance = new UIAnimationManager());
    /// <summary>清理时只获取已存在服务，避免退出阶段重新加载资源。</summary>
    public static UIAnimationManager ExistingInstance => instance;
    public float PlaybackSpeed => playbackSpeed;
    public bool AnimationsEnabled => animationsEnabled;
    public int RegisteredCount => animations.Count;
    public int ProfileCount => profiles.Count;

    private UIAnimationManager()
    {
        profiles = new Dictionary<string, UIAnimationProfile>(StringComparer.Ordinal)
        {
            { DefaultAnimationId, new UIAnimationProfile(new UIAnimationData()) }
        };
        ReloadDefaultConfiguration();
    }

    /// <summary>不启用域重载时，也不能沿用上次播放的实例引用或暂停状态。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
    }

    /// <summary>组件按启停注册；重复注册不会重复计数。</summary>
    internal void Register(BaseUIAnimation animation)
    {
        if (animation != null)
            animations.Add(animation);
    }

    /// <summary>失活或销毁后及时解除强引用。</summary>
    internal void Unregister(BaseUIAnimation animation)
    {
        animations.Remove(animation);
    }
    #endregion

    #region 配置加载
    /// <summary>按稳定 ID 获取配置；未知 ID 安全回退到文件指定的默认配置。</summary>
    public UIAnimationProfile GetProfile(string animationId)
    {
        return !string.IsNullOrWhiteSpace(animationId) && profiles.TryGetValue(animationId, out UIAnimationProfile profile)
            ? profile : profiles[defaultId];
    }

    /// <summary>查询精确匹配，供 Inspector、MOD 和配置验证器检查拼写。</summary>
    public bool HasProfile(string animationId)
    {
        return !string.IsNullOrWhiteSpace(animationId) && profiles.ContainsKey(animationId);
    }

    /// <summary>重新读取随包发布的配置；缺失或无效时保留上一次有效配置。</summary>
    public bool ReloadDefaultConfiguration()
    {
        TextAsset asset = Resources.Load<TextAsset>(ConfigurationResourcePath);
        if (asset == null)
        {
            Debug.LogWarning($"[UIAnimation] 缺少 {ConfigurationResourcePath}.json，保留默认动画配置。");
            return false;
        }
        if (TryReloadJson(asset.text, out string error))
            return true;
        Debug.LogWarning($"[UIAnimation] 配置未应用：{error}");
        return false;
    }

    /// <summary>整份配置校验通过才替换；错误 JSON、重复 ID 和非法数值不会破坏当前配置。</summary>
    public bool TryReloadJson(string json, out string error)
    {
        Dictionary<string, UIAnimationProfile> replacement;
        UIAnimationDocument document;
        try
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("配置不能为空。");
            document = JsonConvert.DeserializeObject<UIAnimationDocument>(json, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None,
                MissingMemberHandling = MissingMemberHandling.Error
            });
            if (document == null || document.version != 1 || document.profiles == null || document.profiles.Length == 0)
                throw new ArgumentException("配置必须使用 version=1 并包含非空 profiles。");
            replacement = new Dictionary<string, UIAnimationProfile>(StringComparer.Ordinal);
            foreach (UIAnimationEntry entry in document.profiles)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || entry.id != entry.id.Trim() || entry.data == null)
                    throw new ArgumentException("每项必须包含非空且无首尾空格的 id 与 data。");
                if (replacement.ContainsKey(entry.id))
                    throw new ArgumentException($"重复的动画配置 ID：{entry.id}");
                replacement.Add(entry.id, new UIAnimationProfile(entry.data));
            }
            if (string.IsNullOrWhiteSpace(document.defaultId) || !replacement.ContainsKey(document.defaultId))
                throw new ArgumentException("defaultId 必须匹配 profiles 中的一项。");
        }
        catch (Exception exception) when (exception is JsonException || exception is ArgumentException)
        {
            error = exception.Message;
            return false;
        }

        profiles = replacement;
        defaultId = document.defaultId;
        foreach (BaseUIAnimation animation in animations)
        {
            if (animation != null)
                animation.RefreshConfiguration();
        }
        error = null;
        return true;
    }
    #endregion

    #region 统一播放控制
    /// <summary>只修改本服务注册的 Tween；0 为暂停，1 为正常，2 为两倍速。</summary>
    public void SetPlaybackSpeed(float speed)
    {
        UIAnimationProfile.ValidateRange(speed, 0f, 100f, nameof(speed));
        playbackSpeed = speed;
        foreach (BaseUIAnimation animation in animations)
        {
            if (animation != null)
                animation.ApplyPlaybackSpeed();
        }
    }

    /// <summary>关闭过渡时立即归位；不改变面板自身的开关、输入锁或业务事件。</summary>
    public void SetAnimationsEnabled(bool enabled)
    {
        animationsEnabled = enabled;
        if (!enabled)
            CompleteAll();
    }

    /// <summary>完成当前 UI 过渡，可用于减少动态效果或切换场景前归位。</summary>
    public void CompleteAll()
    {
        // 完成回调可能禁用组件，使用快照避免遍历期间修改注册表。
        var snapshot = new List<BaseUIAnimation>(animations);
        foreach (BaseUIAnimation animation in snapshot)
        {
            if (animation != null)
                animation.CompleteAnimation();
        }
    }
    #endregion
}
