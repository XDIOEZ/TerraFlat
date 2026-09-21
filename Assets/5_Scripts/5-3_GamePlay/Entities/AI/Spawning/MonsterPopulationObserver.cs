using UnityEngine;

/// <summary>
/// 怪物注册表的显隐通知适配器。只响应 Unity 生命周期，不执行 Update；
/// 祖先节点禁用同样会通知，回池或注销时解除绑定，复用时由正式注册流程重新绑定。
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterPopulationObserver : MonoBehaviour
{
    #region 注册绑定

    private MonsterManager owner;
    private Item item;

    internal void Bind(MonsterManager manager, Item target)
    {
        owner = manager;
        item = target;
    }

    internal void Unbind(MonsterManager manager)
    {
        if (!ReferenceEquals(owner, manager)) return;
        owner = null;
        item = null;
    }

    #endregion

    #region 显隐通知

    private void OnEnable() => NotifyActivity();
    private void OnDisable() => NotifyActivity();

    /// <summary>覆盖直接 Unity 销毁；正常 ItemMgr 注销已先解绑，因此不会重复移除。</summary>
    private void OnDestroy()
    {
        MonsterManager manager = owner;
        Item target = item;
        owner = null;
        item = null;
        if (manager != null)
            manager.Unregister(target);
    }

    private void NotifyActivity()
    {
        if (owner != null)
            owner.NotifyPopulationActivityChanged(item);
    }

    #endregion
}
