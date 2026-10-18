using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 单图生物动画采样

    // 独立视觉夹具只推进正式动画模块，动作结果与复位规则由 Lua 断言。
    private static JObject EntitySingleSpriteAnimation(JObject args)
    {
        if (args["steps"] is not JArray steps || steps.Count == 0 || steps.Count > 128)
            throw new ArgumentException("单图动画采样需要 1 至 128 个连续步骤。");

        var root = new GameObject("[LuaSingleSpriteAnimation]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        Mod_AnimatorController animation = null;
        GameplayTestAnimationMotionSource motionSource = null;
        Mod_DamageReceiver health = null;
        Mod_Damage damage = null;
        int deathStartedCount = 0;
        var sprites = new List<Sprite>();
        try
        {
            root.transform.position = new Vector3(11f, -7f, 0f);
            root.transform.localScale = new Vector3((Flag(args, "mirrored", false) ? -1f : 1f) *
                (float)Number(args, "rootScaleX", 1d), (float)Number(args, "rootScaleY", 1d), 1f);
            Vector3 rootPosition = root.transform.position;
            Quaternion rootRotation = root.transform.rotation;
            Vector3 rootScale = root.transform.localScale;
            GameItem item = root.AddComponent<GameItem>();
            item.BindData(EntityProbeData("test:single_sprite_animation", 1f));
            Rigidbody2D body = root.AddComponent<Rigidbody2D>();
            body.simulated = false;
            body.position = rootPosition;
            Mod_Mover mover = root.AddComponent<Mod_Mover>();
            mover.enabled = false;
            mover.rb = body;
            mover.ModuleInit(item, new Ex_ModData_MemoryPackable { ID = ModText.Mod_Mover, Enabled = true });
            item.itemMods.AddMod(mover);
            if (Flag(args, "withHealth", false))
            {
                health = root.AddComponent<Mod_DamageReceiver>();
                health.ModuleInit(item, new Ex_ModData { ID = ModText.Hp, Enabled = true });
                health.Data = new Mod_DamageReceiver.DamageReceiver_SaveData { Hp = 10f, MaxHp = 10f, DestroyDelay = -1f };
                health.DeathStarted += _ => deathStartedCount++;
                item.itemMods.AddMod(health);
            }
            if (Flag(args, "withDamage", false))
            {
                if (health == null) throw new ArgumentException("死亡伤害窗口采样需要生命模块。");
#if UNITY_EDITOR
                GameObject damagePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/2_Prefabs/Gameplay/Modules/Combat/Mod_Damage.prefab");
#else
                GameObject damagePrefab = GameRes.ExistingInstance?.GetPrefab("Mod_Damage", false);
#endif
                if (damagePrefab == null) throw new InvalidOperationException("死亡伤害窗口采样缺少正式伤害模块Prefab。");
                // 保留真实 Prefab 的显式碰撞体绑定，首次加载前只指定窗口初态。
                damage = UnityEngine.Object.Instantiate(damagePrefab, root.transform, false).GetComponent<Mod_Damage>();
                if (damage == null || damage.DamageCollider == null)
                    throw new InvalidOperationException("正式伤害模块Prefab缺少已绑定伤害碰撞体。");
                damage.DamageCollider.enabled = Flag(args, "damageInitiallyOpen", false);
                damage.ModuleInit(item, new Ex_ModData_MemoryPackable { ID = "Mod_Damage", Enabled = true });
                item.itemMods.AddMod(damage);
            }
            if (Flag(args, "withMotionSource", false))
            {
                motionSource = root.AddComponent<GameplayTestAnimationMotionSource>();
                motionSource.ModuleInit(item, new Ex_ModData_MemoryPackable { ID = "test:animation_motion", Enabled = true });
                item.itemMods.AddMod(motionSource);
            }

            Transform visualParent = root.transform;
            if (Flag(args, "nestedVisual", false))
            {
                visualParent = new GameObject("BodyContainer").transform;
                visualParent.SetParent(root.transform, false);
                visualParent.localPosition = new Vector3(.2f, .1f, 0f);
            }
            new GameObject("SiblingBefore").transform.SetParent(visualParent, false);
            var visual = new GameObject("Sprite");
            visual.transform.SetParent(visualParent, false);
            visual.transform.localPosition = new Vector3(.13f, .17f, 0f);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 7f);
            visual.transform.localScale = new Vector3((float)Number(args, "visualScaleX", 1.1d),
                (float)Number(args, "visualScaleY", .9d), 1f);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.flipX = Flag(args, "flipX", false);
            renderer.flipY = Flag(args, "flipY", false);
            Vector3 visualPosition = visual.transform.position;
            Vector3 visualLocalPosition = visual.transform.localPosition;
            Quaternion visualLocalRotation = visual.transform.localRotation;
            Vector3 visualLocalScale = visual.transform.localScale;
            Vector3 visualWorldScale = visual.transform.lossyScale;
            float visualAngle = visual.transform.eulerAngles.z;
            int siblingIndex = visual.transform.GetSiblingIndex();

            animation = root.AddComponent<Mod_AnimatorController>();
            animation.visualMode = Mod_AnimatorController.VisualMode.SingleSprite;
            animation.idleBreathAmount = 0f;
            animation.targetSpriteRenderer = renderer;
            animation.spriteVisualRoot = visual.transform;
            if (Flag(args, "withSpriteStates", false))
            {
                foreach (string name in new[] { "original", "flying", "landing" })
                {
                    Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f),
                        new Vector2((float)Number(args, "pivotX", 0d), (float)Number(args, "pivotY", 0d)), 1f);
                    sprite.name = name;
                    sprites.Add(sprite);
                }
                renderer.sprite = sprites[0];
                animation.singleSpriteStates = new[]
                {
                    new Mod_AnimatorController.SingleSpriteState { stateName = "Ground", sprite = sprites[0] },
                    new Mod_AnimatorController.SingleSpriteState { stateName = "Flying", sprite = sprites[1] },
                    new Mod_AnimatorController.SingleSpriteState { stateName = "Landing", sprite = sprites[2] },
                    new Mod_AnimatorController.SingleSpriteState { stateName = "Broken", sprite = null }
                };
            }
            float baseBottomY = renderer.bounds.min.y;
            Transform additionalVisual = null;
            int additionalSiblingIndex = -1;
            if (Flag(args, "withAdditionalVisual", false))
            {
                additionalVisual = new GameObject("Accessory").transform;
                additionalVisual.SetParent(root.transform, false);
                additionalSiblingIndex = additionalVisual.GetSiblingIndex();
                animation.additionalVisualRoots = new[] { additionalVisual };
            }
            animation.ModuleInit(item, new Ex_ModData_MemoryPackable { Enabled = true });
            item.itemMods.AddMod(animation);
            Animator sourceAnimator = Flag(args, "withAnimator", false) ? root.AddComponent<Animator>() : null;
            animation.animator = sourceAnimator;
            root.SetActive(true);
            if (motionSource != null) motionSource.Load();
            animation.Load();
            if (damage != null) damage.Load();

            bool network = Flag(args, "network", false), moving = false, running = false;
            var samples = new JArray();
            foreach (JToken value in steps)
            {
                if (value is not JObject step)
                    throw new ArgumentException("单图动画采样步骤必须是对象。");

                network = Flag(step, "network", network);
                moving = Flag(step, "moving", moving);
                running = Flag(step, "running", running);
                mover.IsMoving = moving;
                mover.IsRunning = running;
                if (step["source"] is JObject source)
                {
                    if (motionSource == null) throw new ArgumentException("单图采样没有创建主动表现信号模块。");
                    motionSource.Available = Flag(source, "available", true);
                    motionSource.Motion = new ActorAnimationMotion
                    {
                        IsMoving = Flag(source, "moving", false), IsRunning = Flag(source, "running", false),
                        WorldOffset = new Vector3((float)Number(source, "x", 0d), (float)Number(source, "y", 0d), 0f),
                        RollDegrees = (float)Number(source, "roll", 0d)
                    };
                }
                if (step["sourceEnabled"] != null && motionSource != null)
                {
                    bool sourceEnabled = Flag(step, "sourceEnabled", true);
                    motionSource.SetEnabled(sourceEnabled);
                    if (sourceEnabled) motionSource.Load();
                }
                if (step["velocityX"] != null || step["velocityY"] != null)
                    body.velocity = new Vector2((float)Number(step, "velocityX", 0d), (float)Number(step, "velocityY", 0d));
                animation.SetNetworkPresentation(network, moving, running);
                if (step["componentEnabled"] != null)
                    animation.enabled = Flag(step, "componentEnabled", true);
                if (step["externalY"] != null)
                {
                    visualLocalRotation = Quaternion.Euler(0f, (float)Number(step, "externalY", 0d), 7f);
                    visual.transform.localRotation = visualLocalRotation;
                }

                if (step["mode"] != null)
                {
                    string mode = Text(step, "mode", "SingleSprite");
                    animation.visualMode = mode switch
                    {
                        "SingleSprite" => Mod_AnimatorController.VisualMode.SingleSprite,
                        "Animator" => Mod_AnimatorController.VisualMode.Animator,
                        _ => throw new ArgumentException("单图动画采样不支持该视觉模式：" + mode)
                    };
                }
                if (Flag(step, "attack", false))
                    animation.PlayAttack(new Vector2((float)Number(step, "x", 1d), (float)Number(step, "y", 0d)));
                if (Flag(step, "jump", false)) animation.PlayJump();
                if (Flag(step, "hit", false)) animation.PlayHitReaction(.3f, .05f);
                if (Flag(step, "revive", false))
                {
                    if (health == null) throw new ArgumentException("复活动画采样需要生命模块。");
                    health.Hp = health.MaxHp;
                }
                if (Flag(step, "kill", false) || Flag(step, "zeroHp", false))
                {
                    if (health == null) throw new ArgumentException("死亡事件采样需要生命模块。");
                    health.Hp = 0f;
                    if (Flag(step, "kill", false)) health.ResolveNetworkAuthoritativeDeath("test:animation_death");
                }
                if (Flag(step, "damageAttack", false))
                {
                    if (damage == null) throw new ArgumentException("伤害窗口采样没有创建伤害模块。");
                    damage.StartAttack();
                }
                if (Flag(step, "damageReload", false))
                {
                    if (damage == null) throw new ArgumentException("伤害窗口采样没有创建伤害模块。");
                    damage.Unload();
                    damage.Load();
                }
                bool deathAccepted = Flag(step, "death", false) && animation.PlayDeath();
                bool legacyStateAccepted = step["legacyState"] != null &&
                    animation.TryPlayAnimation(Text(step, "legacyState", string.Empty), Flag(step, "forceRestart", true));
                bool spriteAccepted = step["sprite"] != null && animation.TrySetSprite(
                    Text(step, "sprite", string.Empty) == "alternate" && sprites.Count > 2 ? sprites[2] : null);
                bool legacyMoveAccepted = step["legacyMove"] != null &&
                    animation.SetBool(AnimationText.Move, Flag(step, "legacyMove", false));
                if (Flag(step, "reset", false)) animation.ResetPresentation();
                switch (Text(step, "lifecycle", string.Empty))
                {
                    case "unload": animation.Unload(); break;
                    case "load": animation.Load(); break;
                    case "disable": animation.SetEnabled(false); break;
                    case "enable": animation.SetEnabled(true); animation.Load(); break;
                    case "pool_reuse":
                        // 同一外壳经过正式回收与取池复位，再绑定新载荷，不替生产代码手动清动画。
                        item.PrepareForDespawn(saveData: false);
                        root.SetActive(false);
                        item.NotifyReturnedToPool();
                        item.PrepareForPoolReuse();
                        item.BindData(EntityProbeData("test:single_sprite_animation", 1f));
                        mover.ModuleInit(item, new Ex_ModData_MemoryPackable { ID = ModText.Mod_Mover, Enabled = true });
                        item.itemMods.AddMod(mover);
                        if (health != null)
                        {
                            health.ModuleInit(item, new Ex_ModData { ID = ModText.Hp, Enabled = true });
                            health.Hp = health.MaxHp;
                            item.itemMods.AddMod(health);
                        }
                        if (motionSource != null)
                        {
                            motionSource.ModuleInit(item, new Ex_ModData_MemoryPackable { ID = "test:animation_motion", Enabled = true });
                            motionSource.Available = false;
                            item.itemMods.AddMod(motionSource);
                        }
                        animation.ModuleInit(item, new Ex_ModData_MemoryPackable { Enabled = true });
                        item.itemMods.AddMod(animation);
                        if (damage != null)
                        {
                            damage.ModuleInit(item, new Ex_ModData_MemoryPackable { ID = "Mod_Damage", Enabled = true });
                            item.itemMods.AddMod(damage);
                        }
                        root.SetActive(true);
                        if (motionSource != null) motionSource.Load();
                        animation.Load();
                        if (damage != null) damage.Load();
                        break;
                    case "": break;
                    default: throw new ArgumentException("单图动画采样生命周期操作未登记。");
                }
                bool damageEnabledBeforeAdvance = damage != null && damage.DamageCollider.enabled;
                if (animation.IsRuntimeLoaded && animation.Enabled && animation.enabled)
                    animation.AdvancePresentation((float)Number(step, "deltaTime", .08d));

                Vector3 offset = visual.transform.position - visualPosition;
                bool hasBaseMatrix = animation.TryGetBaseVisualMatrix(visual.transform, out Matrix4x4 baseMatrix);
                Matrix4x4 expectedBaseMatrix = visualParent.localToWorldMatrix *
                    Matrix4x4.TRS(visualLocalPosition, visualLocalRotation, visualLocalScale);
                float baseMatrixError = 0f;
                if (hasBaseMatrix)
                    for (int index = 0; index < 16; index++)
                        baseMatrixError = Mathf.Max(baseMatrixError, Mathf.Abs(baseMatrix[index] - expectedBaseMatrix[index]));
                samples.Add(new JObject
                {
                    ["x"] = offset.x, ["y"] = offset.y, ["z"] = offset.z,
                    ["angle"] = Mathf.DeltaAngle(visualAngle, visual.transform.eulerAngles.z),
                    ["scaleX"] = visual.transform.lossyScale.x / visualWorldScale.x,
                    ["scaleY"] = visual.transform.lossyScale.y / visualWorldScale.y,
                    ["rootPositionError"] = Vector3.Distance(rootPosition, root.transform.position),
                    ["rootRotationError"] = Quaternion.Angle(rootRotation, root.transform.rotation),
                    ["rootScaleError"] = Vector3.Distance(rootScale, root.transform.localScale),
                    ["bodyPositionError"] = Vector2.Distance(rootPosition, body.position),
                    ["bodySpeed"] = body.velocity.magnitude,
                    ["localPositionError"] = Vector3.Distance(visualLocalPosition, visual.transform.localPosition),
                    ["localRotationError"] = Quaternion.Angle(visualLocalRotation, visual.transform.localRotation),
                    ["localFacingX"] = (visual.transform.localRotation * Vector3.right).x,
                    ["localScaleError"] = Vector3.Distance(visualLocalScale, visual.transform.localScale),
                    ["parentRestored"] = visual.transform.parent == visualParent && visual.transform.GetSiblingIndex() == siblingIndex,
                    ["attacking"] = animation.IsAttackPlaying,
                    ["dead"] = animation.IsDeadPresentation,
                    ["deathPlaying"] = animation.IsDeathPlaying,
                    ["deathAccepted"] = deathAccepted,
                    ["deathStartedCount"] = deathStartedCount,
                    ["hp"] = health != null ? new JValue(health.Hp) : JValue.CreateNull(),
                    ["damageEnabled"] = damage != null && damage.DamageCollider.enabled,
                    ["damageEnabledBeforeAdvance"] = damageEnabledBeforeAdvance,
                    ["bottomY"] = renderer.bounds.min.y,
                    ["bottomOffset"] = renderer.bounds.min.y - baseBottomY,
                    ["componentEnabled"] = animation.enabled,
                    ["bodyAnimatorEnabled"] = sourceAnimator != null && sourceAnimator.enabled,
                    ["legacyStateAccepted"] = legacyStateAccepted,
                    ["spriteAccepted"] = spriteAccepted,
                    ["sprite"] = renderer.sprite != null ? renderer.sprite.name : string.Empty,
                    ["legacyMoveAccepted"] = legacyMoveAccepted,
                    ["hasBaseMatrix"] = hasBaseMatrix,
                    ["basePositionError"] = hasBaseMatrix ? Vector3.Distance(visualPosition, baseMatrix.MultiplyPoint3x4(Vector3.zero)) : -1f,
                    ["baseMatrixError"] = hasBaseMatrix ? baseMatrixError : -1f,
                    ["loaded"] = animation.IsRuntimeLoaded
                });
            }
            var result = new JObject
            {
                ["samples"] = samples, ["baseBottomY"] = baseBottomY,
                ["deathDuration"] = animation.deathDuration, ["deathJumpHeight"] = animation.deathJumpHeight
            };
            string finalLifecycle = Text(args, "finalLifecycle", string.Empty);
            if (!string.IsNullOrEmpty(finalLifecycle))
            {
                Transform poseRoot = visual.transform.parent;
                Transform additionalPoseRoot = additionalVisual != null ? additionalVisual.parent : null;
                var errors = new JArray();
                void CaptureLifecycleLog(string message, string stackTrace, LogType type)
                {
                    if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                        errors.Add(message);
                }
                // 直接调用正式生命周期并保留 Unity 日志，让 Lua 同时检查销毁和存活宿主的恢复边界。
                Application.logMessageReceived += CaptureLifecycleLog;
                try
                {
                    switch (finalLifecycle)
                    {
                        case "destroy_owner": UnityEngine.Object.DestroyImmediate(root); break;
                        case "destroy_component": UnityEngine.Object.DestroyImmediate(animation); break;
                        case "despawn": item.PrepareForDespawn(saveData: false); break;
                        default: throw new ArgumentException("单图动画收尾生命周期操作未登记：" + finalLifecycle);
                    }
                }
                finally { Application.logMessageReceived -= CaptureLifecycleLog; }
                result["lifecycle"] = new JObject
                {
                    ["errors"] = errors,
                    ["ownerDestructionStarted"] = item.IsBeingDestroyed,
                    ["ownerDestroyed"] = root == null,
                    ["visualDestroyed"] = visual == null,
                    ["poseDestroyed"] = poseRoot == null,
                    ["poseReleased"] = poseRoot == null || (poseRoot.parent == null && poseRoot.childCount == 0),
                    ["additionalDestroyed"] = additionalVisual == null,
                    ["additionalPoseDestroyed"] = additionalPoseRoot == null,
                    ["additionalPoseReleased"] = additionalPoseRoot == null ||
                        (additionalPoseRoot.parent == null && additionalPoseRoot.childCount == 0),
                    ["parentRestored"] = visual != null && visual.transform.parent == visualParent &&
                        visual.transform.GetSiblingIndex() == siblingIndex,
                    ["additionalParentRestored"] = additionalVisual != null && additionalVisual.parent == root.transform,
                    ["additionalSiblingRestored"] = additionalVisual != null &&
                        additionalVisual.GetSiblingIndex() == additionalSiblingIndex,
                    ["localFacingX"] = visual != null ? (visual.transform.localRotation * Vector3.right).x : 0f,
                    ["localPositionError"] = visual != null ? Vector3.Distance(visualLocalPosition, visual.transform.localPosition) : 0f,
                    ["localScaleError"] = visual != null ? Vector3.Distance(visualLocalScale, visual.transform.localScale) : 0f
                };
            }
            return result;
        }
        finally
        {
            try
            {
                try { if (animation != null) animation.Unload(); }
                finally { if (motionSource != null) motionSource.Unload(); }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach (Sprite sprite in sprites) UnityEngine.Object.DestroyImmediate(sprite);
            }
        }
    }

    #endregion

    #region 模块装配身份边界采样

    // 正式编译计划直接检查模板默认身份、子物品边界及独立 Prefab 的精确组件位置。
    private static JObject EntityModuleAssemblyIdentity(JObject args)
    {
        var root = new GameObject("[LuaModuleAssemblyIdentity]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            var shell = new GameObject("IdentityShell");
            shell.transform.SetParent(root.transform, false);
            shell.AddComponent<GameItem>().IsPreparedCandidate = true;
            var node = new GameObject("SourceAnimation");
            node.transform.SetParent(shell.transform, false);
            var source = node.AddComponent<Mod_AnimatorController>();
            source._Data = new Ex_ModData_MemoryPackable { StableName = "template-default-slot", ModuleId = "template-default-id" };
            var nested = new GameObject("NestedItem");
            nested.transform.SetParent(shell.transform, false);
            nested.AddComponent<GameItem>().IsPreparedCandidate = true;
            var nestedModule = nested.AddComponent<Mod_AnimatorController>();
            nestedModule._Data = new Ex_ModData_MemoryPackable { StableName = "animator", ModuleId = ModText.AnimatorReceiver };
            var definition = new RuntimeItemModuleDefinition("animator", ModText.AnimatorReceiver,
                ModText.AnimatorReceiver, null, true);
            RuntimeItemModuleAssemblyPlan plan = ItemModuleAssemblyCompiler.Compile(null, "test:identity", shell,
                new[] { definition }, usesResourceEntities: false)[0];
            bool nestedExcludedFromCompile = ReferenceEquals(plan.ResolveEmbeddedModule(shell.transform), source);
            bool shellPrototypeExact = ReferenceEquals(plan.ResolveImplementationPrototype(shell.transform), source);

            GameObject first = UnityEngine.Object.Instantiate(shell, root.transform, false);
            Module firstModule = plan.ResolveEmbeddedModule(first.transform);
            bool sourceIdentityRestored = plan.TryRestoreRuntimeIdentity(firstModule, first.transform) && plan.MatchesBoundModule(firstModule);
            GameObject second = UnityEngine.Object.Instantiate(shell, root.transform, false);
            Module secondModule = plan.ResolveEmbeddedModule(second.transform);
            secondModule._Data.ModuleId = ModText.AnimatorReceiver;
            bool canonicalIdentityRestored = plan.TryRestoreRuntimeIdentity(secondModule, second.transform) && plan.MatchesBoundModule(secondModule);
            GameObject corrupted = UnityEngine.Object.Instantiate(shell, root.transform, false);
            Module corruptedModule = plan.ResolveEmbeddedModule(corrupted.transform);
            corruptedModule._Data.StableName = "tampered-slot";
            bool sourceStableConflictRejected = !plan.TryRestoreRuntimeIdentity(corruptedModule, corrupted.transform);
            corruptedModule._Data.StableName = source.StableName;
            corruptedModule._Data.ModuleId = "tampered-id";
            bool sourceIdConflictRejected = !plan.TryRestoreRuntimeIdentity(corruptedModule, corrupted.transform);
            bool unrelatedOwnerRejected = !plan.TryRestoreRuntimeIdentity(source, root.transform);
            bool nestedRestoreRejected = !plan.TryRestoreRuntimeIdentity(nestedModule, shell.transform);

            var prefab = new GameObject("SpecificAnimationPrefab");
            prefab.transform.SetParent(root.transform, false);
            var implementation = new GameObject("Implementation");
            implementation.transform.SetParent(prefab.transform, false);
            var prototype = implementation.AddComponent<Mod_AnimatorController>();
            prototype._Data = new Ex_ModData_MemoryPackable { StableName = "animator", ModuleId = ModText.AnimatorReceiver };
            var prefabDefinition = new RuntimeItemModuleDefinition("animator", ModText.AnimatorReceiver, prefab.name, null, true);
            var prefabPlan = new RuntimeItemModuleAssemblyPlan("test:identity", prefabDefinition, prefab, prototype, null, null, null);
            GameObject correctPrefab = UnityEngine.Object.Instantiate(prefab, shell.transform, false);
            correctPrefab.name = prefab.name;
            Module correctModule = correctPrefab.GetComponentInChildren<Module>(true);
            bool exactPrefabRestored = prefabPlan.TryRestoreRuntimeIdentity(correctModule, shell.transform) && prefabPlan.MatchesBoundModule(correctModule);
            GameObject wrongPrefab = UnityEngine.Object.Instantiate(prefab, shell.transform, false);
            wrongPrefab.name = prefab.name;
            Module wrongModule = wrongPrefab.GetComponentInChildren<Module>(true);
            wrongModule.gameObject.name = "WrongImplementation";
            bool namedAncestorRejected = !prefabPlan.TryRestoreRuntimeIdentity(wrongModule, shell.transform);
            bool prefabPrototypeExact = ReferenceEquals(prefabPlan.ResolveImplementationPrototype(shell.transform), prototype);
            bool nestedPrefabRejected = false;
            try { ItemDefinitionCatalogLoader.FindModulePrototype(null, nested, ModText.AnimatorReceiver); }
            catch (System.IO.InvalidDataException) { nestedPrefabRejected = true; }

            var inactiveFirst = shell.AddComponent<GameplayTestAnimationMotionSource>();
            var inactiveSecond = shell.AddComponent<GameplayTestAnimationMotionSource>();
            inactiveFirst._Data.ModuleId = "test:shared-inactive";
            inactiveSecond._Data.ModuleId = "test:shared-inactive";
            inactiveFirst.EnsureRuntimeIdentity();
            inactiveSecond.EnsureRuntimeIdentity();
            // 使用真实 AI 和转身组件覆盖“先 Awake 再装配”及“先装配再 Awake”两种顺序。
            var awakeShell = new GameObject("AwakeIdentityShell");
            awakeShell.transform.SetParent(root.transform, false);
            awakeShell.AddComponent<GameItem>().IsPreparedCandidate = true;
            var aiNode = new GameObject("Module_AI_Chicken");
            aiNode.transform.SetParent(awakeShell.transform, false);
            var aiSource = aiNode.AddComponent<AI_Chicken>();
            aiSource.ModData = new Ex_ModData_MemoryPackable { StableName = "", ModuleId = "" };
            var turnNode = new GameObject("Module_TurnBody");
            turnNode.transform.SetParent(awakeShell.transform, false);
            var turnSource = turnNode.AddComponent<Mod_TurnBack>();
            turnSource.modData = new Ex_ModData { StableName = "", ModuleId = "" };
            var awakeDefinitions = new[]
            {
                new RuntimeItemModuleDefinition("ai", aiNode.name, aiNode.name, null, true),
                new RuntimeItemModuleDefinition("turn", turnNode.name, turnNode.name, null, true)
            };
            var awakePlans = ItemModuleAssemblyCompiler.Compile(null, "test:awake-identity", awakeShell,
                awakeDefinitions, usesResourceEntities: false);
            GameObject awakeInstance = UnityEngine.Object.Instantiate(awakeShell, root.transform, false);
            var awakeBindings = new JArray();
            foreach (RuntimeItemModuleAssemblyPlan awakePlan in awakePlans)
            {
                Module module = awakePlan.ResolveEmbeddedModule(awakeInstance.transform);
                string sourceId = module.ResolvedModuleId;
                string sourceSlot = module.StableName;
                module.Awake();
                bool sourceUnchanged = module.ResolvedModuleId == sourceId && module.StableName == sourceSlot;
                bool restored = awakePlan.TryRestoreRuntimeIdentity(module, awakeInstance.transform);
                module.Awake();
                awakeBindings.Add(new JObject
                {
                    ["slot"] = awakePlan.StableName,
                    ["sourceUnchanged"] = sourceUnchanged,
                    ["restored"] = restored,
                    ["boundPreserved"] = awakePlan.MatchesBoundModule(module)
                });
            }
            return new JObject
            {
                ["awakeBindings"] = awakeBindings,
                ["nestedExcludedFromCompile"] = nestedExcludedFromCompile,
                ["shellPrototypeExact"] = shellPrototypeExact,
                ["sourceIdentityRestored"] = sourceIdentityRestored,
                ["canonicalIdentityRestored"] = canonicalIdentityRestored,
                ["sourceStableConflictRejected"] = sourceStableConflictRejected,
                ["sourceIdConflictRejected"] = sourceIdConflictRejected,
                ["unrelatedOwnerRejected"] = unrelatedOwnerRejected,
                ["nestedRestoreRejected"] = nestedRestoreRejected,
                ["nestedUnchanged"] = nestedModule.StableName == "animator" && nestedModule.PrefabId == null && nestedModule.Enabled,
                ["exactPrefabRestored"] = exactPrefabRestored,
                ["namedAncestorRejected"] = namedAncestorRejected,
                ["prefabPrototypeExact"] = prefabPrototypeExact,
                ["nestedPrefabRejected"] = nestedPrefabRejected,
                ["inactiveStableNamesDistinct"] = inactiveFirst.StableName != inactiveSecond.StableName
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    #endregion

    #region 单图生物外壳装配采样

    // 正式定义与编译器共同定位改名的内嵌动画，避免理想化组件夹具漏掉重复视觉节点。
    private static JObject EntitySingleSpriteShellAssembly(JObject args)
    {
        string actorId = Text(args, "actorId", "Wolf");
        List<ItemDefinitionDto> definitions = null;
        var candidate = new ResourceReloadContext();
        ActorDefinitionCatalogLoader.ConfigureResourceReload(candidate);
        candidate.InCandidate(() => definitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions());
        ItemDefinitionDto definition = definitions.Find(value => value.Id == actorId && !value.Abstract) ??
            throw new ArgumentException("单图外壳采样找不到正式生物定义：" + actorId);
        if (!definition.Modules.TryGetValue("animator", out ItemModuleDefinitionDto configured))
            throw new InvalidOperationException("单图外壳采样缺少正式 animator 槽位。");

        GameObject shell;
#if UNITY_EDITOR
        shell = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(definition.SourcePrefab);
#else
        shell = GameRes.ExistingInstance?.GetPrefab(definition.ShellPrefab, false);
#endif
        if (shell == null) throw new InvalidOperationException("单图外壳采样缺少真实生物 Prefab。");
        var moduleDefinition = new RuntimeItemModuleDefinition("animator", configured.Id, configured.Prefab,
            configured.Parameters?.ToString(), configured.Enabled ?? true);
        var moduleDefinitions = new[] { moduleDefinition };
        // 不提供资源能力别名，让此规则直接检查显式稳定身份能否命中真实外壳。
        RuntimeItemModuleAssemblyPlan assembly = ItemModuleAssemblyCompiler.Compile(null, definition.Id,
            shell, moduleDefinitions, usesResourceEntities: false)[0];
        Module template = assembly.ResolveEmbeddedModule(shell.transform);
        var root = new GameObject("[LuaSingleSpriteShellAssembly]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            GameObject instance = UnityEngine.Object.Instantiate(shell, root.transform, false);
            Item item = instance.GetComponent<Item>();
            item.IsPreparedCandidate = true;
            item.BindData(EntityProbeData("test:single_sprite_shell:" + actorId, 1f));
            item.itemMods.ResetForReuse(item);
            var animation = assembly.ResolveEmbeddedModule(instance.transform) as Mod_AnimatorController;
            if (animation == null) throw new InvalidOperationException("正式动画装配计划没有复用外壳组件。");
            int animatorCount = instance.GetComponentsInChildren<Animator>(true).Length;
            int rendererCount = instance.GetComponentsInChildren<SpriteRenderer>(true).Length;
            Transform visual = instance.transform.Find(definition.Visual.RendererPath);
            Transform animatorRoot = instance.transform.Find(definition.Visual.AnimatorPath);
            SpriteRenderer renderer = visual != null ? visual.GetComponent<SpriteRenderer>() : null;
            Animator bodyAnimator = animatorRoot != null ? animatorRoot.GetComponent<Animator>() : null;
            ModuleData data = FastCloner.FastCloner.DeepClone<object>(animation._Data) as ModuleData;
            if (data == null || data.GetType() != animation._Data.GetType())
                throw new InvalidOperationException("单图外壳采样缺少独立模块数据。");
            data.Enabled = moduleDefinition.Enabled;
            animation._Data = data;
            bool stableIdentityRequired = false;
            string stableName = animation._Data.StableName;
            try
            {
                animation._Data.StableName = null;
                ItemModuleAssemblyCompiler.Compile(null, definition.Id, instance, moduleDefinitions,
                    usesResourceEntities: false);
            }
            catch (System.IO.InvalidDataException) { stableIdentityRequired = true; }
            finally { animation._Data.StableName = stableName; }
            // 保留真实外壳位置，直接走生产恢复入口覆盖首次绑定前能力 ID 为空的情况。
            data.ModuleId = "test:conflicting_animation";
            bool conflictingModuleIdRejected = !assembly.TryRestoreRuntimeIdentity(animation, instance.transform);
            data.ModuleId = null;
            data.StableName = "test:conflicting_slot";
            bool conflictingStableNameRejected = !assembly.TryRestoreRuntimeIdentity(animation, instance.transform);
            data.StableName = stableName;
            bool unboundRequiresBinding = !assembly.MatchesBoundModule(animation);
            bool restoredRuntimeIdentity = assembly.TryRestoreRuntimeIdentity(animation, instance.transform);
            bool boundAfterRestore = assembly.MatchesBoundModule(animation);
            animation.BindRuntimeIdentity(moduleDefinition.StableName, moduleDefinition.ModuleId, "test:conflicting_prefab");
            bool conflictingPrefabRejected = !assembly.MatchesBoundModule(animation) &&
                !assembly.TryRestoreRuntimeIdentity(animation, instance.transform);
            animation.BindRuntimeIdentity(moduleDefinition.StableName, moduleDefinition.ModuleId, moduleDefinition.PrefabId);
            bool reusedIdentityMatches = assembly.MatchesBoundModule(animation);
            assembly.ApplyConfiguration(animation);
            item.itemData.ModuleDataDic[moduleDefinition.StableName] = data;
            animation.ModuleInit(item, data);
            item.itemMods.AddMod(animation);
            bool registeredSameInstance = ReferenceEquals(animation,
                item.itemMods.RequireSingleModById<Mod_AnimatorController>(ModText.AnimatorReceiver));

            var stateSprites = new JArray();
            foreach (Mod_AnimatorController.SingleSpriteState entry in animation.singleSpriteStates)
                if (entry != null) stateSprites.Add(new JObject
                {
                    ["state"] = entry.stateName, ["sprite"] = entry.sprite != null ? entry.sprite.name : string.Empty
                });
            var birdStates = new JArray();
            foreach (BirdFlightPhase phase in Enum.GetValues(typeof(BirdFlightPhase)))
                foreach (bool moving in new[] { false, true })
                    birdStates.Add(new JObject
                    {
                        ["phase"] = phase.ToString(), ["moving"] = moving,
                        ["state"] = Mod_AI_Bird.ResolveAnimationStateName(phase, moving)
                    });

            return new JObject
            {
                ["actorId"] = actorId,
                ["stateSprites"] = stateSprites,
                ["birdStates"] = birdStates,
                ["templateStableName"] = template?.StableName,
                ["templateIsAnimator"] = template is Mod_AnimatorController,
                ["registeredSameInstance"] = registeredSameInstance,
                ["resolvedSameInstance"] = ReferenceEquals(animation, assembly.ResolveEmbeddedModule(instance.transform)),
                ["bodyAnimatorMatches"] = bodyAnimator != null && animation.animator == bodyAnimator,
                ["bodyRendererMatches"] = renderer != null && renderer.GetComponent<Mod_AnimatorController>() == animation,
                ["controllerName"] = bodyAnimator != null && bodyAnimator.runtimeAnimatorController != null
                    ? bodyAnimator.runtimeAnimatorController.name : string.Empty,
                ["animatorCount"] = animatorCount,
                ["rendererCount"] = rendererCount,
                ["finalAnimatorCount"] = instance.GetComponentsInChildren<Animator>(true).Length,
                ["finalRendererCount"] = instance.GetComponentsInChildren<SpriteRenderer>(true).Length,
                ["stableIdentityRequired"] = stableIdentityRequired,
                ["unboundRequiresBinding"] = unboundRequiresBinding,
                ["restoredRuntimeIdentity"] = restoredRuntimeIdentity,
                ["boundAfterRestore"] = boundAfterRestore,
                ["conflictingModuleIdRejected"] = conflictingModuleIdRejected,
                ["conflictingStableNameRejected"] = conflictingStableNameRejected,
                ["conflictingPrefabRejected"] = conflictingPrefabRejected,
                ["reusedIdentityMatches"] = reusedIdentityMatches,
                ["singleSprite"] = animation.IsSingleSpriteMode,
                ["enabled"] = animation.Enabled,
                ["inactive"] = !instance.activeInHierarchy
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    #endregion
}

internal sealed class GameplayTestAnimationMotionSource : Module, IActorAnimationMotionSource
{
    #region 独立主动表现信号
    private Ex_ModData_MemoryPackable data = new();
    public override ModuleData _Data { get => data; set => data = (Ex_ModData_MemoryPackable)value; }
    public bool Available;
    public ActorAnimationMotion Motion;
    public bool TryGetAnimationMotion(out ActorAnimationMotion motion) { motion = Motion; return Available; }
    protected override void OnLoad() { }
    protected override void OnSave() { }
    #endregion
}
