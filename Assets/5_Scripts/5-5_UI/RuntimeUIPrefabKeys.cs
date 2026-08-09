/// <summary>
/// 运行时只实例化已制作好的 UI Prefab；名称同时作为 GameRes 查询键。
/// </summary>
public static class RuntimeUIPrefabKeys
{
    #region 设置面板

    public const string AudioSettings = "UI_AudioSettings";
    public const string UISettings = "UI_InterfaceSettings";
    public const string CoordinateDisplaySettings = "UI_CoordinateDisplaySettings";
    public const string MainMenuSettings = "UI_MainMenuSettings";
    public const string AutoSaveSettings = "UI_AutoSaveSettings";
    public const string WorldStreamingSettings = "UI_WorldStreamingSettings";
    public const string DifficultySettings = "UI_DifficultySettings";
    public const string InputBindingSettings = "UI_InputBindingSettings";
    public const string InputBindingRow = "UI_InputBindingRow";
    public const string WorldLoading = "UI_WorldLoading";
    public const string PlayerWorldCoordinate = "UI_PlayerWorldCoordinate";
    public const string SaveStatus = "UI_SaveStatus";
    public const string BuffStatus = "UI_BuffStatus";
    public const string BuffStatusItem = "UI_BuffStatusItem";

    #endregion

    #region 对话与玩家反馈

    public const string PlayerChatInput = "UI_PlayerChatInput";
    public const string CharacterSpeechBubble = "UI_CharacterSpeechBubble";

    #endregion
}
