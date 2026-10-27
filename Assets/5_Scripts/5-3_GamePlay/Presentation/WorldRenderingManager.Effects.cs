using System.Collections.Generic;
using UnityEngine;

public sealed partial class WorldRenderingManager
{
    #region 特效功能入口

    private readonly Dictionary<Item, HashSet<Transform>> itemVisualEffectOwners = new();

    public Transform VisualEffectRoot => VisualEffectManager.Instance.transform;

    /// <summary>播放跟随物品的非叠加特效，注销物品时自动回收。</summary>
    public GameObject PlayItemVisualEffect(Item item, string effectName, float autoReturnTime = -1f) =>
        item != null ? PlayEffect(item.transform, effectName, item.transform, Vector3.zero,
            autoReturnTime, EffectStackMode.NonStackable) : null;

    public void StopItemVisualEffect(Item item, string effectName)
    {
        if (item != null) StopOwnerEffect(item.transform, effectName);
    }

    public GameObject PlayEffect(string effectName, Vector3 position, Quaternion rotation,
        Transform parent = null, float autoReturnTime = -1f) =>
        VisualEffectManager.Instance.PlayEffect(effectName, position, rotation, parent, autoReturnTime);

    public GameObject PlayEffect(string effectName, Vector3 position, float autoReturnTime = -1f) =>
        VisualEffectManager.Instance.PlayEffect(effectName, position, autoReturnTime);

    public GameObject PlayEffect(Transform owner, string effectName, Transform parent = null,
        float autoReturnTime = -1f, EffectStackMode stackMode = EffectStackMode.NonStackable)
    {
        GameObject effect = VisualEffectManager.Instance.PlayEffect(owner, effectName, parent, autoReturnTime, stackMode);
        if (effect != null) TrackItemVisualEffectOwner(owner);
        return effect;
    }

    public GameObject PlayEffect(Transform owner, string effectName, Transform parent, Vector3 localPosition,
        float autoReturnTime = -1f, EffectStackMode stackMode = EffectStackMode.Stackable)
    {
        GameObject effect = VisualEffectManager.Instance.PlayEffect(owner, effectName, parent, localPosition,
            autoReturnTime, stackMode);
        if (effect != null) TrackItemVisualEffectOwner(owner);
        return effect;
    }

    public GameObject GetEffectFromPool(string effectName) =>
        VisualEffectManager.Instance.GetEffectFromPool(effectName);

    public void ReturnEffectToPool(string effectName, GameObject effectObject) =>
        VisualEffectManager.ExistingInstance?.ReturnEffectToPool(effectName, effectObject);

    public GameEffect GetGameEffectFromPool(GameEffect prefab) =>
        VisualEffectManager.Instance.GetGameEffectFromPool(prefab);

    public void ReturnGameEffectToPool(GameEffect effect) =>
        VisualEffectManager.ExistingInstance?.ReturnGameEffectToPool(effect);

    public bool PrewarmGameEffect(GameEffect prefab, int minimumInactiveCount) =>
        VisualEffectManager.Instance.PrewarmGameEffect(prefab, minimumInactiveCount);

    public int GetInactiveGameEffectCount(GameEffect prefab) =>
        VisualEffectManager.ExistingInstance?.GetInactiveGameEffectCount(prefab) ?? 0;

    public bool HasEffect(Transform owner, string effectName) =>
        VisualEffectManager.ExistingInstance?.HasEffect(owner, effectName) ?? false;

    public GameObject GetOwnerEffect(Transform owner, string effectName) =>
        VisualEffectManager.ExistingInstance?.GetOwnerEffect(owner, effectName);

    public void StopOwnerEffect(Transform owner, string effectName) =>
        VisualEffectManager.ExistingInstance?.StopOwnerEffect(owner, effectName);

    public void StopOwnerAllEffects(Transform owner) =>
        VisualEffectManager.ExistingInstance?.StopOwnerAllEffects(owner);

    public void StopEffect(string effectName) => VisualEffectManager.ExistingInstance?.StopEffect(effectName);

    public void StopAllEffects()
    {
        VisualEffectManager.ExistingInstance?.StopAllEffects();
        itemVisualEffectOwners.Clear();
    }

    /// <summary>资源代际切换前释放池实例，不在清理过程中创建特效后端。</summary>
    public void ClearVisualEffectPool(bool recreateParent = true) => ClearExistingVisualEffectPool(recreateParent);

    public static void ClearExistingVisualEffectPool(bool recreateParent = true)
    {
        VisualEffectManager.ExistingInstance?.ClearPool(recreateParent);
        ExistingInstance?.itemVisualEffectOwners.Clear();
    }

    #endregion

    #region 物品特效所有权与回收

    private void TrackItemVisualEffectOwner(Transform owner)
    {
        if (owner == null) return;
        Item item = owner.GetComponentInParent<Item>();
        if (item == null) return;
        if (!itemVisualEffectOwners.TryGetValue(item, out HashSet<Transform> owners))
        {
            owners = new HashSet<Transform>();
            itemVisualEffectOwners.Add(item, owners);
        }
        owners.Add(owner);
    }

    private void ReleaseItemVisualEffects(Item item)
    {
        if (ReferenceEquals(item, null)) return;
        VisualEffectManager backend = VisualEffectManager.ExistingInstance;
        if (itemVisualEffectOwners.TryGetValue(item, out HashSet<Transform> owners))
        {
            itemVisualEffectOwners.Remove(item);
            if (backend != null)
                foreach (Transform owner in owners) backend.StopOwnerAllEffects(owner);
        }
        if (backend != null && item != null) backend.StopOwnerAllEffects(item.transform);
    }

    private void ReleaseWorldVisualEffects() => StopAllEffects();

    #endregion
}
