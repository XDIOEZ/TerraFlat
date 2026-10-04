using UnityEngine;

public sealed partial class Mod_MechanicalNode
{
    #region 电线接口
    private const string ElectricalPortState = "electricalPort";
    private const string ElectricalPortObjectName = "MechanicalElectricalPort";
    public Vector3 ElectricalPortLocalPosition; // 电线口使用标准导线切片并由机身遮住内侧。
    private Sprite electricalPortSprite;
    private SpriteRenderer electricalPortRenderer;

    /// <summary>资源重载时同步更新电线口，召唤器只预载贴图供放置虚影使用。</summary>
    private void ConfigureElectricalPortVisual()
    {
        electricalPortSprite = null;
        if (electricalPortRenderer != null) electricalPortRenderer.enabled = false;
        if (spriteRenderer == null || GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            !definition.TryGetVisualStateSprite(ElectricalPortState, out electricalPortSprite)) return;
        if (placed)
            electricalPortRenderer = GetOrCreateAxisPortRenderer(ElectricalPortObjectName, false, electricalPortSprite);
    }

    private void ClearElectricalPortVisual()
    {
        if (electricalPortRenderer != null) electricalPortRenderer.enabled = false;
        electricalPortRenderer = null;
        electricalPortSprite = null;
    }

    /// <summary>电线与轴环一样放在外壳后面，随主体的九十度朝向一起旋转。</summary>
    private void ApplyElectricalPortVisual()
    {
        if (electricalPortRenderer == null || !electricalPortRenderer.enabled || spriteRenderer == null) return;
        electricalPortRenderer.transform.localPosition = ElectricalPortLocalPosition;
        electricalPortRenderer.transform.localRotation = Quaternion.identity;
        electricalPortRenderer.transform.localScale = Vector3.one;
        ApplyAxisPortSorting(electricalPortRenderer);
    }

    /// <summary>放置虚影复用落地接口的尺寸、位置和遮挡顺序。</summary>
    private void ApplyElectricalPortPreview(BuildingShadow shadow, Quaternion placementRotation)
    {
        if (electricalPortSprite == null)
        {
            Transform previous = shadow.transform.Find(ElectricalPortObjectName);
            if (previous != null) previous.gameObject.SetActive(false);
            return;
        }
        SpriteRenderer port = shadow.EnsureOverlay(ElectricalPortObjectName, electricalPortSprite,
            shadow.ShadowRenderer.transform.localPosition + placementRotation * ElectricalPortLocalPosition,
            spriteRenderer?.sharedMaterial);
        if (port == null) return;
        port.gameObject.SetActive(true);
        port.transform.localRotation = placementRotation;
        port.transform.localScale = Vector3.one;
        port.sortingOrder = shadow.ShadowRenderer.sortingOrder - 1;
    }
    #endregion
}
