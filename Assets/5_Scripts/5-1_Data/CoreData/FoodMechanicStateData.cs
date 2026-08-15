using MemoryPack;
using System;

/// <summary>
/// 食物扩展机制的通用持久化负载。机制自行序列化 Payload，核心数据层不依赖具体玩法类型。
/// </summary>
[Serializable]
[MemoryPackable]
public partial class FoodMechanicStateData
{
    public string StateKey;
    public byte[] Payload;
}
