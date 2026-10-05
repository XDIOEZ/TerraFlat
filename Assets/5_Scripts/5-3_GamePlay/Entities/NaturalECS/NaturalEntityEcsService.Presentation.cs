using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.NaturalEntities
{
    public static partial class NaturalEntityEcsService
    {
        #region 共享批量表现

        private sealed partial class Record
        {
            public ChunkTilemapRenderer PresentationOwner;
            public uint PresentedRevision;
            public bool PresentationDirty = true, PresentedFlash;
            public bool PresentationQueued;
            public Vector2 ShadowFoot;
            public float ShadowWidth;
            public Vector3 RenderOffset;
        }

        private static ContactShadowBatchRenderer contactShadows;
        private static readonly Queue<int> presentationDirtyQueue = new();
        private static readonly HashSet<int> flashPresentationWatch = new();
        private static readonly List<int> flashPresentationScratch = new();
        private static bool contactShadowListDirty = true;
        private static bool presentationEventsHooked;
        private static Scene contactShadowScene;

        /// <summary>绑定区块表现；资源主体按脚点 Y 合行网格，阴影继续提交 BRG。</summary>
        public static void BindPresentation(NaturalEntityHandle handle, ChunkTilemapRenderer owner)
        {
            if (!Contains(handle)) return;
            EnsurePresentationEvents();
            Record record = records[handle.Id];
            if (record.PresentationOwner != owner) ReleasePresentation(record);
            record.PresentationOwner = owner;
            record.PresentationDirty = true;
            record.PresentationQueued = false;
            DrawResource(record);
        }

        /// <summary>表现变化只入队一次；静止自然物不会再被每帧全量轮询。</summary>
        private static void MarkPresentationDirty(Record record)
        {
            if (record == null) return;
            record.PresentationDirty = true;
            if (record.PresentationOwner == null || record.PresentationQueued) return;
            record.PresentationQueued = true;
            presentationDirtyQueue.Enqueue(record.Handle.Id);
        }

        private static void RefreshPresentation(NaturalEntityHandle handle)
        {
            if (!Contains(handle)) return;
            Record record = records[handle.Id];
            MarkPresentationDirty(record);
            if (record.FlashUntil > Time.time)
                flashPresentationWatch.Add(handle.Id);
        }

        private static void ReleasePresentation(Record record)
        {
            if (record.PresentationOwner != null)
            {
                for (int part = 0; part < record.RenderPartCount; part++)
                    record.PresentationOwner.ClearNaturalEntityPart(record.Handle.Id, part);
                if (record.ShadowWidth > 0f)
                    contactShadowListDirty = true;
            }
            record.RenderPartCount = 0;
            record.PresentationOwner = null;
            record.PresentationQueued = false;
            flashPresentationWatch.Remove(record.Handle.Id);
        }

        /// <summary>静态主体只在版本变化时重提；太阳方向在 Shader 更新，短期坠果单独逐帧更新。</summary>
        public static void Present()
        {
            if (simulation == null || records.Count == 0)
            {
                contactShadows?.Hide();
                presentationDirtyQueue.Clear();
                flashPresentationWatch.Clear();
                return;
            }

            ProcessFlashPresentationWatch();
            ProcessDirtyPresentations();
            if (contactShadowListDirty)
                RebuildContactShadows();
            UpdateContactShadowOpacity();
        }

        /// <summary>受击闪白到期是少量短生命周期动态状态，只跟踪被击中的实体。</summary>
        private static void ProcessFlashPresentationWatch()
        {
            if (flashPresentationWatch.Count == 0) return;
            flashPresentationScratch.Clear();
            foreach (int id in flashPresentationWatch)
            {
                if (!records.TryGetValue(id, out Record record) || !Contains(record.Handle))
                {
                    flashPresentationScratch.Add(id);
                    continue;
                }
                if (record.FlashUntil > Time.time) continue;
                flashPresentationScratch.Add(id);
                MarkPresentationDirty(record);
            }
            for (int i = 0; i < flashPresentationScratch.Count; i++)
                flashPresentationWatch.Remove(flashPresentationScratch[i]);
            flashPresentationScratch.Clear();
        }

        /// <summary>每帧只消费真正变化的资源；尚未完成 Owner 注册的条目延后一帧重试。</summary>
        private static void ProcessDirtyPresentations()
        {
            int pending = presentationDirtyQueue.Count;
            for (int i = 0; i < pending; i++)
            {
                int id = presentationDirtyQueue.Dequeue();
                if (!records.TryGetValue(id, out Record record) || !record.PresentationQueued)
                    continue;
                if (record.PresentationOwner == null)
                {
                    record.PresentationQueued = false;
                    continue;
                }
                if (!record.PresentationOwner.IsBatchPresentationRegistered)
                {
                    presentationDirtyQueue.Enqueue(id);
                    continue;
                }
                record.PresentationQueued = false;
                DrawResource(record);
            }
        }

        /// <summary>阴影几何只在树体尺寸/位置/绑定变化时全量重建；昼夜透明度单独改批次材质。</summary>
        private static void RebuildContactShadows()
        {
            contactShadows ??= new ContactShadowBatchRenderer(2993);
            contactShadows.BeginFrame();
            contactShadowScene = default;
            var defaults = WorldRenderingConfigCatalog.Default.shadows.contact;
            foreach (Record record in records.Values)
            {
                if (record.PresentationOwner == null || !record.PresentationOwner.IsBatchPresentationRegistered ||
                    record.ShadowWidth <= 0f || record.RenderPartCount <= 0)
                    continue;
                if (!contactShadowScene.IsValid())
                    contactShadowScene = record.PresentationOwner.gameObject.scene;
                Vector2 foot = record.ShadowFoot + new Vector2(record.RenderOffset.x, record.RenderOffset.y);
                foot.y += defaults.groundOffset;
                contactShadows.Append(foot, record.ShadowWidth,
                    record.ShadowWidth * defaults.heightRatio, 1f);
            }
            contactShadows.EndFrame();
            contactShadowListDirty = false;
        }

        private static void UpdateContactShadowOpacity()
        {
            if (contactShadows == null) return;
            ActorShadowManager shadowManager = ActorShadowManager.GetInstance();
            float opacity = shadowManager != null && contactShadowScene.IsValid() && contactShadowScene.isLoaded
                ? shadowManager.GetShadowOpacity(contactShadowScene)
                : 0f;
            contactShadows.SetGlobalOpacity(opacity);
        }

        private static void EnsurePresentationEvents()
        {
            if (presentationEventsHooked) return;
            WorldTopologyRuntime.LocalPlayerWrapped += HandleLocalPresentationWrap;
            presentationEventsHooked = true;
        }

        /// <summary>环世界切换局部镜像时才批量重投影，不再逐实体每帧计算 ProjectPosition。</summary>
        private static void HandleLocalPresentationWrap()
        {
            foreach (Record record in records.Values)
                if (record.PresentationOwner != null)
                    MarkPresentationDirty(record);
            contactShadowListDirty = true;
        }

        private static void ReleasePresentationRuntime()
        {
            if (presentationEventsHooked)
                WorldTopologyRuntime.LocalPlayerWrapped -= HandleLocalPresentationWrap;
            presentationEventsHooked = false;
            presentationDirtyQueue.Clear();
            flashPresentationWatch.Clear();
            flashPresentationScratch.Clear();
            contactShadowListDirty = true;
            contactShadowScene = default;
            contactShadows?.Dispose();
            contactShadows = null;
        }

        private static Matrix4x4 BodyMatrix(NaturalEntityBody body) => Matrix4x4.TRS(
            new Vector3(body.Position.x, body.Position.y), Quaternion.Euler(0f, 0f, body.Rotation),
            new Vector3(body.Scale.x, body.Scale.y, 1f));

        /// <summary>成长、受害和半埋参数由同一 Entity 派生，不保存第二份植物状态。</summary>
        private static void ResolveBodyVisual(Record record, NaturalEntityBody body, out Sprite sprite,
            out Matrix4x4 matrix, out Color tint, out Vector4 crop)
            => ResolveBodyVisual(record, body, out sprite, out matrix, out tint, out crop, out _);

        private static void ResolveBodyVisual(Record record, NaturalEntityBody body, out Sprite sprite,
            out Matrix4x4 matrix, out Color tint, out Vector4 crop, out Vector3 rendererScale)
        {
            var definition = record.Profile.Definition;
            var visual = definition.Visual;
            sprite = definition.Sprite;
            Vector3 scale = visual?.RendererLocalScale ?? Vector3.one;
            tint = visual?.Color ?? Color.white;
            crop = Vector4.zero;
            if (record.Profile.CropVisual is { } configuration)
            {
                float progress = simulation.TryGet(record.Handle.Id, out EntityGrowth growth)
                    ? Mathf.Clamp01(growth.Progress / Mathf.Max(0.0001f, growth.MaxProgress)) : 1f;
                bool stages = definition.TryGetVisualStateSprite("seedling", out Sprite seedling) &&
                    definition.TryGetVisualStateSprite("growing", out Sprite growing) &&
                    definition.TryGetVisualStateSprite("mature", out Sprite mature);
                if (stages)
                {
                    string stage = progress >= 1f ? "mature" : progress >= configuration.growingVisualThreshold ? "growing" : "seedling";
                    definition.TryGetVisualStateSprite(stage, out sprite);
                }
                else
                {
                    float amount = Mathf.Lerp(configuration.seedlingScale, configuration.matureScale, progress);
                    scale.x *= amount; scale.y *= amount;
                }
                if (simulation.TryGet(record.Handle.Id, out EntityPlantLifecycle plant))
                    tint = Color.Lerp(tint, new Color(0.58f, 0.42f, 0.20f, tint.a), plant.ClimateStress);
                if (sprite != null && configuration.buriedClip > 0f)
                    crop = new Vector4(Mathf.Lerp(sprite.bounds.min.y, sprite.bounds.max.y,
                        Mathf.Clamp01(configuration.buriedClip)), 0f, 0f, 1f);
            }
            if (visual?.FlipX == true) scale.x = -scale.x;
            if (visual?.FlipY == true) scale.y = -scale.y;
            rendererScale = scale;
            matrix = BodyMatrix(body) * Matrix4x4.TRS(visual?.RendererLocalPosition ?? Vector3.zero,
                Quaternion.Euler(visual?.RendererLocalEulerAngles ?? Vector3.zero), scale);
            if (record.Highlighted) tint = Color.Lerp(tint, Color.white, 0.6f);
            if (record.FlashUntil > Time.time) tint = new Color(1f, 0.08f, 0.08f, tint.a);
        }

        private static void DrawResource(Record record)
        {
            ChunkTilemapRenderer owner = record.PresentationOwner;
            if (owner == null || !owner.IsBatchPresentationRegistered || !Contains(record.Handle)) return;
            NaturalEntityBody body = simulation.GetBody(record.Handle.Id);
            if (body.Dead != 0) return;
            Vector2 previousShadowFoot = record.ShadowFoot;
            float previousShadowWidth = record.ShadowWidth;
            Vector3 previousRenderOffset = record.RenderOffset;
            ResolveBodyVisual(record, body, out Sprite sprite, out Matrix4x4 matrix, out Color tint,
                out Vector4 crop, out Vector3 rendererScale);
            Material material = record.Profile.Definition.Material;
            if (sprite == null || material == null)
                throw new InvalidOperationException($"资源实体 {record.Profile.Definition.Id} 缺少已加载的 Sprite/材质。");
            Vector3 anchor = new(body.Position.x, body.Position.y);
            record.RenderOffset = WorldLocalPresentation.ProjectPosition(anchor) - anchor;
            Vector3 localAnchor = anchor + record.RenderOffset;
            Matrix4x4 projectedMatrix = Matrix4x4.Translate(record.RenderOffset) * matrix;
            int part = 0;
            // 主体和附属果实共用真实树根行，不再创建逐树补绘或切换两套渲染器。
            owner.SetNaturalEntityPart(record.Handle.Id, part++, sprite, material, projectedMatrix, tint, localAnchor,
                crop, Vector4.zero, Vector4.zero, playerOccluder: record.Profile.Definition.HasTag(Tag.Tree),
                highlighted: record.Highlighted);

            Bounds visible = ShadowFootprintResolver.MeasureVisibleWorldBounds(sprite, matrix);
            owner.IncludeNaturalEntityBounds(visible, record.RenderOffset);
            var settings = record.Profile.Definition.Visual?.Shadows;
            Vector3 foot = ShadowFootprintResolver.ResolveFoot(matrix, visible, settings?.FootLocalPosition,
                0f, settings?.FootOverlap);
            if (record.Profile.CropVisual != null) foot = anchor;
            record.ShadowFoot = foot;
            var contact = WorldRenderingConfigCatalog.Default.shadows.contact;
            float width = settings?.ContactWidth * Mathf.Abs(body.Scale.x) ?? visible.size.x * contact.widthRatio;
            record.ShadowWidth = width <= 0f ? 0f : Mathf.Clamp(width, contact.minimumWidth, contact.maximumWidth);
            Vector4 shadowCrop = crop; shadowCrop.z = 1f;
            owner.SetNaturalEntityPart(record.Handle.Id, part++, sprite, material, projectedMatrix,
                new Color(1f, 1f, 1f, tint.a), localAnchor, shadowCrop, Vector4.zero,
                new Vector4(foot.x + record.RenderOffset.x, foot.y + record.RenderOffset.y, 1f, 0f), sunShadow: true);

            if (record.Profile.Collection is { } collection &&
                simulation.TryGet(record.Handle.Id, out EntityResourceStock stock) && stock.Count > 0 &&
                GameRes.ExistingInstance.TryGetItemDefinition(collection.CollectItemId, out var fruitDefinition))
            {
                int count = Mathf.Min(stock.Count, collection.IndicatorLocalPositions.Count);
                for (int index = 0; index < count; index++)
                {
                    Matrix4x4 fruitMatrix = Matrix4x4.Translate(record.RenderOffset) * BodyMatrix(body) * Matrix4x4.TRS(collection.IndicatorLocalPositions[index],
                        Quaternion.identity, Vector3.one * collection.IndicatorScale);
                    owner.SetNaturalEntityPart(record.Handle.Id, part++, fruitDefinition.Sprite, fruitDefinition.Material,
                        fruitMatrix, Color.white, localAnchor,
                        Vector4.zero, Vector4.zero, Vector4.zero);
                }
            }
            if (record.Profile.Canopy != null)
            {
                CanopyFruitState state = GetCanopy(record);
                foreach (CanopyFruitRecord fruit in state.Fruits)
                    DrawCanopyFruit(record, fruit, CrownPosition(record, fruit.Slot),
                        Mathf.Lerp(record.Profile.Canopy.SmallScale, 1f, fruit.Growth01(state.Time)), anchor, ref part);
                foreach (CanopyFruitRecord fruit in state.Flights)
                    DrawCanopyFruit(record, fruit, Mod_CanopyFruit.FlightPosition(fruit, state.Time), 1f, anchor, ref part);
            }
            for (int old = part; old < record.RenderPartCount; old++) owner.ClearNaturalEntityPart(record.Handle.Id, old);
            record.RenderPartCount = part;
            record.PresentedRevision = body.VisualVersion;
            record.PresentedFlash = record.FlashUntil > Time.time;
            record.PresentationDirty = false;
            record.PresentationQueued = false;
            if (previousShadowFoot != record.ShadowFoot ||
                !Mathf.Approximately(previousShadowWidth, record.ShadowWidth) ||
                previousRenderOffset != record.RenderOffset)
                contactShadowListDirty = true;
        }

        private static void DrawCanopyFruit(Record record, CanopyFruitRecord fruit, Vector2 position,
            float growth, Vector3 anchor, ref int part)
        {
            string itemId = fruit.Split ? record.Profile.Canopy.SplitItemId : record.Profile.Canopy.FruitItemId;
            if (!GameRes.ExistingInstance.TryGetItemDefinition(itemId, out var definition) || definition.Sprite == null)
                throw new InvalidOperationException($"树冠果实 {itemId} 缺少表现资源。");
            float scale = record.Profile.Canopy.FruitWidth * growth / Mathf.Max(0.0001f, definition.Sprite.bounds.size.x);
            record.PresentationOwner.SetNaturalEntityPart(record.Handle.Id, part++, definition.Sprite, definition.Material,
                Matrix4x4.TRS((Vector3)position + record.RenderOffset, Quaternion.identity, new Vector3(scale, scale, 1f)), Color.white,
                anchor + record.RenderOffset, Vector4.zero, Vector4.zero, Vector4.zero);
        }

        #endregion

    }
}
