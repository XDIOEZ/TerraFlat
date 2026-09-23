using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>可由世界模拟与 Unity 游戏共同读取的序列化占格定义，不持有引擎对象。</summary>
    [Serializable]
    public sealed class WorldGridOccupancyData
    {
        /// <summary>相对物品根格的确定性整数偏移列表。</summary>
        public List<WorldGridOccupancyCellData> Cells = new();
    }

    /// <summary>JSON 配置中的单个相对格坐标，校验后转换为不可变 GridCellOffset。</summary>
    [Serializable]
    public sealed class WorldGridOccupancyCellData
    {
        /// <summary>水平格偏移。</summary>
        public int X;

        /// <summary>垂直格偏移。</summary>
        public int Y;
    }
}
