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
        var fixtures = new List<Player>(3);
        Player CreatePlayer(string profile, bool local)
        {
            var root = new GameObject("GM preference rules " + profile);
            root.SetActive(false);
            roots.Add(root);
            Player player = root.AddComponent<Player>();
            fixtures.Add(player);
            player.SetProfileContext(local, false, profile);
            return player;
        }

        try
        {
            Player remote = CreatePlayer("gm_remote", false);
            Player local = CreatePlayer("gm_local", true);
            Player destroyed = CreatePlayer("gm_destroyed", true);
            var players = new Dictionary<string, Player>();
            bool emptyHasPlayer = FindPreferencePlayer(players) != null;
            players.Add(remote.ProfileId, remote);
            bool remoteOnlyHasPlayer = FindPreferencePlayer(players) != null;
            players.Add(local.ProfileId, local);
            string remoteFirstProfile = FindPreferencePlayer(players)?.ProfileId;
            players.Clear();
            players.Add(local.ProfileId, local);
            players.Add(remote.ProfileId, remote);
            string localFirstProfile = FindPreferencePlayer(players)?.ProfileId;
            players.Clear();
            players.Add("missing", null);
            players.Add("destroyed", destroyed);
            players.Add(local.ProfileId, local);
            destroyed.PrepareForDespawn(false);
            Object.DestroyImmediate(destroyed.gameObject);

            return new JObject
            {
                ["nullIndexHasPlayer"] = FindPreferencePlayer(null) != null,
                ["emptyHasPlayer"] = emptyHasPlayer,
                ["remoteOnlyHasPlayer"] = remoteOnlyHasPlayer,
                ["remoteFirstProfile"] = remoteFirstProfile,
                ["localFirstProfile"] = localFirstProfile,
                ["staleEntriesProfile"] = FindPreferencePlayer(players)?.ProfileId,
                ["selectedInactive"] = !local.gameObject.activeSelf,
                ["selectedInitialized"] = local.IsInitialized
            };
        }
        finally
        {
            foreach (Player player in fixtures)
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
