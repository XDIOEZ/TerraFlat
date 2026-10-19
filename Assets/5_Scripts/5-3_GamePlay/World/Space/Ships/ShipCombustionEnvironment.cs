using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 燃烧环境采样
        // 明火读取真实舱室氧分压，敞开区域交回所在星球，太空裸露区域按真空处理。
        public bool TrySampleCombustion(string worldKey, Vector2 position, out bool canBurn)
        {
            canBurn = false;
            if (!running || State == null || worldKey != SceneManager.GetActiveScene().name) return false;
            if (TryGetSupport(position, out ShipState ship, out _, out Vector2 local))
            {
                int x = (int)Math.Floor(local.x / ship.CellSizeMeters);
                int y = (int)Math.Floor(local.y / ship.CellSizeMeters);
                ShipEnvironmentSnapshot environment = ShipAtmosphereService.GetEnvironment(ship, x, y);
                if (environment.IsSealed)
                {
                    canBurn = AtmosphereService.CanSustainCombustion(environment.PressureKPa, environment.OxygenPartialPressureKPa);
                    return true;
                }
                if (!IsSpaceView) return false;
                return true;
            }
            if (IsSpaceView) return true;
            Vector2 point = WorldLocalPresentation.ToLogical(position);
            if (!TryGetGroundEnvironment(worldKey, point, out ShipEnvironmentSnapshot ground)) return false;
            if (!ground.IsSealed) return false;
            canBurn = AtmosphereService.CanSustainCombustion(ground.PressureKPa, ground.OxygenPartialPressureKPa);
            return true;
        }
        #endregion
    }
}
