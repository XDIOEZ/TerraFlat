using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>收集各系统说明并向当前玩家发放一本可实际阅读的命令手册。</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHelpChatCommand : MonoBehaviour, IPlayerChatCommandHandler
    {
        #region 运行时装配与说明

        public const string CommandName = "help";
        public const string ManualDefinitionId = "CommandManual";
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
            => PlayerHelpCatalog.RegisterProvider("dialogue.help", DescribeCommands);

        private static IEnumerable<PlayerHelpEntry> DescribeCommands()
        {
            yield return new PlayerHelpEntry("聊天命令", "领取命令手册", $"/{CommandName}",
                "每次输入向主背包发放 1 本《命令手册》，记录生成时已登记的聊天命令、F4 面板操作和管理员/调试快捷键。" +
                "仅支持单机或主机本地玩家；主背包需要空格，重量与体积必须容得下这本书。" +
                "把书放入快捷栏，选中并使用即可阅读；再次输入可领取一份最新目录。");
        }

        private static void Attach(Player player)
        {
            if (player == null || !player.IsLocalProfile)
                return;
            PlayerChatInputController chat = player.GetComponent<PlayerChatInputController>();
            if (chat == null)
                return;
            if (player.GetComponent<PlayerHelpChatCommand>() == null)
                player.gameObject.AddComponent<PlayerHelpChatCommand>();
            chat.RebuildCommandHandlers();
        }

        #endregion

        #region 聊天命令与书页生成

        public PlayerChatCommandResult Execute(PlayerChatCommandContext context)
        {
            if (!string.Equals(context.CommandName, CommandName, StringComparison.OrdinalIgnoreCase))
                return PlayerChatCommandResult.NotHandled();
            if (context.Arguments.Length != 0)
                return PlayerChatCommandResult.HandledWith($"用法：/{CommandName}，直接领取一本命令手册。");
            if (context.Sender == null || context.Sender != GetComponent<Player>() || !context.Sender.IsLocalProfile)
                return PlayerChatCommandResult.HandledWith("当前本地玩家尚未就绪。");

            IReadOnlyList<ReadableBookPageContent> pages;
            try { pages = BuildPages(PlayerHelpCatalog.Collect()); }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return PlayerChatCommandResult.HandledWith("命令手册内容生成失败，请查看日志。");
            }
            if (!ReadableBookGrantService.TryGive(context.Sender, ManualDefinitionId, pages, out string failureReason))
                return PlayerChatCommandResult.HandledWith(failureReason);
            return PlayerChatCommandResult.HandledWith("命令手册已放入主背包。把书放进快捷栏，选中并使用即可阅读。");
        }

        /// <summary>简介占一页，每个操作独占一页，正文完整保留用法和使用条件。</summary>
        internal static IReadOnlyList<ReadableBookPageContent> BuildPages(IReadOnlyList<PlayerHelpEntry> entries)
        {
            List<ReadableBookPageContent> pages = new()
            {
                CreatePage(0, "命令手册", "这本手册收录生成时已登记的聊天斜杠命令、F4 面板操作和管理员/调试快捷键。\n\n" +
                    $"共 {entries.Count} 条操作说明，每条各占一页。\n\n" +
                    "聊天命令：按 Enter 打开聊天框，输入命令，再按 Enter 提交。\n" +
                    "F4 操作：打开 GM 面板后按对应页签和控件操作。\n" +
                    "快捷键：按各页给出的键位和条件操作。\n\n" +
                    $"每本书保存领取时的目录；需要更新时再次输入 /{CommandName} 领取新书。")
            };
            foreach (PlayerHelpEntry entry in entries)
                pages.Add(CreatePage(pages.Count, entry.Title,
                    $"类别：{entry.Category}\n\n用法：{entry.Usage}\n\n说明：{entry.Description}"));
            return pages;
        }

        private static ReadableBookPageContent CreatePage(int index, string title, string body)
            => new() { PageIndex = index, Title = title, Body = body, HasCustomTitle = true, HasCustomBody = true };

        #endregion
    }
}
