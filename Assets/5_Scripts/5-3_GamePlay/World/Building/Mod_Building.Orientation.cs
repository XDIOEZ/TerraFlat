using System.Collections.Generic;
using UnityEngine;

public partial class Mod_Building
{
    #region 已安装建筑的格子朝向投影
    private readonly Dictionary<Transform, Vector3> orientationChildPositions = new();
    private readonly Dictionary<Collider2D, Vector2> orientationRootColliderOffsets = new();
    private Vector3 orientationLocalOffset;

    private void GetUnrotatedFootprintSize(out int width, out int height)
    {
        width = Mathf.Clamp(Data?.FootprintWidth ?? 1, 1, 8);
        height = Mathf.Clamp(Data?.FootprintHeight ?? 1, 1, 8);
        Mod_ShipPart part = item?.itemMods?.GetMod_ByID<Mod_ShipPart>(Mod_ShipPart.ModuleId);
        if (part == null) return;
        width = Mathf.Max(width, Mathf.Clamp(part.Configuration.FootprintWidth, 1, 8));
        height = Mathf.Max(height, Mathf.Clamp(part.Configuration.FootprintHeight, 1, 8));
    }

    private void ApplyInstalledOrientation()
    {
        if (item == null || item.InHand || Data?.Role != BuildingRole.PlacedBuilding)
        { RestoreInstalledOrientation(); return; }
        int turns = Mathf.RoundToInt((item.itemData?.transform?.rotation ?? Quaternion.identity).eulerAngles.z / 90f) & 3;
        Quaternion rotation = Quaternion.Euler(0f, 0f, turns * 90f);
        item.transform.rotation = rotation;
        GetUnrotatedFootprintSize(out int width, out int height);
        int rotatedWidth = (turns & 1) == 0 ? width : height;
        int rotatedHeight = (turns & 1) == 0 ? height : width;
        Vector3 center = new((width - 1) * .5f, (height - 1) * .5f, 0f);
        Vector3 rotatedCenter = new((rotatedWidth - 1) * .5f, (rotatedHeight - 1) * .5f, 0f);
        // 根固定在左下格心，只补偿子表现和碰撞偏移，避免旋转后越出真实占地。
        orientationLocalOffset = Quaternion.Inverse(rotation) * rotatedCenter - center;
        if (orientationLocalOffset.sqrMagnitude < .000001f) orientationLocalOffset = Vector3.zero;
        if (orientationLocalOffset == Vector3.zero && orientationChildPositions.Count == 0 &&
            orientationRootColliderOffsets.Count == 0) return;
        Transform root = item.transform;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (GhostShadow != null && child == GhostShadow.transform) continue;
            if (!orientationChildPositions.TryGetValue(child, out Vector3 original))
                orientationChildPositions.Add(child, original = child.localPosition);
            child.localPosition = original + orientationLocalOffset;
        }
        foreach (Collider2D collider in root.GetComponents<Collider2D>())
        {
            if (!orientationRootColliderOffsets.TryGetValue(collider, out Vector2 original))
                orientationRootColliderOffsets.Add(collider, original = collider.offset);
            collider.offset = original + (Vector2)orientationLocalOffset;
        }
    }

    private void RefreshInstalledOrientationSources()
    {
        var children = new List<Transform>(orientationChildPositions.Keys);
        foreach (Transform child in children)
        {
            if (child == null) { orientationChildPositions.Remove(child); continue; }
            Vector3 previous = orientationChildPositions[child];
            if ((child.localPosition - previous - orientationLocalOffset).sqrMagnitude > .000001f)
                orientationChildPositions[child] = child.localPosition;
        }
        var colliders = new List<Collider2D>(orientationRootColliderOffsets.Keys);
        foreach (Collider2D collider in colliders)
        {
            if (collider == null) { orientationRootColliderOffsets.Remove(collider); continue; }
            Vector2 previous = orientationRootColliderOffsets[collider];
            if ((collider.offset - previous - (Vector2)orientationLocalOffset).sqrMagnitude > .000001f)
                orientationRootColliderOffsets[collider] = collider.offset;
        }
    }

    private void RestoreInstalledOrientation()
    {
        foreach (var entry in orientationChildPositions)
            if (entry.Key != null) entry.Key.localPosition = entry.Value;
        foreach (var entry in orientationRootColliderOffsets)
            if (entry.Key != null) entry.Key.offset = entry.Value;
        orientationChildPositions.Clear(); orientationRootColliderOffsets.Clear();
        orientationLocalOffset = Vector3.zero;
    }

    protected override void OnUnload()
    {
        RestoreInstalledOrientation();
        base.OnUnload();
    }
    #endregion
}
