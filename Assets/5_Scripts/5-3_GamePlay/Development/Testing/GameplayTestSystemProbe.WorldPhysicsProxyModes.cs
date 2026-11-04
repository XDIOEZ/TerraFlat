using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.NaturalEntities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 游戏物理模式与合批资格规则
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterWorldPhysicsProxyModeRules()
        => Register("world_streaming", "world_physics_proxy_modes", CaptureWorldPhysicsProxyModeRules);

    // 校验配置边界和共享合批资格，不把规则采样当成真实生成或运动验收。
    private static JObject CaptureWorldPhysicsProxyModeRules(JObject args)
    {
        ItemPhysicsDefinitionDto fixedMode = JsonConvert.DeserializeObject<ItemPhysicsDefinitionDto>(
            "{\"mode\":\"WorldFixed\",\"massSource\":\"ItemStack\"}");
        ItemPhysicsDefinitionDto dynamicMode = JsonConvert.DeserializeObject<ItemPhysicsDefinitionDto>(
            "{\"mode\":\"IndependentDynamic\",\"massSource\":\"ItemStack\",\"freezeRotation\":false}");
        bool unityModeRejected = false, fixedGridDynamicRejected = false;
        try { JsonConvert.DeserializeObject<ItemPhysicsDefinitionDto>("{\"mode\":\"Kinematic\"}"); }
        catch (JsonSerializationException) { unityModeRejected = true; }
        try { WorldPhysicsService2D.ResolveRequiredWorldFixedProxyMode(dynamicMode, "规则自有固定格实体"); }
        catch (InvalidOperationException) { fixedGridDynamicRejected = true; }
        ItemPhysicsDefinitionDto immutableBox = JsonConvert.DeserializeObject<ItemPhysicsDefinitionDto>(
            "{\"mode\":\"WorldFixed\",\"regions\":[{\"id\":\"fixed:box\",\"purposes\":\"Blocking\",\"immutable\":true,\"collider\":{\"type\":\"BoxCollider2D\",\"size\":{\"x\":1,\"y\":1}}}]}");
        ItemPhysicsDefinitionDto mutableDynamicBox = JsonConvert.DeserializeObject<ItemPhysicsDefinitionDto>(
            "{\"mode\":\"IndependentDynamic\",\"regions\":[{\"id\":\"moving:box\",\"purposes\":\"Blocking\",\"immutable\":true,\"collider\":{\"type\":\"BoxCollider2D\",\"size\":{\"x\":1,\"y\":1}}}]}");
        ItemPhysicsDefinitionDto immutableModuleBox = JsonConvert.DeserializeObject<ItemPhysicsDefinitionDto>(
            "{\"mode\":\"WorldFixed\",\"regions\":[{\"id\":\"fixed:module\",\"purposes\":\"Blocking\",\"immutable\":true,\"module\":\"vessel\",\"collider\":{\"type\":\"BoxCollider2D\",\"size\":{\"x\":1,\"y\":1}}}]}");
        return new JObject
        {
            ["fixedMode"] = fixedMode.Mode.ToString(), ["dynamicMode"] = dynamicMode.Mode.ToString(),
            ["massSource"] = dynamicMode.MassSource.ToString(), ["rotationCanMove"] = !dynamicMode.FreezeRotation,
            ["unityModeRejected"] = unityModeRejected, ["fixedGridDynamicRejected"] = fixedGridDynamicRejected,
            ["defaultFixed"] = WorldPhysicsService2D.ResolveRequiredWorldFixedProxyMode(null, "默认固定格实体").ToString(),
            ["blockingCanBatch"] = WorldPhysicsService2D.CanBatchWorldFixedBlockingRegions(fixedMode, WorldPhysicsPurpose2D.Blocking),
            ["damageCannotBatch"] = !WorldPhysicsService2D.CanBatchWorldFixedBlockingRegions(fixedMode, WorldPhysicsPurpose2D.Damage),
            ["triggerCannotBatch"] = !WorldPhysicsService2D.CanBatchWorldFixedBlockingRegions(fixedMode, WorldPhysicsPurpose2D.Blocking | WorldPhysicsPurpose2D.Trigger),
            ["receiverCannotBatch"] = !WorldPhysicsService2D.CanBatchWorldFixedBlockingRegions(fixedMode, WorldPhysicsPurpose2D.Blocking, true),
            ["dynamicCannotBatch"] = !WorldPhysicsService2D.CanBatchWorldFixedBlockingRegions(dynamicMode, WorldPhysicsPurpose2D.Blocking),
            ["immutableFixedBoxAccepted"] = ValidatePhysicsDefinitionRules(immutableBox),
            ["immutableDynamicBoxRejected"] = !ValidatePhysicsDefinitionRules(mutableDynamicBox),
            ["immutableModuleBoxRejected"] = !ValidatePhysicsDefinitionRules(immutableModuleBox),
            ["moduleIdentityContract"] = CaptureWorldPhysicsModuleIdentityContract(),
            ["templateProxy"] = CaptureTemplateActorPhysicsProxy(),
            ["defaultInteraction"] = CaptureDefaultNaturalEntityInteractionRegionRules()
        };
    }

    // 直接使用通用 Item 模板验证代理，避免测试依赖玩家控制器。
    private static JObject CaptureTemplateActorPhysicsProxy()
    {
        string identity = "probe:template-proxy:" + Guid.NewGuid().ToString("N");
        using var space = WorldPhysicsScene2D.CreateIsolated(identity);
        using var world = new WorldPhysicsService2D(identity, WorldPhysicsScene2D.Borrow(space.Scene));
        var root = new GameObject("Template actor");
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, space.Scene);
        GameItem item = root.AddComponent<GameItem>();
        try
        {
            item.BindData(new Data_GeneralItem { IDName = identity });
            ItemPhysicsTemplate2D template = root.AddComponent<ItemPhysicsTemplate2D>();
            template.hasBody = true;
            template.bodyGravityScale = 0f;
            template.bodyConstraints = RigidbodyConstraints2D.FreezeRotation;
            template.colliderType = nameof(BoxCollider2D);
            template.size = new Vector2(.4f, .3f);
            template.offset = new Vector2(0f, .15f);
            Mod_DamageReceiver receiver = CreateWorldPhysicsProbeReceiver(item, "health:template", Vector2.zero);
            receiver.GetComponent<ItemPhysicsTemplate2D>().owningModule = receiver;
            world.RegisterItem(item);
            world.TryGetItemProxy(item, out var proxy);
            Collider2D hit = WorldPhysicsService2D.GetItemCollider<Collider2D>(receiver);
            Rigidbody2D body = WorldPhysicsService2D.GetItemRigidbody(item);
            root.SetActive(true);
            world.RegisterItem(item);
            bool leasePreserved = world.TryGetItemProxy(item, out var same) && ReferenceEquals(proxy, same);
            body.position = Vector2.right * 2f;
            world.Simulate(.02f);
            bool nativePosePreserved = Mathf.Abs(root.transform.position.x - 2f) < .001f;
            root.transform.position = Vector3.right * 5f;
            world.Simulate(.02f);
            bool teleportApplied = Mathf.Abs(body.position.x - 5f) < .001f;
            var result = new JObject
            {
                ["logicHasNoPhysics"] = root.GetComponentsInChildren<Collider2D>(true).Length == 0 &&
                    root.GetComponentsInChildren<Rigidbody2D>(true).Length == 0,
                ["physicsOwnsBody"] = body != null && body.gameObject != root && proxy.Shell.OwnsRoot,
                ["exactReceiver"] = GameplayCombatBridge.ResolvePhysicsDamageReceiver(hit) == receiver,
                ["leasePreserved"] = leasePreserved,
                ["nativePosePreserved"] = nativePosePreserved,
                ["teleportApplied"] = teleportApplied
            };
            WorldPhysicsService2D.ReleaseItem(item);
            result["released"] = !proxy.IsValid && !body.simulated && !WorldPhysicsService2D.TryResolveCollider(hit, out _);
            return result;
        }
        finally { WorldPhysicsService2D.ReleaseItem(item); UnityEngine.Object.DestroyImmediate(root); }
    }

    // 校验模板旧别名、正式绑定、重绑和两种克隆都不串运行态身份。
    private static JObject CaptureWorldPhysicsModuleIdentityContract()
    {
        var state = new Ex_ModData_MemoryPackable();
        state.ID = "probe:legacy-template-id";
        bool legacyTemplateSetter = state.ModuleId == "probe:legacy-template-id";
        state.BindRuntimeModuleIdentity("probe:bound-id");
        state.ID = "probe:legacy-overwrite";
        bool boundSetterProtected = state.ModuleId == "probe:bound-id";
        state.BindRuntimeModuleIdentity("probe:rebound-id");
        bool formalRebind = state.ModuleId == "probe:rebound-id";

        var fastCopy = FastCloner.FastCloner.DeepClone<object>(state) as Ex_ModData_MemoryPackable;
        if (fastCopy == null) throw new InvalidOperationException("FastCloner 未保留测试模块数据类型。");
        fastCopy.ID = "probe:fast-copy-id";
        bool fastCloneDropsRuntimeBinding = fastCopy.ModuleId == "probe:fast-copy-id";

        byte[] packed = MemoryPack.MemoryPackSerializer.Serialize<ModuleData>(state);
        ModuleData memoryCopy = MemoryPack.MemoryPackSerializer.Deserialize<ModuleData>(packed);
        if (memoryCopy == null) throw new InvalidOperationException("MemoryPack 未恢复测试模块数据。");
        memoryCopy.ID = "probe:memory-copy-id";
        bool memoryPackDropsRuntimeBinding = memoryCopy.ModuleId == "probe:memory-copy-id";

        return new JObject
        {
            ["legacyTemplateSetter"] = legacyTemplateSetter,
            ["boundSetterProtected"] = boundSetterProtected,
            ["formalRebind"] = formalRebind,
            ["fastCloneDropsRuntimeBinding"] = fastCloneDropsRuntimeBinding,
            ["memoryPackDropsRuntimeBinding"] = memoryPackDropsRuntimeBinding
        };
    }

    private static bool ValidatePhysicsDefinitionRules(ItemPhysicsDefinitionDto physics)
    {
        try
        {
            var itemData = new Data_GeneralItem
            {
                IDName = "probe:immutable-physics-region",
                GameName = "不可变物理区域规则",
                Stack = new ItemStack { Weight = 1f, Volume = 1f, Stackable = false, CanBePickedUp = false }
            };
            var definition = new RuntimeItemDefinition(
                id: itemData.IDName,
                shellPrefabId: "Module_Item",
                shellPrefab: null,
                itemData: itemData,
                visual: new ItemVisualDefinitionDto(),
                health: null,
                lootTableId: null,
                waterEntryTransformItemId: null,
                sprite: null,
                parameters: null,
                prefabIds: null,
                labelKey: "probe:immutable-physics-region",
                descriptionKey: "probe:immutable-physics-region",
                physics: physics);
            ItemPhysicsDefinitionRuntime.ValidatePhysicsDefinition(definition);
            return true;
        }
        catch { return false; }
    }

    private static JObject CaptureDefaultNaturalEntityInteractionRegionRules()
    {
        Texture2D texture = null;
        Sprite sprite = null;
        try
        {
            texture = new Texture2D(32, 16, TextureFormat.RGBA32, false);
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 32f, 16f), new Vector2(.25f, .75f), 16f);
            Vector3 expectedPosition = new(.375f, -.625f, .125f);
            Vector3 expectedEuler = new(0f, 0f, 27f);
            Vector3 expectedScale = new(1.75f, .65f, 1f);
            var visual = new ItemVisualDefinitionDto
            {
                RendererLocalPosition = expectedPosition,
                RendererLocalEulerAngles = expectedEuler,
                RendererLocalScale = expectedScale,
                Collider = new ItemColliderDefinitionDto
                {
                    Type = nameof(BoxCollider2D), Enabled = true, IsTrigger = false,
                    Size = new Vector2(.7f, 1.3f)
                }
            };
            var itemData = new Data_GeneralItem
            {
                IDName = "probe:default-interaction-physics",
                GameName = "默认交互区域探针",
                Stack = new ItemStack { Weight = 1f, Volume = 1f, Stackable = false, CanBePickedUp = false }
            };
            var definition = new RuntimeItemDefinition(
                id: itemData.IDName,
                shellPrefabId: "Module_Item",
                shellPrefab: null,
                itemData: itemData,
                visual: visual,
                health: null,
                lootTableId: null,
                waterEntryTransformItemId: null,
                sprite: sprite,
                parameters: null,
                prefabIds: null,
                labelKey: "probe:default-interaction-physics",
                descriptionKey: "probe:default-interaction-physics",
                entityRuntime: "resource");

            // 直接调用正式资源代理区域编译器，确认定义姿态与 Sprite 几何原样进入交互区域。
            List<WorldPhysicsEntityRegion2D> regions = NaturalEntityEcsService.CompileNaturalEntityPhysicsRegions(
                new NaturalEntityEcsProfile { Definition = definition });
            WorldPhysicsEntityRegion2D interaction = regions.Single(region => region.Id == "interaction");
            Bounds spriteBounds = sprite.bounds;
            return new JObject
            {
                ["compiled"] = true,
                ["regionCount"] = regions.Count,
                ["blockingRegionPreserved"] = regions.Any(region => region.Id == "body" &&
                    (region.Purpose & WorldPhysicsPurpose2D.Blocking) != 0),
                ["hasInteractionPurpose"] = (interaction.Purpose & WorldPhysicsPurpose2D.Interaction) != 0,
                ["isTrigger"] = interaction.Collider?.IsTrigger == true,
                ["localPositionError"] = Vector3.Distance(interaction.LocalPosition, expectedPosition),
                ["localRotationError"] = Quaternion.Angle(interaction.LocalRotation, Quaternion.Euler(expectedEuler)),
                ["localScaleError"] = Vector3.Distance(interaction.LocalScale, expectedScale),
                ["boundsOffsetError"] = Vector2.Distance(interaction.Collider?.Offset ?? Vector2.zero, spriteBounds.center),
                ["boundsSizeError"] = Vector2.Distance(interaction.Collider?.Size ?? Vector2.zero, spriteBounds.size)
            };
        }
        catch (Exception error)
        {
            return new JObject { ["compiled"] = false, ["error"] = error.GetType().Name + ": " + error.Message };
        }
        finally
        {
            if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
        }
    }
    #endregion
}
