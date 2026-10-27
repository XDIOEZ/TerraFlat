namespace FlatWorld.Guide
{
    /// <summary>
    /// 新玩家生存引导当前应提示的任务阶段。
    /// </summary>
    public enum NewPlayerGuideStage
    {
        OpenInventory,
        GatherSurvivalMaterials,
        CraftSparkMaker,
        PlaceSparkMaker,
        CreateFireSeed,
        CraftBonfire,
        PlaceBonfire,
        IgniteBonfire,
        Completed
    }

    /// <summary>
    /// 教程依赖的稳定物品、建筑和里程碑 ID。
    /// </summary>
    public static class NewPlayerGuideIds
    {
        #region 任务

        public const string QuestId = "flatworld:new_player_survival_guide";
        public const string MilestoneSignal = "flatworld.guide.milestone";

        #endregion

        #region 物品与建筑

        public const string StickWood = "Stick_Wood";
        public const string Log = "Log";
        public const string Leaf = "Leaf";
        public const string SparkMakerSummoner = "SparkMaker_Summoner";
        public const string SparkMaker = "SparkMaker";
        public const string FireSeed = "FireSeed";
        public const string BonfireSummoner = "Bonfire_Summoner";
        public const string Bonfire = "Bonfire";

        public const float RequiredStickAmount = 3f;
        public const float RequiredLogAmount = 3f;
        public const float RequiredLeafAmount = 1f;

        #endregion

        #region 里程碑

        public const string InventoryOpened = "inventory-opened";
        public const string SurvivalMaterialsGathered = "survival-materials-gathered";
        public const string SparkMakerCrafted = "spark-maker-crafted";
        public const string SparkMakerPlaced = "spark-maker-placed";
        public const string FireSeedCreated = "fire-seed-created";
        public const string BonfireCrafted = "bonfire-crafted";
        public const string BonfirePlaced = "bonfire-placed";
        public const string BonfireIgnited = "bonfire-ignited";

        public static readonly string[] OrderedMilestones =
        {
            InventoryOpened,
            SurvivalMaterialsGathered,
            SparkMakerCrafted,
            SparkMakerPlaced,
            FireSeedCreated,
            BonfireCrafted,
            BonfirePlaced,
            BonfireIgnited
        };

        #endregion
    }
}
