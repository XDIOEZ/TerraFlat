using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>建筑局部光遮挡策略；太阳长投影使用独立系统，不受此配置影响。</summary>
public enum BuildingLightOcclusionMode
{
    /// <summary>自身有效光源进入实体轮廓时，将遮挡限制到光源下方。</summary>
    Automatic,
    /// <summary>始终使用完整轮廓，适合不透光外壳等需要封闭光源的建筑。</summary>
    FullSilhouette,
    /// <summary>不参与局部光遮挡，适合纯火焰或透明灯罩。</summary>
    None
}

/// <summary>
/// 动态建筑的发光避让与接收层配置。默认在自身光源下方保留实体轮廓，间隙为 0.0625 世界单位。
/// 只在 Sprite、翻转、发光状态或裁剪高度变化时重建阴影网格；燃料光强抖动不触发重建。
/// 这是一种 2D 轮廓近似，不模拟真实三维高度，也不绕过其他建筑与 Blocking Tile 的遮挡。
/// </summary>
public partial class Mod_Building
{
    #region 局部光遮挡配置与缓存

    [Header("局部光照遮挡")]
    [Tooltip("自动：自身光源位于轮廓内时保留光源下方的实体遮挡；完整：封闭轮廓；无：不遮挡局部光。")]
    public BuildingLightOcclusionMode LightOcclusionMode = BuildingLightOcclusionMode.Automatic;

    [Min(0.001f), Tooltip("自身光源与实体遮挡之间的世界单位间隙，默认 0.0625；不移动真实光源。")]
    public float OwnLightClearance = 0.0625f;

    [Tooltip("接收此建筑阴影的 Sorting Layer 名称；空列表表示所有层。不是物理碰撞层。")]
    public string[] ShadowTargetSortingLayers = Array.Empty<string>();

    private SpriteRenderer _lightOccluderSource;
    private Sprite _occluderShapeSprite;
    private bool _occluderFlipX;
    private bool _occluderFlipY;
    private Vector3[] _fullOccluderPath;
    private float _appliedOccluderCutY = float.PositiveInfinity;
    private BuildingLightOcclusionMode _appliedOcclusionMode;
    private readonly List<Vector3> _clippedOccluderPoints = new();

    #endregion

    #region 光源状态与轮廓同步

    /// <summary>放在燃料、动画更新之后读取最终状态；不扫描场景、不随光强抖动重建网格。</summary>
    private void LateUpdate()
    {
        if (!_isLoaded || item == null || item.InHand || Data?.Role != BuildingRole.PlacedBuilding ||
            _lightOccluder == null || _lightOccluderSource == null)
            return;
        RefreshLightOccluderGeometry(false);
    }

    /// <summary>配置或 MOD 修改接收层后显式刷新；保留原 SyncLightOccluder 作为生命周期入口。</summary>
    public void RefreshLightOcclusion()
    {
        SyncLightOccluder();
    }

    /// <summary>更新完整轮廓缓存，再按自身有效光源决定是否裁剪。</summary>
    private void RefreshLightOccluderGeometry(bool force)
    {
        SpriteRenderer source = _lightOccluderSource;
        if (source == null || !source.enabled || !source.gameObject.activeInHierarchy || source.sprite == null ||
            LightOcclusionMode == BuildingLightOcclusionMode.None)
        {
            _lightOccluder.enabled = false;
            _appliedOcclusionMode = LightOcclusionMode;
            return;
        }

        bool shapeChanged = force || _occluderShapeSprite != source.sprite ||
                            _occluderFlipX != source.flipX || _occluderFlipY != source.flipY;
        if (shapeChanged)
        {
            SyncShadowCasterShape(_lightOccluder, source);
            _fullOccluderPath = _lightOccluder.shapePath;
            _occluderShapeSprite = source.sprite;
            _occluderFlipX = source.flipX;
            _occluderFlipY = source.flipY;
        }

        float cutY = ResolveOwnLightCutHeight(source, _fullOccluderPath);
        if (!shapeChanged && _appliedOcclusionMode == LightOcclusionMode &&
            cutY.Equals(_appliedOccluderCutY))
        {
            // 源 Renderer 临时隐藏后重新显示时，沿用已生成的网格。
            bool shouldEnable = _lightOccluder.shapePath.Length >= 3;
            if (!_lightOccluder.enabled && shouldEnable)
            {
                _lightOccluder.enabled = true;
                _lightOccluder.Update();
            }
            return;
        }

        _appliedOccluderCutY = cutY;
        _appliedOcclusionMode = LightOcclusionMode;
        bool clipped = !float.IsPositiveInfinity(cutY);
        Vector3[] path = clipped ? ClipOccluderBelowHeight(_fullOccluderPath, cutY) : _fullOccluderPath;

        // 裁剪后不能继续用整张 Sprite 写入自阴影模板，否则光源仍会被完整轮廓盖住。
        _lightOccluder.useRendererSilhouette = !clipped;
        _lightOccluder.selfShadows = true;
        _lightOccluder.castsShadows = true;
        ShadowCasterShapePathField.SetValue(_lightOccluder, path);
        ShadowCasterShapePathHashField.SetValue(_lightOccluder, CalculateShadowPathHash(path));
        _lightOccluder.enabled = path.Length >= 3;
        if (_lightOccluder.enabled)
            _lightOccluder.Update();
    }

