using FlatWorld.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Gameplay.Events
{
    /// <summary>事件只请求指定真实电台广播，不创建或排队虚拟消息。</summary>
    public sealed class RadioBroadcastGameEventAction : IGameEventActionHandler
    {
        #region 事件广播
        public string Type => "radio.broadcast";
        public bool Validate(JObject parameters, out string error)
        {
            int? stationId = parameters?.Value<int?>("stationId");
            bool valid = stationId.HasValue && stationId.Value != 0 &&
                RadioBroadcastMessages.TryGet(parameters.Value<string>("messageId"), out _);
            error = valid ? string.Empty : "radio.broadcast requires a stationId and a known messageId.";
            return valid;
        }

        public GameEventActionStatus Begin(GameEventActionContext context, JObject parameters, GameEventActionRuntimeSaveData state)
        {
            int id = parameters.Value<int>("stationId");
            string messageId = parameters.Value<string>("messageId");
            if (!TryBroadcast(context.ActiveWorldKey, id, messageId))
                Debug.LogWarning($"[Radio] 电台广播失败：{context.ActiveWorldKey}/{id}/{messageId}");
            return GameEventActionStatus.Completed;
        }

        public static bool TryBroadcast(string worldKey, int stationId, string messageId)
        {
            if (!GameNetwork.HasStateAuthority || string.IsNullOrEmpty(worldKey) ||
                !RadioBroadcastMessages.TryGet(messageId, out _)) return false;
            MachineEntity station = MachineWorld.GetById(stationId);
            return station != null && station.ScopeKey == worldKey &&
                station.Logic is MachineRadioStationLogic logic && logic.TryBroadcast(messageId);
        }

        public void Resume(GameEventActionContext context, JObject parameters, GameEventActionRuntimeSaveData state) { }
        public GameEventActionStatus Tick(GameEventActionContext context, JObject parameters, GameEventActionRuntimeSaveData state)
            => GameEventActionStatus.Completed;
        public void End(GameEventActionContext context, JObject parameters, GameEventActionRuntimeSaveData state, bool cancelled) { }
        #endregion
    }
}
