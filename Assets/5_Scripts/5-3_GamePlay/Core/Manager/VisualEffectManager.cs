using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// 统一管理运行时特效和 GameEffect 对象池。
/// 普通特效使用名称池，伤害数字/切割特效使用 GameEffect 池；回收时统一停止粒子、重置动画、清理拖尾和 CanvasGroup，
/// 延迟回收由单个 Update 调度，避免连续战斗为每个特效创建协程和 WaitForSeconds。
/// </summary>
public class VisualEffectManager : SingletonAutoMono<VisualEffectManager>
{
    #region Pool State

    private sealed class PooledVisualState
    {
        public ParticleSystem[] ParticleSystems;
        public Animator[] Animators;
        public TrailRenderer[] Trails;
        public CanvasGroup[] CanvasGroups;
        public SpriteRenderer[] SpriteRenderers;
        public Color[] SpriteColors;
        public Quaternion InitialLocalRotation;
        public Vector3 InitialLocalScale;
    }

    private struct PendingReturn
    {
        public string EffectName;
        public GameObject EffectObject;
        public float ReturnAt;
    }

    private readonly Dictionary<string, Queue<GameObject>> effectPool =
        new Dictionary<string, Queue<GameObject>>();

    [ShowInInspector]
    public Dictionary<string, List<GameObject>> activeEffects =
        new Dictionary<string, List<GameObject>>();

    [ShowInInspector]
    private readonly Dictionary<Transform, Dictionary<string, GameObject>> ownerEffects =
        new Dictionary<Transform, Dictionary<string, GameObject>>();

    private readonly Dictionary<GameObject, string> activeEffectNames =
        new Dictionary<GameObject, string>();
    private readonly Dictionary<GameObject, Transform> effectOwners =
        new Dictionary<GameObject, Transform>();
    private readonly Dictionary<GameObject, PooledVisualState> visualStates =
        new Dictionary<GameObject, PooledVisualState>();
    private readonly List<PendingReturn> pendingReturns = new List<PendingReturn>();

    private readonly Dictionary<GameEffect, Queue<GameEffect>> gameEffectPool =
        new Dictionary<GameEffect, Queue<GameEffect>>();
    private readonly HashSet<GameEffect> activeGameEffects = new HashSet<GameEffect>();
    private readonly Dictionary<GameEffect, GameEffect> gameEffectPrefabs =
        new Dictionary<GameEffect, GameEffect>();

    private Transform effectPoolParent;

    #endregion

    #region Lifecycle

    protected override void Awake()
    {
        base.Awake();
        if (instance == this)
            EnsurePoolParent();
    }

    private void Update()
    {
        float now = Time.time;
        for (int i = pendingReturns.Count - 1; i >= 0; i--)
        {
            PendingReturn pending = pendingReturns[i];
            pendingReturns.RemoveAt(i);

            if (pending.EffectObject == null)
                continue;

            if (activeEffectNames.ContainsKey(pending.EffectObject) &&
                pending.EffectObject.activeInHierarchy &&
                now < pending.ReturnAt)
            {
                pendingReturns.Add(pending);
                continue;
            }

            if (activeEffectNames.ContainsKey(pending.EffectObject))
                ReturnEffectToPool(pending.EffectName, pending.EffectObject);
        }
    }

    #endregion

    #region 普通特效池

    /// <summary>从名称对象池取出特效；池中没有实例时才向 GameRes 请求实例。</summary>
    public GameObject GetEffectFromPool(string effectName)
    {
        if (string.IsNullOrEmpty(effectName))
            return null;

        EnsurePoolParent();

        GameObject effectObject = null;
        if (effectPool.TryGetValue(effectName, out Queue<GameObject> queue))
        {
            while (queue.Count > 0 && effectObject == null)
                effectObject = queue.Dequeue();
        }

        if (effectObject == null && GameRes.Instance != null)
            effectObject = GameRes.Instance.InstantiatePrefab(effectName);

        if (effectObject == null)
            return null;

        effectObject.transform.SetParent(null, false);
        ResetVisualState(effectObject, false);
        effectObject.SetActive(true);
        ResetVisualState(effectObject, true);

        if (!activeEffects.TryGetValue(effectName, out List<GameObject> activeList))
        {
            activeList = new List<GameObject>();
            activeEffects.Add(effectName, activeList);
        }

        activeList.Add(effectObject);
        activeEffectNames[effectObject] = effectName;
        return effectObject;
    }

