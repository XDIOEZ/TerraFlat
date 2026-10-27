using System;
using System.Globalization;
using UnityEngine;

/// <summary>Lua 测试 GM 白名单：只通过现有玩家模块准备条件与读取结果。</summary>
public static class GameplayTestGm
{
    #region 命令验证

    public static bool TryValidate(string command, string argument, out string error)
    {
        error = string.Empty;
        switch (command)
        {
            case "enable_admin":
            case "enable_invincibility":
            case "disable_invincibility":
            case "initialize_creative_inventory":
            case "mechanical_build":
            case "mechanical_start":
            case "mechanical_clutch_off":
            case "mechanical_clutch_on":
            case "mechanical_clear":
            case "mechanical_conveyor_drop":
            case "mechanical_diagnose":
            case "mechanical_stone_disconnect":
            case "mechanical_stone_push":
                if (string.IsNullOrEmpty(argument)) return true;
                error = command + " 不接受参数。";
                return false;
            case "apply_self_buff":
            case "remove_self_buff":
                if (!string.IsNullOrWhiteSpace(argument) && argument.Length <= 128) return true;
                error = command + " 需要有效的 Buff ID。";
                return false;
            case "set_health_ratio":
                if (TryRatio(argument, out _)) return true;
                error = "set_health_ratio 需要 0.1 至 1 的生命比例。";
                return false;
            case "set_move_speed":
                if (TryFloat(argument, out float speed) &&
                    speed >= Mod_PlayerAdminController.MinAdminMoveSpeedMultiplier &&
                    speed <= Mod_PlayerAdminController.MaxAdminMoveSpeedMultiplier) return true;
                error = "set_move_speed 需要管理员允许的移动倍率。";
                return false;
            default:
                error = "未开放的测试 GM 命令：" + command;
                return false;
        }
    }

