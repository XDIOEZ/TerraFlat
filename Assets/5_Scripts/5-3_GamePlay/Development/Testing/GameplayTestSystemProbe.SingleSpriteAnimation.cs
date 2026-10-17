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
        try
        {
            root.transform.position = new Vector3(11f, -7f, 0f);
            root.transform.localScale = new Vector3(Flag(args, "mirrored", false) ? -1f : 1f, 1f, 1f);
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
            if (Flag(args, "withMotionSource", false))
            {
                motionSource = root.AddComponent<GameplayTestAnimationMotionSource>();
                motionSource.ModuleInit(item, new Ex_ModData_MemoryPackable { ID = "test:animation_motion", Enabled = true });
                item.itemMods.AddMod(motionSource);
            }

            var visual = new GameObject("Sprite");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(.13f, .17f, 0f);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 7f);
            visual.transform.localScale = new Vector3(1.1f, .9f, 1f);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
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
            animation.ModuleInit(item, new Ex_ModData_MemoryPackable { Enabled = true });
            item.itemMods.AddMod(animation);
            Animator sourceAnimator = Flag(args, "withAnimator", false) ? root.AddComponent<Animator>() : null;
            animation.animator = sourceAnimator;
            root.SetActive(true);
            if (motionSource != null) motionSource.Load();
            animation.Load();

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
                bool legacyStateAccepted = step["legacyState"] != null &&
                    animation.ForcePlayAnimation(Text(step, "legacyState", string.Empty));
                bool legacyMoveAccepted = step["legacyMove"] != null &&
                    animation.SetBool(AnimationText.Move, Flag(step, "legacyMove", false));
                if (Flag(step, "reset", false)) animation.ResetPresentation();
                switch (Text(step, "lifecycle", string.Empty))
                {
                    case "unload": animation.Unload(); break;
                    case "load": animation.Load(); break;
                    case "disable": animation.SetEnabled(false); break;
                    case "enable": animation.SetEnabled(true); animation.Load(); break;
                    case "": break;
                    default: throw new ArgumentException("单图动画采样生命周期操作未登记。");
                }
                if (animation.IsRuntimeLoaded && animation.Enabled && animation.enabled)
                    animation.AdvancePresentation((float)Number(step, "deltaTime", .08d));

                Vector3 offset = visual.transform.position - visualPosition;
                bool hasBaseMatrix = animation.TryGetBaseVisualMatrix(visual.transform, out Matrix4x4 baseMatrix);
                Matrix4x4 expectedBaseMatrix = root.transform.localToWorldMatrix *
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
                    ["parentRestored"] = visual.transform.parent == root.transform && visual.transform.GetSiblingIndex() == siblingIndex,
                    ["attacking"] = animation.IsAttackPlaying,
                    ["componentEnabled"] = animation.enabled,
                    ["bodyAnimatorEnabled"] = sourceAnimator != null && sourceAnimator.enabled,
                    ["legacyStateAccepted"] = legacyStateAccepted,
                    ["legacyMoveAccepted"] = legacyMoveAccepted,
                    ["hasBaseMatrix"] = hasBaseMatrix,
                    ["basePositionError"] = hasBaseMatrix ? Vector3.Distance(visualPosition, baseMatrix.MultiplyPoint3x4(Vector3.zero)) : -1f,
                    ["baseMatrixError"] = hasBaseMatrix ? baseMatrixError : -1f,
                    ["loaded"] = animation.IsRuntimeLoaded
                });
            }
            return new JObject { ["samples"] = samples };
        }
        finally
        {
            try
            {
                try { if (animation != null) animation.Unload(); }
                finally { if (motionSource != null) motionSource.Unload(); }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
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
            animation.BindRuntimeIdentity(moduleDefinition.StableName, moduleDefinition.ModuleId, moduleDefinition.PrefabId);
            assembly.ApplyConfiguration(animation);
            item.itemData.ModuleDataDic[moduleDefinition.StableName] = data;
            animation.ModuleInit(item, data);
            item.itemMods.AddMod(animation);
            bool registeredSameInstance = ReferenceEquals(animation,
                item.itemMods.RequireSingleModById<Mod_AnimatorController>(ModText.AnimatorReceiver));

            return new JObject
            {
                ["actorId"] = actorId,
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