    /// <summary>将普通特效回收；只接受当前激活登记中的实例，避免重复回收入池。</summary>
    public void ReturnEffectToPool(string effectName, GameObject effectObject)
    {
        if (effectObject == null || !activeEffectNames.TryGetValue(effectObject, out string registeredName))
            return;

        string poolName = string.IsNullOrEmpty(registeredName) ? effectName : registeredName;
        if (activeEffects.TryGetValue(poolName, out List<GameObject> activeList))
            activeList.Remove(effectObject);

        activeEffectNames.Remove(effectObject);
        RemoveEffectOwner(effectObject, poolName);
        RemovePendingReturns(effectObject);

        ResetVisualState(effectObject, false);
        effectObject.SetActive(false);
        effectObject.transform.SetParent(effectPoolParent, false);

        if (!effectPool.TryGetValue(poolName, out Queue<GameObject> queue))
        {
            queue = new Queue<GameObject>();
            effectPool.Add(poolName, queue);
        }

        queue.Enqueue(effectObject);
    }

    /// <summary>从 GameEffect 池取出实例，并注入结束回收回调。</summary>
    public GameEffect GetGameEffectFromPool(GameEffect prefab)
    {
        if (prefab == null)
            return null;

        EnsurePoolParent();

        GameEffect effect = null;
        if (gameEffectPool.TryGetValue(prefab, out Queue<GameEffect> queue))
        {
            while (queue.Count > 0 && effect == null)
                effect = queue.Dequeue();
        }

        if (effect == null)
        {
            effect = Instantiate(prefab);
            effect.SetPoolReturnCallback(null);
        }

        GameEffect capturedEffect = effect;
        effect.SetPoolReturnCallback(() => ReturnGameEffectToPool(prefab, capturedEffect));
        effect.transform.SetParent(null, false);
        if (!effect.gameObject.activeSelf)
            effect.gameObject.SetActive(true);
        effect.OnSpawnedFromPool();
        activeGameEffects.Add(effect);
        gameEffectPrefabs[effect] = prefab;
        return effect;
    }

    /// <summary>回收 GameEffect；特效自身通过 ReturnToPoolOrDestroy 触发此路径。</summary>
    private void ReturnGameEffectToPool(GameEffect prefab, GameEffect effect)
    {
        if (effect == null || !activeGameEffects.Remove(effect))
            return;

        if (prefab == null && !gameEffectPrefabs.TryGetValue(effect, out prefab))
            return;

        gameEffectPrefabs.Remove(effect);

        effect.OnReturnedToPool();
        effect.gameObject.SetActive(false);
        effect.transform.SetParent(effectPoolParent, false);
        effect.transform.localPosition = Vector3.zero;
        effect.transform.localRotation = Quaternion.identity;

        if (!gameEffectPool.TryGetValue(prefab, out Queue<GameEffect> queue))
        {
            queue = new Queue<GameEffect>();
            gameEffectPool.Add(prefab, queue);
        }

        queue.Enqueue(effect);
    }

    #endregion

    #region 特效播放

    /// <summary>播放普通特效并按时回收。</summary>
    public GameObject PlayEffect(string effectName, Vector3 position, Quaternion rotation, Transform parent = null, float autoReturnTime = -1)
    {
        GameObject effectObject = GetEffectFromPool(effectName);
        if (effectObject == null)
            return null;

        effectObject.transform.SetParent(parent, false);
        effectObject.transform.position = position;
        effectObject.transform.rotation = rotation;
        ScheduleReturn(effectName, effectObject, autoReturnTime);
        return effectObject;
    }

