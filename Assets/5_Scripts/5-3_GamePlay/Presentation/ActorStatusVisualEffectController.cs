using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通用角色状态视觉控制器。
/// 监听角色 BuffManager 的添加、移除和续期事件，为状态提供附着式 Sprite 序列、低强度光晕或 VisualEffectManager 池化特效；当前燃烧使用八帧火焰，
/// 出血/流血/失血复用一个持续循环的红色血滴粒子，光耀复用圆形精灵叠加轻微呼吸光。后续中毒、冰冻等状态只需在 Animator 模块 Prefab 追加配置，无需侵入 Buff 的伤害或 Tick 逻辑。
/// </summary>
[DisallowMultipleComponent]
public sealed class ActorStatusVisualEffectController : MonoBehaviour
{
    #region Inspector Configuration

    [Serializable]
    private sealed class StatusSpriteSequence
    {
        [Tooltip("与 BuffDefinition.Id 一致的状态 ID。")]
        [SerializeField] private string buffId;

        [Tooltip("循环播放的状态精灵帧。")]
        [SerializeField] private Sprite[] frames = Array.Empty<Sprite>();

        [Tooltip("序列帧率。")]
        [SerializeField, Min(0.01f)] private float framesPerSecond = 10f;

        [Tooltip("相对角色当前 Sprite 高度的缩放倍率。")]
        [SerializeField, Min(0.01f)] private float sizeMultiplier = 1.1f;

        [Tooltip("相对角色 Sprite 高度的垂直偏移；负值向下移动。")]
        [SerializeField, Range(-0.25f, 0.25f)] private float verticalOffsetNormalized = -0.05f;

        [Tooltip("相对角色 Sprite 的排序偏移；正值显示在角色前方。")]
        [SerializeField] private int sortingOrderOffset = 1;

        [Tooltip("状态精灵的统一颜色。")]
        [SerializeField] private Color tint = Color.white;

        public string BuffId => buffId;
        public Sprite[] Frames => frames;
        public float FramesPerSecond => Mathf.Max(0.01f, framesPerSecond);
        public float SizeMultiplier => Mathf.Max(0.01f, sizeMultiplier);
        public float VerticalOffsetNormalized => verticalOffsetNormalized;
        public int SortingOrderOffset => sortingOrderOffset;
        public Color Tint => tint;

        /// <summary>限制运行时和 Inspector 都可安全使用的数值范围。</summary>
        public void Validate()
        {
            framesPerSecond = Mathf.Max(0.01f, framesPerSecond);
            sizeMultiplier = Mathf.Max(0.01f, sizeMultiplier);
            verticalOffsetNormalized = Mathf.Clamp(verticalOffsetNormalized, -0.25f, 0.25f);
        }
    }

    [Serializable]
    private sealed class StatusParticleEffect
    {
        [Tooltip("与 BuffDefinition.Id 一致；多个 ID 可用 | 分隔并共用一个粒子特效。")]
        [SerializeField] private string buffId;

        [Tooltip("交给 VisualEffectManager 对象池解析的特效预制体名称。")]
        [SerializeField] private string effectName;

        [Tooltip("相对角色 Sprite 高度的水平偏移。")]
        [SerializeField, Range(-0.5f, 0.5f)] private float horizontalOffsetNormalized;

        [Tooltip("相对角色 Sprite 高度的垂直偏移。")]
        [SerializeField, Range(-0.5f, 0.5f)] private float verticalOffsetNormalized;

        [Tooltip("相对角色 Sprite 的粒子排序偏移；正值显示在角色前方。")]
        [SerializeField] private int sortingOrderOffset = 2;

        [NonSerialized] private string[] parsedBuffIds = Array.Empty<string>();

        public string BuffId => buffId;
        public string EffectName => effectName;
        public float HorizontalOffsetNormalized => horizontalOffsetNormalized;
        public float VerticalOffsetNormalized => verticalOffsetNormalized;
        public int SortingOrderOffset => sortingOrderOffset;

        /// <summary>限制数值并预解析复合 Buff ID，避免每帧拆分字符串。</summary>
        public void Validate()
        {
            horizontalOffsetNormalized = Mathf.Clamp(horizontalOffsetNormalized, -0.5f, 0.5f);
            verticalOffsetNormalized = Mathf.Clamp(verticalOffsetNormalized, -0.5f, 0.5f);

            string rawBuffIds = buffId ?? string.Empty;
            parsedBuffIds = rawBuffIds.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parsedBuffIds.Length; i++)
                parsedBuffIds[i] = parsedBuffIds[i].Trim();
        }

