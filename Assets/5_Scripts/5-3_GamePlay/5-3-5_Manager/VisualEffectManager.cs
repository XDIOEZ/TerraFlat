using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class VisualEffectManager : SingletonAutoMono<VisualEffectManager>
{
    #region 字段和属性
    
    // 对象池字典，用于存储不同类型的特效预制体的对象池
    private Dictionary<string, Queue<GameObject>> effectPool = new Dictionary<string, Queue<GameObject>>();

    // 激活的特效字典，用于跟踪正在使用的特效
    [ShowInInspector]
    public Dictionary<string, List<GameObject>> activeEffects = new Dictionary<string, List<GameObject>>();

    // 按Owner管理的特效字典，用于跟踪每个Owner拥有的特效
    [ShowInInspector]
    private Dictionary<Transform, Dictionary<string, GameObject>> ownerEffects = new Dictionary<Transform, Dictionary<string, GameObject>>();

    // 特效预制体的父对象，用于组织场景层次结构
    private Transform effectPoolParent;
    
    #endregion

    #region 对象池管理
    
    /// <summary>
    /// 从对象池中获取特效
    /// </summary>
    /// <param name="effectName">特效名称</param>
    /// <param name="prefab">特效预制体</param>
    /// <returns>特效实例</returns>
    public GameObject GetEffectFromPool(string effectName)
    {
        GameObject effectObj = null;

        // 检查是否有可用的对象池
        if (effectPool.ContainsKey(effectName) && effectPool[effectName].Count > 0)
        {
            // 从对象池中获取特效
            effectObj = effectPool[effectName].Dequeue();
            effectObj.SetActive(true);
        }
        else
        {
            // 如果对象池为空，创建新的特效实例
            effectObj = GameRes.Instance.InstantiatePrefab(effectName);
        }

        // 将特效添加到激活列表中
        if (!activeEffects.ContainsKey(effectName))
        {
            activeEffects[effectName] = new List<GameObject>();
        }
        activeEffects[effectName].Add(effectObj);

        return effectObj;
    }

    /// <summary>
    /// 将特效返回到对象池
    /// </summary>
    /// <param name="effectName">特效名称</param>
    /// <param name="effectObj">特效对象</param>
    public void ReturnEffectToPool(string effectName, GameObject effectObj)
    {
        // 从激活列表中移除
        if (activeEffects.ContainsKey(effectName))
        {
            activeEffects[effectName].Remove(effectObj);
        }

        // 从Owner特效列表中移除
        foreach (var ownerDict in ownerEffects.Values)
        {
            if (ownerDict.ContainsKey(effectName) && ownerDict[effectName] == effectObj)
            {
                ownerDict.Remove(effectName);
                break;
            }
        }

        // 禁用特效并返回对象池
        effectObj.SetActive(false);
        effectObj.transform.SetParent(effectPoolParent);

        // 添加到对象池
        if (!effectPool.ContainsKey(effectName))
        {
            effectPool[effectName] = new Queue<GameObject>();
        }
        effectPool[effectName].Enqueue(effectObj);
    }
    
    #endregion

    #region 特效播放
    
    /// <summary>
    /// 播放特效
    /// </summary>
    /// <param name="effectName">特效名称</param>
    /// <param name="prefab">特效预制体</param>
    /// <param name="position">位置</param>
    /// <param name="rotation">旋转</param>
    /// <param name="parent">父对象</param>
    /// <param name="autoReturnTime">自动返回对象池的时间（秒），<=0表示不自动返回</param>
    /// <returns>特效实例</returns>
    public GameObject PlayEffect(string effectName, Vector3 position, Quaternion rotation, Transform parent = null, float autoReturnTime = -1)
    {
        GameObject effectObj = GetEffectFromPool(effectName);
        effectObj.transform.SetParent(parent);
        effectObj.transform.position = position;
        effectObj.transform.rotation = rotation;

        // 如果设置了自动返回时间，则启动协程
        if (autoReturnTime > 0)
        {
            StartCoroutine(ReturnEffectAfterTime(effectName, effectObj, autoReturnTime));
        }

        return effectObj;
    }

    /// <summary>
    /// 播放特效（简化版）
    /// </summary>
    /// <param name="effectName">特效名称</param>
    /// <param name="prefab">特效预制体</param>
    /// <param name="position">位置</param>
    /// <param name="autoReturnTime">自动返回对象池的时间（秒），<=0表示不自动返回</param>
    /// <returns>特效实例</returns>
    public GameObject PlayEffect(string effectName, Vector3 position, float autoReturnTime = -1)
    {
        return PlayEffect(effectName, position, Quaternion.identity, null, autoReturnTime);
    }

    /// <summary>
    /// 播放特效，可指定父对象和叠加模式
    /// </summary>
    /// <param name="owner">特效所属对象Transform</param>
    /// <param name="effectName">特效名称</param>
    /// <param name="parent">父对象Transform，如果为null则实例化在世界空间中</param>
    /// <param name="autoReturnTime">自动返回对象池的时间（秒），<=0表示不自动返回</param>
    /// <param name="stackMode">特效叠加模式，默认为不可叠加</param>
    /// <returns>特效实例</returns>
    public GameObject PlayEffect(Transform owner, string effectName, Transform parent = null, float autoReturnTime = -1, EffectStackMode stackMode = EffectStackMode.NonStackable)
    {
        // 检查特效是否不可叠加且已存在
        if (stackMode == EffectStackMode.NonStackable && HasEffect(owner, effectName))
        {
            return GetOwnerEffect(owner, effectName);
        }

        Vector3 position = parent != null ? parent.position : Vector3.zero;
        GameObject effectObj = PlayEffect(effectName, position, Quaternion.identity, parent, autoReturnTime);
        
        // 添加到Owner特效列表
        AddEffectToOwner(owner, effectName, effectObj, stackMode);
        
        return effectObj;
    }

    /// <summary>
    /// 播放特效，可指定父对象、相对位置和叠加模式
    /// </summary>
    /// <param name="owner">特效所属对象Transform</param>
    /// <param name="effectName">特效名称</param>
    /// <param name="parent">父对象Transform</param>
    /// <param name="localPosition">相对于父对象的局部位置</param>
    /// <param name="autoReturnTime">自动返回对象池的时间（秒），<=0表示不自动返回</param>
    /// <param name="stackMode">特效叠加模式，默认为可叠加</param>
    /// <returns>特效实例</returns>
    public GameObject PlayEffect(Transform owner, string effectName, Transform parent, Vector3 localPosition, float autoReturnTime = -1, EffectStackMode stackMode = EffectStackMode.Stackable)
    {
        // 检查特效是否不可叠加且已存在
        if (stackMode == EffectStackMode.NonStackable && HasEffect(owner, effectName))
        {
            return GetOwnerEffect(owner, effectName);
        }

        GameObject effectObj = GetEffectFromPool(effectName);
        effectObj.transform.SetParent(parent);
        effectObj.transform.localPosition = localPosition;
        effectObj.transform.localRotation = Quaternion.identity;

        // 如果设置了自动返回时间，则启动协程
        if (autoReturnTime > 0)
        {
            StartCoroutine(ReturnEffectAfterTime(effectName, effectObj, autoReturnTime));
        }
        
        // 添加到Owner特效列表
        AddEffectToOwner(owner, effectName, effectObj, stackMode);
        
        return effectObj;
    }
    
    #endregion

    #region Owner特效管理
    
    /// <summary>
    /// 添加特效到Owner的特效列表
    /// </summary>
    /// <param name="owner">Owner Transform</param>
    /// <param name="effectName">特效名称</param>
    /// <param name="effectObj">特效对象</param>
    /// <param name="stackMode">特效叠加模式</param>
    private void AddEffectToOwner(Transform owner, string effectName, GameObject effectObj, EffectStackMode stackMode)
    {
        if (!ownerEffects.ContainsKey(owner))
        {
            ownerEffects[owner] = new Dictionary<string, GameObject>();
        }
        
        // 如果特效不可叠加，先移除已存在的特效
        if (stackMode == EffectStackMode.NonStackable && ownerEffects[owner].ContainsKey(effectName))
        {
            ReturnEffectToPool(effectName, ownerEffects[owner][effectName]);
        }
        
        ownerEffects[owner][effectName] = effectObj;
    }

    /// <summary>
    /// 检查Owner是否拥有指定特效
    /// </summary>
    /// <param name="owner">Owner Transform</param>
    /// <param name="effectName">特效名称</param>
    /// <returns>是否拥有特效</returns>
    public bool HasEffect(Transform owner, string effectName)
    {
        return ownerEffects.ContainsKey(owner) && 
               ownerEffects[owner].ContainsKey(effectName) && 
               ownerEffects[owner][effectName] != null && 
               ownerEffects[owner][effectName].activeInHierarchy;
    }

    /// <summary>
    /// 获取Owner的指定特效
    /// </summary>
    /// <param name="owner">Owner Transform</param>
    /// <param name="effectName">特效名称</param>
    /// <returns>特效对象</returns>
    public GameObject GetOwnerEffect(Transform owner, string effectName)
    {
        if (HasEffect(owner, effectName))
        {
            return ownerEffects[owner][effectName];
        }
        return null;
    }

    /// <summary>
    /// 停止指定Owner的特效
    /// </summary>
    /// <param name="owner">Owner Transform</param>
    /// <param name="effectName">特效名称</param>
    public void StopOwnerEffect(Transform owner, string effectName)
    {
        if (ownerEffects.ContainsKey(owner) && ownerEffects[owner].ContainsKey(effectName))
        {
            GameObject effectObj = ownerEffects[owner][effectName];
            if (effectObj != null)
            {
                ReturnEffectToPool(effectName, effectObj);
            }
            ownerEffects[owner].Remove(effectName);
        }
    }

    /// <summary>
    /// 停止指定Owner的所有特效
    /// </summary>
    /// <param name="owner">Owner Transform</param>
    public void StopOwnerAllEffects(Transform owner)
    {
        if (ownerEffects.ContainsKey(owner))
        {
            var effects = new Dictionary<string, GameObject>(ownerEffects[owner]);
            foreach (var kvp in effects)
            {
                if (kvp.Value != null)
                {
                    ReturnEffectToPool(kvp.Key, kvp.Value);
                }
            }
            ownerEffects[owner].Clear();
        }
    }
    
    #endregion

    #region 特效控制
    
    /// <summary>
    /// 停止指定类型的特效
    /// </summary>
    /// <param name="effectName">特效名称</param>
    public void StopEffect(string effectName)
    {
        if (activeEffects.ContainsKey(effectName))
        {
            foreach (GameObject effect in new List<GameObject>(activeEffects[effectName]))
            {
                if (effect != null)
                {
                    ReturnEffectToPool(effectName, effect);
                }
            }
            activeEffects[effectName].Clear();
        }
    }

    /// <summary>
    /// 停止所有特效
    /// </summary>
    public void StopAllEffects()
    {
        foreach (var effectList in activeEffects.Values)
        {
            foreach (GameObject effect in new List<GameObject>(effectList))
            {
                if (effect != null)
                {
                    string effectName = GetEffectName(effect);
                    ReturnEffectToPool(effectName, effect);
                }
            }
        }
        activeEffects.Clear();
        
        // 清空所有Owner的特效
        ownerEffects.Clear();
    }
    
    #endregion

    #region 工具方法
    
    /// <summary>
    /// 延迟一段时间后将特效返回对象池
    /// </summary>
    /// <param name="effectName">特效名称</param>
    /// <param name="effectObj">特效对象</param>
    /// <param name="delay">延迟时间（秒）</param>
    /// <returns></returns>
    private IEnumerator ReturnEffectAfterTime(string effectName, GameObject effectObj, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (effectObj != null && effectObj.activeInHierarchy)
        {
            ReturnEffectToPool(effectName, effectObj);
        }
    }

    /// <summary>
    /// 获取特效对象的名称（通过查找其在激活列表中的记录）
    /// </summary>
    /// <param name="effectObj">特效对象</param>
    /// <returns>特效名称</returns>
    private string GetEffectName(GameObject effectObj)
    {
        foreach (var kvp in activeEffects)
        {
            if (kvp.Value.Contains(effectObj))
            {
                return kvp.Key;
            }
        }
        return "";
    }
    
    #endregion

    #region 生命周期方法
    
    /// <summary>
    /// 清空对象池（在场景切换时调用）
    /// </summary>
    public void ClearPool(bool recreateParent = true)
    {
        foreach (var queue in effectPool.Values)
        {
            while (queue.Count > 0)
            {
                Destroy(queue.Dequeue());
            }
        }
        effectPool.Clear();

        // 销毁所有激活的特效
        foreach (var effectList in activeEffects.Values)
        {
            foreach (GameObject effect in effectList)
            {
                if (effect != null)
                {
                    Destroy(effect);
                }
            }
        }
        activeEffects.Clear();

        // 清空所有Owner的特效
        ownerEffects.Clear();

        // 重新创建特效池父对象（仅在非销毁时调用）
        if (effectPoolParent != null)
        {
            Destroy(effectPoolParent.gameObject);
        }
        
        // 只有在recreateParent为true且对象未被销毁时才创建新的父对象
        if (recreateParent && gameObject != null) // 检查gameObject是否仍然有效
        {
            effectPoolParent = new GameObject("EffectPool").transform;
            effectPoolParent.SetParent(transform);
        }
        else
        {
            effectPoolParent = null;
        }
    }

    private void OnDestroy()
    {
        // 确保在对象销毁时清理所有资源，但不重新创建父对象
        ClearPool(false);
    }
    
    #endregion
}