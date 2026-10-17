using System;
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
                    ["loaded"] = animation.IsRuntimeLoaded
                });
            }
            return new JObject { ["samples"] = samples };
        }
        finally
        {
            try { if (animation != null) animation.Unload(); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    #endregion
}
