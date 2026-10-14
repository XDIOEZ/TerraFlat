using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class GameplayTestPresentationRuntimeProbe
{
    #region 正式角色渲染绑定和特效池租约
    private sealed class RenderingFixture : IDisposable
    {
        public readonly ItemMgr Items;
        public readonly ActorRenderEffectController Controller;
        public readonly VisualEffectManager Effects;
        public Item Item;
        public GameObject Effect;
        private bool disposed;

        public RenderingFixture(Player player)
        {
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            Items = ItemMgr.Instance ?? throw new InvalidOperationException("正式物品管理器未就绪。");
            Controller = player.GetComponentInChildren<ActorRenderEffectController>(true) ??
                throw new InvalidOperationException("已入世玩家没有正式角色渲染控制器。");
            Effects = VisualEffectManager.Instance;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (Item != null)
                {
                    Effects.StopOwnerEffect(Item.transform, "WetStatusEffect");
                    Controller.UnregisterExternalRenderers(Item.transform);
                }
                if (Effect != null) Effects.ReturnEffectToPool("WetStatusEffect", Effect);
            }
            finally
            {
                if (Item != null && !Item.DestructionHandled) Items.DespawnItem(Item, false);
            }
        }
    }

    private static int ActiveWetEffects(VisualEffectManager effects) =>
        effects.activeEffects.TryGetValue("WetStatusEffect", out var values) ? values.Count(value => value != null) : 0;

    private static IEnumerator CaptureRendering(Player player, JObject args, Action<JObject> complete)
    {
        var fixture = new RenderingFixture(player);
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        JObject result = null;
        int beforeEffects = ActiveWetEffects(fixture.Effects);
        try
        {
            fixture.Item = fixture.Items.InstantiateItem("Axe_Stone", player.transform.position + Vector3.right * 2f);
            fixture.Item.Load();
            SpriteRenderer external = fixture.Item.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(value => value.enabled && value.sprite != null);
            if (external == null) throw new InvalidOperationException("正式石斧没有真实 SpriteRenderer。");
            SpriteRenderer body = player.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(value => value.enabled && value.sprite != null && value.sharedMaterial != null &&
                    value.sharedMaterial.HasProperty("_BodyMinV") && fixture.Controller.Renderers.Contains(value));
            if (body == null) throw new InvalidOperationException("真实玩家主体没有支持 MPB 合成的正式共享材质。");
            Material[] originalMaterials = external.sharedMaterials;
            var originalBlock = new MaterialPropertyBlock();
            int markerId = Shader.PropertyToID("_LuaPresentationProbe");
            int bodyMinId = Shader.PropertyToID("_BodyMinV");
            external.GetPropertyBlock(originalBlock);
            originalBlock.SetFloat(markerId, 41f);
            originalBlock.SetFloat(bodyMinId, -987f);
            external.SetPropertyBlock(originalBlock);

            WorldSortingMember member = fixture.Item.GetComponent<WorldSortingMember>() ??
                throw new InvalidOperationException("正式物品激活后没有接入世界排序。");
            WorldSortingManager sorting = UnityEngine.Object.FindObjectOfType<WorldSortingManager>() ??
                throw new InvalidOperationException("真实世界没有排序管理器。");
            sorting.GetSortingKey(member.Category, out int expectedLayer, out int expectedOrder);
            SortingGroup group = external.GetComponentsInParent<SortingGroup>(true).LastOrDefault(value => value.enabled &&
                (value.transform == fixture.Item.transform || value.transform.IsChildOf(fixture.Item.transform)));
            bool sortingMatches = (group != null ? group.sortingLayerID : external.sortingLayerID) == expectedLayer &&
                (group != null ? group.sortingOrder : external.sortingOrder) == expectedOrder;
            fixture.Controller.RegisterExternalRenderers(fixture.Item.transform);
            fixture.Controller.RegisterExternalRenderers(fixture.Item.transform);
            yield return null;
            yield return null;
            var submitted = new MaterialPropertyBlock();
            external.GetPropertyBlock(submitted);
            bool registered = fixture.Controller.Renderers.Contains(external);
            int registrations = fixture.Controller.Renderers.Count(value => value == external);
            bool sharedPlayerMaterial = external.sharedMaterial == body.sharedMaterial;
            float submittedMin = submitted.GetFloat(bodyMinId);
            float marker = submitted.GetFloat(markerId);
            fixture.Controller.UnregisterExternalRenderers(fixture.Item.transform);
            var restored = new MaterialPropertyBlock();
            external.GetPropertyBlock(restored);
            bool materialRestored = external.sharedMaterials.SequenceEqual(originalMaterials);
            bool bindingReleased = !fixture.Controller.Renderers.Contains(external);

            fixture.Effect = fixture.Effects.PlayEffect(fixture.Item.transform, "WetStatusEffect",
                fixture.Item.transform, Vector3.up * .4f, -1f, EffectStackMode.NonStackable);
            if (fixture.Effect == null) throw new InvalidOperationException("正式资源目录无法创建 WetStatusEffect。");
            Vector3 initialScale = fixture.Effect.transform.localScale;
            GameObject repeated = fixture.Effects.PlayEffect(fixture.Item.transform, "WetStatusEffect",
                fixture.Item.transform, Vector3.up * .4f, -1f, EffectStackMode.NonStackable);
            ParticleSystem[] particles = fixture.Effect.GetComponentsInChildren<ParticleSystem>(true);
            yield return null;
            yield return null;
            bool ownerBound = fixture.Effects.HasEffect(fixture.Item.transform, "WetStatusEffect");
            int duringEffects = ActiveWetEffects(fixture.Effects);
            int ownRegistrations = fixture.Effects.activeEffects.TryGetValue("WetStatusEffect", out var activeWet)
                ? activeWet.Count(value => value == fixture.Effect) : 0;
            bool active = fixture.Effect.activeInHierarchy;
            bool particleMaterials = particles.Length > 0 && particles.All(value =>
                value.GetComponent<ParticleSystemRenderer>()?.sharedMaterial != null);
            fixture.Effect.transform.localScale = initialScale * 3f;
            fixture.Effects.StopOwnerEffect(fixture.Item.transform, "WetStatusEffect");
            bool ownerReleased = !fixture.Effects.HasEffect(fixture.Item.transform, "WetStatusEffect");
            bool pooledInactive = !fixture.Effect.activeSelf;
            bool particlesCleared = particles.All(value => value.particleCount == 0 && !value.isPlaying);
            bool poseRestored = fixture.Effect.transform.localScale == initialScale;
            fixture.Effects.ReturnEffectToPool("WetStatusEffect", fixture.Effect);
            int afterDoubleReturn = ActiveWetEffects(fixture.Effects);
            bool ownRegistrationReleased = !fixture.Effects.activeEffects.TryGetValue("WetStatusEffect", out var remainingWet) ||
                !remainingWet.Contains(fixture.Effect);
            result = new JObject
            {
                ["realItemLoaded"] = fixture.Item.IsInitialized, ["itemId"] = fixture.Item.itemData.IDName,
                ["realPlayerController"] = fixture.Controller.isActiveAndEnabled,
                ["bodyVisible"] = body.gameObject.activeInHierarchy && body.enabled && body.color.a > 0f,
                ["worldSortingMatches"] = sortingMatches, ["category"] = member.Category,
                ["registered"] = registered, ["registrationCount"] = registrations,
                ["sharedPlayerMaterial"] = sharedPlayerMaterial,
                ["submittedBodyMin"] = submittedMin, ["expectedBodyMin"] = external.sprite.bounds.min.y,
                ["customPropertyPreserved"] = marker == 41f,
                ["materialRestored"] = materialRestored, ["bindingReleased"] = bindingReleased,
                ["originalBodyMinRestored"] = restored.GetFloat(bodyMinId), ["originalMarkerRestored"] = restored.GetFloat(markerId),
                ["effectActive"] = active, ["sameNonStackableEffect"] = repeated == fixture.Effect,
                ["ownerBound"] = ownerBound, ["beforeEffects"] = beforeEffects, ["duringEffects"] = duringEffects,
                ["ownRegistrations"] = ownRegistrations,
                ["particleMaterialsReady"] = particleMaterials, ["ownerReleased"] = ownerReleased,
                ["pooledInactive"] = pooledInactive, ["particlesCleared"] = particlesCleared,
                ["poseRestored"] = poseRestored, ["afterDoubleReturn"] = afterDoubleReturn,
                ["ownRegistrationReleased"] = ownRegistrationReleased
            };
        }
        finally { fixture.Dispose(); }
        result["itemReleased"] = fixture.Item == null || fixture.Item.IsInPool || fixture.Item.DestructionHandled;
        result["ownEffectReleased"] = fixture.Effect == null || !fixture.Effect.activeSelf;
        complete(result);
    }
    #endregion
}
