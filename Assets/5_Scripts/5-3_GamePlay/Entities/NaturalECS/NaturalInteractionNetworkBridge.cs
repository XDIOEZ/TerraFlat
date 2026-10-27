using System.Globalization;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    public static class NaturalInteractionNetworkBridge
    {
        #region 原生采集权威请求
        public static bool RequestHarvest(Player actor, string worldKey, int guid, Vector2 position,
            string sourceWorldKey = null, int sourceChunkX = 0, int sourceChunkY = 0)
        {
            if (!GameNetwork.IsOnline || GameNetwork.HasStateAuthority) return false;
            if (actor == null || !actor.IsLocalProfile) return true;
            SpaceNetworkBridge.SendIntent(actor.ProfileName, new SpaceNetworkIntent
            {
                Kind = SpaceNetworkIntentKind.HarvestNatural, TargetId = worldKey,
                PieceId = guid.ToString(CultureInfo.InvariantCulture), X = position.x, Y = position.y,
                HasNaturalSource = sourceWorldKey != null, NaturalSourceWorldKey = sourceWorldKey,
                NaturalSourceChunkX = sourceChunkX, NaturalSourceChunkY = sourceChunkY
            });
            return true;
        }
        #endregion
    }
}
