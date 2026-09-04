using UnityEngine;

/// <summary>
/// 雪地地块行为：玩家和动物在雪地上移动速度降低 10%，并在进入雪地时记录可衰减的脚印。
/// 脚印默认完整保留 60 秒，具体表现由 SnowFootprintTrail 负责，便于后续替换脚印材质。
/// </summary>
[System.Serializable]
public sealed class Tile_Snow : TileBlockBehaviour
{
    [SerializeField, Min(0.01f)] private float moveSpeedMultiplier = 0.9f;
    [SerializeField, Min(0.1f)] private float footprintLifetime = 60f;

    public override void OnEnter(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        if (item == null)
            return;

        receiver?.EnvironmentInteractions.SetAvailableEffects(
            new MoveSpeedEnvironmentEffectDefinition(moveSpeedMultiplier));

        SnowFootprintTrail footprintTrail = item.GetComponent<SnowFootprintTrail>();
        if (footprintTrail == null)
            footprintTrail = item.gameObject.AddComponent<SnowFootprintTrail>();

        footprintTrail.ConfigureLifetime(footprintLifetime);
        footprintTrail.SetSurfaceActive(true);
    }

    public override void OnExit(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        receiver?.EnvironmentInteractions.ClearAvailableEffects();
        item?.GetComponent<SnowFootprintTrail>()?.SetSurfaceActive(false);
    }
}
