using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

public class Mod_AnimatorController : Module, ITrunDirection
{
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }

    public Animator animator;
    private readonly Dictionary<string, int> _boolParameterHashes = new Dictionary<string, int>();
    private readonly HashSet<string> _loggedWarnings = new HashSet<string>();
    private int _cachedControllerId;

    public override void Awake()
    {
        _Data.ID = ModText.AnimatorReceiver;
    }

    public override void Load()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        RefreshControllerCache();
      //  throw new System.NotImplementedException();
    }

    public override void Save()
    {
       // throw new System.NotImplementedException();
    }
    public override void ModUpdate(float deltaTime)
    {

    }

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
        if (!TryGetAnimator(animationName))
        {
            return false;
        }

        if (!TryResolveState(animationName, preferredLayer, out int layer, out int stateHash))
        {
            WarnOnce(
                $"state:{_cachedControllerId}:{preferredLayer}:{animationName}",
                $"[{nameof(Mod_AnimatorController)}] Animator 中不存在状态 '{animationName}'，已跳过播放。",
                this);
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
        if (!TryGetAnimator(parameterName) || string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        RefreshControllerCache();
        if (!_boolParameterHashes.TryGetValue(parameterName, out int parameterHash))
        {
            return false;
        }

        animator.SetBool(parameterHash, value);
        return true;
    }

    private bool TryGetAnimator(string operationName)
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            RefreshControllerCache();
            return true;
        }

        WarnOnce(
            $"animator:{operationName}",
            $"[{nameof(Mod_AnimatorController)}] 缺少 Animator 或 RuntimeAnimatorController，无法执行 '{operationName}'。",
            this);
        return false;
    }

    private void RefreshControllerCache()
    {
        int controllerId = animator != null && animator.runtimeAnimatorController != null
            ? animator.runtimeAnimatorController.GetInstanceID()
            : 0;
        if (_cachedControllerId == controllerId)
        {
            return;
        }

        _cachedControllerId = controllerId;
        _boolParameterHashes.Clear();
        _loggedWarnings.Clear();

        if (animator == null)
        {
            return;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Bool)
            {
                _boolParameterHashes[parameter.name] = parameter.nameHash;
            }
        }
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
}
