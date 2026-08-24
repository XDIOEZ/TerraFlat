using UnityEngine;

/// <summary>
/// 冰地块行为：通过共享移动表面效果降低加减速度，让玩家和 AI 都保持惯性并产生打滑效果。
/// </summary>
[System.Serializable]
public sealed class Tile_Ice : TileBlockBehaviour
{
    [SerializeField, Min(0.01f)] private float accelerationMultiplier = 0.35f;
    [SerializeField, Min(0.01f)] private float decelerationMultiplier = 0.15f;

    public override void OnEnter(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        receiver?.EnvironmentInteractions.SetAvailableEffects(
            new MovementSurfaceResponseEnvironmentEffectDefinition(
                accelerationMultiplier,
                decelerationMultiplier));
    }

    public override void OnExit(Item item, TileData tileData, Map map, TileEffectReceiver receiver)
    {
        receiver?.EnvironmentInteractions.ClearAvailableEffects();
    }
}
