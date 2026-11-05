using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class GMReflectionConsole
{
    #region GM 偏好玩家选择规则采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterPreferencePlayerRules()
    {
        GameplayTestSystemProbe.Register("testing", "gm_preference_player", CapturePreferencePlayerRules);
    }

    /// <summary>停用且未加载的玩家夹具只采样本地身份选择，不创建控制模块或注册真实玩家。</summary>
    private static JObject CapturePreferencePlayerRules(JObject arguments)
    {
        var roots = new List<GameObject>(3);
        var fixtures = new List<Item>(3);
        Item CreatePlayer(string profile, bool local)
        {
            var root = new GameObject("GM preference rules " + profile);
            root.SetActive(false);
            roots.Add(root);
            Item player = ActorProfileRegistry.CreateProfileActorForFixture(root);
            fixtures.Add(player);
            ActorProfileRegistry.BindProfile(player, local, false, profile);
            return player;
        }

        try
        {
            Item remote = CreatePlayer("gm_remote", false);
            Item local = CreatePlayer("gm_local", true);
            Item destroyed = CreatePlayer("gm_destroyed", true);
            var players = new Dictionary<string, Item>();
            bool emptyHasPlayer = FindPreferencePlayer(players) != null;
            players.Add(ActorProfileRegistry.GetProfileId(remote), remote);
            bool remoteOnlyHasPlayer = FindPreferencePlayer(players) != null;
            players.Add(ActorProfileRegistry.GetProfileId(local), local);
            string remoteFirstProfile = ActorProfileRegistry.GetProfileId(FindPreferencePlayer(players));
            players.Clear();
            players.Add(ActorProfileRegistry.GetProfileId(local), local);
            players.Add(ActorProfileRegistry.GetProfileId(remote), remote);
            string localFirstProfile = ActorProfileRegistry.GetProfileId(FindPreferencePlayer(players));
            players.Clear();
            players.Add("missing", null);
            players.Add("destroyed", destroyed);
            players.Add(ActorProfileRegistry.GetProfileId(local), local);
            destroyed.PrepareForDespawn(false);
            Object.DestroyImmediate(destroyed.gameObject);

            return new JObject
            {
                ["nullIndexHasPlayer"] = FindPreferencePlayer(null) != null,
                ["emptyHasPlayer"] = emptyHasPlayer,
                ["remoteOnlyHasPlayer"] = remoteOnlyHasPlayer,
                ["remoteFirstProfile"] = remoteFirstProfile,
                ["localFirstProfile"] = localFirstProfile,
                ["staleEntriesProfile"] = ActorProfileRegistry.GetProfileId(FindPreferencePlayer(players)),
                ["selectedInactive"] = !local.gameObject.activeSelf,
                ["selectedInitialized"] = local.IsInitialized
            };
        }
        finally
        {
            foreach (Item player in fixtures)
            {
                if (player != null)
                    player.PrepareForDespawn(false);
            }
            foreach (GameObject root in roots)
            {
                if (root != null)
                    Object.DestroyImmediate(root);
            }
        }
    }

    #endregion
}