    /// <summary>播放普通特效的简化入口。</summary>
    public GameObject PlayEffect(string effectName, Vector3 position, float autoReturnTime = -1)
    {
        return PlayEffect(effectName, position, Quaternion.identity, null, autoReturnTime);
    }

    /// <summary>播放绑定 Owner 的普通特效。</summary>
    public GameObject PlayEffect(Transform owner, string effectName, Transform parent = null, float autoReturnTime = -1, EffectStackMode stackMode = EffectStackMode.NonStackable)
    {
        if (stackMode == EffectStackMode.NonStackable && HasEffect(owner, effectName))
            return GetOwnerEffect(owner, effectName);

        Vector3 position = parent != null ? parent.position : Vector3.zero;
        GameObject effectObject = PlayEffect(effectName, position, Quaternion.identity, parent, autoReturnTime);
        AddEffectToOwner(owner, effectName, effectObject, stackMode);
        return effectObject;
    }

    /// <summary>播放绑定 Owner 的普通特效，并设置相对位置。</summary>
    public GameObject PlayEffect(Transform owner, string effectName, Transform parent, Vector3 localPosition, float autoReturnTime = -1, EffectStackMode stackMode = EffectStackMode.Stackable)
    {
        if (stackMode == EffectStackMode.NonStackable && HasEffect(owner, effectName))
            return GetOwnerEffect(owner, effectName);

        GameObject effectObject = GetEffectFromPool(effectName);
        if (effectObject == null)
            return null;

        effectObject.transform.SetParent(parent, false);
        effectObject.transform.localPosition = localPosition;
        effectObject.transform.localRotation = Quaternion.identity;
        ScheduleReturn(effectName, effectObject, autoReturnTime);
        AddEffectToOwner(owner, effectName, effectObject, stackMode);
        return effectObject;
    }

    #endregion

    #region Owner 特效管理

    /// <summary>登记 Owner 与特效的关系，供非叠加模式快速复用。</summary>
    private void AddEffectToOwner(Transform owner, string effectName, GameObject effectObject, EffectStackMode stackMode)
    {
        if (owner == null || effectObject == null)
            return;

        if (!ownerEffects.TryGetValue(owner, out Dictionary<string, GameObject> ownerDictionary))
        {
            ownerDictionary = new Dictionary<string, GameObject>();
            ownerEffects.Add(owner, ownerDictionary);
        }

        if (stackMode == EffectStackMode.NonStackable && ownerDictionary.TryGetValue(effectName, out GameObject oldEffect))
            ReturnEffectToPool(effectName, oldEffect);

        ownerDictionary[effectName] = effectObject;
        effectOwners[effectObject] = owner;
    }

    /// <summary>判断 Owner 是否拥有仍在激活的特效。</summary>
    public bool HasEffect(Transform owner, string effectName)
    {
        return owner != null &&
               ownerEffects.TryGetValue(owner, out Dictionary<string, GameObject> ownerDictionary) &&
               ownerDictionary.TryGetValue(effectName, out GameObject effectObject) &&
               effectObject != null &&
               effectObject.activeInHierarchy;
    }

    /// <summary>获取 Owner 当前的特效。</summary>
    public GameObject GetOwnerEffect(Transform owner, string effectName)
    {
        return HasEffect(owner, effectName) ? ownerEffects[owner][effectName] : null;
    }

    /// <summary>停止 Owner 的指定特效。</summary>
    public void StopOwnerEffect(Transform owner, string effectName)
    {
        if (owner == null || !ownerEffects.TryGetValue(owner, out Dictionary<string, GameObject> ownerDictionary))
            return;

        if (ownerDictionary.TryGetValue(effectName, out GameObject effectObject))
            ReturnEffectToPool(effectName, effectObject);

        ownerDictionary.Remove(effectName);
    }

