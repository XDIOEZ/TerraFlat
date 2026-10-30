/// <summary>地面影响只询问通用接触资格，不识别飞行、乘船等具体运动能力。</summary>
public interface IWorldTerrainContactState
{
    #region 地面接触资格
    bool HasTerrainContact { get; }
    #endregion
}
