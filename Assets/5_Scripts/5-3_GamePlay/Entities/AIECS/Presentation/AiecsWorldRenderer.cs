using System;
using FlatWorld.Combat;
using Unity.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS
{
    /// <summary>当前物种视觉定义的 Unity 排序键。</summary>
    public readonly struct AiecsActorSortingKey
    {
        public readonly int Layer;
        public readonly int Order;
        public AiecsActorSortingKey(int layer, int order) { Layer = layer; Order = order; }
    }

    /// <summary>由世界排序管理器注入的生物主体及地表阴影排序键，不读取导出资源中的旧层级。</summary>
    public readonly struct AiecsWorldSortingKeys
    {
        public readonly AiecsActorSortingKey[] Actors;
        public readonly int ShadowLayer;
        public readonly int ShadowOrder;

        public AiecsWorldSortingKeys(AiecsActorSortingKey[] actors, int shadowLayer, int shadowOrder)
        {
            Actors = actors;
            ShadowLayer = shadowLayer;
            ShadowOrder = shadowOrder;
        }
    }

    /// <summary>
    /// 正式模拟的只读批量表现；主体按真实排序键和共同脚点合并网格。
    /// </summary>
    public sealed class AiecsWorldRenderer : IDisposable
    {
        #region ECS生物摇摆批量表现
        private sealed class SwayEntry
        {
            public AiecsSwayState State;
            public int LastDraw;
        }
        private struct DrawItem
        {
            public int Index, Visual, Layer, LayerValue, Order; public float Y;
        }
        private sealed class DrawComparer : IComparer<DrawItem>
        {
            public static readonly DrawComparer Instance = new DrawComparer();
            /// <summary>图层、顺序和脚点 Y 保持同屏对象的前后关系。</summary>
            public int Compare(DrawItem a, DrawItem b)
            {
                int result = a.LayerValue.CompareTo(b.LayerValue);
                if (result == 0) result = a.Order.CompareTo(b.Order);
                if (result == 0) result = b.Y.CompareTo(a.Y);
                return result == 0 ? a.Index.CompareTo(b.Index) : result;
            }
        }
        private readonly AiecsAnimationCatalog catalog;
        private readonly int[] visuals;
        private readonly AiecsSwayAnimation[] animations;
        private readonly Dictionary<CombatIdentity, SwayEntry> animationStates = new();
        private readonly List<CombatIdentity> expiredStates = new();
        private int drawSequence;
        private int lastAnimationFrame = -1;
        private readonly List<AiecsRenderBatch> batches = new();
        private readonly List<DrawItem> visible = new List<DrawItem>();
        private readonly UnityEngine.SceneManagement.Scene scene;
        private readonly Transform presentationRoot;
        private Matrix4x4 worldToDisplay = Matrix4x4.identity;
        private bool presentationVisible = true;
        private Action<Renderer, int, int, int, float> applySortingKey;
        private Action<Renderer> removeSortingKey;
        private readonly AiecsShadowRenderer shadows;
        private readonly AiecsSunShadowRenderer sunShadows;
        private readonly AiecsActorSortingKey[] actorSortingKeys;
        private readonly int[] actorSortingLayerValues;
        private readonly float[] sunShadowHeights; // 每物种高度覆盖，0 关闭；不进入模拟或存档。
        private readonly Vector4[] shadowFootprints;
        public bool ShadowsEnabled { get; set; } = true; // 表现开关，不影响模拟。
        public float ShadowOpacity { get; set; } = 0.4f; // 宿主按场景提供昼夜强度。
        public int ShadowCount => shadows.ShadowCount;
        public int ShadowBatchCount => shadows.BatchCount;
        public int SunShadowCount => sunShadows.ShadowCount;
        public int SunShadowBatchCount => sunShadows.BatchCount;
        public int VisibleCount => visible.Count;
        public int BatchCount { get; private set; }
        public int UploadedBytes { get; private set; }
        public Transform PresentationRoot => presentationRoot;
        public Matrix4x4 WorldToDisplay => worldToDisplay;
        public bool PresentationVisible => presentationVisible;
        public event Action Disposed;
        private bool disposed;

        /// <summary>一次匹配当前定义与实际导出动画；缺少动画目录时明确报告，不回退为每只生物 SpriteRenderer。</summary>
        public AiecsWorldRenderer(AiecsAnimationCatalog catalog, string[] actorIds,
            UnityEngine.SceneManagement.Scene scene, AiecsWorldSortingKeys sortingKeys)
        {
            this.catalog = catalog; this.scene = scene;
            if (catalog == null || catalog.Material == null) throw new InvalidOperationException("请先导出 AIECS 动画目录。");
            visuals = new int[actorIds.Length];
            actorSortingKeys = new AiecsActorSortingKey[actorIds.Length];
            actorSortingLayerValues = new int[actorIds.Length];
            SetActorSortingKeys(sortingKeys.Actors);
            animations = new AiecsSwayAnimation[actorIds.Length];
            shadowFootprints = new Vector4[actorIds.Length];
            sunShadowHeights = new float[actorIds.Length];
            for (int i = 0; i < actorIds.Length; i++)
            {
                sunShadowHeights[i] = 1f;
                visuals[i] = Array.FindIndex(catalog.Actors, value => value.Id == actorIds[i]);
                if (visuals[i] < 0) throw new InvalidOperationException("AIECS 动画目录缺少 " + actorIds[i]);
                var definition = catalog.Actors[visuals[i]];
                animations[i] = new AiecsSwayAnimation(definition, catalog.Sprites);
                var idle = animations[i].StandFrame;
                shadowFootprints[i] = AiecsShadowRenderer.MeasureFootprint(definition, catalog.Sprites[idle.Sprite], idle);
            }
            var root = new GameObject("AIECS 世界表现") { hideFlags = HideFlags.DontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            presentationRoot = root.transform;
            try
            {
                shadows = new AiecsShadowRenderer(scene, sortingKeys.ShadowLayer, sortingKeys.ShadowOrder, presentationRoot);
                sunShadows = new AiecsSunShadowRenderer(scene, catalog.Material.mainTexture,
                    sortingKeys.ShadowLayer, sortingKeys.ShadowOrder, presentationRoot);
            }
            catch
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(root);
                else UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }

        /// <summary>显示世界只改变批次的排序归属与投影，不移动 ECS 模拟坐标。</summary>
        public void SetWorldPresentation(Transform parent, Matrix4x4 projection, bool visible)
        {
            if (disposed || presentationRoot == null) return;
            worldToDisplay = projection;
            bool wasVisible = presentationVisible;
            presentationVisible = visible;
            if (presentationRoot.parent != parent)
            {
                presentationRoot.SetParent(parent, true);
                if (parent == null && scene.IsValid() && scene.isLoaded && presentationRoot.gameObject.scene != scene)
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(presentationRoot.gameObject, scene);
            }
            // 域根保持世界轴对齐和单位缩放，顶点使用显示世界坐标，避免重复投影。
            if (presentationRoot.position != Vector3.zero || presentationRoot.rotation != Quaternion.identity)
                presentationRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (presentationRoot.localScale != Vector3.one) presentationRoot.localScale = Vector3.one;
            if (presentationRoot.gameObject.activeSelf != visible) presentationRoot.gameObject.SetActive(visible);
            if (wasVisible && !visible) HideDrawnBatches();
        }

        /// <summary>由所属显示世界注入临时排序回调，表现程序集不依赖具体世界类型。</summary>
        public void SetSortingKeyCallbacks(Action<Renderer, int, int, int, float> apply, Action<Renderer> remove)
        {
            if (ReferenceEquals(applySortingKey, apply) && ReferenceEquals(removeSortingKey, remove)) return;
            foreach (AiecsRenderBatch batch in batches) batch.RemoveSortingKey(removeSortingKey);
            applySortingKey = apply;
            removeSortingKey = remove;
            shadows.SetSortingKeyCallbacks(apply, remove);
            sunShadows.SetSortingKeyCallbacks(apply, remove);
        }

        private void HideDrawnBatches()
        {
            visible.Clear(); BatchCount = UploadedBytes = 0; shadows.Hide(); sunShadows.Hide();
            foreach (AiecsRenderBatch old in batches) { old.Hide(); old.RemoveSortingKey(removeSortingKey); }
            animationStates.Clear();
        }

        /// <summary>资源发布时一次更新各物种排序，不在绘制循环读取定义目录。</summary>
        public void SetActorSortingKeys(AiecsActorSortingKey[] keys)
        {
            if (keys == null || keys.Length != actorSortingKeys.Length)
                throw new ArgumentException("AIECS 物种排序键必须与当前物种目录一一对应。", nameof(keys));
            for (int i = 0; i < keys.Length; i++)
            {
                actorSortingKeys[i] = keys[i];
                actorSortingLayerValues[i] = SortingLayer.GetLayerValueFromID(keys[i].Layer);
            }
        }

        /// <summary>MOD 可按当前定义索引调整太阳投影高度，0 表示关闭该物种投影。</summary>
        public void SetSunShadowHeight(int definitionIndex, float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f)
                throw new ArgumentOutOfRangeException(nameof(multiplier));
            sunShadowHeights[definitionIndex] = multiplier;
        }

        /// <summary>读取真实 ECS 位置/行为/生命，循环世界只绘制相机最近镜像。</summary>
        public void Draw(AiecsSimulation simulation, Camera camera, WorldTopologyDomain domain,
            Func<Vector2, bool> presentationReady = null)
            => DrawRecords(simulation.Display, camera, domain, presentationReady);

        /// <summary>联机呈现直接读取服务器表现快照，不运行第二套动物模拟。</summary>
        public void DrawRecords(NativeArray<AiecsDisplayRecord> records, Camera camera, WorldTopologyDomain domain,
            Func<Vector2, bool> presentationReady = null)
        {
            if (camera == null || !records.IsCreated || !presentationVisible || presentationRoot == null)
            {
                HideDrawnBatches();
                return;
            }
            shadows.Begin();
            sunShadows.Begin();
            visible.Clear();
            SetWorldPresentation(presentationRoot.parent, worldToDisplay, presentationVisible);
            bool projected = !worldToDisplay.Equals(Matrix4x4.identity);
            Matrix4x4 displayToWorld = projected ? worldToDisplay.inverse : Matrix4x4.identity;
            float projectedWidth = projected ? worldToDisplay.MultiplyVector(Vector3.right).magnitude : 1f;
            float projectedHeight = projected ? worldToDisplay.MultiplyVector(Vector3.up).magnitude : 1f;
            float2 center = (Vector2)displayToWorld.MultiplyPoint3x4(camera.transform.position);
            float halfHeight = camera.orthographicSize, halfWidth = halfHeight * camera.aspect;
            float margin = 2f + sunShadows.CullingMargin;
            Vector3 horizontal = displayToWorld.MultiplyVector(new Vector3(halfWidth + margin, 0f));
            Vector3 vertical = displayToWorld.MultiplyVector(new Vector3(0f, halfHeight + margin));
            float xRadius = Mathf.Abs(horizontal.x) + Mathf.Abs(vertical.x);
            float yRadius = Mathf.Abs(horizontal.y) + Mathf.Abs(vertical.y);
            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i]; if (record.External != 0) continue;
                float2 delta = domain.ShortestDelta(center, record.Position);
                // 屏外主体的长投影仍可能落入视口，开启时扩展候选范围。
                if (math.abs(delta.x) > xRadius || math.abs(delta.y) > yRadius) continue;
                // ECS 居民可以继续在纯逻辑层存活，但 ChunkView 一旦离开本机表现窗口就停止提交主体和阴影。
                if (presentationReady != null &&
                    !presentationReady(new Vector2(record.Position.x, record.Position.y)))
                    continue;
                int visual = visuals[record.Definition];
                AiecsActorSortingKey key = actorSortingKeys[record.Definition];
                Vector3 ground = new Vector3(center.x + delta.x, center.y + delta.y, 0f);
                float sortingY = projected ? worldToDisplay.MultiplyPoint3x4(ground).y : ground.y;
                visible.Add(new DrawItem { Index = i, Visual = visual, Y = sortingY,
                    Layer = key.Layer, LayerValue = actorSortingLayerValues[record.Definition], Order = key.Order });
            }
            visible.Sort(DrawComparer.Instance);
            AiecsRenderBatch batch = null;
            DrawItem batchKey = default;
            int batchItems = 0, batchCount = 0;
            int uploadedBytes = 0;
            drawSequence++;
            float animationDelta = lastAnimationFrame == Time.frameCount ? 0f : Time.deltaTime;
            lastAnimationFrame = Time.frameCount;
            for (int i = 0; i < visible.Count; i++)
            {
                var item = visible[i]; var record = records[item.Index]; var definition = catalog.Actors[item.Visual];
                if (batch == null || batchItems == AiecsRenderBatch.MaxSprites || batchKey.Layer != item.Layer ||
                    batchKey.Order != item.Order || batchKey.Y != item.Y)
                {
                    if (batch != null) uploadedBytes += SubmitBatch(batch, batchKey);
                    if (batchCount == batches.Count) batches.Add(new AiecsRenderBatch(scene, catalog.Material, presentationRoot));
                    batch = batches[batchCount++];
                    batch.SetWaterProjection(projected, displayToWorld);
                    batch.Begin(); batchKey = item; batchItems = 0;
                }
                if (!animationStates.TryGetValue(record.Key, out SwayEntry entry))
                    animationStates.Add(record.Key, entry = new SwayEntry());
                entry.LastDraw = drawSequence;
                AiecsSwayPose pose = animations[record.Definition].Sample(ref entry.State, record, animationDelta);
                var frame = pose.Frame;
                float2 ground = center + domain.ShortestDelta(center, record.Position);
                var actor = new AiecsPrototypeActor
                {
                    Position = ground,
                    WaterBlend = Mathf.Clamp01(record.WaterBlend)
                };
                float liquidDepth = Mathf.Clamp01(record.LiquidDepth);
                float waterTint = Mathf.Lerp(0.12f, 0.8f, liquidDepth);
                Color color = definition.Color;
                if (record.Dead != 0) color.a *= Mathf.Clamp01(2f - record.ActionElapsed);
                // 复用真实水态和当前可见列表，绝不逐实体查询地形或创建阴影组件。
                if (ShadowsEnabled)
                {
                    Vector4 footprint = shadowFootprints[record.Definition];
                    Vector2 shadowPosition = new Vector2(ground.x, ground.y);
                    bool mirror = pose.Mirror;
                    if (projected)
                    {
                        shadowPosition = worldToDisplay.MultiplyPoint3x4(new Vector3(
                            ground.x + (mirror ? -footprint.x : footprint.x), ground.y + footprint.y, 0f));
                        footprint = new Vector4(0f, 0f, footprint.z * projectedWidth, footprint.w * projectedHeight);
                        mirror = false;
                    }
                    shadows.Append(shadowPosition, footprint, mirror,
                        AiecsShadowRenderer.ResolveOpacity(ShadowOpacity, color.a, record.LiquidDepth, record.WaterBlend));
                }
                if (sunShadows.Active)
                {
                    Vector4 footprint = shadowFootprints[record.Definition];
                    float footY = ground.y + footprint.y - 0.02f;
                    if (projected) footY = worldToDisplay.MultiplyPoint3x4(new Vector3(
                        ground.x + (pose.Mirror ? -footprint.x : footprint.x), footY, 0f)).y;
                    sunShadows.Append(actor, definition, frame, catalog.Sprites[frame.Sprite], pose.Mirror,
                        color.a, footY, sunShadowHeights[record.Definition], projected ? (Matrix4x4?)worldToDisplay : null);
                }
                batch.Append(actor, definition, frame, catalog.Sprites[frame.Sprite], liquidDepth, waterTint,
                    pose.Mirror, color, projected ? (Matrix4x4?)worldToDisplay : null);
                batchItems++;
            }
            // 离开可见列表就释放实体动作，身份换槽不会继承别的生物的姿态。
            expiredStates.Clear();
            foreach (var pair in animationStates)
                if (pair.Value.LastDraw != drawSequence) expiredStates.Add(pair.Key);
            foreach (CombatIdentity key in expiredStates) animationStates.Remove(key);
            if (batch != null) uploadedBytes += SubmitBatch(batch, batchKey);
            for (int i = batchCount; i < batches.Count; i++)
            {
                batches[i].Hide();
                batches[i].RemoveSortingKey(removeSortingKey);
            }
            BatchCount = batchCount;
            UploadedBytes = uploadedBytes;
            shadows.End();
            sunShadows.End();
        }

        private int SubmitBatch(AiecsRenderBatch batch, DrawItem key)
        {
            int bytes = batch.Submit(key.Layer, key.Order, key.Y);
            batch.SubmitSortingKey(applySortingKey, key.Layer, key.Order, key.Y);
            return bytes;
        }

        /// <summary>开发 HUD 最多绘制 64 个可见实体血条，不创建逐实体 UI 节点。</summary>
        public void DrawHealth(AiecsSimulation simulation, Camera camera, WorldTopologyDomain domain)
        {
            // 无 GUILayout 控件；布局/输入事件不重复进行坐标换算与批量血条遍历。
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (camera == null || !simulation.Display.IsCreated) return;
            Color previous = GUI.color;
            Matrix4x4 displayToWorld = worldToDisplay.inverse;
            float2 center = (Vector2)displayToWorld.MultiplyPoint3x4(camera.transform.position);
            for (int i = 0; i < math.min(64, visible.Count); i++)
            {
                var actor = simulation.Display[visible[i].Index]; if (actor.Dead != 0) continue;
                float2 position = center + domain.ShortestDelta(center, actor.Position) +
                    new float2(0, 0.8f + actor.FlightHeight);
                Vector3 point = camera.WorldToScreenPoint(worldToDisplay.MultiplyPoint3x4(new Vector3(position.x, position.y, 0f)));
                Rect rect = new Rect(point.x - 20f, Screen.height - point.y, 40f, 5f);
                GUI.color = Color.black; GUI.DrawTexture(rect, Texture2D.whiteTexture);
                rect.width *= math.saturate(actor.Hp / math.max(1f, actor.MaxHp));
                GUI.color = actor.Group % 2 == 0 ? Color.cyan : new Color(1f, 0.35f, 0.2f); GUI.DrawTexture(rect, Texture2D.whiteTexture);
            }
            GUI.color = previous;
        }

        /// <summary>释放该表现入口创建的批次，保留共享内容资源。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SetSortingKeyCallbacks(null, null);
            // 阴影节点清理失败也必须释放主体网格与排序订阅。
            try { shadows.Dispose(); }
            finally
            {
                try { sunShadows.Dispose(); }
                finally
                {
                    try { foreach (AiecsRenderBatch batch in batches) batch.Dispose(); }
                    finally
                    {
                        batches.Clear();
                        if (presentationRoot != null)
                        {
                            if (Application.isPlaying) UnityEngine.Object.Destroy(presentationRoot.gameObject);
                            else UnityEngine.Object.DestroyImmediate(presentationRoot.gameObject);
                        }
                        animationStates.Clear(); expiredStates.Clear();
                        visible.Clear(); BatchCount = UploadedBytes = 0;
                        Action onDisposed = Disposed;
                        Disposed = null;
                        onDisposed?.Invoke();
                    }
                }
            }
        }
        #endregion
    }
}
