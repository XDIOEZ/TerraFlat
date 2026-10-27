using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class WorldRenderingManager
{
    #region 阴影功能模块

    private ActorShadowManager contactShadowRenderingBackend;
    private WorldShadowProjectionManager sunShadowRenderingBackend;

    private void InitializeShadowRenderingModule()
    {
        contactShadowRenderingBackend = ActorShadowManager.GetInstance();
        sunShadowRenderingBackend = FindFirstObjectByType<WorldShadowProjectionManager>(FindObjectsInactive.Include);
    }

    internal void AttachContactShadowRenderingBackend(ActorShadowManager backend)
    {
        contactShadowRenderingBackend = backend;
        foreach (var pair in itemRenderingRegistrations) ApplyItemShadowRegistration(pair.Key, pair.Value, false);
    }

    internal void AttachSunShadowRenderingBackend(WorldShadowProjectionManager backend)
    {
        sunShadowRenderingBackend = backend;
        foreach (var pair in itemRenderingRegistrations) ApplyItemShadowRegistration(pair.Key, pair.Value, false);
    }

    /// <summary>单物品开关同时控制接触阴影和太阳阴影，不改变全局画质偏好。</summary>
    public void SetItemShadowEnabled(Item item, bool enabled)
    {
        if (!IsItemRenderingRegistered(item) && !RegisterItemRendering(item)) return;
        ItemRenderingRegistration registration = itemRenderingRegistrations[item];
        if (registration.ShadowEnabled == enabled) return;
        registration.ShadowEnabled = enabled;
        ApplyItemShadowRegistration(item, registration, false);
    }

    /// <summary>水态只影响阴影表现，真实入水结算仍由水体系统处理。</summary>
    public void SetItemInWater(Item item, bool inWater)
    {
        if (!IsItemRenderingRegistered(item) && !RegisterItemRendering(item)) return;
        ItemRenderingRegistration registration = itemRenderingRegistrations[item];
        registration.InWater = inWater;
        if (contactShadowRenderingBackend != null)
            contactShadowRenderingBackend.SetActorInWater(item, inWater);
    }

    public bool IsItemShadowEnabled(Item item) =>
        IsItemRenderingRegistered(item) && itemRenderingRegistrations[item].ShadowEnabled;

    public bool IsItemInWater(Item item) =>
        IsItemRenderingRegistered(item) && itemRenderingRegistrations[item].InWater;

    public float GetShadowOpacity(Scene scene) =>
        contactShadowRenderingBackend != null ? contactShadowRenderingBackend.GetShadowOpacity(scene) : 0f;

    internal bool IsItemContactShadowBound(Item item) =>
        contactShadowRenderingBackend != null && contactShadowRenderingBackend.IsItemShadowBound(item);

    internal bool IsItemSunShadowBound(Item item) =>
        sunShadowRenderingBackend != null && sunShadowRenderingBackend.IsItemShadowBound(item);

    private void ApplyItemShadowRegistration(Item item, ItemRenderingRegistration registration, bool rebuild)
    {
        if (rebuild || !registration.ShadowEnabled) UnregisterItemShadows(item);
        if (!registration.ShadowEnabled) return;
        if (contactShadowRenderingBackend != null)
        {
            contactShadowRenderingBackend.RegisterActor(item);
            contactShadowRenderingBackend.SetActorInWater(item, registration.InWater);
        }
        if (sunShadowRenderingBackend != null) sunShadowRenderingBackend.RegisterCaster(item);
    }

    private void UnregisterItemShadows(Item item)
    {
        if (contactShadowRenderingBackend != null) contactShadowRenderingBackend.UnregisterActor(item);
        if (sunShadowRenderingBackend != null) sunShadowRenderingBackend.UnregisterCaster(item);
    }

    #endregion
}
