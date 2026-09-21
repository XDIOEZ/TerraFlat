using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 单次资源会话的不可变液体编号表；0 表示无液体，其余编号仅用于内存。
    /// 后台生成只接收此纯数据副本，JSON、MOD 与存档始终使用稳定字符串 ID。
    /// </summary>
    public sealed class LiquidTypeCatalog
    {
        #region 稳定身份与会话映射
        public const string DirtyWaterId = "core:dirty_water";
        public const string SeaWaterId = "core:sea_water";
        public static readonly LiquidTypeCatalog BuiltIn = new(new[] { DirtyWaterId, SeaWaterId });
        private readonly string[] ids;
        private readonly Dictionary<string, int> indices = new(StringComparer.OrdinalIgnoreCase);

        public LiquidTypeCatalog(IEnumerable<string> liquidIds)
        {
            if (liquidIds == null) throw new ArgumentNullException(nameof(liquidIds));
            var ordered = new List<string>(liquidIds);
            ordered.Sort(StringComparer.OrdinalIgnoreCase);
            ids = new string[ordered.Count + 1];
            ids[0] = string.Empty;
            for (int i = 0; i < ordered.Count; i++)
            {
                string id = ordered[i];
                if (string.IsNullOrWhiteSpace(id) || !indices.TryAdd(id, i + 1))
                    throw new ArgumentException("液体目录包含空白或重复 ID。", nameof(liquidIds));
                ids[i + 1] = id;
            }
        }

        /// <summary>查询稳定身份；缺失定义必须明确失败，不能把未知 MOD 液体变成无液体。</summary>
        public int GetIndex(string id) => string.IsNullOrEmpty(id) ? 0 :
            indices.TryGetValue(id, out int index) ? index :
            throw new InvalidOperationException($"液体目录缺少定义：{id}");

        /// <summary>把当前会话编号还原为存档使用的稳定 ID。</summary>
        public string GetId(int index) => (uint)index < (uint)ids.Length ? ids[index] :
            throw new ArgumentOutOfRangeException(nameof(index));
        #endregion
    }
}
