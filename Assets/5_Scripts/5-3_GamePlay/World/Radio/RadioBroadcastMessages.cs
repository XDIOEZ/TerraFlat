using System;
using FlatWorld.Localization;

/// <summary>广播 ID 随消息传输，由接收端按当前语言选择文本。</summary>
public static class RadioBroadcastMessages
{
    #region 内置广播
    public static bool TryGet(string messageId, out string text)
    {
        bool english = FlatWorldLocalizationService.CurrentLocaleCode.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        text = messageId switch
        {
            "distress" => english ? "Mayday! We need immediate assistance." : "紧急求救！我们需要立即支援。",
            "warning" => english ? "Warning: danger has been reported nearby. Stay alert." : "危险预警：附近出现危险，请保持警惕。",
            "announcement" => english ? "Attention: this is a public announcement." : "请注意：这里发布一条公共通告。",
            _ => null
        };
        return text != null;
    }
    #endregion
}
