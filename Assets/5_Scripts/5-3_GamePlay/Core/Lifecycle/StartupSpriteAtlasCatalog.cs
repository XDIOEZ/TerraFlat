using System;
using UnityEngine;

/// <summary>构建时保存稳定地址与真实 Sprite 引用，资源依赖由 Unity 二进制目录一次接入。</summary>
public sealed class StartupSpriteAtlasCatalog : ScriptableObject
{
    #region 启动 Sprite 引用

    public const string Address = "FlatWorld/StartupSpriteAtlasCatalog";
    public StartupSpriteAtlasEntry[] Entries = Array.Empty<StartupSpriteAtlasEntry>();
    public StartupSpriteAtlasLocation[] Locations = Array.Empty<StartupSpriteAtlasLocation>();

    #endregion
}

[Serializable]
public sealed class StartupSpriteAtlasEntry
{
    public string Address;
    public Sprite Sprite;
}

[Serializable]
public sealed class StartupSpriteAtlasLocation
{
    public string Address;
    public string CatalogAddress;
}
