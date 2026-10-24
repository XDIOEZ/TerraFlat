/// <summary>在世界根档发布时装配配置来源，业务规则入口不承担存档查找。</summary>
public static class GameWorldRuleSession
{
    #region 世界配置装配

    public static void Bind(GameSaveData saveData)
    {
        GameDifficultyService.PublishRules(saveData, replaceSession: true);
    }

    #endregion
}