    private static bool TryFloat(string input, out float value)
    {
        return float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool TryRatio(string input, out float value)
        => TryFloat(input, out value) && value >= 0.1f && value <= 1f;

    #endregion

    #region GM 执行

    public static bool TryExecute(Player player, string command, string argument, out string message)
    {
        if (!TryValidate(command, argument, out message)) return false;
        if (command == "mechanical_build")
            return GameplayTestMechanicalPlant.TryBuild(player, out message);
        if (command.StartsWith("mechanical_", StringComparison.Ordinal))
            return GameplayTestMechanicalPlant.TryOperate(command, out message);
        Mod_PlayerAdminController admin = player?.GetComponentInChildren<Mod_PlayerAdminController>(true);
        if (admin == null)
        {
            message = "当前测试玩家缺少管理员模块。";
            return false;
        }

        switch (command)
        {
            case "enable_admin":
                if (!admin.TryEnableAdministrator()) break;
                message = "已通过正式 GM 接口开启管理员身份。";
                return true;
            case "enable_invincibility":
                if (!admin.IsAdministrator && !admin.TryEnableAdministrator()) break;
                if (!admin.TrySetAdminInvincibilityEnabled(true)) break;
                message = "已开启管理员无敌。";
                return true;
            case "disable_invincibility":
                if (!admin.TrySetAdminInvincibilityEnabled(false)) break;
                message = "已关闭管理员无敌。";
                return true;
            case "initialize_creative_inventory":
                if (!admin.IsAdministrator && !admin.TryEnableAdministrator()) break;
                Mod_PlayerTraits traits = player.itemMods?.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
                if (traits == null)
                {
                    message = "缺少创造背包所需的玩家特质模块。";
                    return false;
                }
                string summary = traits.InitializeCreativeInventoryForAdmin();
                message = summary;
                return CreativeInventoryState.IsEnabled(player) &&
                       summary.StartsWith("创造背包完成", StringComparison.Ordinal);
            case "apply_self_buff":
            case "remove_self_buff":
                Mod_BuffManager buffs = player.itemMods?.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
                if (buffs == null)
                {
                    message = "缺少玩家 Buff 模块。";
                    return false;
                }
                if (command == "remove_self_buff")
                {
                    buffs.RemoveBuff(argument);
                    message = "已移除 Buff：" + argument;
                    return !buffs.HasBuff(argument);
                }
                BuffDefinition definition = GameRes.ExistingInstance?.GetBuffDefinition(argument);
                if (definition == null)
                {
                    message = "未注册 Buff：" + argument;
                    return false;
                }
                bool applied = buffs.AddBuff(definition.Id);
                message = "GM 施加 Buff：" + definition.Id;
                return applied || buffs.HasBuff(definition.Id);
            case "set_health_ratio":
                if (!admin.IsAdministrator) break;
                Mod_DamageReceiver health = player.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
                if (health == null || health.MaxHp <= 0f)
                {
                    message = "缺少有效生命模块。";
                    return false;
                }
                TryRatio(argument, out float ratio);
                health.Hp = health.MaxHp * ratio;
                message = $"GM 设置玩家生命比例为 {ratio:0.##}。";
                return Mathf.Abs(health.Hp - health.MaxHp * ratio) < 0.1f;
            case "set_move_speed":
                if (!admin.IsAdministrator) break;
                TryFloat(argument, out float speed);
                if (!admin.TrySetAdminMoveSpeedMultiplier(speed, out float appliedSpeed)) break;
                message = $"GM 设置移动倍率为 {appliedSpeed:0.##}。";
                return Mathf.Approximately(appliedSpeed, speed);
        }

        message = "GM 命令被玩家正式模块拒绝：" + command;
        return false;
    }

    #endregion

    #region 真实状态断言

    public static bool TryCheck(Player player, string name, out bool passed)
    {
        if (name.StartsWith("mechanical_", StringComparison.Ordinal))
            return GameplayTestMechanicalPlant.TryCheck(name, out passed);
        passed = false;
        Mod_PlayerAdminController admin = player?.GetComponentInChildren<Mod_PlayerAdminController>(true);
        switch (name)
        {
            case "gm_admin_enabled":
                passed = admin != null && admin.IsAdministrator;
                return true;
            case "gm_invincibility_enabled":
                passed = admin != null && admin.IsAdminInvincibilityEnabled;
                return true;
            case "gm_invincibility_disabled":
                passed = admin != null && admin.IsAdministrator && !admin.IsAdminInvincibilityEnabled;
                return true;
            case "gm_damage_blocked":
                passed = admin != null && Mathf.Approximately(admin.GetDamageMultiplier((IDamageSender)null), 0f);
                return true;
            case "gm_damage_normal":
                passed = admin != null && Mathf.Approximately(admin.GetDamageMultiplier((IDamageSender)null), 1f);
                return true;
            case "gm_health_full":
            case "gm_health_partial":
                Mod_DamageReceiver health = player?.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
                if (health != null && health.MaxHp > 0f)
                    passed = name == "gm_health_full"
                        ? Mathf.Abs(health.Hp - health.MaxHp) < 0.1f
                        : health.Hp > 0f && health.Hp < health.MaxHp * 0.75f;
                return true;
            case "gm_creative_inventory_enabled":
                passed = CreativeInventoryState.IsEnabled(player);
                return true;
        }

        const string speedPrefix = "gm_move_speed:";
        if (name.StartsWith(speedPrefix, StringComparison.Ordinal))
        {
            if (!TryFloat(name.Substring(speedPrefix.Length), out float expected))
                throw new ArgumentException("移动倍率检查参数不是有限数字：" + name);
            passed = admin != null && Mathf.Abs(admin.AdminMoveSpeedMultiplier - expected) < 0.001f;
            return true;
        }

        const string buffPrefix = "gm_buff_active:";
        if (name.StartsWith(buffPrefix, StringComparison.Ordinal))
        {
            Mod_BuffManager buffs = player?.itemMods?.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
            passed = buffs != null && buffs.HasBuff(name.Substring(buffPrefix.Length));
            return true;
        }

        const string buffAbsentPrefix = "gm_buff_absent:";
        if (name.StartsWith(buffAbsentPrefix, StringComparison.Ordinal))
        {
            Mod_BuffManager buffs = player?.itemMods?.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
            passed = buffs != null && !buffs.HasBuff(name.Substring(buffAbsentPrefix.Length));
            return true;
        }

        const string slotPrefix = "hotbar_slot:";
        if (name.StartsWith(slotPrefix, StringComparison.Ordinal))
        {
            if (!int.TryParse(name.Substring(slotPrefix.Length), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int slot))
                throw new ArgumentException("快捷栏检查参数不是整数：" + name);
            Mod_HotBar hotbar = player?.GetComponentInChildren<Mod_HotBar>(true);
            passed = hotbar != null && hotbar.CurrentIndex == slot;
            return true;
        }

        return false;
    }

    #endregion
}
