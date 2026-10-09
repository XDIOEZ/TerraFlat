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
        public float StructuralMassKg = 20f, Health = 250f;
        public bool SealsAtmosphere;
        public float MinimumPressureKPa, MaximumPressureKPa = 500f, PressureDamagePerKPaSecond = .04f;
        public float MinimumTemperatureCelsius = -250f, MaximumTemperatureCelsius = 500f;
        public float TemperatureDamagePerCelsiusSecond = .02f;
        public float EngineThrustNewtons = 60000f, FuelMolesPerSecond = 3f;
        public float FuelRatioTolerance = .1f, MaximumImpurityFraction = .05f;
        public float PowerWatts = 20f, TargetPressureKPa = 101.325f, SupplyMolesPerSecond;
        public float GravityRadiusCells = 8f;
        public string RecoveryItemId;
    }
    #endregion
}
