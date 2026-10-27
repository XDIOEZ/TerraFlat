using UnityEngine;

using UnityEngine.SceneManagement;

/// <summary>
/// Gameplay 的物理命中解析入口：先还原真实碰撞源，再按原有顺序查找组件。
/// Item 根下的兄弟模块查找只留在本层，通用 ColliderSource2D 不认识业务实体。
/// </summary>
public static partial class GameplayPhysics2D
{
    #region 物理命中组件解析
    public static T ResolveComponent<T>(Collider2D collider) where T : class
    {
        Collider2D source = ColliderSource2D.Resolve(collider);
        if (source == null)
            return null;
        if (source != collider && source.gameObject.scene.GetPhysicsScene2D() != collider.gameObject.scene.GetPhysicsScene2D())
            return null;

        // 区域登记优先返回真实接收方，数据代理和多区域物品不靠层级猜目标。
        Item registeredItem = null;
        if (WorldPhysicsService2D.TryResolveCollider(collider, out WorldPhysicsColliderBinding2D binding))
        {
            bool namedRegion = !string.IsNullOrEmpty(binding.RegionId);
            if (namedRegion && typeof(T) == typeof(Mod_DamageReceiver) &&
                (binding.Purpose & WorldPhysicsPurpose2D.Damage) == 0) return null;
            if (namedRegion && typeof(T) == typeof(IInteractable) &&
                (binding.Purpose & WorldPhysicsPurpose2D.Interaction) == 0) return null;
            if (binding.Receiver is T receiver) return receiver;
            if (binding.Entity is T entity) return entity;
            if (binding.Item is T registeredComponent) return registeredComponent;
            if (namedRegion) return null;
            registeredItem = binding.Item;
        }

        // 可选能力未命中时避免创建 GetComponent 缺失诊断。
        source.TryGetComponent(out T component);
        component ??= source.GetComponentInParent<T>();
        component ??= source.GetComponentInChildren<T>(true);
        if (component != null)
            return component;

        Item owner = registeredItem != null ? registeredItem : source.GetComponentInParent<Item>();
        return owner != null ? owner.GetComponentInChildren<T>(true) : null;
    }
    #endregion
}