    /// <summary>只读取当前 Item 的光源模块注册表；多个内嵌光源共同取最低安全裁剪高度。</summary>
    private float ResolveOwnLightCutHeight(SpriteRenderer source, Vector3[] path)
    {
        if (LightOcclusionMode != BuildingLightOcclusionMode.Automatic || path == null)
            return float.PositiveInfinity;

        List<Module> lightModules = item.itemMods.GetModList_ByID(ModText.LightSource);
        if (lightModules == null)
            return float.PositiveInfinity;

        float localScale = source.transform.TransformVector(Vector3.up).magnitude;
        float clearance = Mathf.Max(0.001f, OwnLightClearance) / Mathf.Max(0.001f, localScale);
        float cutY = float.PositiveInfinity;
        for (int i = 0; i < lightModules.Count; i++)
        {
            if (lightModules[i] is not Mod_LightSource emitter || emitter == null ||
                !emitter.TryGetActiveOcclusionLight(out Light2D light) ||
                !Light2DSortingLayerUtility.SharesShadowLayers(light, _lightOccluder))
                continue;

            Vector2 localPoint = source.transform.InverseTransformPoint(light.transform.position);
            if (ContainsOccluderPoint(path, localPoint, clearance))
                cutY = Mathf.Min(cutY, localPoint.y - clearance);
        }

        return cutY;
    }

    #endregion

    #region 轮廓几何

    /// <summary>使用真实路径判断包含关系，并覆盖贴着边缘的光源；不能仅比较包围盒。</summary>
    private static bool ContainsOccluderPoint(Vector3[] path, Vector2 point, float edgeClearance)
    {
        bool inside = false;
        for (int i = 0, previous = path.Length - 1; i < path.Length; previous = i++)
        {
            Vector2 a = path[previous];
            Vector2 b = path[i];
            Vector2 segment = b - a;
            float t = segment.sqrMagnitude > 0f
                ? Mathf.Clamp01(Vector2.Dot(point - a, segment) / segment.sqrMagnitude)
                : 0f;
            if ((point - (a + segment * t)).sqrMagnitude <= edgeClearance * edgeClearance)
                return true;

            if ((a.y > point.y) != (b.y > point.y) &&
                point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }

        return inside;
    }

    /// <summary>将实体轮廓裁到光源下方；保留原始顶点顺序，空结果直接停用阴影体。</summary>
    private Vector3[] ClipOccluderBelowHeight(Vector3[] path, float cutY)
    {
        _clippedOccluderPoints.Clear();
        Vector3 previous = path[path.Length - 1];
        bool previousInside = previous.y <= cutY;
        for (int i = 0; i < path.Length; i++)
        {
            Vector3 current = path[i];
            bool currentInside = current.y <= cutY;
            if (currentInside != previousInside)
            {
                float t = (cutY - previous.y) / (current.y - previous.y);
                AddOccluderPoint(Vector3.LerpUnclamped(previous, current, t));
            }
            if (currentInside)
                AddOccluderPoint(current);
            previous = current;
            previousInside = currentInside;
        }

        int count = _clippedOccluderPoints.Count;
        if (count > 1 && (_clippedOccluderPoints[0] - _clippedOccluderPoints[count - 1]).sqrMagnitude < 0.00000001f)
            _clippedOccluderPoints.RemoveAt(--count);

        float area = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 a = _clippedOccluderPoints[i];
            Vector3 b = _clippedOccluderPoints[(i + 1) % count];
            area += a.x * b.y - b.x * a.y;
        }

        return count < 3 || Mathf.Abs(area) < 0.00000001f
            ? Array.Empty<Vector3>()
            : _clippedOccluderPoints.ToArray();
    }

    /// <summary>移除裁剪边上的重复顶点，避免 URP 阴影三角化生成退化边。</summary>
    private void AddOccluderPoint(Vector3 point)
    {
        if (_clippedOccluderPoints.Count == 0 ||
            (_clippedOccluderPoints[_clippedOccluderPoints.Count - 1] - point).sqrMagnitude >= 0.00000001f)
            _clippedOccluderPoints.Add(point);
    }

    #endregion
}
