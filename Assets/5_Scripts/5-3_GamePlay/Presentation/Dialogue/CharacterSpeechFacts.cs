namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 自言自语系统使用的稳定 Fact 键，Contributor 与 JSON 配置必须保持一致。
    /// </summary>
    public static class CharacterSpeechFacts
    {
        #region 饥饿

        public const string HungerRate = "hunger.rate";
        public const string HungerTier = "hunger.tier";
        public const string HungerIsTakingDamage = "hunger.isTakingDamage";

        #endregion

        #region 水分

        public const string HydrationRate = "hydration.rate";
        public const string HydrationTier = "hydration.tier";
        public const string HydrationIsTakingDamage = "hydration.isTakingDamage";

        #endregion

        #region 新手引导

        public const string TutorialEnabled = "tutorial.enabled";
        public const string TutorialStage = "tutorial.stage";
        public const string TutorialCompleted = "tutorial.completed";

        #endregion

        #region 天气与暴露

        public const string WeatherType = "weather.type";
        public const string WeatherPhase = "weather.phase";
        public const string WeatherIntensity = "weather.intensity";
        public const string WeatherIsRaining = "weather.isRaining";
        public const string WeatherIsExposed = "weather.isExposed";
        public const string WeatherHasHeatSource = "weather.hasHeatSource";
        public const string WeatherRemainingSeconds = "weather.remainingSeconds";

        #endregion
    }
}
