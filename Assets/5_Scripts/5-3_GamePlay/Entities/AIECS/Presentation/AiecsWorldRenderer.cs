using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS
{
    /// <summary>
    /// 正式模拟的只读批量表现；复用 P1 图集/网格，不使用原型运动，不为每个单位创建对象。
    /// 开发显示按相机高度分为 24 行批次，行内按 Y 稳定排序；与旧透明对象的逐像素交错属于后续正式排序接入。
    /// </summary>
    public sealed class AiecsWorldRenderer : IDisposable
    {
        private struct DrawItem
        {
            public int Index, Visual, Row, Layer, Order; public float Y;
        }
        private sealed class DrawComparer : IComparer<DrawItem>
        {
            public static readonly DrawComparer Instance = new DrawComparer();
            /// <summary>图层、行和 Y 排序保持同屏对象的可解释前后关系。</summary>
            public int Compare(DrawItem a, DrawItem b)
            {
                int result = a.Layer.CompareTo(b.Layer);
                if (result == 0) result = a.Order.CompareTo(b.Order);
                if (result == 0) result = b.Row.CompareTo(a.Row);
                if (result == 0) result = b.Y.CompareTo(a.Y);
                return result == 0 ? a.Index.CompareTo(b.Index) : result;
            }
        }
        private readonly AiecsAnimationCatalog catalog;
        private readonly int[] visuals;
        private readonly int[,] clips;
        private readonly List<AiecsRenderBatch> batches = new List<AiecsRenderBatch>();
        private readonly List<DrawItem> visible = new List<DrawItem>();
        private readonly UnityEngine.SceneManagement.Scene scene;
        private readonly AiecsShadowRenderer shadows;
        private readonly AiecsSunShadowRenderer sunShadows;
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

        /// <summary>一次匹配当前定义与实际导出动画；缺少动画目录时明确报告，不回退为每只生物 SpriteRenderer。</summary>
        public AiecsWorldRenderer(AiecsAnimationCatalog catalog, string[] actorIds, UnityEngine.SceneManagement.Scene scene)
        {
            this.catalog = catalog; this.scene = scene;
            if (catalog == null || catalog.Material == null) throw new InvalidOperationException("请先导出 AIECS 动画目录。");
            visuals = new int[actorIds.Length]; clips = new int[actorIds.Length, 4];
            shadowFootprints = new Vector4[actorIds.Length];
            sunShadowHeights = new float[actorIds.Length];
            for (int i = 0; i < actorIds.Length; i++)
            {
                sunShadowHeights[i] = 1f;
                visuals[i] = Array.FindIndex(catalog.Actors, value => value.Id == actorIds[i]);
                if (visuals[i] < 0) throw new InvalidOperationException("AIECS 动画目录缺少 " + actorIds[i]);
                var definition = catalog.Actors[visuals[i]];
                clips[i, 0] = FindClip(definition, "Stand", "Idle");
                clips[i, 1] = FindClip(definition, "Move", "Walk");
                clips[i, 2] = FindClip(definition, "Attack", "Attack");
                clips[i, 3] = FindClip(definition, "Death", "Dead");
                var idle = definition.Clips[clips[i, 0]].Sample(0f);
                shadowFootprints[i] = AiecsShadowRenderer.MeasureFootprint(definition, catalog.Sprites[idle.Sprite], idle);
            }
            shadows = new AiecsShadowRenderer(scene);
            sunShadows = new AiecsSunShadowRenderer(scene, catalog.Material.mainTexture);
        }

        /// <summary>MOD 可按当前定义索引调整太阳投影高度，0 表示关闭该物种投影。</summary>
        public void SetSunShadowHeight(int definitionIndex, float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f)
                throw new ArgumentOutOfRangeException(nameof(multiplier));
            sunShadowHeights[definitionIndex] = multiplier;
        }

        /// <summary>按原始状态尾名匹配，不把缺少的功能动画当作已迁移动作。</summary>
        private static int FindClip(AiecsActorVisual visual, string first, string second)
        {
            for (int i = 0; i < visual.Clips.Length; i++)
            {
                string state = visual.Clips[i].State;
                if (state.EndsWith("." + first, StringComparison.OrdinalIgnoreCase) || state.Equals(first, StringComparison.OrdinalIgnoreCase) ||
                    state.EndsWith("." + second, StringComparison.OrdinalIgnoreCase) || state.Equals(second, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }

        /// <summary>读取真实 ECS 位置/行为/生命，循环世界只绘制相机最近镜像。</summary>
        public void Draw(AiecsSimulation simulation, Camera camera, WorldTopologyDomain domain)
        {
            if (camera == null || !simulation.Display.IsCreated)
            {
                visible.Clear(); BatchCount = 0; shadows.Hide(); sunShadows.Hide();
                foreach (var batch in batches) batch.Hide();
                return;
            }
            shadows.Begin();
            sunShadows.Begin();
            visible.Clear();
            float2 center = (Vector2)camera.transform.position;
            float halfHeight = camera.orthographicSize, halfWidth = halfHeight * camera.aspect;
            var records = simulation.Display;
            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i]; if (record.External != 0) continue;
                float2 delta = domain.ShortestDelta(center, record.Position);
                // 屏外主体的长投影仍可能落入视口，开启时扩展候选范围。
                float margin = 2f + sunShadows.CullingMargin;
                if (math.abs(delta.x) > halfWidth + margin || math.abs(delta.y) > halfHeight + margin) continue;
                int visual = visuals[record.Definition]; var definition = catalog.Actors[visual];
                int row = math.clamp((int)((delta.y + halfHeight) / math.max(0.01f, halfHeight * 2f) * 24), 0, 23);
                visible.Add(new DrawItem { Index = i, Visual = visual, Row = row, Y = delta.y,
                    Layer = definition.SortingLayerId, Order = definition.SortingOrder });
            }
            visible.Sort(DrawComparer.Instance); BatchCount = 0;
            int start = 0;
            while (start < visible.Count)
            {
                DrawItem first = visible[start]; int end = start + 1;
                while (end < visible.Count && end - start < AiecsRenderBatch.MaxSprites && visible[end].Row == first.Row &&
                    visible[end].Layer == first.Layer && visible[end].Order == first.Order) end++;
                if (BatchCount == batches.Count) batches.Add(new AiecsRenderBatch(scene, catalog.Material));
                var batch = batches[BatchCount++]; batch.Begin();
                for (int i = start; i < end; i++)
                {
                    var item = visible[i]; var record = records[item.Index]; var definition = catalog.Actors[item.Visual];
                    // 攻击前摇/后摇是明确的战斗阶段；专用素材完成前复用待机，只有 Active 播放真正攻击动作。
                    int action = record.Dead != 0 ? 3 : record.Behavior == (int)AiecsBehavior.Attack
                        ? record.AttackPhase == AiecsAttackPhase.Active ? 2 : 0
                        : record.Behavior == (int)AiecsBehavior.Idle ? 0 : 1;
                    var frame = definition.Clips[clips[record.Definition, action]].Sample(record.ActionElapsed);
                    var actor = new AiecsPrototypeActor
                    {
                        Position = center + domain.ShortestDelta(center, record.Position),
                        WaterBlend = Mathf.Clamp01(record.WaterBlend)
                    };
                    float liquidDepth = Mathf.Clamp01(record.LiquidDepth);
                    float waterTint = Mathf.Lerp(0.12f, 0.8f, liquidDepth);
                    Color color = definition.Color * (record.Group % 2 == 0 ? new Color(0.7f, 0.85f, 1f) : new Color(1f, 0.7f, 0.65f));
                    if (record.Dead != 0) color.a *= Mathf.Clamp01(2f - record.ActionElapsed);
                    // 复用真实水态和当前可见列表，绝不逐实体查询地形或创建阴影组件。
                    if (ShadowsEnabled)
                        shadows.Append(new Vector2(actor.Position.x, actor.Position.y), shadowFootprints[record.Definition],
                            record.Facing.x < 0f, AiecsShadowRenderer.ResolveOpacity(ShadowOpacity, color.a, record.LiquidDepth, record.WaterBlend));
                    if (sunShadows.Active)
                        sunShadows.Append(actor, definition, frame, catalog.Sprites[frame.Sprite], record.Facing.x < 0f,
                            color.a, actor.Position.y + shadowFootprints[record.Definition].y - 0.02f,
                            sunShadowHeights[record.Definition]);
                    batch.Append(actor, definition, frame, catalog.Sprites[frame.Sprite], liquidDepth, waterTint,
                        record.Facing.x < 0f, color);
                }
                batch.Submit(first.Layer, first.Order); start = end;
            }
            for (int i = BatchCount; i < batches.Count; i++) batches[i].Hide();
            shadows.End();
            sunShadows.End();
        }

        /// <summary>开发 HUD 最多绘制 64 个可见实体血条，不创建逐实体 UI 节点。</summary>
        public void DrawHealth(AiecsSimulation simulation, Camera camera, WorldTopologyDomain domain)
        {
            // 无 GUILayout 控件；布局/输入事件不重复进行坐标换算与批量血条遍历。
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (camera == null || !simulation.Display.IsCreated) return;
            Color previous = GUI.color; float2 center = (Vector2)camera.transform.position;
            for (int i = 0; i < math.min(64, visible.Count); i++)
            {
                var actor = simulation.Display[visible[i].Index]; if (actor.Dead != 0) continue;
                float2 position = center + domain.ShortestDelta(center, actor.Position) + new float2(0, 0.8f);
                Vector3 point = camera.WorldToScreenPoint(new Vector3(position.x, position.y, 0f));
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
            shadows.Dispose();
            sunShadows.Dispose();
            foreach (var batch in batches) batch.Dispose();
            batches.Clear(); visible.Clear(); BatchCount = 0;
        }
    }
}
