using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>已显示机械节点的阴影数据；区块负责登记，阴影管理器只消费可见区块的数据。</summary>
internal static class MechanicalShadowRegistry
{
    #region 节点与图层

    internal sealed class Entry
    {
        public Scene Scene; // 所属运行时世界场景。
        public int Layer; // 主体相机可见层。
        public Vector3 Foot; // 主体可见底边的统一落点。
        public float ContactWidth; // 底部椭圆的世界宽度。
        public Sprite PrimarySprite; // 主体贴图；端口等小附件不重复投影。
        public bool Active; // 区块卸载后立即失效。
        public readonly Part[] Parts = new Part[4]; // 与机械主体子层一一对应。
    }

    internal sealed class Part
    {
        public Entry Owner; // 所属节点及落点。
        public Sprite Sprite; // 真实子图层贴图。
        public Vector3 Origin; // 建造格中心。
        public Quaternion Rotation; // 建造朝向。
        public Vector3 Offset; // 朝向内的子图层偏移。
        public Vector3 Scale; // 子图层缩放。
        public float AngularSpeed; // 与机械主体共用的每秒弧度。
        public float Phase; // 与机械主体共用的相位截距。
        public float Stroke; // 往复子层的世界行程。
        public int Mode; // 0 静止、1 旋转、3 往复。
        public bool Animated; // 固定层不做每帧运动计算。
        public bool Active; // 本次重建仍提交的图层。
        public int Revision; // 只在节点重新提交时刷新静态代理属性。

        /// <summary>按机械主体相同的 Time.time 相位还原本帧子图层。</summary>
        public void ResolveTransform(out Vector3 position, out Quaternion rotation)
        {
            position = Origin + Rotation * Offset;
            if (Mode == 3)
                position += Rotation * (Vector3.down *
                    ((1f - Mathf.Cos(Phase + Time.time * AngularSpeed)) * .5f * Stroke));
            rotation = Mode == 1
                ? Rotation * Quaternion.Euler(0f, 0f, (Phase + Time.time * AngularSpeed) * Mathf.Rad2Deg)
                : Rotation;
        }
    }

    private static readonly Dictionary<(ChunkTilemapRenderer, Vector3Int), Entry> entries = new();
    internal static Dictionary<(ChunkTilemapRenderer, Vector3Int), Entry>.ValueCollection Entries => entries.Values;
    internal static int Count => entries.Count;

    /// <summary>关闭 Domain Reload 的编辑器重新进入播放时清除旧区块引用。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        foreach (Entry entry in entries.Values)
        {
            entry.Active = false;
            foreach (Part part in entry.Parts)
                if (part != null) part.Active = false;
        }
        entries.Clear();
    }

    /// <summary>重用格子的阴影记录，只在资源或节点重建时计算可见轮廓。</summary>
    internal static void BeginNode(ChunkTilemapRenderer owner, Vector3Int key, Scene scene,
        Sprite sprite, Vector3 origin, Quaternion rotation, ItemShadowVisualDefinitionDto settings)
    {
        var identity = (owner, key);
        if (!entries.TryGetValue(identity, out Entry entry))
        {
            entry = new Entry();
            entries.Add(identity, entry);
        }
        Matrix4x4 matrix = Matrix4x4.TRS(origin, rotation, Vector3.one);
        Bounds bounds = ShadowFootprintResolver.MeasureVisibleWorldBounds(sprite, matrix);
        entry.Scene = scene;
        entry.Layer = owner.gameObject.layer;
        entry.PrimarySprite = sprite;
        entry.Foot = ShadowFootprintResolver.ResolveFoot(matrix, bounds, settings?.FootLocalPosition);
        WorldRenderingConfig.ContactShadow defaults = WorldRenderingConfigCatalog.Default.shadows.contact;
        float width = settings?.ContactWidth ?? bounds.size.x * defaults.widthRatio;
        entry.ContactWidth = width <= 0f ? 0f :
            Mathf.Clamp(width, defaults.minimumWidth, defaults.maximumWidth);
        entry.Active = true;
        foreach (Part part in entry.Parts)
            if (part != null) part.Active = false;
    }

    /// <summary>将设备实际绘制的子图层交给太阳投影，不复制玩法实体。</summary>
    internal static void SetPart(ChunkTilemapRenderer owner, Vector3Int key, int index,
        Sprite sprite, Vector3 origin, Quaternion rotation, Vector3 offset, Vector3 scale,
        int mode, Vector4 animation)
    {
        if (!entries.TryGetValue((owner, key), out Entry entry)) return;
        if (mode != 1 && mode != 3 && sprite != entry.PrimarySprite) return;
        Part part = entry.Parts[index] ??= new Part { Owner = entry };
        part.Sprite = sprite;
        part.Origin = origin;
        part.Rotation = rotation;
        part.Offset = offset;
        part.Scale = scale;
        part.Mode = mode;
        part.Animated = mode == 1 || mode == 3;
        part.AngularSpeed = animation.y;
        part.Phase = animation.z;
        part.Stroke = animation.w;
        part.Active = true;
        part.Revision++;
    }

    /// <summary>拆除或区块卸载时使旧投影代理在本帧末释放。</summary>
    internal static void Remove(ChunkTilemapRenderer owner, Vector3Int key)
    {
        if (!entries.Remove((owner, key), out Entry entry)) return;
        entry.Active = false;
        foreach (Part part in entry.Parts)
            if (part != null) part.Active = false;
    }

    /// <summary>清除区块的全部节点，避免跨世界保留旧 Sprite 引用。</summary>
    internal static void RemoveOwner(ChunkTilemapRenderer owner)
    {
        foreach (Vector3Int key in owner.MechanicalShadowKeys)
            Remove(owner, key);
    }

    #endregion
}
