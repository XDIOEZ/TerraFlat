using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 命中手感正式时钟与材质采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterHitFeedbackRules()
    {
        Register("combat", "hit_feedback_clock", EntityHitFeedbackClock);
        Register("combat", "hit_shake_render", EntityHitShakeRender);
        Register("combat", "logical_hit_levels", EntityLogicalHitLevels);
    }

    /// <summary>直接调用战斗生产公式，检测命中等级、体型和独立闪避的乘算边界。</summary>
    private static JObject EntityLogicalHitLevels(JObject args)
    {
        int accuracy = (int)Number(args, "accuracy", CreatureHitChanceRules.DefaultAccuracyLevel);
        int size = (int)Number(args, "size", CreatureHitChanceRules.DefaultSizeLevel);
        float weapon = (float)Number(args, "weapon", 1);
        float state = (float)Number(args, "state", 1);
        float dodge = (float)Number(args, "dodge", 0);
        float secondDodge = (float)Number(args, "secondDodge", 0);
        float baseHit = CreatureHitChanceRules.Calculate(accuracy, size, weapon, state);
        float afterFirst = CreatureHitChanceRules.ApplyDodge(baseHit, dodge);
        return new JObject
        {
            ["accuracyFactor"] = CreatureHitChanceRules.AccuracyFactor(accuracy),
            ["sizeFactor"] = CreatureHitChanceRules.SizeFactor(size),
            ["baseHit"] = baseHit,
            ["afterFirst"] = afterFirst,
            ["finalHit"] = CreatureHitChanceRules.ApplyDodge(afterFirst, secondDodge),
            ["maximumDodge"] = CreatureHitChanceRules.MaximumExtraDodgeChance
        };
    }

    private static JObject EntityHitFeedbackClock(JObject args)
    {
        var clock = new WeaponHitStopClock();
        var result = new JObject
        {
            ["defaultStop"] = CombatHitFeedback.DefaultHitStopDuration,
            ["defaultShake"] = CombatHitFeedback.DefaultShakeDuration,
            ["defaultDistance"] = CombatHitFeedback.DefaultShakeDistance,
            ["zeroRejected"] = !clock.TryPause(0f, .12f, 10d, 100d),
            ["negativeRejected"] = !clock.TryPause(-1f, .12f, 10d, 100d),
            ["invalidRejected"] = !clock.TryPause(float.NaN, .12f, 10d, 100d) &&
                !clock.TryPause(float.PositiveInfinity, .12f, 10d, 100d),
            ["disabledRejected"] = !clock.TryPause(1f, 0f, 10d, 100d),
            ["invalidDurationRejected"] = !clock.TryPause(1f, float.NaN, 10d, 100d),
            ["invalidClockRejected"] = !clock.TryPause(1f, .12f, double.NaN, 100d),
            ["started"] = clock.TryPause(1f, .12f, 10d, 100d),
            ["frozen"] = clock.AnimationTime(10.08d),
            ["multiTargetRejected"] = !clock.TryPause(2f, .2f, 10.08d, 100.08d),
            ["earlyResumeRejected"] = !clock.ResumeIfReady(10.11d, 100.11d),
            ["resumed"] = clock.ResumeIfReady(10.13d, 100.13d),
            ["resumeTime"] = clock.AnimationTime(10.13d),
            ["advancedTime"] = clock.AnimationTime(10.18d),
            ["sameAttackCannotRestart"] = !clock.TryPause(2f, .12f, 10.18d, 100.18d)
        };
        clock.Reset();
        result["nextAttackStarted"] = clock.TryPause(1f, .12f, 20d, 200d);
        clock.Reset();
        result["cancelCleared"] = !clock.IsPaused && clock.AnimationTime(20d) == 20d;
        clock.TryPause(1f, .12f, 30d, 300d);
        result["slowTimeResumed"] = clock.ResumeIfReady(30.06d, 300.13d);
        result["slowTimePreserved"] = clock.AnimationTime(30.11d);
        clock.Reset();
        clock.TryPause(1f, .12f, 40d, 400d);
        result["pausedGameResumed"] = clock.ResumeIfReady(40d, 400.13d) && clock.AnimationTime(40d) == 40d;

        float peak = 0f;
        for (int i = 0; i <= 160; i++)
            peak = Mathf.Max(peak, CombatHitFeedback.EvaluateShake(i * .001f, .16f, .035f, Vector2.right).magnitude);
        result["peak"] = peak;
        result["firstKick"] = CombatHitFeedback.EvaluateShake(0f, .16f, .035f, Vector2.left).x;
        result["fallbackKick"] = CombatHitFeedback.EvaluateShake(0f, .16f, .035f, Vector2.zero).x;
        result["settled"] = CombatHitFeedback.EvaluateShake(.16f, .16f, .035f, Vector2.right) == Vector2.zero;
        result["disabledShake"] = CombatHitFeedback.EvaluateShake(0f, 0f, .035f, Vector2.right) == Vector2.zero &&
            CombatHitFeedback.EvaluateShake(0f, .16f, 0f, Vector2.right) == Vector2.zero;
        result["invalidShake"] = CombatHitFeedback.EvaluateShake(float.NaN, .16f, .035f, Vector2.right) == Vector2.zero;
        result["stopClamped"] = CombatHitFeedback.HitStopDuration(99f);
        return result;
    }

    // 独立显示夹具只调用正式合成与清理入口，Lua 判断视觉、碰撞隔离和回池恢复。
    private static JObject EntityHitShakeRender(JObject args)
    {
        GameObject root = null;
        Texture2D texture = null;
        Sprite sprite = null;
        Material material = null;
        try
        {
            Shader shader = Shader.Find("Game/2D/Sprite-Lit-Master");
            if (shader == null) throw new InvalidOperationException("缺少正式角色 Shader，无法验证受击抖动。");
            material = new Material(shader);
            if (!material.HasProperty(ActorRenderHitShakeEffect.OffsetId))
                throw new InvalidOperationException("角色 Shader 尚未导入受击位移通道。");
            texture = new Texture2D(16, 16);
            sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(.5f, .5f), 16f);
            root = new GameObject("GameplayTest Hit Shake") { hideFlags = HideFlags.HideAndDontSave };
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            var collider = visual.AddComponent<BoxCollider2D>();
            var controller = root.AddComponent<ActorRenderEffectController>();
            var effect = root.AddComponent<ActorRenderHitShakeEffect>();
            controller.RefreshBindings();
            var block = new MaterialPropertyBlock();
            int tintId = Shader.PropertyToID("_HitFlash");
            block.SetFloat(tintId, .37f);
            renderer.SetPropertyBlock(block);
            Vector3 originalPosition = visual.transform.position;
            Vector3 originalRoot = root.transform.position;
            Bounds originalCollider = collider.bounds;
            Bounds originalBounds = renderer.localBounds;
            effect.PlayShake(Vector2.right, .16f, .035f);
            controller.RenderFrame(0f);
            renderer.GetPropertyBlock(block);
            var result = new JObject
            {
                ["shaderOffset"] = block.GetVector(ActorRenderHitShakeEffect.OffsetId).x,
                ["boundsExpanded"] = renderer.localBounds.size.x > originalBounds.size.x,
                ["rootUnchanged"] = root.transform.position == originalRoot,
                ["visualTransformUnchanged"] = visual.transform.position == originalPosition,
                ["colliderUnchanged"] = collider.bounds == originalCollider,
                ["otherChannelPreserved"] = block.GetFloat(tintId)
            };
            effect.PlayShake(Vector2.right, .16f, .035f);
            controller.RenderFrame(0f);
            result["repeatedAmplitude"] = effect.CurrentOffset.magnitude;
            controller.RenderFrame(.2f);
            renderer.GetPropertyBlock(block);
            result["finishedOffset"] = block.GetVector(ActorRenderHitShakeEffect.OffsetId).magnitude;
            result["boundsRestored"] = renderer.localBounds == originalBounds;
            effect.PlayShake(Vector2.left, .16f, .035f);
            controller.RenderFrame(0f);
            effect.enabled = false;
            renderer.GetPropertyBlock(block);
            result["disabledOffset"] = block.GetVector(ActorRenderHitShakeEffect.OffsetId).magnitude;
            result["disabledOtherChannel"] = block.GetFloat(tintId);
            effect.PlayShake(Vector2.right, .16f, .035f);
            controller.RenderFrame(0f);
            root.SetActive(false);
            renderer.GetPropertyBlock(block);
            result["pooledOffset"] = block.GetVector(ActorRenderHitShakeEffect.OffsetId).magnitude;
            result["pooledBoundsRestored"] = renderer.localBounds == originalBounds;
            return result;
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }
    #endregion
}
