using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using UnityEngine;
using UnityEngine.Rendering;

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
            public Vector2 ShadowFoot;
            public float ShadowWidth;
            public Vector3 RenderOffset;
            public TreeSortingVisual SortingVisual;
        }

        private static ContactShadowBatchRenderer contactShadows;

        /// <summary>绑定区块表现；树木只增加排序用视觉外壳，资源玩法仍完全由 Entity 管理。</summary>
        public static void BindPresentation(NaturalEntityHandle handle, ChunkTilemapRenderer owner)
        {
            if (!Contains(handle)) return;
            Record record = records[handle.Id];
            if (record.PresentationOwner != owner) ReleasePresentation(record);
            record.PresentationOwner = owner;
            record.PresentationDirty = true;
            DrawResource(record);
        }

        private static void RefreshPresentation(NaturalEntityHandle handle)
        {
            if (Contains(handle)) records[handle.Id].PresentationDirty = true;
        }

        private static void ReleasePresentation(Record record)
        {
            record.SortingVisual?.Dispose();
            record.SortingVisual = null;
            if (record.PresentationOwner != null)
                for (int part = 0; part < record.RenderPartCount; part++)
                    record.PresentationOwner.ClearNaturalEntityPart(record.Handle.Id, part);
            record.RenderPartCount = 0;
            record.PresentationOwner = null;
        }

        /// <summary>静态主体只在版本变化时重提；太阳方向在 Shader 更新，短期坠果单独逐帧更新。</summary>
        public static void Present()
        {
            if (simulation == null || records.Count == 0) { contactShadows?.Hide(); return; }
            contactShadows ??= new ContactShadowBatchRenderer(2993);
            contactShadows.BeginFrame();
            foreach (Record record in records.Values)
            {
                if (record.PresentationOwner == null || !record.PresentationOwner.IsBatchPresentationRegistered) continue;
                NaturalEntityBody body = simulation.GetBody(record.Handle.Id);
                if (body.Dead != 0) continue;
                bool flashing = record.FlashUntil > Time.time;
                Vector3 logicalPosition = new(body.Position.x, body.Position.y);
                Vector3 projectionOffset = WorldLocalPresentation.ProjectPosition(logicalPosition) - logicalPosition;
                bool missingTreeVisual = record.Profile.Definition.HasTag(Tag.Tree) &&
                    (record.SortingVisual == null || !record.SortingVisual.IsValid);
                if (record.PresentationDirty || missingTreeVisual || record.PresentedRevision != body.VisualVersion ||
                    record.PresentedFlash != flashing || record.Profile.Canopy != null || record.RenderOffset != projectionOffset)
                    DrawResource(record);
                if (record.ShadowWidth <= 0f) continue;
                ActorShadowManager shadowManager = ActorShadowManager.GetInstance();
                float opacity = shadowManager != null ? shadowManager.GetShadowOpacity(record.PresentationOwner.gameObject.scene) : 0f;
                Vector2 foot = WorldLocalPresentation.ProjectPosition(record.ShadowFoot);
                var defaults = WorldRenderingConfigCatalog.Default.shadows.contact;
                foot.y += defaults.groundOffset;
                contactShadows.Append(foot, record.ShadowWidth, record.ShadowWidth * defaults.heightRatio, opacity);
            }
            contactShadows.EndFrame();
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
            if (record.Profile.Definition.HasTag(Tag.Tree))
            {
                if (record.SortingVisual == null || !record.SortingVisual.IsValid)
                {
                    record.SortingVisual?.Dispose();
                    record.SortingVisual = new TreeSortingVisual(owner.transform, record.Handle.Id);
                }
                record.SortingVisual.BeginUpdate(localAnchor, body);
                var visual = record.Profile.Definition.Visual;
                // 主体退出 Default 层 BRG，整个树冠以实体根部参与 Player 层的原生 Y 排序。
                owner.ClearNaturalEntityPart(record.Handle.Id, part);
                record.SortingVisual.SetPart(part++, true, sprite, material,
                    visual?.RendererLocalPosition ?? Vector3.zero,
                    Quaternion.Euler(visual?.RendererLocalEulerAngles ?? Vector3.zero), rendererScale,
                    tint, crop, true);
            }
            else
            {
                record.SortingVisual?.Dispose();
                record.SortingVisual = null;
                owner.SetNaturalEntityPart(record.Handle.Id, part++, sprite, material, projectedMatrix, tint, localAnchor,
                    crop, Vector4.zero, Vector4.zero);
            }

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
                    if (record.SortingVisual != null)
                    {
                        owner.ClearNaturalEntityPart(record.Handle.Id, part);
                        record.SortingVisual.SetPart(part++, true, fruitDefinition.Sprite, fruitDefinition.Material,
                            collection.IndicatorLocalPositions[index], Quaternion.identity,
                            Vector3.one * collection.IndicatorScale, Color.white, Vector4.zero, false);
                        continue;
                    }
                    Matrix4x4 fruitMatrix = Matrix4x4.Translate(record.RenderOffset) * BodyMatrix(body) * Matrix4x4.TRS(collection.IndicatorLocalPositions[index],
                        Quaternion.identity, Vector3.one * collection.IndicatorScale);
                    owner.SetNaturalEntityPart(record.Handle.Id, part++, fruitDefinition.Sprite, fruitDefinition.Material,
                        fruitMatrix, Color.white, localAnchor + Vector3.down * 0.0001f,
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
        }

        private static void DrawCanopyFruit(Record record, CanopyFruitRecord fruit, Vector2 position,
            float growth, Vector3 anchor, ref int part)
        {
            string itemId = fruit.Split ? record.Profile.Canopy.SplitItemId : record.Profile.Canopy.FruitItemId;
            if (!GameRes.ExistingInstance.TryGetItemDefinition(itemId, out var definition) || definition.Sprite == null)
                throw new InvalidOperationException($"树冠果实 {itemId} 缺少表现资源。");
            float scale = record.Profile.Canopy.FruitWidth * growth / Mathf.Max(0.0001f, definition.Sprite.bounds.size.x);
            if (record.SortingVisual != null)
            {
                record.PresentationOwner.ClearNaturalEntityPart(record.Handle.Id, part);
                record.SortingVisual.SetPart(part++, false, definition.Sprite, definition.Material,
                    (Vector3)position - anchor, Quaternion.identity, new Vector3(scale, scale, 1f),
                    Color.white, Vector4.zero, false);
                return;
            }
            record.PresentationOwner.SetNaturalEntityPart(record.Handle.Id, part++, definition.Sprite, definition.Material,
                Matrix4x4.TRS((Vector3)position + record.RenderOffset, Quaternion.identity, new Vector3(scale, scale, 1f)), Color.white,
                anchor + record.RenderOffset + Vector3.down * 0.0001f, Vector4.zero, Vector4.zero, Vector4.zero);
        }

        #endregion

        #region 树木原生排序桥

        /// <summary>只含 Transform、SortingGroup 和 SpriteRenderer，没有 Item、碰撞体或逐树更新脚本。</summary>
        private sealed class TreeSortingVisual : IDisposable
        {
            private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
            private static readonly int AlphaTextureId = Shader.PropertyToID("_AlphaTex");
            private static readonly int ExternalAlphaId = Shader.PropertyToID("_EnableExternalAlpha");
            private static readonly int PlayerOccluderId = Shader.PropertyToID("_PlayerOccluder");
            private static readonly int BodyMinId = Shader.PropertyToID("_BodyMinV");
            private static readonly int BodyMaxId = Shader.PropertyToID("_BodyMaxV");
            private static readonly int BodyClipId = Shader.PropertyToID("_BodyClip");

            private readonly Transform root;
            private readonly Transform bodySpace;
            private readonly SortingGroup group;
            private readonly Dictionary<int, SpriteRenderer> parts = new();
            private readonly MaterialPropertyBlock properties = new();
            private int sortingLayer;

            public bool IsValid => root != null;

            public TreeSortingVisual(Transform owner, int entityId)
            {
                root = CreateNode("EntityTreeVisual_" + entityId, owner);
                group = root.gameObject.AddComponent<SortingGroup>();
                bodySpace = CreateNode("BodySpace", root);
            }

            /// <summary>排序锚点只取本地镜像的树根，成长缩放和风摆不移动排序线。</summary>
            public void BeginUpdate(Vector3 anchor, NaturalEntityBody body)
            {
                root.position = anchor;
                bodySpace.localRotation = Quaternion.Euler(0f, 0f, body.Rotation);
                bodySpace.localScale = new Vector3(body.Scale.x, body.Scale.y, 1f);
                WorldSortingManager manager = WorldSortingManager.GetInstance();
                int order;
                if (manager != null)
                    manager.GetSortingKey(WorldSortingManager.WorldItemCategory, out sortingLayer, out order);
                else
                    WorldSortingManager.GetResourceSortingKey(WorldSortingManager.WorldItemCategory, out sortingLayer, out order);
                group.sortingLayerID = sortingLayer;
                group.sortingOrder = order;
                foreach (SpriteRenderer renderer in parts.Values) renderer.enabled = false;
            }

            /// <summary>附属果实只使用组内相对顺序，不能越过外部角色的 Y 深度。</summary>
            public void SetPart(int index, bool followsBody, Sprite sprite, Material material,
                Vector3 position, Quaternion rotation, Vector3 scale, Color tint, Vector4 crop, bool occluder)
            {
                if (sprite == null || material == null)
                    throw new InvalidOperationException("树木排序表现缺少 Sprite 或材质。");
                if (!parts.TryGetValue(index, out SpriteRenderer renderer))
                {
                    renderer = CreateNode("Part_" + index, followsBody ? bodySpace : root)
                        .gameObject.AddComponent<SpriteRenderer>();
                    renderer.spriteSortPoint = SpriteSortPoint.Pivot;
                    parts.Add(index, renderer);
                }
                Transform target = renderer.transform;
                Transform parent = followsBody ? bodySpace : root;
                if (target.parent != parent) target.SetParent(parent, false);
                target.localPosition = position;
                target.localRotation = rotation;
                target.localScale = scale;
                renderer.sprite = sprite;
                renderer.sharedMaterial = material;
                renderer.color = tint;
                renderer.sortingLayerID = sortingLayer;
                renderer.sortingOrder = index;

                // 复用源植被 Shader；MPB 明确恢复贴图、裁剪和遮挡开关，保留风摆与 Light2D。
                properties.Clear();
                properties.SetTexture(MainTextureId, sprite.texture);
                Texture2D alpha = sprite.associatedAlphaSplitTexture;
                if (alpha != null) properties.SetTexture(AlphaTextureId, alpha);
                properties.SetFloat(ExternalAlphaId, alpha != null ? 1f : 0f);
                properties.SetFloat(PlayerOccluderId, occluder ? 1f : 0f);
                Bounds bounds = sprite.bounds;
                properties.SetFloat(BodyMinId, bounds.min.y);
                properties.SetFloat(BodyMaxId, bounds.max.y);
                properties.SetFloat(BodyClipId, crop.w > 0f
                    ? Mathf.InverseLerp(bounds.min.y, bounds.max.y, crop.x) : 0f);
                renderer.SetPropertyBlock(properties);
                renderer.enabled = true;
            }

            private static Transform CreateNode(string name, Transform parent)
            {
                GameObject node = new(name) { hideFlags = HideFlags.DontSave, layer = parent.gameObject.layer };
                node.transform.SetParent(parent, false);
                return node.transform;
            }

            /// <summary>卸载先隐藏再销毁，避免同帧残影；不触及实体数据或存档。</summary>
            public void Dispose()
            {
                parts.Clear();
                if (root == null) return;
                root.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(root.gameObject);
                else UnityEngine.Object.DestroyImmediate(root.gameObject);
            }
        }

        #endregion
    }
}
