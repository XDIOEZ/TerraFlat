using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_AnimatorController
{
    #region 单图表现配置
    public enum VisualMode { Animator = 0, SingleSprite = 1 }

    [Serializable]
    public sealed class SingleSpriteState
    {
        public string stateName;
        public Sprite sprite;
    }

    [Header("视觉模式")]
    public VisualMode visualMode = VisualMode.Animator;
    public SpriteRenderer targetSpriteRenderer;
    public Transform spriteVisualRoot;
    [Tooltip("只填写不含刚体和碰撞体的附加视觉节点，附加节点只跟随位移。")]
    public Transform[] additionalVisualRoots = Array.Empty<Transform>();
    public Rigidbody2D targetRb;

    [Header("单图状态贴图")]
    [Tooltip("按状态名切换身体精灵图，摇摆动作仍由本模块负责。")]
    public SingleSpriteState[] singleSpriteStates = Array.Empty<SingleSpriteState>();

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

    [Header("单图死亡翻身")]
    [Min(0.05f)] public float deathDuration = 0.76f;
    [Min(0f)] public float deathJumpHeight = 0.35f;

    public bool IsSingleSpriteMode => visualMode == VisualMode.SingleSprite;
    public bool IsAttackPlaying => IsSingleSpriteMode && _attackElapsed >= 0f;
    public bool IsDeadPresentation => IsSingleSpriteMode && _deathElapsed >= 0f;
    public bool IsDeathPlaying => IsDeadPresentation && _deathElapsed < Mathf.Max(0.05f, deathDuration);
    protected bool CanAdvancePresentation => IsRuntimeLoaded || _networkPresentationActive;
    public event Action PresentationUpdated;
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
    private SpriteRenderer _overriddenSpriteRenderer;
    private Sprite _originalSprite;
    private readonly List<IVisualGroundOffset> _externalGroundOffsetSources = new List<IVisualGroundOffset>();
    private float _deathElapsed = -1f;
    private bool _deathFromHealth;
    private Sprite _deathSprite;
    private bool _deathFlipX, _deathFlipY;
    private Vector3 _deathBodyPosition, _deathBodyScale;
    private Quaternion _deathBodyRotation;
    private Vector3 _deathGroundCenterLocal, _deathStartCenterOffset;
    private float _deathGroundBottomOffset, _deathStartBottomOffset;
    private float _deathStartAngle, _deathEndAngle;
    private Vector3 _deathStartScale;
    #endregion

    #region 动作与网络入口
    // 只替换身体图片，保留朝向、材质和摇摆姿态，也支持来自不同图集的 Sprite。
    public bool TrySetSprite(Sprite sprite)
    {
        if (sprite == null || !CanStartSingleSpriteAction()) return false;
        if (_overriddenSpriteRenderer != targetSpriteRenderer)
        {
            RestoreSingleSprite();
            _overriddenSpriteRenderer = targetSpriteRenderer;
            _originalSprite = targetSpriteRenderer.sprite;
        }
        targetSpriteRenderer.sprite = sprite;
        _lastSingleSpriteState = null;
        return true;
    }

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

    [Button("播放单图死亡")]
    public bool PlayDeath()
    {
        if (IsDeadPresentation) return true;
        if (!isActiveAndEnabled || !IsSingleSpriteMode || !CanAdvancePresentation || !PreparePresentation())
            return false;

        // 致命受击立即冻结图片和当前姿态，后续死亡回调不能把飞行高度或攻击动作抢走。
        _deathSprite = targetSpriteRenderer.sprite;
        _deathFlipX = targetSpriteRenderer.flipX;
        _deathFlipY = targetSpriteRenderer.flipY;
        _deathBodyPosition = spriteVisualRoot.localPosition;
        _deathBodyRotation = spriteVisualRoot.localRotation;
        _deathBodyScale = spriteVisualRoot.localScale;
        Bounds startBounds = targetSpriteRenderer.bounds;
        _deathStartAngle = Mathf.DeltaAngle(0f, _bodyLayer.PoseRoot.localEulerAngles.z);
        _deathStartScale = _bodyLayer.PoseRoot.localScale;
        Vector3 facing = targetSpriteRenderer.transform.TransformVector(Vector3.right) * (_deathFlipX ? -1f : 1f);
        _deathEndAngle = facing.x < 0f ? -180f : 180f;
        Vector3 externalOffset = ResolveExternalGroundOffset();
        _bodyLayer.ResetPose();
        Bounds neutralBounds = targetSpriteRenderer.bounds;
        Vector3 groundCenter = neutralBounds.center - externalOffset;
        _deathGroundCenterLocal = item != null ? item.transform.InverseTransformPoint(groundCenter) : groundCenter;
        _deathGroundBottomOffset = neutralBounds.min.y - neutralBounds.center.y;
        _deathStartCenterOffset = startBounds.center - groundCenter;
        _deathStartBottomOffset = startBounds.min.y - (groundCenter.y + _deathGroundBottomOffset);
        _deathFromHealth = _presentationHealth != null && _presentationHealth.Hp <= 0f;
        _deathElapsed = 0f;
        _attackElapsed = _jumpElapsed = _hitElapsed = -1f;
        _walkPhase = _walkBlend = _idlePhase = 0f;
        _requestedMoving = _requestedRunning = false;
        _lastSingleSpriteState = "Death";
        SampleDeath(0f);
        return true;
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
            PlayDeath();
            _deathFromHealth = true;
        }
        else if (_deathFromHealth)
            ResetPresentation();
        if (IsDeadPresentation)
        {
            SampleDeath(Mathf.Max(0f, deltaTime));
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
        PresentationUpdated?.Invoke();
    }

    [Button("复位单图动作")]
    public void ResetPresentation()
    {
        if (_deathElapsed >= 0f) RestoreDeathBody();
        _deathElapsed = -1f;
        _deathFromHealth = false;
        _deathSprite = null;
        RestoreSingleSprite();
        _attackElapsed = _jumpElapsed = _hitElapsed = -1f;
        _walkPhase = _walkBlend = _idlePhase = 0f;
        _requestedMoving = _requestedRunning = false;
        _lastSingleSpriteState = null;
        _presentedWorldOffset = Vector3.zero;
        _bodyLayer?.ResetPose();
        for (int i = 0; i < _additionalLayers.Count; i++)
            _additionalLayers[i].ResetPose();
        PresentationUpdated?.Invoke();
    }
    #endregion

    #region 生命周期与视觉分层
    protected virtual void OnEnable()
    {
        if (IsSingleSpriteMode && CanAdvancePresentation)
            PreparePresentation();
        else if (!IsSingleSpriteMode)
            FlushReadyAnimatorParameters();
    }

    protected virtual void LateUpdate()
    {
        // 激活首帧 Animator 仍未初始化时，继续履行 Item 已登记的初始状态播放。
        if (CanAdvancePresentation && item?.HasPendingInitialAnimatorState == true)
            item.FlushPendingInitialAnimatorState();
        if (IsSingleSpriteMode || _bodyLayer != null)
            AdvancePresentation(Time.deltaTime);
        else
            FlushReadyAnimatorParameters();
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
        if (_presentationHealth != null) _presentationHealth.DeathStarted -= HandlePresentationDeath;
        _presentationOwner = item;
        _sourcesCached = true;
        _presentationMover = item?.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
        _presentationFocus = item?.itemMods?.GetMod_ByID<Mod_FocusPoint>(ModText.FocusPoint);
        _presentationTurn = item?.itemMods?.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody);
        _presentationHealth = item?.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (_presentationHealth != null) _presentationHealth.DeathStarted += HandlePresentationDeath;
        _presentationMotionSource = null;
        _externalGroundOffsetSources.Clear();
        if (item?.itemMods != null)
            foreach (Module module in item.itemMods.Mods.Values)
            {
                if (_presentationMotionSource == null && module is IActorAnimationMotionSource source)
                    _presentationMotionSource = source;
                if (module != this && module is IVisualGroundOffset offsetSource)
                    _externalGroundOffsetSources.Add(offsetSource);
            }
    }

    public void BindModuleDependencies(ItemMods modules) => CachePresentationSources();

    private void HandlePresentationDeath(Mod_DamageReceiver receiver) => PlayDeath();

    private void ClearPresentationSources()
    {
        if (_presentationHealth != null) _presentationHealth.DeathStarted -= HandlePresentationDeath;
        _networkPresentationActive = _networkIsMoving = _networkIsRunning = false;
        _sourcesCached = false;
        _presentationOwner = null;
        _presentationMover = null;
        _presentationFocus = null;
        _presentationTurn = null;
        _presentationHealth = null;
        _presentationMotionSource = null;
        _externalGroundOffsetSources.Clear();
    }

    private bool CanStartSingleSpriteAction() =>
        isActiveAndEnabled && IsSingleSpriteMode && !IsDeadPresentation &&
        CanAdvancePresentation && PreparePresentation() && (_presentationHealth == null || _presentationHealth.Hp > 0f);

    private bool PreparePresentation()
    {
        if (!isActiveAndEnabled || !IsSingleSpriteMode) return false;
        // 单图接管后不再消费旧 Animator 状态，切回时由当前运动源重建。
        _pendingBoolValues.Clear();
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
            _suppressedAnimator.enabled = false;
        }
        ApplyInitialSingleSpriteFrame();
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

    private void ApplyInitialSingleSpriteFrame()
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || item?.itemData == null ||
            !resources.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            !definition.IsActor)
            return;

        // 单图从正式定义取得静态首帧，不启动旧 Animator，也不借用共享外壳的其他物种图片。
        if (definition.Sprite == null)
            throw new InvalidOperationException($"生物 {definition.Id} 的摇摆动画缺少初始图片，请配置 visual.spriteAddress。");
        targetSpriteRenderer.sprite = definition.Sprite;
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
            bool restoreHierarchy = item?.IsBeingDestroyed != true;
            for (int i = _additionalLayers.Count - 1; i >= 0; i--)
                _additionalLayers[i].Dispose(restoreHierarchy);
            _additionalLayers.Clear();
            _bodyLayer?.Dispose(restoreHierarchy);
            _bodyLayer = null;
            // 宿主正在销毁时不能重启原 Animator，否则会在清理期间重新初始化动画图。
            if (_suppressedAnimator != null && restoreHierarchy)
            {
                _suppressedAnimator.enabled = _animatorWasEnabled;
                if (!IsSingleSpriteMode && _suppressedAnimator == animator)
                {
                    // 切回序列帧时同步当前迈步状态，不等待下一次移动起止事件。
                    SetBool(AnimationText.Move, moving);
                    SetBool(AnimationText.Run, running);
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

    /// <summary>把基础挂点映射到当前身体姿态，只提供位置，不接管附属物的旋转和缩放。</summary>
    public Vector3 GetPresentedAttachmentPosition(Transform attachment)
    {
        Vector3 position = attachment.position;
        if (!isActiveAndEnabled || !IsSingleSpriteMode || _bodyLayer == null || !_bodyLayer.IsBound)
            return position;
        Transform pose = _bodyLayer.PoseRoot;
        if (attachment == pose || attachment.IsChildOf(pose)) return position;
        Vector3 localPoint = pose.parent != null ? pose.parent.InverseTransformPoint(position) : position;
        return pose.TransformPoint(localPoint);
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
    /// <summary>位置突变时只清行走过渡，不撤销攻击、受击或死亡表现。</summary>
    public void ResetLocomotion()
    {
        _walkPhase = _walkBlend = 0f;
        SetBool(AnimationText.Move, false);
    }

    private void RestoreSingleSprite()
    {
        if (_overriddenSpriteRenderer != null)
            _overriddenSpriteRenderer.sprite = _originalSprite;
        _overriddenSpriteRenderer = null;
        _originalSprite = null;
    }

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
        if (IsDeadPresentation) return parameterName == AnimationText.Move ||
            parameterName == AnimationText.Run || parameterName == AnimationText.Attack;
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
        if (string.Equals(state, "Death", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "Dead", StringComparison.OrdinalIgnoreCase)) return PlayDeath();
        if (!CanStartSingleSpriteAction()) return false;
        if (!forceRestart && string.Equals(state, _lastSingleSpriteState, StringComparison.OrdinalIgnoreCase)) return true;
        Sprite stateSprite = null;
        if (singleSpriteStates != null)
            for (int i = 0; i < singleSpriteStates.Length; i++)
            {
                SingleSpriteState entry = singleSpriteStates[i];
                if (entry != null && string.Equals(entry.stateName, state, StringComparison.OrdinalIgnoreCase))
                {
                    if (entry.sprite == null) return false;
                    stateSprite = entry.sprite;
                    break;
                }
            }
        switch (state.ToLowerInvariant())
        {
            case "move": case "walk": _requestedMoving = true; _requestedRunning = false; break;
            case "run": _requestedMoving = _requestedRunning = true; break;
            case "idle": case "stand": case "ground": _requestedMoving = _requestedRunning = false; break;
            // 状态机进入 Attack 仍可能处于前摇，实际攻击窗口显式调用 PlayAttack。
            case "attack": _requestedMoving = false; break;
            case "jump": PlayJump(); break;
            default: if (stateSprite == null) return false; break;
        }
        if (stateSprite != null) TrySetSprite(stateSprite);
        else RestoreSingleSprite();
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

    #region 死亡冻结与落地采样
    private Vector3 ResolveExternalGroundOffset()
    {
        Vector3 offset = Vector3.zero;
        for (int i = 0; i < _externalGroundOffsetSources.Count; i++)
        {
            IVisualGroundOffset source = _externalGroundOffsetSources[i];
            if (source is UnityEngine.Object unitySource && unitySource == null) continue;
            if (source.TryGetVisualGroundOffset(targetSpriteRenderer.transform, out Vector3 contribution))
                offset += contribution;
        }
        return offset;
    }

    private void RestoreDeathBody()
    {
        if (spriteVisualRoot != null)
        {
            spriteVisualRoot.localPosition = _deathBodyPosition;
            spriteVisualRoot.localRotation = _deathBodyRotation;
            spriteVisualRoot.localScale = _deathBodyScale;
        }
        if (targetSpriteRenderer == null) return;
        targetSpriteRenderer.sprite = _deathSprite;
        targetSpriteRenderer.flipX = _deathFlipX;
        targetSpriteRenderer.flipY = _deathFlipY;
    }

    private void SampleDeath(float step)
    {
        _deathElapsed = Mathf.Min(_deathElapsed + step, Mathf.Max(0.05f, deathDuration));
        float progress = Mathf.Clamp01(_deathElapsed / Mathf.Max(0.05f, deathDuration));
        float turn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.78f));
        float settle = 1f - Mathf.SmoothStep(0f, 1f, progress);
        float height = _deathStartBottomOffset * settle + Mathf.Max(0f, deathJumpHeight) * 4f * progress * (1f - progress);
        Vector3 groundCenter = item != null ? item.transform.TransformPoint(_deathGroundCenterLocal) : _deathGroundCenterLocal;

        RestoreDeathBody();
        _bodyLayer.ResetPose();
        Vector3 center = targetSpriteRenderer.sprite != null
            ? targetSpriteRenderer.transform.TransformPoint(targetSpriteRenderer.sprite.bounds.center)
            : targetSpriteRenderer.transform.position;
        Vector3 pivot = _bodyLayer.PoseRoot.InverseTransformPoint(center);
        float angle = Mathf.Lerp(_deathStartAngle, _deathEndAngle, turn);
        Vector3 scale = Vector3.Lerp(_deathStartScale, Vector3.one, turn);
        _bodyLayer.Apply(Vector3.zero, angle, scale, pivot);
        Bounds rotated = targetSpriteRenderer.bounds;
        Vector3 offset = new Vector3(groundCenter.x + _deathStartCenterOffset.x * settle - rotated.center.x,
            groundCenter.y + _deathGroundBottomOffset + height - rotated.min.y,
            groundCenter.z + _deathStartCenterOffset.z * settle - rotated.center.z);
        _bodyLayer.Apply(offset, angle, scale, pivot);
        Vector3 groundOffset = new Vector3(_deathStartCenterOffset.x * settle, height,
            _deathStartCenterOffset.z * settle) - ResolveExternalGroundOffset();
        for (int i = 0; i < _additionalLayers.Count; i++)
            _additionalLayers[i].Apply(groundOffset, 0f, Vector3.one, Vector3.zero);
        _presentedWorldOffset = groundOffset;
        PresentationUpdated?.Invoke();
    }
    #endregion

    #region 独立视觉变换层
    private sealed class SingleSpriteVisualLayer
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

        public void Dispose(bool restoreHierarchy)
        {
            if (PoseRoot == null) return;
            // 宿主销毁时让视觉层随原层级销毁，正常卸载与回池仍完整恢复父级。
            if (!restoreHierarchy) return;
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
