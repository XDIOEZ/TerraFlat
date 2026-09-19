using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>掉落物共享视觉定义；只读取资源和库存数据，不实例化 Item 外壳或业务模块。</summary>
internal sealed class DroppedItemVisual
{
    public Sprite Sprite;
    public Material SpriteMaterial;
    public Vector2[] Vertices;
    public Vector2[] Uvs;
    public ushort[] Triangles;
    public Matrix4x4 LocalMatrix;
    public Color Color;
    public int Layer;
    public int Order;
    public Vector3 LocalPosition;
    public Quaternion LocalRotation;
    public Vector3 LocalScale;

    public static Sprite ResolveSprite(ItemData data)
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemPresentation(data.IDName, out _, out Sprite sprite) || sprite == null)
            throw new InvalidOperationException($"掉落物缺少显示贴图：{data.IDName}");
        if (Mod_WaterVessel.TryResolvePresentationSprite(data, out Sprite vesselSprite)) sprite = vesselSprite;
        return sprite;
    }

    public static DroppedItemVisual Resolve(ItemData data, Sprite sprite)
    {
        GameRes resources = GameRes.ExistingInstance;
        resources.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition);
        ItemVisualDefinitionDto visual = definition?.Visual;
        Transform shellRenderer = definition?.ShellPrefab != null && !string.IsNullOrEmpty(visual?.RendererPath)
            ? definition.ShellPrefab.transform.Find(visual.RendererPath) : null;
        SpriteRenderer source = shellRenderer != null ? shellRenderer.GetComponent<SpriteRenderer>() : null;
        Vector3 position = visual?.RendererLocalPosition ?? (shellRenderer != null ? shellRenderer.localPosition : Vector3.zero);
        Quaternion rotation = Quaternion.Euler(visual?.RendererLocalEulerAngles ??
            (shellRenderer != null ? shellRenderer.localEulerAngles : Vector3.zero));
        Vector3 scale = visual?.RendererLocalScale ?? (shellRenderer != null ? shellRenderer.localScale : Vector3.one);
        if (visual?.FlipX ?? (source != null && source.flipX)) scale.x *= -1f;
        if (visual?.FlipY ?? (source != null && source.flipY)) scale.y *= -1f;
        return new DroppedItemVisual
        {
            Sprite = sprite, Vertices = sprite.vertices, Uvs = sprite.uv, Triangles = sprite.triangles,
            SpriteMaterial = definition?.Material ?? (source != null ? source.sharedMaterial :
                Resources.Load<Material>("DroppedItems/DroppedItemLit")),
            LocalPosition = position, LocalRotation = rotation, LocalScale = scale,
            LocalMatrix = Matrix4x4.TRS(position, rotation, scale),
            Color = visual?.Color ?? (source != null ? source.color : Color.white),
            Layer = !string.IsNullOrWhiteSpace(visual?.SortingLayerName)
                ? SortingLayer.NameToID(visual.SortingLayerName) : source != null ? source.sortingLayerID : 0,
            Order = visual?.SortingOrder ?? (source != null ? source.sortingOrder : 0)
        };
    }
}