        /// <summary>判断指定 Buff 是否属于该粒子表现配置。</summary>
        public bool MatchesBuffId(string candidateBuffId)
        {
            if (string.IsNullOrWhiteSpace(candidateBuffId))
                return false;

            if (parsedBuffIds == null || parsedBuffIds.Length == 0)
                Validate();

            for (int i = 0; i < parsedBuffIds.Length; i++)
            {
                if (string.Equals(parsedBuffIds[i], candidateBuffId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>判断该配置绑定的任意一个 Buff 当前是否仍存在。</summary>
        public bool HasAnyBuff(BuffManager manager)
        {
            if (manager == null)
                return false;

            if (parsedBuffIds == null || parsedBuffIds.Length == 0)
                Validate();

            for (int i = 0; i < parsedBuffIds.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(parsedBuffIds[i]) && manager.HasBuff(parsedBuffIds[i]))
                    return true;
            }

            return false;
        }
    }

    [Serializable]
    private sealed class StatusGlowEffect
    {
        [Tooltip("与 BuffDefinition.Id 一致的状态 ID。")]
        [SerializeField] private string buffId;

        [Tooltip("光晕精灵；通常使用白色圆形 Sprite，再通过颜色和透明度染色。")]
        [SerializeField] private Sprite sprite;

        [Tooltip("光晕颜色与基础透明度。")]
        [SerializeField] private Color tint = new Color(1f, 0.86f, 0.35f, 0.14f);

        [Tooltip("相对角色 Sprite 高度的光晕缩放倍率。")]
        [SerializeField, Min(0.01f)] private float sizeMultiplier = 1.65f;

        [Tooltip("光晕呼吸速度，每秒周期数。")]
        [SerializeField, Min(0f)] private float pulseSpeed = 1.4f;

        [Tooltip("光晕呼吸的缩放幅度。")]
        [SerializeField, Range(0f, 0.5f)] private float pulseAmplitude = 0.08f;

        [Tooltip("相对角色 Sprite 高度的垂直偏移。")]
        [SerializeField, Range(-0.5f, 0.5f)] private float verticalOffsetNormalized;

        [Tooltip("相对角色 Sprite 的排序偏移；负值显示在角色后方。")]
        [SerializeField] private int sortingOrderOffset = -1;

        public string BuffId => buffId;
        public Sprite Sprite => sprite;
        public Color Tint => tint;
        public float SizeMultiplier => Mathf.Max(0.01f, sizeMultiplier);
        public float PulseSpeed => Mathf.Max(0f, pulseSpeed);
        public float PulseAmplitude => Mathf.Clamp(pulseAmplitude, 0f, 0.5f);
        public float VerticalOffsetNormalized => verticalOffsetNormalized;
        public int SortingOrderOffset => sortingOrderOffset;

        /// <summary>限制光晕运行参数，避免错误配置导致异常缩放。</summary>
        public void Validate()
        {
            sizeMultiplier = Mathf.Max(0.01f, sizeMultiplier);
            pulseSpeed = Mathf.Max(0f, pulseSpeed);
            pulseAmplitude = Mathf.Clamp(pulseAmplitude, 0f, 0.5f);
            verticalOffsetNormalized = Mathf.Clamp(verticalOffsetNormalized, -0.5f, 0.5f);
        }
    }

    [Header("状态表现")]
    [Tooltip("Buff ID 到附着精灵序列的映射；每个 ID 最多一条。")]
    [SerializeField] private StatusSpriteSequence[] statusEffects = Array.Empty<StatusSpriteSequence>();

    [Tooltip("Buff ID 到 VisualEffectManager 池化粒子特效的映射；多个 ID 可用 | 合并为一个持续表现。")]
    [SerializeField] private StatusParticleEffect[] particleEffects = Array.Empty<StatusParticleEffect>();

    [Tooltip("Buff ID 到低强度、可呼吸光晕的映射；适合光耀等持续状态。")]
    [SerializeField] private StatusGlowEffect[] glowEffects = Array.Empty<StatusGlowEffect>();

    [Tooltip("事件之外的状态校验间隔，用于存档恢复和对象池复用后的补同步。")]
    [SerializeField, Min(0.05f)] private float reconciliationInterval = 0.2f;

    #endregion

    #region Runtime State

    private sealed class RuntimeStatusVisual
    {
        public StatusSpriteSequence Sequence;
        public GameObject EffectObject;
        public SpriteRenderer Renderer;
        public float Elapsed;
        public bool IsActive;
    }

    private sealed class RuntimeStatusParticleVisual
    {
        public StatusParticleEffect Effect;
        public GameObject EffectObject;
        public ParticleSystemRenderer[] Renderers;
        public bool IsActive;
    }

    private sealed class RuntimeStatusGlowVisual
    {
        public StatusGlowEffect Glow;
        public GameObject EffectObject;
        public SpriteRenderer Renderer;
        public float Elapsed;
        public float PulsePhase;
        public bool IsActive;
    }

    private readonly Dictionary<string, RuntimeStatusVisual> visualsByBuffId =
        new Dictionary<string, RuntimeStatusVisual>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, RuntimeStatusParticleVisual> particleVisualsByEffectName =
        new Dictionary<string, RuntimeStatusParticleVisual>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, RuntimeStatusGlowVisual> glowVisualsByBuffId =
        new Dictionary<string, RuntimeStatusGlowVisual>(StringComparer.OrdinalIgnoreCase);

    private SpriteRenderer sourceRenderer;
    private BuffManager buffManager;
    private float nextReconciliationTime;
    private bool statusesDirty = true;

    #endregion

    #region Lifecycle

    private void Awake()
    {
        sourceRenderer = GetComponent<SpriteRenderer>();
        BuildRuntimeVisuals();
    }

    private void OnEnable()
    {
        BindBuffManager();
        statusesDirty = true;
    }

    private void Start()
    {
        RefreshStatusVisuals();
    }

    private void Update()
    {
        if (buffManager == null)
            BindBuffManager();

        if (statusesDirty || Time.unscaledTime >= nextReconciliationTime)
            RefreshStatusVisuals();

        UpdateActiveVisuals(Time.deltaTime);
    }

    private void OnDisable()
    {
        UnbindBuffManager();
        SetAllVisualsActive(false);
    }

    private void OnDestroy()
    {
        UnbindBuffManager();
        SetAllParticleVisualsActive(false);
        SetAllGlowVisualsActive(false);
    }

    private void OnValidate()
    {
        reconciliationInterval = Mathf.Max(0.05f, reconciliationInterval);
        if (statusEffects != null)
        {
            for (int i = 0; i < statusEffects.Length; i++)
                statusEffects[i]?.Validate();
        }

        if (particleEffects != null)
        {
            for (int i = 0; i < particleEffects.Length; i++)
                particleEffects[i]?.Validate();
        }

        if (glowEffects == null)
            return;

        for (int i = 0; i < glowEffects.Length; i++)
            glowEffects[i]?.Validate();
    }

    #endregion

    #region Public API

    /// <summary>立即按当前 Buff 状态重新同步，供存档恢复、对象池复用和自动化验证调用。</summary>
    public void RefreshStatusVisuals()
    {
        BindBuffManager();
        ReconcileStatusVisuals();
    }

    /// <summary>返回指定 Buff 是否配置了有效的状态表现。</summary>
    public bool IsStatusVisualConfigured(string buffId)
    {
        return (TryGetStatusSequence(buffId, out StatusSpriteSequence sequence) && HasValidFrames(sequence)) ||
               (TryGetParticleVisual(buffId, out RuntimeStatusParticleVisual particleVisual) &&
                particleVisual.Effect != null &&
                !string.IsNullOrWhiteSpace(particleVisual.Effect.EffectName)) ||
               (TryGetGlowVisual(buffId, out RuntimeStatusGlowVisual glowVisual) &&
                glowVisual.Glow != null &&
                glowVisual.Glow.Sprite != null);
    }

    /// <summary>返回指定 Buff 已配置的序列帧数。</summary>
    public int GetStatusVisualFrameCount(string buffId)
    {
        return TryGetStatusSequence(buffId, out StatusSpriteSequence sequence) &&
               sequence.Frames != null
            ? sequence.Frames.Length
            : 0;
    }

    /// <summary>返回指定 Buff 的附着视觉当前是否启用。</summary>
    public bool IsStatusVisualActive(string buffId)
    {
        if (string.IsNullOrWhiteSpace(buffId))
            return false;

        if (visualsByBuffId.TryGetValue(buffId, out RuntimeStatusVisual visual) && visual.IsActive)
            return true;

        if (TryGetParticleVisual(buffId, out RuntimeStatusParticleVisual particleVisual) && particleVisual.IsActive)
            return true;

        return TryGetGlowVisual(buffId, out RuntimeStatusGlowVisual glowVisual) && glowVisual.IsActive;
    }

    #endregion

    #region Buff Binding

    /// <summary>解析父级角色上的 BuffManager；模块延后创建时会在下一次校验自动重新绑定。</summary>
    private void BindBuffManager()
    {
        BuffManager resolvedManager = ResolveBuffManager();
        if (ReferenceEquals(buffManager, resolvedManager))
            return;

        UnbindBuffManager();
        buffManager = resolvedManager;
        if (buffManager != null)
        {
            buffManager.BuffAdded += OnBuffAdded;
            buffManager.BuffRemoved += OnBuffRemoved;
            buffManager.BuffDurationChanged += OnBuffDurationChanged;
        }

        statusesDirty = true;
    }

    /// <summary>优先从角色 Item 的模块容器查找，兼容 Prefab 装配尚未完成的早期阶段。</summary>
    private BuffManager ResolveBuffManager()
    {
        Item owner = GetComponentInParent<Item>();
        if (owner == null)
            return GetComponentInParent<BuffManager>();

        BuffManager module = owner.itemMods?.GetMod_ByID<BuffManager>(ModText.BuffManager);
        return module != null ? module : owner.GetComponentInChildren<BuffManager>(true);
    }

    /// <summary>解除旧模块事件，避免对象池复用后继续驱动已离场角色。</summary>
    private void UnbindBuffManager()
    {
        if (buffManager == null)
            return;

        buffManager.BuffAdded -= OnBuffAdded;
        buffManager.BuffRemoved -= OnBuffRemoved;
        buffManager.BuffDurationChanged -= OnBuffDurationChanged;
        buffManager = null;
    }

    private void OnBuffAdded(BuffInstance runtime)
    {
        SetVisualActive(runtime?.DefinitionId, true);
        SetParticleVisualsForBuff(runtime?.DefinitionId);
        SetGlowVisualActive(runtime?.DefinitionId, true);
    }

    private void OnBuffRemoved(BuffInstance runtime)
    {
        SetVisualActive(runtime?.DefinitionId, false);
        SetParticleVisualsForBuff(runtime?.DefinitionId);
        SetGlowVisualActive(runtime?.DefinitionId, false);
    }

    private void OnBuffDurationChanged(BuffInstance runtime)
    {
        if (runtime != null)
        {
            SetVisualActive(runtime.DefinitionId, true);
            SetParticleVisualsForBuff(runtime.DefinitionId);
            SetGlowVisualActive(runtime.DefinitionId, true);
        }
    }

    #endregion

    #region Visual State

    /// <summary>把 Inspector 配置转换为无分配的运行时索引。</summary>
    private void BuildRuntimeVisuals()
    {
        visualsByBuffId.Clear();
        particleVisualsByEffectName.Clear();
        glowVisualsByBuffId.Clear();

        if (statusEffects != null)
        {
            for (int i = 0; i < statusEffects.Length; i++)
            {
                StatusSpriteSequence sequence = statusEffects[i];
                if (sequence == null ||
                    string.IsNullOrWhiteSpace(sequence.BuffId) ||
                    sequence.Frames == null ||
                    sequence.Frames.Length == 0)
                {
                    continue;
                }

                sequence.Validate();
                if (!visualsByBuffId.TryAdd(sequence.BuffId, new RuntimeStatusVisual { Sequence = sequence }))
                {
                    Debug.LogWarning($"[{nameof(ActorStatusVisualEffectController)}] 重复的状态精灵表现 ID：{sequence.BuffId}", this);
                }
            }
        }

        if (particleEffects != null)
        {
            for (int i = 0; i < particleEffects.Length; i++)
            {
                StatusParticleEffect effect = particleEffects[i];
                if (effect == null ||
                    string.IsNullOrWhiteSpace(effect.BuffId) ||
                    string.IsNullOrWhiteSpace(effect.EffectName))
                {
                    continue;
                }

                effect.Validate();
                if (!particleVisualsByEffectName.TryAdd(
                        effect.EffectName,
                        new RuntimeStatusParticleVisual { Effect = effect }))
                {
                    Debug.LogWarning($"[{nameof(ActorStatusVisualEffectController)}] 重复的状态粒子特效名称：{effect.EffectName}", this);
                }
            }
        }

        if (glowEffects == null)
            return;

        for (int i = 0; i < glowEffects.Length; i++)
        {
            StatusGlowEffect glow = glowEffects[i];
            if (glow == null ||
                string.IsNullOrWhiteSpace(glow.BuffId) ||
                glow.Sprite == null)
            {
                continue;
            }

            glow.Validate();
            if (!glowVisualsByBuffId.TryAdd(
                    glow.BuffId,
                    new RuntimeStatusGlowVisual { Glow = glow }))
            {
                Debug.LogWarning($"[{nameof(ActorStatusVisualEffectController)}] 重复的状态光晕表现 ID：{glow.BuffId}", this);
            }
        }
    }

    /// <summary>运行时索引尚未建立时回退检查序列化配置，便于 Prefab 校验和编辑器工具读取。</summary>
    private bool TryGetStatusSequence(string buffId, out StatusSpriteSequence sequence)
    {
        sequence = null;
        if (string.IsNullOrWhiteSpace(buffId))
            return false;

        if (visualsByBuffId.TryGetValue(buffId, out RuntimeStatusVisual visual))
        {
            sequence = visual.Sequence;
            return sequence != null;
        }

        if (statusEffects == null)
            return false;

        for (int i = 0; i < statusEffects.Length; i++)
        {
            StatusSpriteSequence candidate = statusEffects[i];
            if (candidate != null &&
                string.Equals(candidate.BuffId, buffId, StringComparison.OrdinalIgnoreCase))
            {
                sequence = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>按 Buff ID 查找可复用的池化粒子表现。</summary>
    private bool TryGetParticleVisual(string buffId, out RuntimeStatusParticleVisual visual)
    {
        visual = null;
        if (string.IsNullOrWhiteSpace(buffId))
            return false;

        foreach (KeyValuePair<string, RuntimeStatusParticleVisual> pair in particleVisualsByEffectName)
        {
            if (pair.Value?.Effect != null && pair.Value.Effect.MatchesBuffId(buffId))
            {
                visual = pair.Value;
                return true;
            }
        }

        if (particleEffects == null)
            return false;

        for (int i = 0; i < particleEffects.Length; i++)
        {
            StatusParticleEffect candidate = particleEffects[i];
            if (candidate != null && candidate.MatchesBuffId(buffId))
            {
                visual = new RuntimeStatusParticleVisual { Effect = candidate };
                return true;
            }
        }

        return false;
    }

    /// <summary>按 Buff ID 查找低强度状态光晕表现。</summary>
    private bool TryGetGlowVisual(string buffId, out RuntimeStatusGlowVisual visual)
    {
        visual = null;
        if (string.IsNullOrWhiteSpace(buffId))
            return false;

        if (glowVisualsByBuffId.TryGetValue(buffId, out visual))
            return visual?.Glow != null;

        if (glowEffects == null)
            return false;

        for (int i = 0; i < glowEffects.Length; i++)
        {
            StatusGlowEffect candidate = glowEffects[i];
            if (candidate != null &&
                string.Equals(candidate.BuffId, buffId, StringComparison.OrdinalIgnoreCase))
            {
                visual = new RuntimeStatusGlowVisual { Glow = candidate };
                return true;
            }
        }

        return false;
    }

    /// <summary>确认每一帧都已正确引用，防止动画因缺图静默退化。</summary>
    private static bool HasValidFrames(StatusSpriteSequence sequence)
    {
        Sprite[] frames = sequence?.Frames;
        if (frames == null || frames.Length == 0)
            return false;

        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] == null)
                return false;
        }

        return true;
    }

    /// <summary>事件遗漏或 Buff 从存档恢复时，按权威 BuffManager 状态修正视觉。</summary>
    private void ReconcileStatusVisuals()
    {
        foreach (KeyValuePair<string, RuntimeStatusVisual> pair in visualsByBuffId)
        {
            bool shouldBeActive = buffManager != null && buffManager.HasBuff(pair.Key);
            SetVisualActive(pair.Value, shouldBeActive);
        }

        foreach (KeyValuePair<string, RuntimeStatusParticleVisual> pair in particleVisualsByEffectName)
        {
            RuntimeStatusParticleVisual visual = pair.Value;
            bool shouldBeActive = visual?.Effect != null && visual.Effect.HasAnyBuff(buffManager);
            SetParticleVisualActive(visual, shouldBeActive);
        }

        foreach (KeyValuePair<string, RuntimeStatusGlowVisual> pair in glowVisualsByBuffId)
        {
            bool shouldBeActive = buffManager != null && buffManager.HasBuff(pair.Key);
            SetGlowVisualActive(pair.Value, shouldBeActive);
        }

        statusesDirty = false;
        nextReconciliationTime = Time.unscaledTime + reconciliationInterval;
    }

    /// <summary>按 Buff ID 处理一次视觉启停。</summary>
    private void SetVisualActive(string buffId, bool active)
    {
        if (!string.IsNullOrWhiteSpace(buffId) && visualsByBuffId.TryGetValue(buffId, out RuntimeStatusVisual visual))
            SetVisualActive(visual, active);
    }

    /// <summary>按事件即时校验复合 Buff 粒子表现，支持出血、流血和失血共用一个池化实例。</summary>
    private void SetParticleVisualsForBuff(string buffId)
    {
        if (string.IsNullOrWhiteSpace(buffId))
            return;

        foreach (KeyValuePair<string, RuntimeStatusParticleVisual> pair in particleVisualsByEffectName)
        {
            RuntimeStatusParticleVisual visual = pair.Value;
            if (visual?.Effect != null && visual.Effect.MatchesBuffId(buffId))
                SetParticleVisualActive(visual, visual.Effect.HasAnyBuff(buffManager));
        }
    }

    /// <summary>按 Buff ID 处理一次状态光晕启停。</summary>
    private void SetGlowVisualActive(string buffId, bool active)
    {
        if (!string.IsNullOrWhiteSpace(buffId) && glowVisualsByBuffId.TryGetValue(buffId, out RuntimeStatusGlowVisual visual))
            SetGlowVisualActive(visual, active);
    }

    /// <summary>激活时惰性创建火焰精灵，停用时保留实例以避免每次 Buff 重复分配。</summary>
    private void SetVisualActive(RuntimeStatusVisual visual, bool active)
    {
        if (visual == null || visual.IsActive == active)
            return;

        visual.IsActive = active;
        if (!active)
        {
            if (visual.EffectObject != null)
                visual.EffectObject.SetActive(false);
            return;
        }

        EnsureEffectObject(visual);
        if (visual.EffectObject == null)
        {
            visual.IsActive = false;
            return;
        }

        visual.Elapsed = GetInitialFrameOffset(visual.Sequence.FramesPerSecond);
        UpdateVisualFrame(visual);
        UpdateVisualTransform(visual);
        visual.EffectObject.SetActive(true);
    }

    /// <summary>禁用整个角色时同步隐藏状态精灵；重新启用后由状态校验恢复。</summary>
    private void SetAllVisualsActive(bool active)
    {
        foreach (KeyValuePair<string, RuntimeStatusVisual> pair in visualsByBuffId)
            SetVisualActive(pair.Value, active);

        SetAllParticleVisualsActive(active);
        SetAllGlowVisualsActive(active);
    }

    /// <summary>统一启停所有池化粒子表现，确保对象池回收时清理 Owner 绑定。</summary>
    private void SetAllParticleVisualsActive(bool active)
    {
        foreach (KeyValuePair<string, RuntimeStatusParticleVisual> pair in particleVisualsByEffectName)
            SetParticleVisualActive(pair.Value, active);
    }

    /// <summary>统一启停角色上的状态光晕，确保对象禁用和对象池复用时不会残留。</summary>
    private void SetAllGlowVisualsActive(bool active)
    {
        foreach (KeyValuePair<string, RuntimeStatusGlowVisual> pair in glowVisualsByBuffId)
            SetGlowVisualActive(pair.Value, active);
    }

    #endregion

    #region Sprite Sequence

    /// <summary>为首次命中的状态创建子对象，并排除角色 MPB 染色/水体模块的影响。</summary>
    private void EnsureEffectObject(RuntimeStatusVisual visual)
    {
        if (visual.EffectObject != null)
            return;

        GameObject effectObject = new GameObject($"StatusVisual_{visual.Sequence.BuffId}");
        effectObject.transform.SetParent(transform, false);
        effectObject.AddComponent<ActorRenderEffectExclude>();

        SpriteRenderer renderer = effectObject.AddComponent<SpriteRenderer>();
        renderer.color = visual.Sequence.Tint;
        renderer.sprite = GetFrame(visual.Sequence, 0);

        visual.EffectObject = effectObject;
        visual.Renderer = renderer;
        effectObject.SetActive(false);
    }

    /// <summary>更新所有激活序列的帧、尺寸和排序，使动画与角色换帧/换层保持同步。</summary>
    private void UpdateActiveVisuals(float deltaTime)
    {
        foreach (KeyValuePair<string, RuntimeStatusVisual> pair in visualsByBuffId)
        {
            RuntimeStatusVisual visual = pair.Value;
            if (!visual.IsActive)
                continue;

            if (visual.EffectObject == null || visual.Renderer == null)
            {
                visual.IsActive = false;
                SetVisualActive(visual, true);
                continue;
            }

            visual.Elapsed += Mathf.Max(0f, deltaTime);
            UpdateVisualFrame(visual);
            UpdateVisualTransform(visual);
        }

        UpdateActiveParticleVisuals();
        UpdateActiveGlowVisuals(deltaTime);
    }

    /// <summary>同步池化粒子的位置和排序，使角色换 Sprite 或排序层后仍保持附着。</summary>
    private void UpdateActiveParticleVisuals()
    {
        foreach (KeyValuePair<string, RuntimeStatusParticleVisual> pair in particleVisualsByEffectName)
        {
            RuntimeStatusParticleVisual visual = pair.Value;
            if (visual == null || !visual.IsActive)
                continue;

            if (visual.EffectObject == null || !visual.EffectObject.activeInHierarchy)
            {
                visual.IsActive = false;
                SetParticleVisualActive(visual, true);
                continue;
            }

            UpdateParticleTransform(visual);
        }
    }

    /// <summary>根据时间选取循环帧；每个角色用实例 ID 产生不同起始相位，避免所有火焰同步跳动。</summary>
    private void UpdateVisualFrame(RuntimeStatusVisual visual)
    {
        Sprite[] frames = visual.Sequence.Frames;
        if (frames == null || frames.Length == 0 || visual.Renderer == null)
            return;

        int frameIndex = Mathf.FloorToInt(visual.Elapsed * visual.Sequence.FramesPerSecond) % frames.Length;
        Sprite nextFrame = GetFrame(visual.Sequence, frameIndex);
        if (nextFrame != null && visual.Renderer.sprite != nextFrame)
            visual.Renderer.sprite = nextFrame;
    }

    /// <summary>让附着精灵覆盖当前角色主体，并随动态排序层/方向实时更新。</summary>
    private void UpdateVisualTransform(RuntimeStatusVisual visual)
    {
        sourceRenderer ??= GetComponent<SpriteRenderer>();
        if (sourceRenderer == null || sourceRenderer.sprite == null || visual.Renderer == null)
            return;

        Sprite sourceSprite = sourceRenderer.sprite;
        Sprite effectSprite = visual.Renderer.sprite;
        if (effectSprite == null)
            return;

        float sourceHeight = Mathf.Max(0.01f, sourceSprite.bounds.size.y);
        float effectHeight = Mathf.Max(0.01f, effectSprite.bounds.size.y);
        float scale = sourceHeight * visual.Sequence.SizeMultiplier / effectHeight;
        float verticalOffset = sourceHeight * visual.Sequence.VerticalOffsetNormalized;

        Transform effectTransform = visual.EffectObject.transform;
        effectTransform.localPosition = new Vector3(
            sourceSprite.bounds.center.x,
            sourceSprite.bounds.min.y + verticalOffset,
            0f);
        effectTransform.localScale = Vector3.one * scale;

        visual.Renderer.sortingLayerID = sourceRenderer.sortingLayerID;
        visual.Renderer.sortingOrder = sourceRenderer.sortingOrder + visual.Sequence.SortingOrderOffset;
        visual.Renderer.flipX = sourceRenderer.flipX;
        visual.Renderer.color = visual.Sequence.Tint;
    }

    /// <summary>安全获取指定索引的帧，避免个别缺图破坏整个状态表现。</summary>
    private static Sprite GetFrame(StatusSpriteSequence sequence, int frameIndex)
    {
        Sprite[] frames = sequence?.Frames;
        if (frames == null || frames.Length == 0)
            return null;

        int normalizedIndex = Mathf.Clamp(frameIndex, 0, frames.Length - 1);
        return frames[normalizedIndex];
    }

    /// <summary>基于实例 ID 计算稳定初始相位。</summary>
    private float GetInitialFrameOffset(float framesPerSecond)
    {
        int phase = (GetInstanceID() & int.MaxValue) % 97;
        return phase / 97f / Mathf.Max(0.01f, framesPerSecond);
    }

    #endregion

    #region Glow Effects

    /// <summary>激活或停用状态光晕；对象只创建一次，避免 Buff 反复切换产生额外分配。</summary>
    private void SetGlowVisualActive(RuntimeStatusGlowVisual visual, bool active)
    {
        if (visual == null || visual.Glow == null || visual.Glow.Sprite == null)
            return;

        if (!active)
        {
            visual.IsActive = false;
            if (visual.EffectObject != null)
                visual.EffectObject.SetActive(false);
            return;
        }

        if (visual.IsActive && visual.EffectObject != null && visual.EffectObject.activeInHierarchy)
            return;

        EnsureGlowEffectObject(visual);
        if (visual.EffectObject == null || visual.Renderer == null)
        {
            visual.IsActive = false;
            return;
        }

        visual.IsActive = true;
        visual.Elapsed = 0f;
        visual.PulsePhase = GetInitialPulsePhase();
        UpdateGlowTransform(visual);
        visual.EffectObject.SetActive(true);
    }

    /// <summary>为首次命中的状态创建圆形光晕子对象，并排除角色渲染控制器的颜色/水体处理。</summary>
    private void EnsureGlowEffectObject(RuntimeStatusGlowVisual visual)
    {
        if (visual.EffectObject != null)
            return;

        GameObject effectObject = new GameObject($"StatusGlow_{visual.Glow.BuffId}");
        effectObject.transform.SetParent(transform, false);
        effectObject.AddComponent<ActorRenderEffectExclude>();

        SpriteRenderer renderer = effectObject.AddComponent<SpriteRenderer>();
        renderer.sprite = visual.Glow.Sprite;
        renderer.color = visual.Glow.Tint;

        visual.EffectObject = effectObject;
        visual.Renderer = renderer;
        effectObject.SetActive(false);
    }

    /// <summary>更新激活光晕的呼吸动画、位置、尺寸和排序。</summary>
    private void UpdateActiveGlowVisuals(float deltaTime)
    {
        foreach (KeyValuePair<string, RuntimeStatusGlowVisual> pair in glowVisualsByBuffId)
        {
            RuntimeStatusGlowVisual visual = pair.Value;
            if (visual == null || !visual.IsActive)
                continue;

            if (visual.EffectObject == null || visual.Renderer == null || !visual.EffectObject.activeInHierarchy)
            {
                visual.IsActive = false;
                SetGlowVisualActive(visual, true);
                continue;
            }

            visual.Elapsed += Mathf.Max(0f, deltaTime);
            UpdateGlowTransform(visual);
        }
    }

    /// <summary>让光晕与角色当前 Sprite 的尺寸、方向和排序层保持同步。</summary>
    private void UpdateGlowTransform(RuntimeStatusGlowVisual visual)
    {
        sourceRenderer ??= GetComponent<SpriteRenderer>();
        if (sourceRenderer == null || sourceRenderer.sprite == null ||
            visual?.EffectObject == null || visual.Renderer == null || visual.Glow?.Sprite == null)
        {
            return;
        }

        Sprite sourceSprite = sourceRenderer.sprite;
        Sprite glowSprite = visual.Glow.Sprite;
        float sourceHeight = Mathf.Max(0.01f, sourceSprite.bounds.size.y);
        float glowHeight = Mathf.Max(0.01f, glowSprite.bounds.size.y);
        float pulse = 1f;
        float intensity = 1f;
        if (visual.Glow.PulseSpeed > 0f && visual.Glow.PulseAmplitude > 0f)
        {
            float angle = (visual.Elapsed + visual.PulsePhase) * visual.Glow.PulseSpeed * Mathf.PI * 2f;
            float wave = Mathf.Sin(angle);
            pulse += wave * visual.Glow.PulseAmplitude;
            intensity += wave * visual.Glow.PulseAmplitude * 0.35f;
        }

        Transform effectTransform = visual.EffectObject.transform;
        effectTransform.localPosition = new Vector3(
            sourceSprite.bounds.center.x,
            sourceSprite.bounds.center.y + sourceHeight * visual.Glow.VerticalOffsetNormalized,
            0f);
        effectTransform.localScale = Vector3.one * (sourceHeight * visual.Glow.SizeMultiplier / glowHeight) * pulse;

        visual.Renderer.sortingLayerID = sourceRenderer.sortingLayerID;
        visual.Renderer.sortingOrder = sourceRenderer.sortingOrder + visual.Glow.SortingOrderOffset;
        visual.Renderer.flipX = sourceRenderer.flipX;
        Color tint = visual.Glow.Tint;
        tint.a = Mathf.Clamp01(tint.a * intensity);
        visual.Renderer.color = tint;
    }

    /// <summary>基于实例 ID 计算稳定初始相位，避免多个角色的光晕同时呼吸。</summary>
    private float GetInitialPulsePhase()
    {
        return ((GetInstanceID() & int.MaxValue) % 97) / 97f;
    }

    #endregion

    #region Pooled Particle Effects

    /// <summary>通过现有 VisualEffectManager 播放绑定角色的非叠加池化粒子。</summary>
    private void SetParticleVisualActive(RuntimeStatusParticleVisual visual, bool active)
    {
        if (visual == null || visual.Effect == null)
            return;

        if (!active)
        {
            visual.IsActive = false;
            if (visual.EffectObject != null)
            {
                VisualEffectManager manager = VisualEffectManager.Instance;
                if (manager != null)
                    manager.StopOwnerEffect(transform, visual.Effect.EffectName);
                else
                    visual.EffectObject.SetActive(false);
            }

            visual.EffectObject = null;
            visual.Renderers = null;
            return;
        }

        if (visual.IsActive && visual.EffectObject != null && visual.EffectObject.activeInHierarchy)
            return;

        VisualEffectManager visualEffectManager = VisualEffectManager.Instance;
        if (visualEffectManager == null)
        {
            visual.IsActive = false;
            return;
        }

        if (visual.EffectObject != null)
            visualEffectManager.ReturnEffectToPool(visual.Effect.EffectName, visual.EffectObject);

        GameObject effectObject = visualEffectManager.PlayEffect(
            transform,
            visual.Effect.EffectName,
            transform,
            GetParticleLocalPosition(visual.Effect),
            -1f,
            EffectStackMode.NonStackable);
        if (effectObject == null)
        {
            visual.IsActive = false;
            visual.EffectObject = null;
            visual.Renderers = null;
            return;
        }

        visual.IsActive = true;
        visual.EffectObject = effectObject;
        visual.Renderers = effectObject.GetComponentsInChildren<ParticleSystemRenderer>(true);
        UpdateParticleTransform(visual);
    }

    /// <summary>根据角色主体 Sprite 计算血滴粒子中心位置，避免固定坐标脱离不同体型角色。</summary>
    private Vector3 GetParticleLocalPosition(StatusParticleEffect effect)
    {
        sourceRenderer ??= GetComponent<SpriteRenderer>();
        if (sourceRenderer == null || sourceRenderer.sprite == null || effect == null)
            return Vector3.zero;

        Sprite sourceSprite = sourceRenderer.sprite;
        float sourceHeight = Mathf.Max(0.01f, sourceSprite.bounds.size.y);
        return new Vector3(
            sourceSprite.bounds.center.x + sourceHeight * effect.HorizontalOffsetNormalized,
            sourceSprite.bounds.center.y + sourceHeight * effect.VerticalOffsetNormalized,
            0f);
    }

    /// <summary>同步粒子局部位置和排序层，使血滴稳定显示在角色前方。</summary>
    private void UpdateParticleTransform(RuntimeStatusParticleVisual visual)
    {
        if (visual?.EffectObject == null || visual.Effect == null)
            return;

        visual.EffectObject.transform.localPosition = GetParticleLocalPosition(visual.Effect);
        if (visual.Renderers == null)
            visual.Renderers = visual.EffectObject.GetComponentsInChildren<ParticleSystemRenderer>(true);

        int sortingLayerId = sourceRenderer != null ? sourceRenderer.sortingLayerID : 0;
        int sortingOrder = sourceRenderer != null ? sourceRenderer.sortingOrder : 0;
        for (int i = 0; i < visual.Renderers.Length; i++)
        {
            ParticleSystemRenderer renderer = visual.Renderers[i];
            if (renderer == null)
                continue;

            renderer.sortingLayerID = sortingLayerId;
            renderer.sortingOrder = sortingOrder + visual.Effect.SortingOrderOffset;
        }
    }

    #endregion
}
