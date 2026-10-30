using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using FlatWorld.AIECS;

public static partial class GameplayTestSystemProbe
{
    #region 正式生物单图动画配置采样

    private static void RegisterAiAnimationOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("ai/network_snapshot_buffer", AiNetworkSnapshotBuffer);
        operations.Add("ai/single_sprite_catalog", EntityAiSingleSpriteCatalog);
        operations.Add("ai/single_sprite_aquatic", EntityAiSingleSpriteAquatic);
        operations.Add("ai/ecs_sway_animation", AiEcsSwayAnimation);
        operations.Add("ai/ecs_resident_ownership", AiEcsResidentOwnership);
        operations.Add("ai/debug_overlay_cache", AiDebugOverlayCache);
        operations.Add("ai/animal_tree_animation", AiAnimalBehaviorTreeAnimation);
    }

    // 直接采样正式头顶缓存，检查同秒 Buff、换语言、池代际与字符串复用边界。
    private static JObject AiDebugOverlayCache(JObject args)
    {
        var cache = new AI_DebugOverlay.TextCache<(int Count, bool Leader)>();
        bool initialMiss = !cache.TryGet((8, true), "zh-CN", 1, out _);
        string initial = new string(new[] { 'w', 'o', 'l', 'f' });
        cache.Store((8, true), "zh-CN", 1, initial);
        bool reused = cache.TryGet((8, true), "zh-CN", 1, out string same) && ReferenceEquals(initial, same);
        bool stateChanged = !cache.TryGet((7, true), "zh-CN", 1, out _) &&
                            !cache.TryGet((8, false), "zh-CN", 1, out _);
        bool localeChanged = !cache.TryGet((8, true), "en", 1, out _);
        bool generationChanged = !cache.TryGet((8, true), "zh-CN", 2, out _);

        var host = new GameObject("ai-debug-cache-rule");
        host.SetActive(false);
        Item receiver = null;
        try
        {
            receiver = host.AddComponent<Item>();
            var definition = new BuffDefinition { Id = "test:wet", DisplayName = "", DurationSeconds = 3f };
            var timed = new BuffInstance();
            timed.Initialize(definition, receiver);
            timed.RemainingDurationSeconds = 2.9f;
            var buffs = new Dictionary<string, BuffInstance> { ["timed"] = timed };
            var buffCache = new AI_DebugOverlay.BuffTextCache();
            string first = buffCache.GetText(buffs, "zh-CN");
            timed.RemainingDurationSeconds = 2.1f;
            string sameSecond = buffCache.GetText(buffs, "zh-CN");
            timed.RemainingDurationSeconds = 1.9f;
            string nextSecond = buffCache.GetText(buffs, "zh-CN");
            bool seconds = ReferenceEquals(first, sameSecond) && first.Contains("3s") &&
                           nextSecond.Contains("2s") && !ReferenceEquals(nextSecond, first);

            var replacement = new BuffInstance();
            replacement.Initialize(new BuffDefinition { Id = "test:other", DisplayName = "", DurationSeconds = 3f }, receiver);
            replacement.RemainingDurationSeconds = 1.9f;
            buffs["timed"] = replacement;
            string replaced = buffCache.GetText(buffs, "zh-CN");
            bool replacementUpdated = replaced.Contains("test:other") && !replaced.Contains("test:wet");
            replacement.RemainingDurationSeconds = 0f;
            bool expired = buffCache.GetText(buffs, "zh-CN") == string.Empty;

            var permanent = new BuffInstance();
            permanent.Initialize(new BuffDefinition { Id = "test:permanent", DisplayName = "" }, receiver);
            buffs["permanent"] = permanent;
            buffs["missing"] = new BuffInstance();
            string permanentText = buffCache.GetText(buffs, "zh-CN");
            permanent.RemainingDurationSeconds = 20f;
            bool permanentReused = permanentText.Contains("test:permanent") &&
                                   ReferenceEquals(permanentText, buffCache.GetText(buffs, "zh-CN"));
            string otherLocale = buffCache.GetText(buffs, "en");
            bool buffLocaleInvalidated = !ReferenceEquals(permanentText, otherLocale);
            buffs.Clear();
            bool cleared = buffCache.GetText(buffs, "en") == string.Empty;
            buffs["permanent"] = permanent;
            buffCache.Clear();
            bool restored = buffCache.GetText(buffs, "en").Contains("test:permanent");

            return new JObject
            {
                ["initialMiss"] = initialMiss, ["reused"] = reused, ["stateChanged"] = stateChanged,
                ["localeChanged"] = localeChanged, ["generationChanged"] = generationChanged,
                ["seconds"] = seconds, ["replacementUpdated"] = replacementUpdated, ["expired"] = expired,
                ["permanentReused"] = permanentReused, ["buffLocaleInvalidated"] = buffLocaleInvalidated,
                ["cleared"] = cleared, ["restored"] = restored
            };
        }
        finally
        {
            receiver?.PrepareForDespawn(false);
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    // Lua 读取正式摇摆采样器的姿态，不创建世界、实体、Renderer 或动画组件。
    private static JObject AiEcsSwayAnimation(JObject args)
    {
        AiecsAnimationClip Clip(string name, int sprite) => new AiecsAnimationClip
        {
            State = name, Duration = 1f, Loop = true,
            Frames = new[] { new AiecsAnimationFrame { Sprite = sprite, Scale = Vector3.one } }
        };
        var visual = new AiecsActorVisual { Clips = new[] { Clip("Stand", 0), Clip("Flying", 1) } };
        visual.Sway.idleBreathAmount = 0f;
        var geometry = new[]
        {
            new AiecsSpriteGeometry { LocalRect = new Rect(-0.5f, 0f, 1f, 1f) },
            new AiecsSpriteGeometry { LocalRect = new Rect(-0.5f, 0f, 1f, 1f) }
        };
        var animation = new AiecsSwayAnimation(visual, geometry);
        JObject Sample(ref AiecsSwayState sampleState, AiecsDisplayRecord sampleRecord, float delta)
        {
            AiecsSwayPose pose = animation.Sample(ref sampleState, sampleRecord, delta);
            Bounds bounds = animation.GetBounds(pose.Frame, pose.Mirror);
            return new JObject { ["sprite"] = pose.Frame.Sprite, ["mirror"] = pose.Mirror,
                ["angle"] = pose.Frame.Rotation, ["centerX"] = bounds.center.x, ["bottom"] = bounds.min.y };
        }
        var result = new JObject();
        var state = new AiecsSwayState();
        var record = new AiecsDisplayRecord { Facing = new Unity.Mathematics.float2(1f, 0f), Moving = 1 };
        result["moving"] = Sample(ref state, record, 0.1f);
        record.Moving = 0;
        result["stopped"] = Sample(ref state, record, 0.2f);
        record.AttackPhase = AiecsAttackPhase.Windup;
        result["windup"] = Sample(ref state, record, 0.1f);
        record.AttackPhase = AiecsAttackPhase.Active; record.ActionElapsed = 0.03f;
        result["attack"] = Sample(ref state, record, 0f);
        record.AttackPhase = AiecsAttackPhase.Recovery; record.ActionElapsed = 0f;
        record.Facing.x = -1f;
        result["recovery"] = Sample(ref state, record, 0.06f);
        result["attackEnded"] = Sample(ref state, record, 0.2f);
        var other = new AiecsSwayState();
        record.Facing.x = -1f; record.AttackPhase = AiecsAttackPhase.Active; record.ActionElapsed = 0.03f;
        result["leftAttack"] = Sample(ref other, record, 0f);
        record.Facing.x = 1f; record.AttackPhase = AiecsAttackPhase.Ready;
        record.FlightCruiseHeight = 1f; record.FlightHeight = 1f; record.FlightAirborne = 1;
        result["flying"] = Sample(ref state, record, 0f);
        record.Dead = 1; record.Facing.x = -1f; record.FlightHeight = 0f; record.FlightAirborne = 0;
        record.ActionElapsed = 0f;
        result["deathStart"] = Sample(ref state, record, 0f);
        record.ActionElapsed = 0.38f;
        result["deathMiddle"] = Sample(ref state, record, 0f);
        record.ActionElapsed = 0.76f;
        result["deathEnd"] = Sample(ref state, record, 0f);
        record.ActionElapsed = 2f;
        result["deathHeld"] = Sample(ref state, record, 0f);
        record.Dead = 0; record.FlightCruiseHeight = 0f; record.Facing.x = 1f;
        result["revived"] = Sample(ref state, record, 0f);
#if UNITY_EDITOR
        var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<AiecsAnimationCatalog>(
            "Assets/6_Art/Generated/Actors/AIECS/生物动画目录.asset");
        var actors = new JArray();
        foreach (AiecsActorVisual actor in catalog.Actors)
        {
            var states = new JArray();
            foreach (AiecsAnimationClip clip in actor.Clips)
                states.Add(new JObject { ["state"] = clip.State, ["frames"] = clip.Frames.Length,
                    ["source"] = catalog.Sprites[clip.Frames[0].Sprite].Source });
            actors.Add(new JObject { ["id"] = actor.Id, ["states"] = states,
                ["deathDuration"] = actor.Sway.deathDuration });
        }
        result["actors"] = actors;
#endif
        return result;
    }

    // 规则驱动正式快照缓冲，覆盖分片与世界代际边界，不启动网络或模拟。
    private static JObject AiNetworkSnapshotBuffer(JObject args)
    {
        var buffer = new AiEcologySnapshotBuffer();
        var a = new AiEcologyNetworkActor { Guid = 1, Species = "Wolf", Position = Vector2.one };
        var b = new AiEcologyNetworkActor { Guid = 2, Species = "Bird", Position = Vector2.right };
        bool incomplete = !buffer.Accept("a", 7, 1, 1, 2, new[] { b }, out _);
        bool duplicatePart = !buffer.Accept("a", 7, 1, 1, 2, new[] { b }, out _);
        bool mixedRealm = !buffer.Accept("a", 8, 1, 0, 2, new[] { a }, out _);
        bool mixedWorld = !buffer.Accept("b", 7, 1, 0, 2, new[] { a }, out _);
        bool assembled = buffer.Accept("a", 7, 1, 0, 2, new[] { a }, out AiEcologyNetworkActor[] result) &&
            result.Length == 2 && result[0].Guid == 1 && result[1].Guid == 2;
        bool oldRejected = !buffer.Accept("a", 7, 1, 0, 1, new[] { a }, out _);
        buffer.Accept("a", 7, 2, 0, 2, new[] { a }, out _);
        bool newerReplacesIncomplete = buffer.Accept("a", 7, 3, 0, 1, new[] { b }, out result) && result.Length == 1 && result[0].Guid == 2;
        bool emptyClears = buffer.Accept("a", 7, 4, 0, 1, Array.Empty<AiEcologyNetworkActor>(), out result) && result.Length == 0;
        bool duplicateGuid = !buffer.Accept("a", 7, 5, 0, 1, new[] { a, a }, out _);
        a.Position = new Vector2(float.NaN, 0);
        bool invalidRejected = !buffer.Accept("a", 7, 6, 0, 1, new[] { a }, out _);
        buffer.Reset();
        bool resetAcceptsNewRealm = buffer.Accept("b", 9, 1, 0, 1, new[] { b }, out result) && result.Length == 1;
        return new JObject { ["incomplete"] = incomplete, ["duplicatePart"] = duplicatePart,
            ["mixedRealm"] = mixedRealm, ["mixedWorld"] = mixedWorld, ["assembled"] = assembled,
            ["oldRejected"] = oldRejected, ["newerReplacesIncomplete"] = newerReplacesIncomplete,
            ["emptyClears"] = emptyClears, ["duplicateGuid"] = duplicateGuid,
            ["invalidRejected"] = invalidRejected, ["resetAcceptsNewRealm"] = resetAcceptsNewRealm };
    }

    // 正式加载器负责 Manifest 和继承，候选上下文保证规则不替换正在使用的目录缓存。
    private static JObject EntityAiSingleSpriteCatalog(JObject args)
    {
        List<ItemDefinitionDto> definitions = null;
        var candidate = new ResourceReloadContext();
        ActorDefinitionCatalogLoader.ConfigureResourceReload(candidate);
        candidate.InCandidate(() => definitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions());
        var actors = new JArray();
        foreach (ItemDefinitionDto definition in definitions)
        {
            if (definition.Abstract) continue;
            var sample = new JObject { ["id"] = definition.Id };
            actors.Add(sample);
            sample["initialSpriteAddress"] = definition.Visual?.SpriteAddress ?? string.Empty;
            Sprite initialSprite = null;
            string initialSpriteError = string.Empty;
#if UNITY_EDITOR
            bool hasInitialSprite = ItemDefinitionCatalogLoader.TryLoadEditorSprite(definition.Visual?.SpriteAddress,
                out initialSprite, out initialSpriteError);
#else
            bool hasInitialSprite = GameRes.ExistingInstance != null &&
                GameRes.ExistingInstance.TryGetItemDefinition(definition.Id, out RuntimeItemDefinition runtimeDefinition) &&
                (initialSprite = runtimeDefinition.Sprite) != null;
#endif
            sample["hasInitialSprite"] = hasInitialSprite;
            sample["initialSpriteName"] = initialSprite != null ? initialSprite.name : string.Empty;
            sample["initialSpriteError"] = initialSpriteError ?? string.Empty;
            sample["runtimeSprite"] = CaptureAiInitialSpriteDefinition(definition, initialSprite);
            bool hasModule = definition.Modules.TryGetValue("animator", out ItemModuleDefinitionDto configured);
            sample["hasAnimatorModule"] = hasModule;
            if (!hasModule) continue;

            GameObject shell;
            GameObject controllerPrefab;
#if UNITY_EDITOR
            shell = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(definition.SourcePrefab);
            controllerPrefab = configured.Prefab == "Module_SingleSpriteController"
                ? UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/2_Prefabs/Gameplay/Modules/Animation/Module_SingleSpriteController.prefab") : null;
#else
            shell = GameRes.ExistingInstance?.GetPrefab(definition.ShellPrefab, false);
            controllerPrefab = GameRes.ExistingInstance?.GetPrefab(configured.Prefab, false);
#endif
            Module prototype = ItemDefinitionCatalogLoader.FindModulePrototype(shell, controllerPrefab,
                string.IsNullOrWhiteSpace(configured.Id) ? configured.Prefab : configured.Id);
            sample["hasControllerPrototype"] = prototype is Mod_AnimatorController;
            sample["enabled"] = configured.Enabled.HasValue ? new JValue(configured.Enabled.Value) : JValue.CreateNull();
            sample["bodyRendererCount"] = shell != null ? shell.GetComponentsInChildren<SpriteRenderer>(true).Length : -1;
            sample["moduleRendererCount"] = controllerPrefab != null ? controllerPrefab.GetComponentsInChildren<Renderer>(true).Length : -1;
            sample["moduleColliderCount"] = controllerPrefab != null ? controllerPrefab.GetComponentsInChildren<Collider2D>(true).Length : -1;
            sample["moduleBodyCount"] = controllerPrefab != null ? controllerPrefab.GetComponentsInChildren<Rigidbody2D>(true).Length : -1;
            if (prototype is not Mod_AnimatorController) continue;

            JObject registration = CaptureAiAnimationRegistration(definition, configured, prototype, configured.Id);
            foreach (JProperty property in registration.Properties()) sample[property.Name] = property.Value.DeepClone();
            sample["declaredPrefabId"] = configured.Prefab;
            sample["wrongIdRegistration"] = CaptureAiAnimationRegistration(definition, configured, prototype, "test:wrong_animation");
            sample["deathHealth"] = CaptureAiDeathHealthConfiguration(definition, shell);
            if (definition.Id == "Bird" || definition.Id == "Bee")
                sample["flightDependencies"] = CaptureAiFlightDependencies(definition, shell);
        }
        return new JObject { ["actors"] = actors, ["requiredAnimationId"] = ModText.AnimatorReceiver };
    }

    // 真实构建运行时定义，避免仅验证编辑器能读到图片而漏掉 Actor 编译丢弃 Sprite。
    private static JObject CaptureAiInitialSpriteDefinition(ItemDefinitionDto source, Sprite sprite)
    {
        var root = new GameObject("[LuaActorInitialSpriteDefinition]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            GameItem shell = root.AddComponent<GameItem>();
            shell.BindData(EntityProbeData("test:actor_sprite_shell", 1f));
            var definition = new ItemDefinitionDto
            {
                Id = source.Id,
                GameName = source.GameName,
                ShellPrefab = "test:actor_sprite_shell",
                Visual = new ItemVisualDefinitionDto { SpriteAddress = source.Visual?.SpriteAddress }
            };
            var shells = new Dictionary<string, GameObject> { [definition.ShellPrefab] = root };
            var sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            string address = definition.Visual.SpriteAddress?.Trim();
            if (!string.IsNullOrWhiteSpace(address)) sprites[address] = sprite;
            RuntimeItemDefinition built = ItemDefinitionCatalogLoader.BuildRuntimeDefinition(
                null, definition, sprites, isActor: true, preloadedShells: shells);
            bool missingRejected = false, nullRejected = false, absentAddressRejected = false;
            string missingError = string.Empty;
            try
            {
                ItemDefinitionCatalogLoader.BuildRuntimeDefinition(null, definition,
                    new Dictionary<string, Sprite>(), isActor: true, preloadedShells: shells);
            }
            catch (System.IO.InvalidDataException exception)
            {
                missingRejected = true;
                missingError = exception.Message;
            }
            if (!string.IsNullOrWhiteSpace(address)) sprites[address] = null;
            try
            {
                ItemDefinitionCatalogLoader.BuildRuntimeDefinition(
                    null, definition, sprites, isActor: true, preloadedShells: shells);
            }
            catch (System.IO.InvalidDataException) { nullRejected = true; }
            definition.Visual.SpriteAddress = null;
            try
            {
                ItemDefinitionCatalogLoader.BuildRuntimeDefinition(
                    null, definition, sprites, isActor: true, preloadedShells: shells);
            }
            catch (System.IO.InvalidDataException) { absentAddressRejected = true; }
            return new JObject
            {
                ["isActor"] = built.IsActor,
                ["sameSprite"] = sprite != null && ReferenceEquals(built.Sprite, sprite),
                ["spriteName"] = built.Sprite != null ? built.Sprite.name : string.Empty,
                ["missingRejected"] = missingRejected, ["nullRejected"] = nullRejected,
                ["absentAddressRejected"] = absentAddressRejected, ["missingError"] = missingError,
                ["inactive"] = !root.activeSelf && !shell.IsInitialized
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // 先按显式 JSON 身份绑定再初始化注册，避免 Canonical 默认值把错误 ID 悄悄纠正。
    private static JObject CaptureAiAnimationRegistration(ItemDefinitionDto definition,
        ItemModuleDefinitionDto configured, Module prototype, string moduleId)
    {
        var root = new GameObject("[LuaActorAnimationRegistration]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            GameItem item = root.AddComponent<GameItem>();
            item.BindData(EntityProbeData("test:actor_animation_identity:" + definition.Id, 1f));
            item.itemMods.BindOwner(item);
            ModuleData data = FastCloner.FastCloner.DeepClone<object>(prototype._Data) as ModuleData;
            if (data == null || data.GetType() != prototype._Data.GetType())
                throw new InvalidOperationException("生物动画身份采样缺少同类型的独立模块数据。");
            if (configured.Enabled.HasValue) data.Enabled = configured.Enabled.Value;
            Module instance = (Module)root.AddComponent(prototype.GetType());
            instance._Data = data;
            instance.BindRuntimeIdentity("animator", moduleId, configured.Prefab);
            ModuleJsonConfigurator.Apply(instance, definition.Id, "animator", configured.Id,
                configured.Parameters?.ToString());
            item.itemData.ModuleDataDic["animator"] = data;
            instance.ModuleInit(item, data);
            item.itemMods.AddMod(instance);

            bool requiredRejected = false, requiredSameInstance = false;
            string requiredError = string.Empty;
            try
            {
                requiredSameInstance = ReferenceEquals(instance,
                    item.itemMods.RequireSingleModById<Mod_AnimatorController>(ModText.AnimatorReceiver));
            }
            catch (InvalidOperationException exception)
            {
                requiredRejected = true;
                requiredError = exception.Message;
            }
            return new JObject
            {
                ["visualMode"] = (int)((Mod_AnimatorController)instance).visualMode,
                ["deathDuration"] = ((Mod_AnimatorController)instance).deathDuration,
                ["deathJumpHeight"] = ((Mod_AnimatorController)instance).deathJumpHeight,
                ["enabled"] = instance.Enabled,
                ["stableName"] = instance.StableName, ["moduleId"] = instance.ResolvedModuleId,
                ["prefabId"] = instance.PrefabId,
                ["requiredCount"] = item.itemMods.GetModList_ByID(ModText.AnimatorReceiver)?.Count ?? 0,
                ["registeredIdCount"] = item.itemMods.GetModList_ByID(moduleId)?.Count ?? 0,
                ["requiredSameInstance"] = requiredSameInstance, ["requiredRejected"] = requiredRejected,
                ["requiredError"] = requiredError,
                ["slotSameInstance"] = ReferenceEquals(item.itemMods.GetMod_ByName("animator"), instance) &&
                    ReferenceEquals(item.itemData.ModuleDataDic["animator"], instance._Data),
                ["dataBoundToItem"] = ReferenceEquals(instance.Item_Data, item.itemData)
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // 生命窗口读取最终继承定义并交给正式参数装配，避免只扫描某一份 JSON 漏掉继承覆盖。
    private static JObject CaptureAiDeathHealthConfiguration(ItemDefinitionDto definition, GameObject shell)
    {
        foreach (KeyValuePair<string, ItemModuleDefinitionDto> pair in definition.Modules)
        {
            ItemModuleDefinitionDto configured = pair.Value;
            if (configured.Id != ModText.Hp) continue;
            var prototype = ItemDefinitionCatalogLoader.FindModulePrototype(shell, null, configured.Id) as Mod_DamageReceiver;
            if (prototype == null) return new JObject { ["present"] = true, ["hasPrototype"] = false };
            var root = new GameObject("[LuaActorDeathHealth]") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            try
            {
                var instance = root.AddComponent<Mod_DamageReceiver>();
                instance._Data = FastCloner.FastCloner.DeepClone<object>(prototype._Data) as ModuleData;
                instance.Data = FastCloner.FastCloner.DeepClone<object>(prototype.Data) as Mod_DamageReceiver.DamageReceiver_SaveData;
                if (configured.Enabled.HasValue) instance._Data.Enabled = configured.Enabled.Value;
                ModuleJsonConfigurator.Apply(instance, definition.Id, pair.Key, configured.Id, configured.Parameters?.ToString());
                return new JObject
                {
                    ["present"] = true, ["hasPrototype"] = true, ["enabled"] = instance.Enabled,
                    ["destroyDelay"] = instance.Data.DestroyDelay
                };
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        return new JObject { ["present"] = false, ["hasPrototype"] = false };
    }

    #endregion

    #region 飞行生物真实外壳依赖采样

    // 复用真实外壳与正式模块定位，防止理想化的单动画夹具漏掉鸟和蜜蜂的依赖绑定。
    private static JObject CaptureAiFlightDependencies(ItemDefinitionDto definition, GameObject shell)
    {
        var requiredIds = new HashSet<string>(StringComparer.Ordinal)
        {
            Mod_AnimalFlightMovement.ModuleId, ModText.Mod_Mover, ModText.Mod_TileEffectReceiver,
            ModText.Hp, ModText.Food, ModText.Detector, ModText.AnimatorReceiver
        };
        var root = new GameObject("[LuaFlightDependencies]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            GameObject instance = UnityEngine.Object.Instantiate(shell, root.transform, false);
            Item item = instance.GetComponent<Item>();
            item.IsPreparedCandidate = true;
            item.BindData(EntityProbeData("test:flight_dependencies:" + definition.Id, 1f));
            item.itemMods.ResetForReuse(item);
            var modules = new List<Module>();
            using (item.itemMods.BeginMutation())
            {
                foreach (KeyValuePair<string, ItemModuleDefinitionDto> pair in definition.Modules)
                {
                    ItemModuleDefinitionDto configured = pair.Value;
                    if (!requiredIds.Contains(configured.Id)) continue;
                    Module module = ItemDefinitionCatalogLoader.FindModulePrototype(instance, null, configured.Id) ??
                        ItemDefinitionCatalogLoader.FindModulePrototype(instance, null, configured.Prefab);
                    if (module == null)
                        throw new InvalidOperationException($"飞行生物 {definition.Id} 的真实外壳缺少依赖模块 {pair.Key}。");
                    ModuleData data = FastCloner.FastCloner.DeepClone<object>(module._Data) as ModuleData;
                    if (data == null || data.GetType() != module._Data.GetType())
                        throw new InvalidOperationException("飞行依赖采样缺少同类型的独立模块数据。");
                    if (configured.Enabled.HasValue) data.Enabled = configured.Enabled.Value;
                    module._Data = data;
                    module.BindRuntimeIdentity(pair.Key, configured.Id, configured.Prefab);
                    ModuleJsonConfigurator.Apply(module, definition.Id, pair.Key, configured.Id,
                        configured.Parameters?.ToString());
                    item.itemData.ModuleDataDic[pair.Key] = data;
                    item.itemMods.AddMod(module);
                    modules.Add(module);
                }
                foreach (Module module in modules) module.ModuleInit(item, module._Data);
            }

            Mod_AnimalFlightMovement flight = item.itemMods.RequireSingleModById<Mod_AnimalFlightMovement>(Mod_AnimalFlightMovement.ModuleId);
            flight.BindModuleDependencies(item.itemMods);
            Mod_AnimatorController animation = item.itemMods.RequireSingleModById<Mod_AnimatorController>(ModText.AnimatorReceiver);
            bool allBound = true, allEnabled = true, noneLoaded = true;
            foreach (Module module in modules)
            {
                allBound &= ReferenceEquals(item.itemData.ModuleDataDic[module.StableName], module._Data) &&
                    ReferenceEquals(module.Item_Data, item.itemData) &&
                    ReferenceEquals(item.itemMods.GetMod_ByName(module.StableName), module);
                allEnabled &= module.Enabled;
                noneLoaded &= !module.IsRuntimeLoaded;
            }
            var sample = new JObject
            {
                ["moduleCount"] = modules.Count, ["allBound"] = allBound,
                ["allEnabled"] = allEnabled, ["noneLoaded"] = noneLoaded,
                ["animationCount"] = item.itemMods.GetModList_ByID(ModText.AnimatorReceiver)?.Count ?? 0,
                ["singleSprite"] = animation.IsSingleSpriteMode,
                ["bound"] = true, ["inactive"] = !instance.activeInHierarchy
            };

            item.itemMods.RemoveMod(animation);
            animation.BindRuntimeIdentity(animation.StableName, "test:wrong_animation", animation.PrefabId);
            item.itemMods.AddMod(animation);
            bool rejected = false;
            string error = string.Empty;
            try { flight.BindModuleDependencies(item.itemMods); }
            catch (InvalidOperationException exception) { rejected = true; error = exception.Message; }
            sample["wrongIdRejected"] = rejected;
            sample["wrongIdError"] = error;
            sample["wrongIdCount"] = item.itemMods.GetModList_ByID("test:wrong_animation")?.Count ?? 0;
            sample["requiredCountAfterWrongId"] = item.itemMods.GetModList_ByID(ModText.AnimatorReceiver)?.Count ?? 0;
            return sample;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    #endregion

    #region 水生表现的单图接管采样

    private static JObject EntityAiSingleSpriteAquatic(JObject args)
    {
#if UNITY_EDITOR
        GameObject shell = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2_Prefabs/Gameplay/AI/SmallFish.prefab");
#else
        GameObject shell = GameRes.ExistingInstance?.GetPrefab("CoreActor.SmallFish.Shell", false);
#endif
        Material material = shell != null ? shell.GetComponentInChildren<SpriteRenderer>(true)?.sharedMaterial : null;
        if (material == null) throw new InvalidOperationException("水生单图采样缺少正式小鱼主体材质。");
        var root = new GameObject("[LuaAquaticAnimation]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        AquaticActorPresentation presentation = null;
        try
        {
            GameItem item = root.AddComponent<GameItem>();
            item.BindData(EntityProbeData("test:aquatic_animation", 1f));
            var visual = new GameObject("FishVisual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(.13f, .17f, 0f);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 7f);
            Vector3 originalPosition = visual.transform.localPosition;
            Quaternion originalRotation = visual.transform.localRotation;
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sharedMaterial = material;
            Mod_AnimatorController animation = root.AddComponent<Mod_AnimatorController>();
            animation.ModuleInit(item, new Ex_ModData_MemoryPackable { Enabled = true });
            animation.visualMode = Mod_AnimatorController.VisualMode.SingleSprite;
            item.itemMods.AddMod(animation);
            presentation = new AquaticActorPresentation(item, renderer);
            Vector3 commandedPosition = Vector3.right;
            root.transform.position = commandedPosition;
            JObject Capture()
            {
                Vector3 offset = presentation.VisualWorldOffset;
                return new JObject
                {
                    ["singleSprite"] = presentation.UseSingleSpriteAnimation,
                    ["swimming"] = presentation.IsSwimming,
                    ["height"] = offset.y, ["roll"] = presentation.VisualRollDegrees,
                    ["positionError"] = Vector3.Distance(originalPosition, visual.transform.localPosition),
                    ["rotationError"] = Quaternion.Angle(originalRotation, visual.transform.localRotation),
                    ["rootPositionError"] = Vector3.Distance(commandedPosition, root.transform.position)
                };
            }
            presentation.Tick(true, .08f);
            var result = new JObject { ["swimming"] = Capture() };
            presentation.SetStrandedHop(.24f, 18f);
            presentation.Tick(false, .08f);
            result["stranded"] = Capture();
            presentation.SetCarried(true, .4f);
            presentation.Tick(false, .08f);
            result["carried"] = Capture();
            presentation.SetCarried(false, 0f);
            presentation.SetStrandedHop(0f, 0f);
            animation.visualMode = Mod_AnimatorController.VisualMode.Animator;
            commandedPosition = Vector3.right * 2f;
            root.transform.position = commandedPosition;
            presentation.Tick(true, .08f);
            result["animatorSwimming"] = Capture();
            return result;
        }
        finally
        {
            try { presentation?.Dispose(); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    #endregion
}
