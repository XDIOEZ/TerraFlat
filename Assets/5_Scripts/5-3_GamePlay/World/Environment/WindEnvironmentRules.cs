using UnityEngine;

/// <summary>风力按十级满风换算玩家的被动推动速度。</summary>
public static class WindEnvironmentRules
{
    #region 玩家风力推动

    public const float PlayerPushStartStrength = 0.8f;
    public const float PlayerStrongPushStrength = 0.9f;
    public const float PlayerMaximumPushStrength = 1f;
    public const float PlayerLightPushSpeed = 0.3f;
    public const float PlayerStrongPushSpeed = 1f;
    public const float PlayerMaximumPushSpeed = 2.5f;

    /// <summary>八级前不推动，八至九级、九至十级之间连续增强。</summary>
    public static float ResolvePlayerPushSpeed(float windStrength)
    {
        if (!float.IsFinite(windStrength) || windStrength < PlayerPushStartStrength)
            return 0f;

        if (windStrength < PlayerStrongPushStrength)
            return Mathf.Lerp(PlayerLightPushSpeed, PlayerStrongPushSpeed,
                Mathf.InverseLerp(PlayerPushStartStrength, PlayerStrongPushStrength, windStrength));

        return Mathf.Lerp(PlayerStrongPushSpeed, PlayerMaximumPushSpeed,
            Mathf.InverseLerp(PlayerStrongPushStrength, PlayerMaximumPushStrength, windStrength));
    }

    #endregion
}