/// <summary>
/// 按空间行与材质合并的只读表现。每批一个 MeshRenderer，无逐掉落物 GameObject；
/// 静态网格只在内容或水线改变时上传，循环世界通过批次根节点的最近镜像显示。
/// </summary>
internal sealed class DroppedItemPresentation : IDisposable
{
    private readonly struct BatchKey : IEquatable<BatchKey>
    {
        public readonly int X, Y, Texture, Layer, Order;
        public BatchKey(DroppedBody body, DroppedItemVisual visual)
        {
            X = Mathf.FloorToInt(body.Position.x / 8f); Y = Mathf.FloorToInt(body.Position.y);
            Texture = visual.Sprite.texture.GetInstanceID(); Layer = visual.Layer; Order = visual.Order;
        }
        public bool Equals(BatchKey other) => X == other.X && Y == other.Y && Texture == other.Texture && Layer == other.Layer && Order == other.Order;
        public override bool Equals(object obj) => obj is BatchKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Texture, Layer, Order);
        public Vector3 Origin => new(X * 8f + 4f, Y + 0.5f, 0f);
    }

    private sealed class Batch
    {
        public readonly HashSet<int> Ids = new();
        public bool Dirty = true;
        public GameObject Root;
        public Mesh Mesh;
        public MeshRenderer Renderer;
        public Texture Texture;
        public readonly MaterialPropertyBlock Properties = new();
        public float WaterOffset = float.NaN;
    }

    private readonly Scene scene;
    private readonly Material template;
    private readonly DroppedItemSimulation simulation;
    private readonly Dictionary<int, DroppedItemVisual> visuals;
    private readonly Dictionary<BatchKey, Batch> batches = new();
    private readonly Dictionary<int, BatchKey> ownership = new();
    private readonly Dictionary<int, Material> materials = new();
    private readonly List<Vector3> vertices = new();
    private readonly List<Vector2> uvs = new();
    private readonly List<Color> colors = new();
    private readonly List<Vector4> water = new();
    private readonly List<int> triangles = new();
    private readonly List<int> sortedIds = new();
    public int VisibleBatchCount { get; private set; }

    public DroppedItemPresentation(Scene scene, DroppedItemSimulation simulation, Dictionary<int, DroppedItemVisual> visuals)
    {
        this.scene = scene; this.simulation = simulation; this.visuals = visuals;
        template = Resources.Load<Material>("DroppedItems/DroppedItemLit");
        if (template == null) throw new InvalidOperationException("缺少掉落物共享材质 DroppedItems/DroppedItemLit。");
    }

    #region 增量登记

    public void Changed(int id)
    {
        DroppedBody body = simulation.Get(id);
        BatchKey key = new(body, visuals[id]);
        if (ownership.TryGetValue(id, out BatchKey old))
        {
            if (old.Equals(key)) { batches[old].Dirty = true; return; }
            Remove(id);
        }
        if (!batches.TryGetValue(key, out Batch batch))
            batches.Add(key, batch = new Batch { Texture = visuals[id].Sprite.texture });
        batch.Ids.Add(id); batch.Dirty = true; ownership[id] = key;
    }

    public void Remove(int id)
    {
        if (!ownership.TryGetValue(id, out BatchKey key)) return;
        ownership.Remove(id);
        Batch batch = batches[key]; batch.Ids.Remove(id); batch.Dirty = true;
        if (batch.Ids.Count != 0) return;
        DestroyBatch(batch); batches.Remove(key);
    }

    #endregion

    #region 只读批量绘制

    public void Present(Camera camera, WorldTopologyDomain domain)
    {
        VisibleBatchCount = 0;
        if (camera == null) return;
        Vector2 center = camera.transform.position;
        float halfHeight = camera.orthographic ? camera.orthographicSize : 50f;
        foreach (KeyValuePair<BatchKey, Batch> pair in batches)
        {
            Batch batch = pair.Value;
            Vector3 origin = pair.Key.Origin;
            Unity.Mathematics.float2 nearest = domain.NearestImagePosition(center, (Vector2)origin);
            bool visible = Mathf.Abs(nearest.x - center.x) <= halfHeight * camera.aspect + 12f &&
                           Mathf.Abs(nearest.y - center.y) <= halfHeight + 12f;
            if (!visible)
            {
                if (batch.Root != null) DestroyBatch(batch);
                continue;
            }
            EnsureBatch(pair.Key, batch);
            if (batch.Dirty) Rebuild(pair.Key, batch);
            batch.Root.transform.position = new Vector3(nearest.x, nearest.y, 0f);
            float waterOffset = nearest.y - origin.y;
            if (batch.WaterOffset != waterOffset)
            {
                batch.WaterOffset = waterOffset;
                batch.Properties.SetFloat("_WaterLineOffset", waterOffset);
                batch.Renderer.SetPropertyBlock(batch.Properties);
            }
            batch.Renderer.enabled = true;
            VisibleBatchCount++;
        }
    }

    private void EnsureBatch(BatchKey key, Batch batch)
    {
        if (batch.Root != null) return;
        batch.Root = new GameObject("ECS掉落物_共享批次") { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(batch.Root, scene);
        batch.Mesh = new Mesh { name = "ECS掉落物_增量网格", indexFormat = IndexFormat.UInt32 };
        batch.Mesh.MarkDynamic();
        batch.Root.AddComponent<MeshFilter>().sharedMesh = batch.Mesh;
        batch.Renderer = batch.Root.AddComponent<MeshRenderer>();
        if (!materials.TryGetValue(key.Texture, out Material material))
        {
            material = new Material(template) { name = "ECS掉落物_共享贴图材质", mainTexture = batch.Texture };
            materials.Add(key.Texture, material);
        }
        batch.Renderer.sharedMaterial = material;
        batch.Renderer.sortingLayerID = key.Layer; batch.Renderer.sortingOrder = key.Order;
        batch.Renderer.shadowCastingMode = ShadowCastingMode.Off;
        batch.Renderer.receiveShadows = false;
        batch.Renderer.lightProbeUsage = LightProbeUsage.Off;
        batch.Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        batch.Dirty = true;
    }

    private void Rebuild(BatchKey key, Batch batch)
    {
        vertices.Clear(); uvs.Clear(); colors.Clear(); water.Clear(); triangles.Clear();
        sortedIds.Clear(); sortedIds.AddRange(batch.Ids);
        sortedIds.Sort((left, right) =>
        {
            int result = simulation.Get(right).Position.y.CompareTo(simulation.Get(left).Position.y);
            return result != 0 ? result : left.CompareTo(right);
        });
        foreach (int id in sortedIds)
        {
            DroppedBody body = simulation.Get(id);
            DroppedItemVisual visual = visuals[id];
            Vector3 position = new(body.Position.x, body.Position.y + body.VisualHeight, 0f);
            float submergedScale = WorldItemWaterRules.ResolveSubmergedScale(body.SubmergedProgress);
            Matrix4x4 matrix = Matrix4x4.TRS(position - key.Origin, Quaternion.Euler(0f, 0f, body.Rotation),
                new Vector3(body.Scale.x * submergedScale, body.Scale.y * submergedScale, 1f)) * visual.LocalMatrix;
            int offset = vertices.Count;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            for (int i = 0; i < visual.Vertices.Length; i++)
            {
                Vector3 vertex = matrix.MultiplyPoint3x4(visual.Vertices[i]);
                vertices.Add(vertex); uvs.Add(visual.Uvs[i]); colors.Add(visual.Color);
                minY = Mathf.Min(minY, vertex.y); maxY = Mathf.Max(maxY, vertex.y);
            }
            float height = Mathf.Max(0.0001f, maxY - minY);
            // 水线保持规范世界坐标；循环镜像仅通过批次 MPB 补偏移，不依赖动态合批后的 ObjectToWorld。
            Vector4 parameters = new(body.WaterKind != 0 ? 1f : 0f,
                key.Origin.y + minY + height * body.WaterDepth, height, 0f);
            for (int i = 0; i < visual.Vertices.Length; i++) water.Add(parameters);
            for (int i = 0; i < visual.Triangles.Length; i++) triangles.Add(offset + visual.Triangles[i]);
        }
        batch.Mesh.Clear(); batch.Mesh.SetVertices(vertices); batch.Mesh.SetUVs(0, uvs);
        batch.Mesh.SetColors(colors); batch.Mesh.SetUVs(1, water); batch.Mesh.SetTriangles(triangles, 0, true);
        batch.Dirty = false;
    }

    #endregion

    private static void Release(UnityEngine.Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(target); else UnityEngine.Object.DestroyImmediate(target);
    }

    private static void DestroyBatch(Batch batch)
    {
        if (batch.Renderer != null) batch.Renderer.enabled = false;
        Release(batch.Mesh); Release(batch.Root);
        batch.Mesh = null; batch.Root = null; batch.Renderer = null; batch.Dirty = true;
        batch.WaterOffset = float.NaN;
    }

    public void Dispose()
    {
        foreach (Batch batch in batches.Values) DestroyBatch(batch);
        foreach (Material material in materials.Values) Release(material);
        batches.Clear(); ownership.Clear(); materials.Clear();
    }
}
