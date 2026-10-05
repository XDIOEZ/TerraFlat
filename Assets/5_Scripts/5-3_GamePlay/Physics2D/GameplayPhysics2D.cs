using UnityEngine;

/// <summary>
/// Gameplay 的物理命中解析入口：先还原真实碰撞源，再按原有顺序查找组件。
/// Item 根下的兄弟模块查找只留在本层，通用 ColliderSource2D 不认识业务实体。
/// </summary>
public static class GameplayPhysics2D
{
    #region 物理命中组件解析
    public static T ResolveComponent<T>(Collider2D collider) where T : class
    {
        Collider2D source = ColliderSource2D.Resolve(collider);
        if (source == null)
            return null;

        // 可选能力未命中时避免创建 GetComponent 缺失诊断。
        source.TryGetComponent(out T component);
        component ??= source.GetComponentInParent<T>();
        component ??= source.GetComponentInChildren<T>(true);
        if (component != null)
            return component;

        Item owner = source.GetComponentInParent<Item>();
        return owner != null ? owner.GetComponentInChildren<T>(true) : null;
    }
    #endregion
}