    /// <summary>停止 Owner 的全部特效。</summary>
    public void StopOwnerAllEffects(Transform owner)
    {
        if (owner == null || !ownerEffects.TryGetValue(owner, out Dictionary<string, GameObject> ownerDictionary))
            return;

        KeyValuePair<string, GameObject>[] effects = new KeyValuePair<string, GameObject>[ownerDictionary.Count];
        int index = 0;
        foreach (KeyValuePair<string, GameObject> pair in ownerDictionary)
            effects[index++] = pair;

        for (int i = 0; i < effects.Length; i++)
        {
            if (effects[i].Value != null)
                ReturnEffectToPool(effects[i].Key, effects[i].Value);
        }

        ownerDictionary.Clear();
    }

    #endregion

    #region 特效控制

    /// <summary>停止指定名称的全部特效。</summary>
    public void StopEffect(string effectName)
    {
        if (!activeEffects.TryGetValue(effectName, out List<GameObject> activeList))
            return;

        GameObject[] effects = activeList.ToArray();
        for (int i = 0; i < effects.Length; i++)
        {
            if (effects[i] != null)
                ReturnEffectToPool(effectName, effects[i]);
        }
    }

    /// <summary>停止所有普通特效和 GameEffect。</summary>
    public void StopAllEffects()
    {
        GameObject[] effects = new GameObject[activeEffectNames.Count];
        activeEffectNames.Keys.CopyTo(effects, 0);
        for (int i = 0; i < effects.Length; i++)
        {
            if (effects[i] != null && activeEffectNames.TryGetValue(effects[i], out string effectName))
                ReturnEffectToPool(effectName, effects[i]);
        }

        GameEffect[] gameEffects = new GameEffect[activeGameEffects.Count];
        activeGameEffects.CopyTo(gameEffects);
        for (int i = 0; i < gameEffects.Length; i++)
        {
            if (gameEffects[i] != null)
                ReturnGameEffectToPool(null, gameEffects[i]);
        }

        ownerEffects.Clear();
    }

    #endregion

    #region Pool Reset

    /// <summary>记录实例上的可重置组件；GetComponentsInChildren 只在实例首次进入池时执行一次。</summary>
    private PooledVisualState GetVisualState(GameObject effectObject)
    {
        if (visualStates.TryGetValue(effectObject, out PooledVisualState state))
            return state;

        state = new PooledVisualState
        {
            ParticleSystems = effectObject.GetComponentsInChildren<ParticleSystem>(true),
            Animators = effectObject.GetComponentsInChildren<Animator>(true),
            Trails = effectObject.GetComponentsInChildren<TrailRenderer>(true),
            CanvasGroups = effectObject.GetComponentsInChildren<CanvasGroup>(true),
            SpriteRenderers = effectObject.GetComponentsInChildren<SpriteRenderer>(true),
            InitialLocalRotation = effectObject.transform.localRotation,
            InitialLocalScale = effectObject.transform.localScale
        };

        state.SpriteColors = new Color[state.SpriteRenderers.Length];
        for (int i = 0; i < state.SpriteRenderers.Length; i++)
        {
            if (state.SpriteRenderers[i] != null)
                state.SpriteColors[i] = state.SpriteRenderers[i].color;
        }

        visualStates.Add(effectObject, state);
        return state;
    }

