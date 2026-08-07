using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlatWorld.Gameplay.Progress
{
    /// <summary>
    /// 在玩家现有 ItemSpecialData 中按命名空间安全读写 JSON，避免不同系统互相覆盖。
    /// </summary>
    public static class ItemSpecialDataJsonStore
    {
        #region 常量

        public const string LegacyProperty = "flatworld.legacyItemSpecialData";

        #endregion

        #region 根数据

        public static JObject ReadRoot(string rawData)
        {
            if (string.IsNullOrWhiteSpace(rawData))
                return new JObject();

            try
            {
                JToken token = JToken.Parse(rawData);
                if (token is JObject root)
                    return root;
            }
            catch (JsonException)
            {
                // 旧数据可能不是 JSON，转为根对象时会原样保留。
            }

            return new JObject
            {
                [LegacyProperty] = rawData
            };
        }

        #endregion

        #region 命名空间

        public static JObject ReadNamespace(Data_Player playerData, string namespaceKey)
        {
            ValidateNamespaceKey(namespaceKey);
            if (playerData == null)
                return new JObject();

            JObject root = ReadRoot(playerData.ItemSpecialData);
            return root[namespaceKey] is JObject namespaceData
                ? (JObject)namespaceData.DeepClone()
                : new JObject();
        }

        public static void WriteNamespace(Data_Player playerData, string namespaceKey, JObject namespaceData)
        {
            if (playerData == null)
                throw new ArgumentNullException(nameof(playerData));

            ValidateNamespaceKey(namespaceKey);

            JObject root = ReadRoot(playerData.ItemSpecialData);
            root[namespaceKey] = namespaceData != null
                ? namespaceData.DeepClone()
                : new JObject();
            playerData.ItemSpecialData = root.ToString(Formatting.None);
        }

        private static void ValidateNamespaceKey(string namespaceKey)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
                throw new ArgumentException("ItemSpecialData 命名空间不能为空。", nameof(namespaceKey));
        }

        #endregion
    }
}
