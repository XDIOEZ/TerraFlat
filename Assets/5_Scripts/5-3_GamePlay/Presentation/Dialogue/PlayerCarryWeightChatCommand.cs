using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>在本地玩家聊天框中显式调整随身重量与体积上限。</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCarryWeightChatCommand : MonoBehaviour, IPlayerChatCommandHandler
    {
        #region 运行时装配

        public const string WeightCommandName = "carryweight";
        public const string VolumeCommandName = "carryvolume";
        public int CommandOrder => 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            RegisterHelpEntries();
            GameManager.Event_PlayerEnterWorld -= Attach;
            GameManager.Event_PlayerEnterWorld += Attach;
            GameManager.Event_LocalPlayerRuntimeReloaded -= Attach;
            GameManager.Event_LocalPlayerRuntimeReloaded += Attach;
        }

        internal static void RegisterHelpEntries()
            => PlayerHelpCatalog.RegisterProvider("dialogue.carryCapacity", DescribeCommands);

        private static IEnumerable<PlayerHelpEntry> DescribeCommands()
        {
            yield return new PlayerHelpEntry("聊天命令", "修改负重上限", $"/{WeightCommandName} 数值",
                $"单位 kg，参数必须是大于 0 的有限数字，可用小数。例如 /{WeightCommandName} 5000；恢复默认用 /{WeightCommandName} 100。" +
                "仅支持单机或主机本地玩家，联机客户端拒绝；创造背包已无限时拒绝。超过常规负重会减速，硬上限为设定值的 1.5 倍。" +
                "设置当前玩家的负重上限，体积与槽位保持原值。");
            yield return new PlayerHelpEntry("聊天命令", "修改体积上限", $"/{VolumeCommandName} 数值",
                $"单位 L（升），参数必须是大于 0 的有限数字，可用小数。例如 /{VolumeCommandName} 5000；恢复默认用 /{VolumeCommandName} 100。" +
                "仅支持单机或主机本地玩家，联机客户端拒绝；创造背包已无限时拒绝。体积按设定值作为硬上限，没有超额宽限。" +
                "设置当前玩家的体积上限，重量与槽位保持原值，体积不会触发超重减速。");
        }

        /// <summary>首次入世与角色重建后定向补齐组件，并更新聊天控制器的命令列表。</summary>
        private static void Attach(Player player)
        {
            if (player == null || !player.IsLocalProfile)
                return;

            PlayerChatInputController chat = player.GetComponent<PlayerChatInputController>();
            if (chat == null)
                return;

            if (player.GetComponent<PlayerCarryWeightChatCommand>() == null)
                player.gameObject.AddComponent<PlayerCarryWeightChatCommand>();
            chat.RebuildCommandHandlers();
        }

        #endregion

        #region 携带容量命令

        public PlayerChatCommandResult Execute(PlayerChatCommandContext context)
        {
            bool setWeight = string.Equals(context.CommandName, WeightCommandName, StringComparison.OrdinalIgnoreCase);
            bool setVolume = string.Equals(context.CommandName, VolumeCommandName, StringComparison.OrdinalIgnoreCase);
            if (!setWeight && !setVolume)
                return PlayerChatCommandResult.NotHandled();

            string label = setWeight ? "负重" : "体积";
            string unit = setWeight ? "kg" : "L";
            if (!TryParseCapacity(context.Arguments, out float capacity))
                return PlayerChatCommandResult.HandledWith(setWeight
                    ? $"用法：/{WeightCommandName} 5000，重量必须是大于 0 的有限数字，单位 kg。"
                    : $"用法：/{VolumeCommandName} 5000，体积必须是大于 0 的有限数字，单位 L（升）。");

            if (context.Sender == null || context.Sender != GetComponent<Player>())
                return PlayerChatCommandResult.HandledWith("当前本地玩家尚未就绪。");

            string failureReason;
            bool changed = setWeight
                ? PlayerCarryCapacityUtility.TrySetMaxWeight(context.Sender, capacity, out failureReason)
                : PlayerCarryCapacityUtility.TrySetMaxVolume(context.Sender, capacity, out failureReason);
            if (!changed)
                return PlayerChatCommandResult.HandledWith(failureReason);

            return PlayerChatCommandResult.HandledWith(
                $"{label}上限已设为 {capacity.ToString("0.##", CultureInfo.InvariantCulture)} {unit}。");
        }

        /// <summary>使用固定小数点解析容量数值，拒绝缺参、多参和非正非有限值。</summary>
        internal static bool TryParseCapacity(string[] arguments, out float capacity)
        {
            capacity = 0f;
            return arguments != null && arguments.Length == 1 &&
                   float.TryParse(arguments[0], NumberStyles.Float, CultureInfo.InvariantCulture, out capacity) &&
                   capacity > 0f && !float.IsNaN(capacity) && !float.IsInfinity(capacity);
        }

        #endregion
    }
}
