using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_AnimatorController
{
    #region 单图表现配置
    public enum VisualMode { Animator = 0, SingleSprite = 1 }

    [Header("视觉模式")]
    public VisualMode visualMode = VisualMode.Animator;
    public SpriteRenderer targetSpriteRenderer;
    public Transform spriteVisualRoot;
    [Tooltip("只填写不含刚体和碰撞体的附加视觉节点，附加节点只跟随位移。")]
    public Transform[] additionalVisualRoots = Array.Empty<Transform>();
    public Rigidbody2D targetRb;

    [Header("单图行走与奔跑")]
    [Min(0f)] public float moveDetectSpeed = 0.05f;
    [Min(0f)] public float walkSwingAngle = 8f;
    [Min(0f)] public float walkSwingSpeed = 12f;
    [Min(0f)] public float walkBobAmount = 0.04f;
    [Min(0.01f)] public float walkReturnSpeed = 12f;
    [Min(1f)] public float runSwingMultiplier = 1.2f;
    [Min(1f)] public float runSpeedMultiplier = 1.5f;
    [Range(0f, 0.05f)] public float idleBreathAmount = 0.008f;

    [Header("单图攻击撞击")]
    [Min(0f)] public float attackDashDistance = 0.22f;
    [Min(0.01f)] public float attackDashForwardTime = 0.06f;
    [Min(0.01f)] public float attackDashBackTime = 0.12f;
    public bool autoAttackDirection = true;
    public Vector2 attackDirection = Vector2.right;

    [Header("单图跳跃表现")]
    [Min(0f)] public float jumpHeight = 0.32f;
    [Min(0.05f)] public float jumpDuration = 0.42f;
    [Range(0f, 0.3f)] public float jumpSquash = 0.08f;

    public bool IsSingleSpriteMode => visualMode == VisualMode.SingleSprite;
    public bool IsAttackPlaying => IsSingleSpriteMode && _attackElapsed >= 0f;
    protected bool CanAdvancePresentation => IsRuntimeLoaded || _networkPresentationActive;
    #endregion

    #region 单图运行态
    private readonly List<SingleSpriteVisualLayer> _additionalLayers = new List<SingleSpriteVisualLayer>();
    private SingleSpriteVisualLayer _bodyLayer;
    private Mod_Mover _presentationMover;
    private Mod_FocusPoint _presentationFocus;
    private Mod_TurnBack _presentationTurn;
    private Mod_DamageReceiver _presentationHealth;
    private IActorAnimationMotionSource _presentationMotionSource;
    private Item _presentationOwner;
    private bool _sourcesCached;
    private Animator _suppressedAnimator;
    private bool _animatorWasEnabled;
    private bool _releasingPresentation;
    private bool _networkPresentationActive;
    private bool _networkIsMoving;
    private bool _networkIsRunning;
    private bool _requestedMoving;
    private bool _requestedRunning;
    private string _lastSingleSpriteState;
    private float _walkPhase;
    private float _walkBlend;
    private float _idlePhase;
    private float _attackElapsed = -1f;
    private Vector2 _attackWorldDirection;
    private float _jumpElapsed = -1f;
    private float _hitElapsed = -1f;
    private float _hitDuration;
    private float _hitMagnitude;
    private Vector3 _presentedWorldOffset;
    #endregion

    #region 动作与网络入口
    [Button("播放单图攻击")]
    public void PlayAttack(Vector2 worldDirection = default)
    {
        if (!CanStartSingleSpriteAction()) return;
        _attackWorldDirection = worldDirection.sqrMagnitude > 0.0001f
            ? worldDirection.normalized : ResolveAttackDirection();
        _attackElapsed = 0f;
        _lastSingleSpriteState = AnimationText.Attack;
    }

    [Button("播放单图跳跃")]
    public void PlayJump()
    {
        if (!CanStartSingleSpriteAction()) return;
        _jumpElapsed = 0f;
    }

    public void PlayHitReaction(float duration, float magnitude)
    {
        if (!CanStartSingleSpriteAction()) return;
        _hitDuration = Mathf.Max(0.01f, duration);
        _hitMagnitude = Mathf.Max(0f, magnitude);
        _hitElapsed = 0f;
    }

    public void SetNetworkPresentation(bool active, bool isMoving, bool isRunning = false)
    {
        _networkPresentationActive = active;
        _networkIsMoving = active && isMoving;
        _networkIsRunning = active && isMoving && isRunning;
        if (active)
        {
            enabled = true;
            if (IsSingleSpriteMode) PreparePresentation();
        }
    }

    // 独立测试与真实 LateUpdate 共用同一推进入口，不复制动作算法。
    public void AdvancePresentation(float deltaTime)
    {
        if (!isActiveAndEnabled)
        {
            // 停用期间拒绝外部推进，只归零自己的表现，避免在停用回调中改层级。
            ResetPresentation();
            if (_suppressedAnimator != null) _suppressedAnimator.enabled = _animatorWasEnabled;
            return;
        }
        if (!IsSingleSpriteMode || !CanAdvancePresentation)
        {
            ReleasePresentation();
            return;
        }
        if (!PreparePresentation()) return;
        if (_presentationHealth != null && _presentationHealth.Hp <= 0f)
        {
            ResetPresentation();
            return;
        }

        float step = Mathf.Max(0f, deltaTime);
        ActorAnimationMotion motion = ResolveAnimationMotion();
        bool moving = motion.IsMoving;
        bool running = moving && motion.IsRunning;
        bool fullBodyAction = _attackElapsed >= 0f || _jumpElapsed >= 0f;
        _walkBlend = Mathf.MoveTowards(_walkBlend, moving && !fullBodyAction ? 1f : 0f,
            step * Mathf.Max(0.01f, walkReturnSpeed));
        if (moving)
            _walkPhase = Mathf.Repeat(_walkPhase + step * (running ? runSpeedMultiplier : 1f), 1000f);
        else if (_walkBlend <= 0f)
            _walkPhase = 0f;
        _idlePhase = Mathf.Repeat(_idlePhase + step * 2f, Mathf.PI * 2f);

        float swing = Mathf.Sin(_walkPhase * Mathf.Max(0f, walkSwingSpeed));
        float walkStrength = _walkBlend * (running ? runSwingMultiplier : 1f);
        float angle = motion.RollDegrees + swing * walkSwingAngle * walkStrength;
        Vector3 worldOffset = motion.WorldOffset + Vector3.up * (Mathf.Abs(swing) * walkBobAmount * walkStrength);
        float breath = !moving && !fullBodyAction ? Mathf.Sin(_idlePhase) * idleBreathAmount : 0f;
        Vector3 scale = new Vector3(1f - breath * 0.5f, 1f + breath, 1f);

        SampleAttack(step, ref worldOffset, ref angle);
        SampleJump(step, ref worldOffset, ref scale);
        SampleHit(step, ref worldOffset);
        _bodyLayer.ResetPose();
        Vector3 pivot = ResolveVisualFoot();
        _bodyLayer.Apply(worldOffset, angle, scale, pivot);
        for (int i = 0; i < _additionalLayers.Count; i++)
            _additionalLayers[i].Apply(worldOffset, 0f, Vector3.one, Vector3.zero);
        _presentedWorldOffset = worldOffset;
    }

    [Button("复位单图动作")]
    public void ResetPresentation()
    {
        _attackElapsed = _jumpElapsed = _hitElapsed = -1f;
        _walkPhase = _walkBlend = _idlePhase = 0f;
        _requestedMoving = _requestedRunning = false;
        _lastSingleSpriteState = null;
        _presentedWorldOffset = Vector3.zero;
        _bodyLayer?.ResetPose();
        for (int i = 0; i < _additionalLayers.Count; i++)
            _additionalLayers[i].ResetPose();
    }
    #endregion

    #region 生命周期与视觉分层
    protected virtual void OnEnable()
    {
        if (IsSingleSpriteMode && CanAdvancePresentation)
            PreparePresentation();
    }

    protected virtual void LateUpdate()
    {
        if (IsSingleSpriteMode || _bodyLayer != null)
            AdvancePresentation(Time.deltaTime);
    }

    protected virtual void OnDisable()
    {
        // 父物体停用期间只撤销姿态，正式卸载再恢复层级，避免在激活回调中改父级。
        if (gameObject.activeInHierarchy)
            ReleasePresentation();
        else
        {
            ResetPresentation();
            if (_suppressedAnimator != null) _suppressedAnimator.enabled = _animatorWasEnabled;
        }
        ClearPresentationSources();
    }

    protected virtual void OnDestroy() => ReleasePresentation();

    private void InitializePresentation()
    {
        ReleasePresentation();
        ClearPresentationSources();
        CachePresentationSources();
        if (IsSingleSpriteMode) PreparePresentation();
    }

    private void CachePresentationSources()
    {
        _presentationOwner = item;
        _sourcesCached = true;
        _presentationMover = item?.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
        _presentationFocus = item?.itemMods?.GetMod_ByID<Mod_FocusPoint>(ModText.FocusPoint);
        _presentationTurn = item?.itemMods?.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody);
        _presentationHealth = item?.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        _presentationMotionSource = null;
        if (item?.itemMods != null)
            foreach (Module module in item.itemMods.Mods.Values)
                if (module is IActorAnimationMotionSource source)
                {
                    _presentationMotionSource = source;
                    break;
                }
    }

    private void ClearPresentationSources()
    {
        _networkPresentationActive = _networkIsMoving = _networkIsRunning = false;
        _sourcesCached = false;
        _presentationOwner = null;
        _presentationMover = null;
        _presentationFocus = null;
        _presentationTurn = null;
        _presentationHealth = null;
        _presentationMotionSource = null;
    }

    private bool CanStartSingleSpriteAction() =>
        isActiveAndEnabled && IsSingleSpriteMode && CanAdvancePresentation && PreparePresentation();

    private bool PreparePresentation()
    {
        if (!isActiveAndEnabled || !IsSingleSpriteMode) return false;
        if (!_sourcesCached || _presentationOwner != item) CachePresentationSources();
        if (_bodyLayer != null && (!_bodyLayer.IsBound || _bodyLayer.Root != spriteVisualRoot))
            ReleasePresentation();
        if (_bodyLayer != null)
        {
            if (_suppressedAnimator != null) _suppressedAnimator.enabled = false;
            return true;
        }

        if (targetSpriteRenderer == null)
            targetSpriteRenderer = item != null && item.Sprite != null ? item.Sprite : GetComponent<SpriteRenderer>();
        if (spriteVisualRoot == null && targetSpriteRenderer != null)
            spriteVisualRoot = targetSpriteRenderer.transform;
        if (spriteVisualRoot == null || targetSpriteRenderer == null ||
            (targetSpriteRenderer.transform != spriteVisualRoot && !targetSpriteRenderer.transform.IsChildOf(spriteVisualRoot)))
            return false;
        if (!IsVisualOnlyRoot(spriteVisualRoot)) return false;

        // 只暂停当前角色明确绑定的 Animator，武器与子物品动画保持自己的时序。
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = targetSpriteRenderer.GetComponent<Animator>();
        _suppressedAnimator = animator;
        if (_suppressedAnimator != null)
        {
            _animatorWasEnabled = _suppressedAnimator.enabled;
            SampleInitialSingleSpriteFrame();
            _suppressedAnimator.enabled = false;
        }
        _bodyLayer = new SingleSpriteVisualLayer(spriteVisualRoot);
        if (additionalVisualRoots != null)
        {
            for (int i = 0; i < additionalVisualRoots.Length; i++)
            {
                Transform extra = additionalVisualRoots[i];
                if (extra == null || extra == spriteVisualRoot || extra.IsChildOf(spriteVisualRoot) ||
                    spriteVisualRoot.IsChildOf(extra) || !IsVisualOnlyRoot(extra)) continue;
                bool overlaps = false;
                for (int j = 0; j < _additionalLayers.Count; j++)
                {
                    Transform other = _additionalLayers[j].Root;
                    if (extra == other || extra.IsChildOf(other) || other.IsChildOf(extra)) { overlaps = true; break; }
                }
                if (!overlaps) _additionalLayers.Add(new SingleSpriteVisualLayer(extra));
            }
        }
        return true;
    }

    private void SampleInitialSingleSpriteFrame()
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || item?.itemData == null ||
            !resources.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            !definition.IsActor || string.IsNullOrWhiteSpace(definition.Visual?.AnimationState) ||
            !_suppressedAnimator.gameObject.activeInHierarchy || _suppressedAnimator.runtimeAnimatorController == null)
            return;

        // 外壳可被多物种共用，激活后只采正式初态图片一次，再保留 JSON 与朝向模块的姿态。
        Transform visual = targetSpriteRenderer.transform;
        Vector3 position = visual.localPosition;
        Quaternion rotation = visual.localRotation;
        Vector3 scale = visual.localScale;
        Color color = targetSpriteRenderer.color;
        bool flipX = targetSpriteRenderer.flipX, flipY = targetSpriteRenderer.flipY;
        _suppressedAnimator.enabled = true;
        try
        {
            if (!_suppressedAnimator.isInitialized) _suppressedAnimator.Rebind();
            RefreshControllerCache();
            if (!TryResolveState(definition.Visual.AnimationState, 0, out int layer, out int stateHash))
                throw new InvalidOperationException($"生物 {definition.Id} 无法采样初始动画 {definition.Visual.AnimationState}。");
            _suppressedAnimator.Play(stateHash, layer, 0f);
            _suppressedAnimator.Update(0f);
        }
        finally
        {
            visual.localPosition = position;
            visual.localRotation = rotation;
            visual.localScale = scale;
            targetSpriteRenderer.color = color;
            targetSpriteRenderer.flipX = flipX;
            targetSpriteRenderer.flipY = flipY;
            _suppressedAnimator.enabled = _animatorWasEnabled;
        }
    }

    private bool IsVisualOnlyRoot(Transform root)
    {
        if ((item != null && (root == item.transform || !root.IsChildOf(item.transform))) ||
            root.GetComponentInChildren<Collider2D>(true) != null ||
            root.GetComponentInChildren<Rigidbody2D>(true) != null)
        {
            WarnOnce($"singleSpriteRoot:{root.GetInstanceID()}",
                $"[{nameof(Mod_AnimatorController)}] 单图动作只能使用角色内部不含刚体或碰撞体的视觉节点。", this);
            return false;
        }
        return true;
    }

    private void ReleasePresentation()
    {
        if (_releasingPresentation) return;
        _releasingPresentation = true;
        try
        {
            ResolveLocomotion(out bool moving, out bool running);
            ResetPresentation();
            for (int i = _additionalLayers.Count - 1; i >= 0; i--)
                _additionalLayers[i].Dispose();
            _additionalLayers.Clear();
            _bodyLayer?.Dispose();
            _bodyLayer = null;
            if (_suppressedAnimator != null)
            {
                _suppressedAnimator.enabled = _animatorWasEnabled;
                if (!IsSingleSpriteMode && _suppressedAnimator == animator && animator.runtimeAnimatorController != null)
                {
                    // 切回序列帧时同步当前迈步状态，不等待下一次移动起止事件。
                    RefreshControllerCache();
                    if (_boolParameterHashes.TryGetValue(AnimationText.Move, out int moveHash)) animator.SetBool(moveHash, moving);
                    if (_boolParameterHashes.TryGetValue(AnimationText.Run, out int runHash)) animator.SetBool(runHash, running);
                }
            }
            _suppressedAnimator = null;
        }
        finally { _releasingPresentation = false; }
    }

    private Vector3 ResolveVisualFoot()
    {
        Sprite sprite = targetSpriteRenderer != null ? targetSpriteRenderer.sprite : null;
        if (sprite == null) return spriteVisualRoot.localPosition;
        Vector3 foot = targetSpriteRenderer.transform.TransformPoint(
            new Vector3(sprite.bounds.center.x, sprite.bounds.min.y, 0f));
        return _bodyLayer.PoseRoot.InverseTransformPoint(foot);
    }

    public bool TryGetVisualGroundOffset(Transform visual, out Vector3 worldOffset)
    {
        worldOffset = Vector3.zero;
        if (_bodyLayer == null || !_bodyLayer.IsBound || visual == null) return false;
        if (visual == _bodyLayer.Root || visual.IsChildOf(_bodyLayer.Root))
        {
            worldOffset = _presentedWorldOffset;
            return true;
        }
        for (int i = 0; i < _additionalLayers.Count; i++)
        {
            Transform root = _additionalLayers[i].Root;
            if (root != null && (visual == root || visual.IsChildOf(root)))
            {
                worldOffset = _presentedWorldOffset;
                return true;
            }
        }
        return false;
    }

    public bool TryGetBaseVisualMatrix(Transform visual, out Matrix4x4 localToWorld)
    {
        localToWorld = default;
        if (visual == null) return false;
        SingleSpriteVisualLayer layer = _bodyLayer;
        if (layer == null || !layer.IsBound || (visual != layer.Root && !visual.IsChildOf(layer.Root)))
        {
            layer = null;
            for (int i = 0; i < _additionalLayers.Count; i++)
            {
                SingleSpriteVisualLayer candidate = _additionalLayers[i];
                if (candidate.IsBound && (visual == candidate.Root || visual.IsChildOf(candidate.Root)))
                {
                    layer = candidate;
                    break;
                }
            }
        }
        if (layer == null) return false;
        // 需要贴图几何的物理系统读取基础矩阵，程序摇摆与前探不会改变命中范围。
        Matrix4x4 parentMatrix = layer.PoseRoot.parent != null ? layer.PoseRoot.parent.localToWorldMatrix : Matrix4x4.identity;
        localToWorld = parentMatrix * layer.PoseRoot.worldToLocalMatrix * visual.localToWorldMatrix;
        return true;
    }
    #endregion

    #region 动作信号与采样
    private void ResolveLocomotion(out bool moving, out bool running)
    {
        ActorAnimationMotion motion = ResolveAnimationMotion();
        moving = motion.IsMoving;
        running = moving && motion.IsRunning;
    }

    private ActorAnimationMotion ResolveAnimationMotion()
    {
        if (_networkPresentationActive)
            return new ActorAnimationMotion { IsMoving = _networkIsMoving, IsRunning = _networkIsRunning };
        bool sourceAlive = _presentationMotionSource != null &&
            !(_presentationMotionSource is UnityEngine.Object unitySource && unitySource == null);
        if (sourceAlive && _presentationMotionSource is Module sourceModule)
            sourceAlive = sourceModule.IsRuntimeLoaded && sourceModule.Enabled && sourceModule.isActiveAndEnabled;
        if (sourceAlive && _presentationMotionSource.TryGetAnimationMotion(out ActorAnimationMotion motion))
            return motion;
        bool moving = _presentationMover is Mod_Mover_AI aiMover ? aiMover.IsActuallyMoving :
            _presentationMover != null ? _presentationMover.IsMoving : _requestedMoving;
        return new ActorAnimationMotion
        {
            IsMoving = moving,
            IsRunning = moving && (_presentationMover != null ? _presentationMover.IsRunning : _requestedRunning)
        };
    }

    private Vector2 ResolveAttackDirection()
    {
        if (!autoAttackDirection && attackDirection.sqrMagnitude > 0.0001f) return attackDirection.normalized;
        if (_presentationFocus != null && item != null)
        {
            Vector2 aim = WorldTopologyRuntime.ShortestDelta(item.transform.position, _presentationFocus.Data.See_Point);
            if (aim.sqrMagnitude > 0.0001f) return aim.normalized;
        }
        if (_presentationMover != null && _presentationMover.DrivenVelocity.sqrMagnitude > 0.0001f)
            return _presentationMover.DrivenVelocity.normalized;
        return _presentationTurn != null && _presentationTurn.currentDirection.x < 0f ? Vector2.left : Vector2.right;
    }

    private bool SetSingleSpriteBool(string parameterName, bool value)
    {
        if (parameterName == AnimationText.Move) { _requestedMoving = value; return true; }
        if (parameterName == AnimationText.Run) { _requestedRunning = value; return true; }
        if (parameterName == AnimationText.Attack) return true;
        return false;
    }

    private bool TryPlaySingleSpriteAnimation(string stateName, bool forceRestart)
    {
        if (string.IsNullOrWhiteSpace(stateName)) return false;
        string state = stateName.Trim();
        int separator = state.LastIndexOf('.');
        if (separator >= 0) state = state.Substring(separator + 1);
        if (!forceRestart && string.Equals(state, _lastSingleSpriteState, StringComparison.OrdinalIgnoreCase)) return true;
        switch (state.ToLowerInvariant())
        {
            case "move": case "walk": _requestedMoving = true; _requestedRunning = false; break;
            case "run": _requestedMoving = _requestedRunning = true; break;
            case "idle": case "stand": case "ground": _requestedMoving = _requestedRunning = false; break;
            // 状态机进入 Attack 仍可能处于前摇，实际攻击窗口显式调用 PlayAttack。
            case "attack": _requestedMoving = false; break;
            case "jump": PlayJump(); break;
            default: return false;
        }
        _lastSingleSpriteState = state;
        return true;
    }

    private void SampleAttack(float step, ref Vector3 offset, ref float angle)
    {
        if (_attackElapsed < 0f) return;
        _attackElapsed += step;
        float forward = Mathf.Max(0.01f, attackDashForwardTime);
        float back = Mathf.Max(0.01f, attackDashBackTime);
        if (_attackElapsed >= forward + back) { _attackElapsed = -1f; return; }
        float amount = _attackElapsed <= forward
            ? Mathf.SmoothStep(0f, 1f, _attackElapsed / forward)
            : Mathf.SmoothStep(1f, 0f, (_attackElapsed - forward) / back);
        offset += (Vector3)(_attackWorldDirection * (Mathf.Max(0f, attackDashDistance) * amount));
        angle -= _attackWorldDirection.x * 4f * amount;
    }

    private void SampleJump(float step, ref Vector3 offset, ref Vector3 scale)
    {
        if (_jumpElapsed < 0f) return;
        _jumpElapsed += step;
        float progress = _jumpElapsed / Mathf.Max(0.05f, jumpDuration);
        if (progress >= 1f) { _jumpElapsed = -1f; return; }
        float squash;
        if (progress < 0.12f)
            squash = Mathf.Sin(progress / 0.12f * Mathf.PI) * jumpSquash;
        else if (progress < 0.82f)
        {
            float flight = Mathf.Sin((progress - 0.12f) / 0.7f * Mathf.PI);
            offset.y += Mathf.Max(0f, jumpHeight) * flight;
            squash = -flight * jumpSquash * 0.35f;
        }
        else
            squash = Mathf.Sin((progress - 0.82f) / 0.18f * Mathf.PI) * jumpSquash;
        scale.x *= 1f + squash * 0.5f;
        scale.y *= 1f - squash;
    }

    private void SampleHit(float step, ref Vector3 offset)
    {
        if (_hitElapsed < 0f) return;
        _hitElapsed += step;
        float progress = _hitElapsed / _hitDuration;
        if (progress >= 1f) { _hitElapsed = -1f; return; }
        float strength = _hitMagnitude * (1f - progress);
        offset += new Vector3(Mathf.Sin(progress * Mathf.PI * 8f),
            Mathf.Sin(progress * Mathf.PI * 12f), 0f) * strength;
    }
    #endregion

    #region 独立视觉变换层
    private sealed class SingleSpriteVisualLayer : IDisposable
    {
        public readonly Transform Root;
        public readonly Transform PoseRoot;
        public bool IsBound => Root != null && PoseRoot != null && Root.parent == PoseRoot;
        private readonly Transform originalParent;
        private readonly int originalSiblingIndex;

        public SingleSpriteVisualLayer(Transform root)
        {
            Root = root;
            originalParent = root.parent;
            originalSiblingIndex = root.GetSiblingIndex();
            PoseRoot = new GameObject($"{root.name}_SingleSpritePose").transform;
            PoseRoot.SetParent(originalParent, false);
            PoseRoot.SetSiblingIndex(originalSiblingIndex);
            root.SetParent(PoseRoot, false);
        }

        public void ResetPose()
        {
            if (PoseRoot == null) return;
            PoseRoot.localPosition = Vector3.zero;
            PoseRoot.localRotation = Quaternion.identity;
            PoseRoot.localScale = Vector3.one;
        }

        public void Apply(Vector3 worldOffset, float angle, Vector3 scale, Vector3 pivot)
        {
            if (PoseRoot == null) return;
            Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.forward);
            Vector3 translation = PoseRoot.parent != null
                ? PoseRoot.parent.InverseTransformVector(worldOffset) : worldOffset;
            PoseRoot.localPosition = translation + pivot - rotation * Vector3.Scale(pivot, scale);
            PoseRoot.localRotation = rotation;
            PoseRoot.localScale = scale;
        }

        public void Dispose()
        {
            if (PoseRoot == null) return;
            // 先恢复原层级再移出临时节点，回池校验当帧即可看到原始结构。
            if (Root != null && Root.parent == PoseRoot)
            {
                Root.SetParent(originalParent, false);
                Root.SetSiblingIndex(originalSiblingIndex);
            }
            PoseRoot.SetParent(null, false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(PoseRoot.gameObject);
            else UnityEngine.Object.DestroyImmediate(PoseRoot.gameObject);
        }
    }
    #endregion
}
