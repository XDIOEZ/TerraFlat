#if UNITY_EDITOR || DEVELOPMENT_BUILD
using FlatWorld.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public static class GameplayTestNetworkJoinGateRules
    {
        #region 菜单联机资源门禁

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif
        private static void Register() => GameplayTestSystemProbe.Register(
            "networking", "menu_join_gate", Capture);

        private static JObject Capture(JObject _)
        {
            const int request = 7;
            bool CanBegin(bool startup, NetworkSessionState state, bool world, bool entering, bool pending) =>
                NetworkModeUIController.CanBeginClientJoinFromMenu(startup, state, world, entering, pending);
            bool CanFinish(int active, ResourceLoadState load, NetworkSessionState state, bool world, bool entering) =>
                NetworkModeUIController.CanFinishClientJoinAfterResourcesReady(request, active, load, state, world, entering);

            return new JObject
            {
                ["startupOnlyMenuJoin"] = CanBegin(true, NetworkSessionState.Offline, false, false, false),
                ["rejectNoStartup"] = !CanBegin(false, NetworkSessionState.Offline, false, false, false),
                ["rejectOnline"] = !CanBegin(true, NetworkSessionState.Online, false, false, false),
                ["rejectWorld"] = !CanBegin(true, NetworkSessionState.Offline, true, false, false),
                ["rejectEntry"] = !CanBegin(true, NetworkSessionState.Offline, false, true, false),
                ["rejectPending"] = !CanBegin(true, NetworkSessionState.Offline, false, false, true),
                ["readyCurrentOffline"] = CanFinish(request, ResourceLoadState.Ready, NetworkSessionState.Offline, false, false),
                ["rejectLoading"] = !CanFinish(request, ResourceLoadState.Loading, NetworkSessionState.Offline, false, false),
                ["rejectFailure"] = !CanFinish(request, ResourceLoadState.Failed, NetworkSessionState.Offline, false, false),
                ["rejectCancelled"] = !CanFinish(request + 1, ResourceLoadState.Ready, NetworkSessionState.Offline, false, false),
                ["rejectSessionChange"] = !CanFinish(request, ResourceLoadState.Ready, NetworkSessionState.Starting, false, false),
                ["rejectWorldAtReady"] = !CanFinish(request, ResourceLoadState.Ready, NetworkSessionState.Offline, true, false),
                ["rejectEntryAtReady"] = !CanFinish(request, ResourceLoadState.Ready, NetworkSessionState.Offline, false, true),
                ["hostFullReady"] = NetworkModeUIController.CanEnableHostButton(true, false, true, true, true, true),
                ["hostRejectLoading"] = !NetworkModeUIController.CanEnableHostButton(true, false, false, true, true, true),
                ["hostRejectNoWorld"] = !NetworkModeUIController.CanEnableHostButton(true, false, true, false, true, true),
                ["hostRejectNoPlayer"] = !NetworkModeUIController.CanEnableHostButton(true, false, true, true, false, true),
                ["hostRejectGameplayPending"] = !NetworkModeUIController.CanEnableHostButton(true, false, true, true, true, false)
            };
        }

        #endregion
    }
}
#endif
