using System.Collections;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>最终 MOD 目录就绪后建立会话液体表并加载世界外观，数组编号不会写回 JSON 或存档。</summary>
public partial class GameRes
{
    #region 世界液体资源
    public LiquidTypeCatalog LiquidTypes { get; private set; }

    /// <summary>纯生成器仅收到不可变编号副本；Unity 资源继续由本资源会话持有。</summary>
    private IEnumerator LoadWorldLiquidResources()
    {
        LiquidTypes = new LiquidTypeCatalog(LiquidDefinitions.Keys);
        foreach (LiquidDefinition definition in LiquidDefinitions.Values)
        {
            WorldLiquidSettings settings = definition.WorldWater;
            if (settings == null) continue;
            settings.Validate(definition.Id);
            if (settings.Sprite == null)
            {
                var sprite = resourceAssets.Own(Addressables.LoadAssetAsync<Sprite>(settings.SpriteAddress));
                yield return sprite;
                ResourceAssetScope.Require(sprite, $"液体 {definition.Id} Sprite");
                settings.Sprite = sprite.Result;
            }
            if (settings.Material == null)
            {
                var material = resourceAssets.Own(Addressables.LoadAssetAsync<Material>(settings.MaterialAddress));
                yield return material;
                ResourceAssetScope.Require(material, $"液体 {definition.Id} Material");
                settings.Material = material.Result;
            }
        }
    }
    #endregion
}
