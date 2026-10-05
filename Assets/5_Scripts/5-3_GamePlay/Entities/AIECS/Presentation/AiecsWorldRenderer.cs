using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS
{
    /// <summary>由世界排序管理器注入的生物主体及地表阴影排序键，不读取导出资源中的旧层级。</summary>
    public readonly struct AiecsWorldSortingKeys
    {
        public readonly int ActorLayer;
        public readonly int ActorOrder;
        public readonly int ShadowLayer;
        public readonly int ShadowOrder;

        public AiecsWorldSortingKeys(int actorLayer, int actorOrder, int shadowLayer, int shadowOrder)
        {
            ActorLayer = actorLayer;
            ActorOrder = actorOrder;
            ShadowLayer = shadowLayer;
            ShadowOrder = shadowOrder;
        }
    }

    /// <summary>
    /// 正式模拟的只读批量表现；主体交给独立 BRG，共享图集、网格和实例缓冲。
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
        private const int StandClip = 0;
        private const int MoveClip = 1;
        private const int AttackClip = 2;
        private const int DeathClip = 3;
        private const int TakingOffClip = 4;
        private const int FlyingClip = 5;
        private const int LandingClip = 6;
        private readonly AiecsBatchRendererGroup batch;
        private readonly List<DrawItem> visible = new List<DrawItem>();
        private readonly UnityEngine.SceneManagement.Scene scene;
        private readonly AiecsShadowRenderer shadows;
        private readonly AiecsSunShadowRenderer sunShadows;
        private readonly AiecsWorldSortingKeys sortingKeys;
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
        public AiecsWorldRenderer(AiecsAnimationCatalog catalog, string[] actorIds,
            UnityEngine.SceneManagement.Scene scene, AiecsWorldSortingKeys sortingKeys)
        {
            this.catalog = catalog; this.scene = scene; this.sortingKeys = sortingKeys;
            if (catalog == null || catalog.Material == null) throw new InvalidOperationException("请先导出 AIECS 动画目录。");
            visuals = new int[actorIds.Length]; clips = new int[actorIds.Length, 7];
            shadowFootprints = new Vector4[actorIds.Length];
            sunShadowHeights = new float[actorIds.Length];
            for (int i = 0; i < actorIds.Length; i++)
            {
                sunShadowHeights[i] = 1f;
                visuals[i] = Array.FindIndex(catalog.Actors, value => value.Id == actorIds[i]);
                if (visuals[i] < 0) throw new InvalidOperationException("AIECS 动画目录缺少 " + actorIds[i]);
                var definition = catalog.Actors[visuals[i]];
                clips[i, StandClip] = FindClip(definition, "Stand", "Idle");
                clips[i, MoveClip] = FindClip(definition, "Move", "Walk");
                clips[i, AttackClip] = FindClip(definition, "Attack", "Attack");
                clips[i, DeathClip] = FindClip(definition, "Death", "Dead");
                clips[i, TakingOffClip] = FindClip(definition, "TakingOff", "TakeOff");
                clips[i, FlyingClip] = FindClip(definition, "Flying", "Fly");
                clips[i, LandingClip] = FindClip(definition, "Landing", "Land");
                var idle = definition.Clips[clips[i, StandClip]].Sample(0f);
                shadowFootprints[i] = AiecsShadowRenderer.MeasureFootprint(definition, catalog.Sprites[idle.Sprite], idle);
            }
            shadows = new AiecsShadowRenderer(scene, sortingKeys.ShadowLayer, sortingKeys.ShadowOrder);
            sunShadows = new AiecsSunShadowRenderer(scene, catalog.Material.mainTexture,
                sortingKeys.ShadowLayer, sortingKeys.ShadowOrder);
            batch = new AiecsBatchRendererGroup(catalog.Material);
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
        public void Draw(AiecsSimulation simulation, Camera camera, WorldTopologyDomain domain,
            Func<Vector2, bool> presentationReady = null)
        {
            if (camera == null || !simulation.Display.IsCreated)
            {
                visible.Clear(); BatchCount = 0; shadows.Hide(); sunShadows.Hide();
                batch.Hide();
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
                // ECS 居民可以继续在纯逻辑层存活，但 ChunkView 一旦离开本机表现窗口就停止提交主体和阴影。
                if (presentationReady != null &&
                    !presentationReady(new Vector2(record.Position.x, record.Position.y)))
                    continue;
                int visual = visuals[record.Definition];
                int row = math.clamp((int)((delta.y + halfHeight) / math.max(0.01f, halfHeight * 2f) * 24), 0, 23);
                visible.Add(new DrawItem { Index = i, Visual = visual, Row = row, Y = delta.y,
                    Layer = sortingKeys.ActorLayer, Order = sortingKeys.ActorOrder });
            }
            visible.Sort(DrawComparer.Instance);
            batch.Begin();
            for (int i = 0; i < visible.Count; i++)
            {
                var item = visible[i]; var record = records[item.Index]; var definition = catalog.Actors[item.Visual];
                    int action = ResolveClip(record);
                    AiecsAnimationClip clip = definition.Clips[clips[record.Definition, action]];
                    float sampleTime = ResolveSampleTime(record, action, clip);
                    var frame = clip.Sample(sampleTime);
                    float2 ground = center + domain.ShortestDelta(center, record.Position);
                    var actor = new AiecsPrototypeActor
                    {
                        Position = ground + new float2(0f, record.FlightHeight),
                        WaterBlend = Mathf.Clamp01(record.WaterBlend)
                    };
                    float liquidDepth = Mathf.Clamp01(record.LiquidDepth);
                    float waterTint = Mathf.Lerp(0.12f, 0.8f, liquidDepth);
                    Color color = definition.Color;
                    if (record.Dead != 0) color.a *= Mathf.Clamp01(2f - record.ActionElapsed);
                    // 复用真实水态和当前可见列表，绝不逐实体查询地形或创建阴影组件。
                    if (ShadowsEnabled)
                        shadows.Append(new Vector2(ground.x, ground.y), shadowFootprints[record.Definition],
                            record.Facing.x < 0f, AiecsShadowRenderer.ResolveOpacity(ShadowOpacity, color.a, record.LiquidDepth, record.WaterBlend));
                    if (sunShadows.Active)
                        sunShadows.Append(actor, definition, frame, catalog.Sprites[frame.Sprite], record.Facing.x < 0f,
                            color.a, ground.y + shadowFootprints[record.Definition].y - 0.02f,
                            sunShadowHeights[record.Definition]);
                batch.Append(actor, definition, frame, catalog.Sprites[frame.Sprite], liquidDepth, waterTint,
                    record.Facing.x < 0f, color);
            }
            batch.Submit();
            BatchCount = batch.Count > 0 ? 1 : 0;
            shadows.End();
            sunShadows.End();
        }

        /// <summary>飞行表现优先于地面移动，避免 ECS 鸟升空后仍播放 Walk。</summary>
        private static int ResolveClip(AiecsDisplayRecord record)
        {
            if (record.Dead != 0) return DeathClip;
            if (record.Behavior == (int)AiecsBehavior.Attack && record.AttackPhase == AiecsAttackPhase.Active)
                return AttackClip;
            if (record.FlightCruiseHeight > 0.001f)
            {
                if (record.FlightAirborne != 0)
                    return record.FlightHeight < record.FlightCruiseHeight * 0.5f ? TakingOffClip : FlyingClip;
                if (record.FlightHeight > 0.01f) return LandingClip;
            }
            return record.Moving != 0 ? MoveClip : StandClip;
        }

        /// <summary>起降动画直接跟随实际高度进度，行为状态切换不会让非循环动画跳到末帧。</summary>
        private static float ResolveSampleTime(AiecsDisplayRecord record, int action, AiecsAnimationClip clip)
        {
            if (action == TakingOffClip)
            {
                float takeoffHeight = Mathf.Max(0.001f, record.FlightCruiseHeight * 0.5f);
                return clip.Duration * Mathf.Clamp01(record.FlightHeight / takeoffHeight);
            }
            if (action == LandingClip)
            {
                float cruiseHeight = Mathf.Max(0.001f, record.FlightCruiseHeight);
                return clip.Duration * Mathf.Clamp01((1f - record.FlightHeight / cruiseHeight) * 2f);
            }
            return record.ActionElapsed;
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
                float2 position = center + domain.ShortestDelta(center, actor.Position) +
                    new float2(0, 0.8f + actor.FlightHeight);
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
            // 阴影节点清理失败也必须释放 BRG，避免脚本域重载时遗失原生句柄。
            try { shadows.Dispose(); }
            finally
            {
                try { sunShadows.Dispose(); }
                finally
                {
                    batch.Dispose();
                    visible.Clear(); BatchCount = 0;
                }
            }
        }
    }
}
