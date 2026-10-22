using UnityEngine;

public enum CharacterBodyStyle
{
    Male = 0,
    Female = 1
}

/// <summary>角色母版与穿戴部位共用的像素坐标契约。</summary>
[CreateAssetMenu(fileName = "PlayerAppearance_Default", menuName = "FlatWorld/Player/Appearance Profile")]
public sealed class CharacterAppearanceProfile : ScriptableObject
{
    #region 母版配置

    public Sprite BaseMale;
    public Sprite BaseFemale;
    public CharacterBodyStyle DefaultBodyStyle = CharacterBodyStyle.Female;
    [Min(1)] public int Width = 32;
    [Min(1)] public int Height = 48;
    [Min(0.01f)] public float PixelsPerUnit = 32f;

    #endregion

    #region 部位像素边界

    // 坐标从画布左下角起算，部位划分同时用于世界拼合与库存图标。
    public int HeadBottom = 25;
    public int FeetTop = 10;
    public int LegsTop = 17;
    public int HandsBottom = 13;
    public int HandsTop = 19;
    public int HandsInnerLeft = 10;
    public int HandsInnerRight = 22;

    public int GetPartIndex(int x, int y)
    {
        if (y >= HeadBottom) return 0;
        if (y < FeetTop) return 4;
        if (y >= HandsBottom && y < HandsTop && (x < HandsInnerLeft || x >= HandsInnerRight)) return 2;
        if (y < LegsTop) return 3;
        return 1;
    }

    public Sprite GetBaseSprite(CharacterBodyStyle style)
    {
        Sprite preferred = style == CharacterBodyStyle.Female ? BaseFemale : BaseMale;
        return preferred != null ? preferred : style == CharacterBodyStyle.Female ? BaseMale : BaseFemale;
    }

    #endregion
}
