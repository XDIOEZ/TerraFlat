using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 液体在世界中的资源与接触规则；液深统一为 0～1，默认寻路成本 20000。
/// 容器专用液体可以不声明 worldWater；声明后的本体与 MOD 液体共用渲染和角色生存链。
/// </summary>
public sealed class WorldLiquidSettings
{
    #region JSON 配置
    [JsonProperty("spriteAddress")] public string SpriteAddress;
    [JsonProperty("materialAddress")] public string MaterialAddress;
    [JsonProperty("spriteBundle")] public string SpriteBundle;
    [JsonProperty("spriteAsset")] public string SpriteAsset;
    [JsonProperty("materialBundle")] public string MaterialBundle;
    [JsonProperty("materialAsset")] public string MaterialAsset;
    [JsonProperty("navigationCost")] public int NavigationCost = 20000;
    [JsonProperty("shallowMoveSpeedMultiplier")] public float ShallowMoveSpeedMultiplier = 0.5f;
    [JsonProperty("deepMoveSpeedMultiplier")] public float DeepMoveSpeedMultiplier = 0.2f;
    [JsonProperty("entryTemperatureFloor")] public float EntryTemperatureFloor = 10f;
    [JsonProperty("entryTemperatureTransitionSeconds")] public float EntryTemperatureTransitionSeconds = 5f;
    [JsonProperty("drinkHoldSeconds")] public float DrinkHoldSeconds = 1f;
    [JsonProperty("drinkTickSeconds")] public float DrinkTickSeconds = 1f;
    [JsonProperty("followWaterVisualStyle")] public bool FollowWaterVisualStyle;
    #endregion

    #region 会话资源与校验
    private WorldLiquidBehaviour behaviour;
    [JsonIgnore] public WorldLiquidBehaviour Behaviour => behaviour ??= new WorldLiquidBehaviour(this);
    [JsonIgnore] public Sprite Sprite { get; internal set; }
    [JsonIgnore] public Material Material { get; internal set; }
    /// <summary>目录注册前验证，拒绝无法绘制或数值不合法的世界液体。</summary>
    public void Validate(string id)
    {
        if (!ValidResource(SpriteAddress, SpriteBundle, SpriteAsset) || !ValidResource(MaterialAddress, MaterialBundle, MaterialAsset))
            throw new InvalidDataException($"液体 {id} 缺少世界 Sprite/Material 地址。");
        if (NavigationCost < 1 || NavigationCost > short.MaxValue ||
            !Positive(ShallowMoveSpeedMultiplier) || ShallowMoveSpeedMultiplier > 1f ||
            !Positive(DeepMoveSpeedMultiplier) || DeepMoveSpeedMultiplier > ShallowMoveSpeedMultiplier ||
            float.IsNaN(EntryTemperatureFloor) || float.IsInfinity(EntryTemperatureFloor) ||
            !Positive(EntryTemperatureTransitionSeconds) || !Positive(DrinkTickSeconds) ||
            float.IsNaN(DrinkHoldSeconds) || float.IsInfinity(DrinkHoldSeconds) || DrinkHoldSeconds < 0f)
            throw new InvalidDataException($"液体 {id} 的世界玩法参数无效。");
    }
    private static bool ValidResource(string address, string bundle, string asset) =>
        !string.IsNullOrWhiteSpace(address)
            ? string.IsNullOrWhiteSpace(bundle) && string.IsNullOrWhiteSpace(asset)
            : !string.IsNullOrWhiteSpace(bundle) && !string.IsNullOrWhiteSpace(asset);
    private static bool Positive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    #endregion
}
