using UnityEngine;

/// <summary>两种世界掉落后端共用的 Shader 火焰工厂，不参与燃烧结算。</summary>
public static class WorldItemCombustionVisual
{
    #region 燃烧网格

    public static ShaderFlameEffect Create(Transform parent)
    {
        var root = new GameObject("世界掉落物_Shader火焰");
        if (parent != null) root.layer = parent.gameObject.layer;
        root.transform.SetParent(parent, false);
        ShaderFlameEffect flame = root.AddComponent<ShaderFlameEffect>();
        flame.Configure(new Vector2(0.22f, 0.34f));
        flame.SetBurning(false);
        return flame;
    }

    #endregion
}
