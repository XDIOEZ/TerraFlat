using System;
using System.Globalization;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>在本地玩家聊天框中显式调整随身负重上限。</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCarryWeightChatCommand : MonoBehaviour, IPlayerChatCommandHandler
    {
        #region 运行时装配

        public int CommandOrder => 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            GameManager.Event_PlayerEnterWorld -= Attach;
            GameManager.Event_PlayerEnterWorld += Attach;
            GameManager.Event_LocalPlayerRuntimeReloaded -= Attach;
            GameManager.Event_LocalPlayerRuntimeReloaded += Attach;
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

        #region 负重命令

        public PlayerChatCommandResult Execute(PlayerChatCommandContext context)
        {
            if (!string.Equals(context.CommandName, "carryweight", StringComparison.OrdinalIgnoreCase))
                return PlayerChatCommandResult.NotHandled();

            if (!TryParseWeight(context.Arguments, out float maxWeight))
                return PlayerChatCommandResult.HandledWith("用法：/carryweight 5000，重量必须是大于 0 的有限数字，单位 kg。");

            if (context.Sender == null || context.Sender != GetComponent<Player>())
                return PlayerChatCommandResult.HandledWith("当前本地玩家尚未就绪。");

            if (!PlayerCarryCapacityUtility.TrySetMaxWeight(context.Sender, maxWeight, out string failureReason))
                return PlayerChatCommandResult.HandledWith(failureReason);

            return PlayerChatCommandResult.HandledWith(
                $"负重上限已设为 {maxWeight.ToString("0.##", CultureInfo.InvariantCulture)} kg。");
        }

        /// <summary>使用固定小数点解析公斤数，拒绝缺参、多参和非正非有限值。</summary>
        internal static bool TryParseWeight(string[] arguments, out float maxWeight)
        {
            maxWeight = 0f;
            return arguments != null && arguments.Length == 1 &&
                   float.TryParse(arguments[0], NumberStyles.Float, CultureInfo.InvariantCulture, out maxWeight) &&
                   maxWeight > 0f && !float.IsNaN(maxWeight) && !float.IsInfinity(maxWeight);
        }

        #endregion
    }
}
