using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>高大机械的纯视觉代理；与玩家共用 Y 轴排序键，最多四个旋转或往复 Sprite 子层。</summary>
public sealed class MechanicalDynamicVisual : MonoBehaviour
{
    #region 子层状态
    private sealed class PartVisual
    {
        public Transform Transform; // 子图层的局部变换。
        public SpriteRenderer Renderer; // 与玩家处在同一动态排序域的精灵。
        public Quaternion BaseRotation; // 建造朝向，不包含运转角度。
        public Vector3 BasePosition; // 子图层的静止局部位置。
        public Vector3 BaseScale; // 子图层展开时的原始缩放。
        public float AngularSpeed; // 弧度每秒。
        public float Phase; // 与机械网络同步的相位截距。
        public float Stroke; // 往复行程，世界单位。
        public int Mode; // 0 静止、1 旋转、3 往复、4 皮革压缩。
        public bool Animated; // 运动子层需要逐帧更新。
    }

    private readonly PartVisual[] parts = new PartVisual[4];
    private int sortingLayerId; // 玩家与建筑共用的排序层。
    #endregion

    #region 生命周期
    /// <summary>创建视觉根并沿用普通建筑的动态排序键。</summary>
    internal static MechanicalDynamicVisual Create(Transform owner, Vector3 position)
    {
        GameObject root = new("MechanicalDynamicVisual");
        root.hideFlags = HideFlags.DontSave;
        root.transform.SetParent(owner, false);
        root.transform.position = position;
        WorldSortingManager.GetResourceSortingKey(WorldSortingManager.BuildingCategory,
            out int layerId, out int order);
        SortingGroup group = root.AddComponent<SortingGroup>();
        group.sortingLayerID = layerId;
        group.sortingOrder = order;
        MechanicalDynamicVisual visual = root.AddComponent<MechanicalDynamicVisual>();
        visual.sortingLayerId = layerId;
        return visual;
    }

    /// <summary>节点刷新前隐藏旧子层，未再次提交的状态贴图不会残留。</summary>
    internal void BeginUpdate(Vector3 position)
    {
        transform.position = position;
        foreach (PartVisual part in parts)
            if (part != null) part.Renderer.enabled = false;
    }

    /// <summary>按机械图层参数重用子精灵；运动相位与机械网络使用同一截距。</summary>
    internal void SetPart(int index, Sprite sprite, Material material, Quaternion rotation,
        Vector3 offset, Vector3 scale, int mode, Vector4 animation)
    {
        if (mode == 2)
            throw new InvalidOperationException("地面滚动轴芯不能使用动态机械视觉代理。");
        PartVisual part = GetOrCreatePart(index);
        part.Renderer.sprite = sprite;
        part.Renderer.sharedMaterial = material;
        part.Renderer.enabled = true;
        part.BasePosition = rotation * offset;
        part.BaseScale = scale;
        part.BaseRotation = rotation;
        part.Mode = mode;
        part.Animated = mode == 1 || mode == 3 || mode == 4;
        part.AngularSpeed = animation.y;
        part.Phase = animation.z;
        part.Stroke = animation.w;
        ApplyMotion(part);
    }

    /// <summary>只有高大建筑的运动子层逐帧更新，底座始终静止。</summary>
    private void LateUpdate()
    {
        foreach (PartVisual part in parts)
            if (part != null && part.Animated && part.Renderer.enabled)
                ApplyMotion(part);
    }

    /// <summary>拆除和区块卸载时立即隐藏，随后释放视觉对象。</summary>
    internal void Dispose()
    {
        gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }
    #endregion

    #region 子层装配
    /// <summary>每个运动图层只分配一次 SpriteRenderer。</summary>
    private PartVisual GetOrCreatePart(int index)
    {
        if (parts[index] != null) return parts[index];
        GameObject child = new("Part_" + index);
        child.transform.SetParent(transform, false);
        SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
        renderer.spriteSortPoint = SpriteSortPoint.Pivot;
        renderer.sortingLayerID = sortingLayerId;
        renderer.sortingOrder = index;
        PartVisual part = new()
        {
            Transform = child.transform,
            Renderer = renderer
        };
        parts[index] = part;
        return part;
    }

    /// <summary>用连续机械相位驱动旋转或竖直行程，停转时保留当前姿态。</summary>
    private static void ApplyMotion(PartVisual part)
    {
        float phase = part.Phase + Time.time * part.AngularSpeed;
        float stroke = part.Mode == 3 ? (1f - Mathf.Cos(phase)) * .5f * part.Stroke : 0f;
        part.Transform.localPosition = part.BasePosition + part.BaseRotation * (Vector3.down * stroke);
        float degrees = part.Mode == 1 ? phase * Mathf.Rad2Deg : 0f;
        part.Transform.localRotation = part.BaseRotation * Quaternion.Euler(0f, 0f, degrees);
        float compression = part.Mode == 4
            ? (1f - Mathf.Cos(phase)) * .5f * Mathf.Clamp01(part.Stroke) : 0f;
        part.Transform.localScale = new Vector3(part.BaseScale.x,
            part.BaseScale.y * (1f - compression), part.BaseScale.z);
    }
    #endregion
}
