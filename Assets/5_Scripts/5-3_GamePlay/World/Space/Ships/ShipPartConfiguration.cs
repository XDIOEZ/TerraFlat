using System;
using Newtonsoft.Json;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    #region 船体部件配置
    [Serializable]
    public sealed class ShipPartConfiguration
    {
        public ShipPieceKind Kind;
        public int Layer;
        public int FootprintWidth = 1, FootprintHeight = 1;
        public bool? BlocksMovement;
        public string MaterialId;
        public string SupportTileBlockId; // 地板向普通世界查询提供实际材料的支撑规则，外观仍由原物品负责。
        public float StructuralMassKg = 20f, Health = 250f;
        // 防御抵消每次物理伤害，零功能耐久上限表示该部件没有单独瘫痪规则。
        public float? PhysicalDefense;
        public float DisableHealth;
        public bool SealsAtmosphere;
        public float MinimumPressureKPa, MaximumPressureKPa = 500f, PressureDamagePerKPaSecond = .04f;
        public float MinimumTemperatureCelsius = -250f, MaximumTemperatureCelsius = 500f;
        public float TemperatureDamagePerCelsiusSecond = .02f;
        public float EngineThrustNewtons = 60000f, FuelMolesPerSecond = 3f;
        public float LiquidFuelOxygenMolesPerMole = 20f; // 燃油使用可配置的游戏耗氧比，氢氧始终按二比一消耗。
        public float PowerWatts = 20f, TargetPressureKPa = 101.325f, SupplyMolesPerSecond;
        public float GravityRadiusCells = 8f;
        public string RecoveryItemId;
    }
    #endregion
}
