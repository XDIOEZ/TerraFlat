using System;
using Newtonsoft.Json.Linq;

namespace FlatWorld.Gameplay.Progress
{
    /// <summary>
    /// 保存玩家首次冶炼粗铁与首次制作金属工具的幂等里程碑。
    /// 数据写入独立的 flatworld.progression.metallurgy 命名空间，版本 1 只包含两个布尔值，
    /// 因此不会覆盖任务、教程或自言自语完成标记，也能随现有 Data_Player 存档链自然落盘。
    /// </summary>
    public sealed class PlayerMetallurgyProgressStore
    {
        #region 常量

        public const string NamespaceKey = "flatworld.progression.metallurgy";
        public const int CurrentVersion = 1;
        public const string RawIronItemId = "Ingot_RawIron";
        public const string RawIronPickaxeItemId = "Pickaxe_RawIron";

        private const string VersionProperty = "version";
        private const string FirstRawIronSmeltedProperty = "firstRawIronSmelted";
        private const string FirstMetalToolCraftedProperty = "firstMetalToolCrafted";

        private static readonly string[] MetalToolItemIds =
        {
            "Axe_RawIron",
            RawIronPickaxeItemId,
            "Axe_Iron",
            "Pickaxe_Iron",
            "Spear_Iron",
            "Chestplate_Iron"
        };

        #endregion

        #region 状态

        private readonly Data_Player playerData;
        private JObject namespaceData;

        public bool FirstRawIronSmelted { get; private set; }
        public bool FirstMetalToolCrafted { get; private set; }

        #endregion

        public PlayerMetallurgyProgressStore(Data_Player playerData)
        {
            this.playerData = playerData ?? throw new ArgumentNullException(nameof(playerData));
            Load();
        }

        #region 推进

        public bool RecordSmeltedOutput(string outputItemId)
        {
            if (FirstRawIronSmelted ||
                !string.Equals(outputItemId, RawIronItemId, StringComparison.Ordinal))
            {
                return false;
            }

            FirstRawIronSmelted = true;
            Save();
            return true;
        }

        public bool RecordCraftedOutput(string outputItemId)
        {
            if (FirstMetalToolCrafted || !IsMetalTool(outputItemId))
                return false;

            FirstMetalToolCrafted = true;
            Save();
            return true;
        }

        public static bool IsMetalTool(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            for (int index = 0; index < MetalToolItemIds.Length; index++)
            {
                if (string.Equals(MetalToolItemIds[index], itemId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        #endregion

        #region 持久化

        private void Load()
        {
            namespaceData = ItemSpecialDataJsonStore.ReadNamespace(playerData, NamespaceKey);
            FirstRawIronSmelted = namespaceData.Value<bool?>(FirstRawIronSmeltedProperty) == true;
            FirstMetalToolCrafted = namespaceData.Value<bool?>(FirstMetalToolCraftedProperty) == true;
        }

        private void Save()
        {
            namespaceData ??= new JObject();
            namespaceData[VersionProperty] = CurrentVersion;
            namespaceData[FirstRawIronSmeltedProperty] = FirstRawIronSmelted;
            namespaceData[FirstMetalToolCraftedProperty] = FirstMetalToolCrafted;
            ItemSpecialDataJsonStore.WriteNamespace(playerData, NamespaceKey, namespaceData);
        }

        #endregion
    }
}
