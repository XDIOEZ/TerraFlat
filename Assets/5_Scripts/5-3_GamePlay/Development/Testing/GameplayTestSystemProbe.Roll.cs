using System;
using InputSystem;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 玩家翻滚正式入口采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterRollRules()
    {
        Register("player", "roll_direction", PlayerRollDirection);
        Register("player", "roll_input", PlayerRollInput);
        Register("player", "roll_action", PlayerRollAction);
#if UNITY_EDITOR
        Register("player", "collider_visual_alignment", PlayerColliderVisualAlignment);
#endif
    }

    private static JObject PlayerRollDirection(JObject args)
    {
        Vector2 result = Mod_Roll.ResolveDirection(
            new Vector2((float)Number(args, "x", 0d), (float)Number(args, "y", 0d)),
            new Vector2((float)Number(args, "cursorX", 0d), (float)Number(args, "cursorY", 0d)),
            new Vector2((float)Number(args, "facingX", 1d), (float)Number(args, "facingY", 0d)));
        return new JObject { ["x"] = result.x, ["y"] = result.y };
    }

    private static JObject PlayerRollInput(JObject args)
    {
        var actions = new PlayerInputActions();
        try
        {
            using var bindings = new InputBindingService(actions.asset, new InputSettingsMemoryStore());
            int editableCount = 0;
            foreach (InputBindingEntry entry in bindings.Entries)
                if (entry.Action == actions.Win10.Roll && entry.DisplayName == "翻滚") editableCount++;
            return new JObject
            {
                ["path"] = actions.Win10.Roll.bindings[0].path,
                ["actionType"] = actions.Win10.Roll.type.ToString(), ["editableCount"] = editableCount,
                ["initialStateCheck"] = actions.Win10.Roll.wantsInitialStateCheck
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(actions.asset); }
    }

    private static JObject PlayerRollAction(JObject args)
    {
        if (args["steps"] is not JArray steps || steps.Count == 0 || steps.Count > 64)
            throw new ArgumentException("翻滚采样需要 1 至 64 个步骤。");
        var root = new GameObject("[LuaPlayerRoll]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        Mod_Roll roll = null;
        try
        {
            Player player = root.AddComponent<Player>();
            player.BindData(new Data_Player { IDName = "test:roll", Stack = new ItemStack { Amount = 1f, Weight = 1f, Volume = 1f } });
            bool serverRemote = Flag(args, "serverRemote", false);
            player.SetProfileContext(!serverRemote, false, "test-roll");
            Rigidbody2D body = root.AddComponent<Rigidbody2D>();
            body.simulated = false;
            BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(.4f, .3f);
            collider.offset = new Vector2(0f, -.07f);
            Mod_Mover mover = EnableGameplayTestProbeLifecycleCallbacks(root.AddComponent<Mod_Mover>());
            mover.ModuleInit(player, new Ex_ModData_MemoryPackable { ID = ModText.Mod_Mover, Enabled = true });
            mover.rb = body;
            mover.IsLock = serverRemote;
            player.itemMods.AddMod(mover);
            Mod_Stamina stamina = root.AddComponent<Mod_Stamina>();
            stamina.ModuleInit(player, new Ex_ModData_MemoryPackable { ID = ModText.Stamina, Enabled = true });
            stamina.Data = new Mod_Stamina.StaminaData { CurrentStamina = (float)Number(args, "stamina", 100d), MaxStamina = 100f };
            player.itemMods.AddMod(stamina);
            Mod_DamageReceiver health = root.AddComponent<Mod_DamageReceiver>();
            health.ModuleInit(player, new Ex_ModData { ID = ModText.Hp, Enabled = true });
            player.itemMods.AddMod(health);
            var visual = new GameObject("RollVisual");
            visual.transform.SetParent(root.transform, false);
            var sprite = new GameObject("Sprite");
            sprite.transform.SetParent(visual.transform, false);
            sprite.transform.localRotation = Quaternion.Euler(0f, 180f, 7f);
            roll = EnableGameplayTestProbeLifecycleCallbacks(root.AddComponent<Mod_Roll>());
            roll.ModuleInit(player, new Ex_ModData { ID = Mod_Roll.ModuleId, Enabled = true });
            roll.visualRoot = visual.transform;
            roll.bodyColliders = Flag(args, "missingBody", false) ? Array.Empty<Collider2D>() :
                Flag(args, "duplicateBody", false) ? new Collider2D[] { collider, collider } : new Collider2D[] { collider };
            collider.isTrigger = Flag(args, "triggerOnly", false);
            player.itemMods.AddMod(roll);
            float requestedCost = 0f;
            string costSource = string.Empty;
            int transactions = 0;
            stamina.StaminaConsumed += record => { requestedCost = record.RequestedBaseAmount; costSource = record.SourceId; transactions++; };
            bool reentrantAccepted = false;
            if (Flag(args, "reentrant", false))
                stamina.StaminaConsumed += _ => reentrantAccepted |= roll.TryStartRoll(Vector2.left);
            root.SetActive(true);
            if (Flag(args, "running", false)) mover.SetRunState(true);
            if (serverRemote) roll.EnableNetworkAuthority(true);
            else roll.Load();
            var samples = new JArray();
            foreach (JObject step in steps)
            {
                bool accepted = false;
                switch (Text(step, "action", "advance"))
                {
                    case "start": accepted = roll.TryStartRoll(new Vector2((float)Number(step, "x", 1d), (float)Number(step, "y", 0d))); break;
                    case "advance": roll.AdvanceRoll((float)Number(step, "seconds", 0d)); break;
                    case "cancel": roll.CancelRoll(); break;
                    case "disable": roll.enabled = false; break;
                    case "unload": roll.Unload(); break;
                    case "mover_unload": mover.Unload(); break;
                    case "mover_reset": mover.ResetMotion(); break;
                    case "mover_disable": mover.enabled = false; break;
                    case "locked": mover.IsLock = true; roll.AdvanceRoll(0f); break;
                    case "dead": health.Hp = 0f; roll.AdvanceRoll(0f); break;
                    case "presentation": roll.ApplyNetworkPresentation(Flag(step, "active", true), Vector2.right,
                        (float)Number(step, "elapsed", .2d), (uint)Number(step, "sequence", 1d)); break;
                    case "owned": roll.ApplyNetworkOwnedRoll(Vector2.right, (float)Number(step, "elapsed", .2d),
                        (uint)Number(step, "sequence", 1d)); break;
                    default: throw new ArgumentException("翻滚采样操作未登记。");
                }
                samples.Add(new JObject
                {
                    ["accepted"] = accepted, ["rolling"] = roll.IsRolling,
                    ["elapsed"] = roll.Elapsed, ["cooldown"] = roll.CooldownRemaining,
                    ["stamina"] = stamina.CurrentValue, ["requestedCost"] = requestedCost,
                    ["transactions"] = transactions, ["costSource"] = costSource,
                    ["directionX"] = roll.Direction.x, ["directionY"] = roll.Direction.y,
                    ["width"] = collider.size.x, ["height"] = collider.size.y,
                    ["offsetX"] = collider.offset.x, ["offsetY"] = collider.offset.y,
                    ["invulnerable"] = health.IsTemporarilyInvulnerable,
                    ["drivenSpeed"] = mover.DrivenVelocity.magnitude, ["motionLease"] = mover.HasDrivenMotion,
                    ["isWalking"] = mover.IsMoving, ["rootAngle"] = root.transform.eulerAngles.z,
                    ["visualAngle"] = visual.transform.localEulerAngles.z,
                    ["spriteFacing"] = (sprite.transform.localRotation * Vector3.right).x,
                    ["bodySimulated"] = body.simulated, ["bodyType"] = body.bodyType.ToString(),
                    ["moverLocked"] = mover.IsLock, ["sequence"] = roll.Sequence,
                    ["reentrantAccepted"] = reentrantAccepted
                });
            }
            return new JObject { ["samples"] = samples };
        }
        finally
        {
            if (roll != null) { roll.CancelRoll(); roll.EnableNetworkAuthority(false); roll.Unload(); }
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
#if UNITY_EDITOR
    // 校验玩家预制体的受击范围跟随人物底锚点画布，脚下实体阻挡框仍保持独立。
    private static JObject PlayerColliderVisualAlignment(JObject args)
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/Gameplay/Player/Player.prefab");
        Transform visual = prefab != null ? prefab.transform.Find("RollVisual/Module_Animator") : null;
        Transform receiver = prefab != null ? prefab.transform.Find("Module_DamageReciver") : null;
        Transform heldAnchor = prefab != null ? prefab.transform.Find("Module_Hotbar") : null;
        PlayerOxygenWorldBar oxygenBar = prefab != null ? prefab.GetComponentInChildren<PlayerOxygenWorldBar>(true) : null;
        RectTransform oxygenBarRect = oxygenBar != null ? oxygenBar.GetComponent<RectTransform>() : null;
        Mod_CharacterAppearance appearance = prefab != null
            ? prefab.GetComponentInChildren<Mod_CharacterAppearance>(true) : null;
        Mod_TurnBack turning = prefab != null ? prefab.GetComponentInChildren<Mod_TurnBack>(true) : null;
        FlatWorld.Dialogue.ScreenSpaceSpeechBubblePresenter speech = prefab != null
            ? prefab.GetComponent<FlatWorld.Dialogue.ScreenSpaceSpeechBubblePresenter>() : null;
        BoxCollider2D body = prefab != null ? prefab.GetComponent<BoxCollider2D>() : null;
        BoxCollider2D hitbox = receiver != null ? receiver.GetComponent<BoxCollider2D>() : null;
        if (visual == null || receiver == null || heldAnchor == null || turning == null ||
            speech == null ||
            oxygenBarRect == null || oxygenBarRect.parent != prefab.transform ||
            body == null || hitbox == null ||
            appearance?.profile == null || appearance.profile.PixelsPerUnit <= 0f)
            throw new InvalidOperationException("玩家预制体缺少外观、实体碰撞或独立受击框。");

        float spriteBottom = visual.position.y - prefab.transform.position.y;
        float spriteHeight = appearance.profile.Height / appearance.profile.PixelsPerUnit * visual.lossyScale.y;
        float spriteWidth = appearance.profile.Width / appearance.profile.PixelsPerUnit * visual.lossyScale.x;
        float rollPivotY = visual.parent.position.y - prefab.transform.position.y;
        float bubbleY = new UnityEditor.SerializedObject(speech).FindProperty("worldOffset").vector3Value.y;
        float hitCenterY = receiver.TransformPoint(hitbox.offset).y - prefab.transform.position.y;
        float hitHalfHeight = hitbox.size.y * receiver.lossyScale.y * 0.5f;
        return new JObject
        {
            ["spriteBottom"] = spriteBottom,
            ["spriteTop"] = spriteBottom + spriteHeight,
            ["spriteWidth"] = spriteWidth,
            ["rollPivotY"] = rollPivotY,
            ["heldAnchorY"] = heldAnchor.position.y - prefab.transform.position.y,
            ["turnMirrorY"] = turning.DefaultPosition.y,
            ["bubbleY"] = bubbleY,
            ["oxygenBarY"] = oxygenBarRect.anchoredPosition.y,
            ["oxygenBarOffsetFromSpriteTop"] = oxygenBarRect.anchoredPosition.y - (spriteBottom + spriteHeight),
            ["hitBottom"] = hitCenterY - hitHalfHeight,
            ["hitTop"] = hitCenterY + hitHalfHeight,
            ["hitWidth"] = hitbox.size.x * receiver.lossyScale.x,
            ["hitTrigger"] = hitbox.isTrigger,
            ["bodyTrigger"] = body.isTrigger,
            ["bodyBottom"] = body.offset.y - body.size.y * 0.5f,
            ["bodyTop"] = body.offset.y + body.size.y * 0.5f,
            ["bodyCenter"] = body.offset.y,
            ["bodyWidth"] = body.size.x
        };
    }
#endif
    #endregion
}