    /// <summary>统一清理和启动内置可复用视觉组件。</summary>
    private void ResetVisualState(GameObject effectObject, bool spawned)
    {
        PooledVisualState state = GetVisualState(effectObject);

        for (int i = 0; i < state.ParticleSystems.Length; i++)
        {
            ParticleSystem particleSystem = state.ParticleSystems[i];
            if (particleSystem == null)
                continue;

            if (!spawned)
            {
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            else if (particleSystem.main.playOnAwake)
            {
                particleSystem.Play(true);
            }
        }

        for (int i = 0; i < state.Animators.Length; i++)
        {
            Animator animator = state.Animators[i];
            if (animator == null || animator.runtimeAnimatorController == null)
                continue;

            animator.Rebind();
            animator.Update(0f);
        }

        for (int i = 0; i < state.Trails.Length; i++)
        {
            if (state.Trails[i] != null)
                state.Trails[i].Clear();
        }

        for (int i = 0; i < state.CanvasGroups.Length; i++)
        {
            if (state.CanvasGroups[i] != null)
                state.CanvasGroups[i].alpha = 1f;
        }

        for (int i = 0; i < state.SpriteRenderers.Length; i++)
        {
            if (state.SpriteRenderers[i] != null)
                state.SpriteRenderers[i].color = state.SpriteColors[i];
        }

        if (!spawned)
        {
            effectObject.transform.localPosition = Vector3.zero;
            effectObject.transform.localRotation = state.InitialLocalRotation;
            effectObject.transform.localScale = state.InitialLocalScale;
        }
    }

    #endregion

    #region Helpers

    /// <summary>统一调度延迟回收，替代每次播放 StartCoroutine。</summary>
    private void ScheduleReturn(string effectName, GameObject effectObject, float delay)
    {
        if (effectObject == null || delay <= 0f)
            return;

        RemovePendingReturns(effectObject);
        pendingReturns.Add(new PendingReturn
        {
            EffectName = effectName,
            EffectObject = effectObject,
            ReturnAt = Time.time + delay
        });
    }

    /// <summary>移除实例的旧回收计划，避免复用实例后被上一轮计时提前回收。</summary>
    private void RemovePendingReturns(GameObject effectObject)
    {
        for (int i = pendingReturns.Count - 1; i >= 0; i--)
        {
            if (pendingReturns[i].EffectObject == effectObject)
                pendingReturns.RemoveAt(i);
        }
    }

    /// <summary>通过反向索引移除 Owner 关系，避免遍历所有 Owner 字典。</summary>
    private void RemoveEffectOwner(GameObject effectObject, string effectName)
    {
        if (!effectOwners.TryGetValue(effectObject, out Transform owner))
            return;

        effectOwners.Remove(effectObject);
        if (owner != null && ownerEffects.TryGetValue(owner, out Dictionary<string, GameObject> ownerDictionary) &&
            ownerDictionary.TryGetValue(effectName, out GameObject registeredEffect) && registeredEffect == effectObject)
        {
            ownerDictionary.Remove(effectName);
        }
    }

    /// <summary>确保池父节点存在，场景切换后仍可继续复用。</summary>
    private void EnsurePoolParent()
    {
        if (effectPoolParent != null)
            return;

        GameObject poolObject = new GameObject("EffectPool");
        effectPoolParent = poolObject.transform;
        effectPoolParent.SetParent(transform, false);
    }

    #endregion

    #region Cleanup

    /// <summary>清理全部池实例；场景切换时可选择重建池根节点。</summary>
    public void ClearPool(bool recreateParent = true)
    {
        foreach (Queue<GameObject> queue in effectPool.Values)
        {
            while (queue.Count > 0)
            {
                GameObject effectObject = queue.Dequeue();
                if (effectObject != null)
                    Destroy(effectObject);
            }
        }

        foreach (List<GameObject> activeList in activeEffects.Values)
        {
            for (int i = 0; i < activeList.Count; i++)
            {
                if (activeList[i] != null)
                    Destroy(activeList[i]);
            }
        }

        foreach (Queue<GameEffect> queue in gameEffectPool.Values)
        {
            while (queue.Count > 0)
            {
                GameEffect effect = queue.Dequeue();
                if (effect != null)
                    Destroy(effect.gameObject);
            }
        }

        foreach (GameEffect effect in activeGameEffects)
        {
            if (effect != null)
            {
                effect.SetPoolReturnCallback(null);
                Destroy(effect.gameObject);
            }
        }

        effectPool.Clear();
        activeEffects.Clear();
        activeEffectNames.Clear();
        ownerEffects.Clear();
        effectOwners.Clear();
        visualStates.Clear();
        pendingReturns.Clear();
        gameEffectPool.Clear();
        activeGameEffects.Clear();
        gameEffectPrefabs.Clear();

        if (effectPoolParent != null)
            Destroy(effectPoolParent.gameObject);

        effectPoolParent = null;
        if (recreateParent && gameObject != null)
            EnsurePoolParent();
    }

    protected override void OnDestroy()
    {
        ClearPool(false);
        base.OnDestroy();
    }

    #endregion
}
