using System;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 斜杠命令的只读提交上下文。
    /// 命令实现必须显式注册为玩家节点上的组件，不允许通过反射执行任意方法。
    /// </summary>
    public readonly struct PlayerChatCommandContext
    {
        public Player Sender { get; }
        public CharacterSoliloquyController SpeechController { get; }
        public string RawText { get; }
        public string CommandName { get; }
        public string[] Arguments { get; }

        public PlayerChatCommandContext(
            Player sender,
            CharacterSoliloquyController speechController,
            string rawText,
            string commandName,
            string[] arguments)
        {
            Sender = sender;
            SpeechController = speechController;
            RawText = rawText;
            CommandName = commandName;
            Arguments = arguments ?? Array.Empty<string>();
        }
    }

    /// <summary>命令处理结果；未识别的命令仍按普通聊天文字显示。</summary>
    public readonly struct PlayerChatCommandResult
    {
        public bool Handled { get; }
        public string Feedback { get; }
        public CharacterSpeechPriority FeedbackPriority { get; }

        private PlayerChatCommandResult(
            bool handled,
            string feedback,
            CharacterSpeechPriority feedbackPriority)
        {
            Handled = handled;
            Feedback = feedback;
            FeedbackPriority = feedbackPriority;
        }

        public static PlayerChatCommandResult NotHandled()
        {
            return new PlayerChatCommandResult(
                false,
                string.Empty,
                CharacterSpeechPriority.Player);
        }

        public static PlayerChatCommandResult HandledWith(
            string feedback = null,
            CharacterSpeechPriority feedbackPriority = CharacterSpeechPriority.Player)
        {
            return new PlayerChatCommandResult(true, feedback, feedbackPriority);
        }
    }

    /// <summary>
    /// 玩家聊天斜杠命令扩展接口。
    /// 联机命令实现必须自行执行权限校验，并将权威操作交给服务端。
    /// </summary>
    public interface IPlayerChatCommandHandler
    {
        int CommandOrder { get; }
        PlayerChatCommandResult Execute(PlayerChatCommandContext context);
    }
}
