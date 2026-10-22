using UnityEngine;

public enum CharacterBodyStyle
{
    Male = 0,
    Female = 1
}

/// <summary>人物身体、独立头发与共用装备层的像素坐标契约。</summary>
[CreateAssetMenu(fileName = "PlayerAppearance_Default", menuName = "FlatWorld/Player/Appearance Profile")]
public sealed class CharacterAppearanceProfile : ScriptableObject
{
    #region 母版配置

    public Sprite BaseMale;
    public Sprite BaseFemale;
    public Sprite HairMale;
    public Sprite HairFemale;
    public CharacterBodyStyle DefaultBodyStyle = CharacterBodyStyle.Female;
    [Tooltip("母版人物是否朝左；组合完成后统一转为默认朝右。")]
    public bool SourceFacesLeft = true;
    [Min(1)] public int Width = 32;
    [Min(1)] public int Height = 48;
    [Min(0.01f)] public float PixelsPerUnit = 32f;

    #endregion

    #region 身体与头发选择

    public Sprite GetBaseSprite(CharacterBodyStyle style)
    {
        return ResolveBodyStyle(style) == CharacterBodyStyle.Female ? BaseFemale : BaseMale;
    }

    public Sprite GetHairSprite(CharacterBodyStyle style)
    {
        // 身体缺图回退时使用同一套头发，主动留空的发型则保持光头。
        return ResolveBodyStyle(style) == CharacterBodyStyle.Female ? HairFemale : HairMale;
    }

    private CharacterBodyStyle ResolveBodyStyle(CharacterBodyStyle style)
    {
        if (style == CharacterBodyStyle.Female)
            return BaseFemale != null || BaseMale == null ? CharacterBodyStyle.Female : CharacterBodyStyle.Male;
        return BaseMale != null || BaseFemale == null ? CharacterBodyStyle.Male : CharacterBodyStyle.Female;
    }

    #endregion
}
