using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public partial class Mod_AnimatorController : Module, ITrunDirection, IVisualGroundOffset, IItemModuleDependencyBinder
{
    #region 模块身份
    // 模板读取早于 Awake，能力 ID 必须在装配前确定。
    public override string CanonicalModuleId => ModText.AnimatorReceiver;
    #endregion

    #region 配置与状态
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }

    public Animator animator;
    private readonly Dictionary<string, int> _boolParameterHashes = new Dictionary<string, int>();
    private readonly Dictionary<string, bool> _pendingBoolValues = new Dictionary<string, bool>();
    private readonly HashSet<string> _loggedWarnings = new HashSet<string>();
    private int _cachedControllerId;
    private int _cachedAnimatorId;
    #endregion

    #region 模块生命周期
    protected override void OnLoad()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        // 分帧生成时物品仍处于停用状态，Animator 的参数表必须等正式激活后才能读取。
        InvalidateControllerCache();
        InitializePresentation();
    }

    protected override void OnSave()
    {
    }
    public override void ModUpdate(float deltaTime)
    {

    }

    protected override void OnUnload()
    {
        ReleasePresentation();
        ClearPresentationSources();
        _pendingBoolValues.Clear();
        InvalidateControllerCache();
        _loggedWarnings.Clear();
    }

    public override void OnResourcesReloaded()
    {
        base.OnResourcesReloaded();
        InvalidateControllerCache();
        ReleasePresentation();
        CachePresentationSources();
        if (CanAdvancePresentation)
            PreparePresentation();
    }
    #endregion

    #region 动画入口
    [Button]
    public bool PlayAnimation(string animationName)
    {
        return TryPlayAnimation(animationName);
    }

    [Button]
    public bool ForcePlayAnimation(string animationName, int layer = 0)
    {
        return TryPlayAnimation(animationName, true, layer);
    }

    public bool TryPlayAnimation(
        string animationName,
        bool forceRestart = false,
        int preferredLayer = -1,
        float normalizedTime = 0f)
    {
        if (IsSingleSpriteMode)
            return TryPlaySingleSpriteAnimation(animationName, forceRestart);

        if (!TryGetAnimator(animationName))
        {
            return false;
        }

        if (!TryResolveState(animationName, preferredLayer, out int layer, out int stateHash))
        {
            // Animator 刚激活的首帧可能尚未建立状态表；Try 语义下安静返回，AI 后续状态刷新会自然重试。
            return false;
        }

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(layer);
        if (!forceRestart &&
            !animator.IsInTransition(layer) &&
            currentState.fullPathHash == stateHash)
        {
            return true;
        }

        animator.Play(stateHash, layer, Mathf.Clamp01(normalizedTime));
        return true;
    }

    [Button]
    public bool SetBool(string parameterName, bool value)
    {
        if (IsSingleSpriteMode)
            return SetSingleSpriteBool(parameterName, value);

        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        if (!TryGetAnimator(parameterName))
        {
            // 冷外壳尚未初始化时保留最新参数值，避免激活后漏掉奔跑等一次性状态变更。
            if (animator != null && (!animator.isActiveAndEnabled || !animator.isInitialized))
            {
                _pendingBoolValues[parameterName] = value;
                return true;
            }
            return false;
        }

        FlushPendingBoolValues();
        if (!_boolParameterHashes.TryGetValue(parameterName, out int parameterHash))
        {
            return false;
        }

        animator.SetBool(parameterHash, value);
        return true;
    }
    #endregion

    #region Animator 状态查询
    private bool TryGetAnimator(string operationName)
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator != null && animator.runtimeAnimatorController != null)
            return RefreshControllerCache();

        // 停用的出生候选尚未完成资源与控制器绑定，不把合法的冷加载误报为配置错误。
        if (isActiveAndEnabled && CanAdvancePresentation)
            WarnOnce(
                $"animator:{operationName}",
                $"[{nameof(Mod_AnimatorController)}] 物品 {item?.itemData?.IDName} 缺少 Animator 或 RuntimeAnimatorController，无法执行 '{operationName}'。",
                this);
        return false;
    }

    private bool RefreshControllerCache()
    {
        RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
        if (controller == null || !animator.isActiveAndEnabled || !animator.isInitialized)
        {
            InvalidateControllerCache();
            return false;
        }

        int controllerId = controller.GetInstanceID();
        int animatorId = animator.GetInstanceID();
        if (_cachedControllerId == controllerId && _cachedAnimatorId == animatorId)
            return true;

        _cachedControllerId = controllerId;
        _cachedAnimatorId = animatorId;
        _boolParameterHashes.Clear();
        _loggedWarnings.Clear();

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Bool)
            {
                _boolParameterHashes[parameter.name] = parameter.nameHash;
            }
        }
        return true;
    }

    private void InvalidateControllerCache()
    {
        _cachedControllerId = 0;
        _cachedAnimatorId = 0;
        _boolParameterHashes.Clear();
    }

    private void FlushPendingBoolValues()
    {
        foreach (KeyValuePair<string, bool> pending in _pendingBoolValues)
            if (_boolParameterHashes.TryGetValue(pending.Key, out int hash))
                animator.SetBool(hash, pending.Value);
        _pendingBoolValues.Clear();
    }

    private void FlushReadyAnimatorParameters()
    {
        if (!CanAdvancePresentation || !isActiveAndEnabled ||
            (_pendingBoolValues.Count == 0 && _cachedControllerId != 0))
            return;

        // 激活后验证控制器是否存在，并把冷加载阶段的参数恢复到正式状态机。
        if (TryGetAnimator("激活动画控制器"))
            FlushPendingBoolValues();
    }

    private bool TryResolveState(
        string animationName,
        int preferredLayer,
        out int resolvedLayer,
        out int resolvedHash)
    {
        resolvedLayer = -1;
        resolvedHash = 0;
        if (string.IsNullOrWhiteSpace(animationName))
        {
            return false;
        }

        int firstLayer = preferredLayer >= 0 ? preferredLayer : 0;
        int lastLayer = preferredLayer >= 0 ? preferredLayer : animator.layerCount - 1;
        if (firstLayer < 0 || lastLayer >= animator.layerCount)
        {
            WarnOnce(
                $"layer:{_cachedControllerId}:{preferredLayer}",
                $"[{nameof(Mod_AnimatorController)}] Animator Layer {preferredLayer} 不存在。",
                this);
            return false;
        }

        string stateName = animationName.Trim();
        for (int layer = firstLayer; layer <= lastLayer; layer++)
        {
            int directHash = Animator.StringToHash(stateName);
            if (animator.HasState(layer, directHash))
            {
                resolvedLayer = layer;
                resolvedHash = directHash;
                return true;
            }

            string fullPath = $"{animator.GetLayerName(layer)}.{stateName}";
            int fullPathHash = Animator.StringToHash(fullPath);
            if (animator.HasState(layer, fullPathHash))
            {
                resolvedLayer = layer;
                resolvedHash = fullPathHash;
                return true;
            }
        }

        return false;
    }

    private void WarnOnce(string key, string message, Object context)
    {
        if (_loggedWarnings.Add(key))
        {
            Debug.LogWarning(message, context);
        }
    }
    #endregion
}
