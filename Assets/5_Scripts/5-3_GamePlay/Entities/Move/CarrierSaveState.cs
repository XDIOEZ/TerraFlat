/// <summary>载具持久化协议只记录版本；位置由 Item 保存，速度和乘员均不跨会话恢复。</summary>
public sealed class CarrierSaveState
{
    #region 快照协议
    public int Version = 1; // 当前唯一协议。
    #endregion
}
