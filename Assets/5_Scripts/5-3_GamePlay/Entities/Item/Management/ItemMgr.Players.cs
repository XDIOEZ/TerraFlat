using Force.DeepCloner;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ItemMgr
{
    [ShowInInspector]
    public Dictionary<string, Player> Player_DIC = new();

    private readonly HashSet<Player> _networkPlayers = new();
    private readonly HashSet<Player> _networkRemoteReplicas = new();
    private readonly HashSet<Player> _networkInitializedPlayers = new();

    #region 加载玩家

    /// <summary>
    /// 保存场景中的所有玩家
    /// </summary>
    /// <returns>保存的玩家数量</returns>
    [Button("保存玩家")]
    public int SavePlayer()
    {
        int playerCount = 0;
        Player[] players = ItemMgr.Instance.Player_DIC.Values.ToArray();

        foreach (Player player in players)
        {
            if (player == null) continue;
            if (_networkRemoteReplicas.Contains(player)) continue;
            player.Save();

            SaveDataMgr.Instance.SaveData.PlayerData_Dict[RequireProfileName(player)] = player.Data;

            playerCount++;
        }

        return playerCount;
    }

    [Button("加载玩家")]
    [Tooltip("根据传入的玩家名称,加载玩家数据\n" +
        "优先加载当前存档中的同名玩家数据\n" +
        "如果加载不到就自动创建新的玩家数据")]
    public Player LoadPlayer(string playerName)
    {
        // 加载或者创建玩家数据
        Data_Player playerData = LoadOrCreatePlayerData(playerName, out bool wasCreated);
        //传入数据创建玩家
        Player player = CreatePlayer(playerData);
        if (wasCreated)
            ApplyPlayerCreationTemplate(player, ResolveDefaultPlayerCreationTemplate());
        player.SetProfileContext(
            localProfile: true,
            profileDataWasCreated: wasCreated,
            runtimeProfileName: playerName);
        //设置玩家数据到玩家引用字典
        ItemMgr.Instance.Player_DIC[player.ProfileName] = player;

        player.Load();

        return player;
    }

    public void ReleasePlayerForWorldTransition(Player player)
    {
        if (player == null)
            return;

        string profileName = player.ProfileName;
        if (!string.IsNullOrWhiteSpace(profileName) &&
            Player_DIC.TryGetValue(profileName, out Player registeredPlayer) &&
            registeredPlayer == player)
        {
            Player_DIC.Remove(profileName);
        }

        _networkPlayers.Remove(player);
        _networkRemoteReplicas.Remove(player);
        _networkInitializedPlayers.Remove(player);
        DespawnItem(player, saveData: false);
    }

    [Tooltip("实例化玩家 但是不初始化")]
    public Player CreatePlayer(string playerName)
    {
        return CreatePlayer(playerName, ResolveDefaultPlayerCreationTemplate());
    }

    public Player CreatePlayer(string playerName, PlayerCreationTemplateConfig creationTemplate)
    {
        // 加载或者创建玩家数据
        Data_Player playerData = LoadOrCreatePlayerData(playerName, out bool wasCreated);
        //传入数据创建玩家
        Player player = CreatePlayer(playerData);
        if (wasCreated)
            ApplyPlayerCreationTemplate(player, creationTemplate);
        player.SetProfileContext(
            localProfile: true,
            profileDataWasCreated: wasCreated,
            runtimeProfileName: playerName);
        //设置玩家数据到玩家引用字典
        ItemMgr.Instance.Player_DIC[player.ProfileName] = player;

        return player;
    }

    /// <summary>
    /// 为 Mirror 网络身份创建对应的核心 Player Item。
    /// 本地玩家完整加载模块；远端玩家只创建数据与外观副本，避免重复启用输入、UI 和相机。
    /// </summary>
    public Player LoadNetworkPlayer(string playerName, int networkGuid, Vector3 spawnPosition, bool initializeLocalModules)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            throw new ArgumentException("联机玩家名称不能为空", nameof(playerName));

        if (Player_DIC.TryGetValue(playerName, out Player existingPlayer) && existingPlayer != null)
        {
            if (_networkPlayers.Contains(existingPlayer) && initializeLocalModules)
                PromoteNetworkPlayerToLocal(existingPlayer, spawnPosition);

            return existingPlayer;
        }

        bool hasSavedData = SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(playerName, out Data_Player playerData);
        if (!hasSavedData || playerData == null)
        {
            playerData = CreateDefaultPlayerData(playerName);
            if (networkGuid != 0)
                playerData.Guid = networkGuid;
        }

        playerData.Name_User = playerName;
        playerData.CurrentSceneName = SceneManager.GetActiveScene().name;
        playerData.transform.position = spawnPosition;
        playerData.transform.rotation = Quaternion.identity;
        if (playerData.transform.scale == Vector3.zero)
            playerData.transform.scale = Vector3.one;

        Player player = CreatePlayer(playerData);
        if (!hasSavedData)
            ApplyPlayerCreationTemplate(player, ResolveDefaultPlayerCreationTemplate());
        player.SetProfileContext(
            localProfile: initializeLocalModules,
            profileDataWasCreated: !hasSavedData,
            runtimeProfileName: playerName);
        Player_DIC[playerName] = player;
        _networkPlayers.Add(player);
        SaveDataMgr.Instance.SaveData.PlayerData_Dict[playerName] = playerData;

        if (initializeLocalModules)
        {
            InitializeNetworkLocalPlayer(player, spawnPosition);
        }
        else
        {
            _networkRemoteReplicas.Add(player);
            ConfigureRemoteNetworkReplica(player, spawnPosition);
        }

        return player;
    }

    public void PromoteNetworkPlayerToLocal(Player player, Vector3 spawnPosition)
    {
        if (player == null || !_networkPlayers.Contains(player))
            return;

        _networkRemoteReplicas.Remove(player);
        player.SetProfileContext(
            localProfile: true,
            profileDataWasCreated: player.WasProfileDataCreated,
            runtimeProfileName: player.ProfileName);
        InitializeNetworkLocalPlayer(player, spawnPosition);
    }

    public void ReleaseNetworkPlayer(Player player, bool persistData)
    {
        if (player == null || !_networkPlayers.Remove(player))
            return;

        _networkRemoteReplicas.Remove(player);
        _networkInitializedPlayers.Remove(player);

        if (player.Data != null)
        {
            string profileName = RequireProfileName(player);
            if (Player_DIC.TryGetValue(profileName, out Player registeredPlayer) && registeredPlayer == player)
                Player_DIC.Remove(profileName);

            if (persistData)
            {
                player.Save();
                SaveDataMgr.Instance.SaveData.PlayerData_Dict[profileName] = player.Data;
            }
        }

        DespawnItem(player, saveData: false);
    }

    private void InitializeNetworkLocalPlayer(Player player, Vector3 spawnPosition)
    {
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.bodyType = RigidbodyType2D.Dynamic;
            body.velocity = Vector2.zero;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        if (_networkInitializedPlayers.Add(player))
            player.Load();

        player.transform.position = spawnPosition;
        player.Data.transform.position = spawnPosition;

        GameController controller = player.GetComponentInChildren<GameController>(true);
        controller?.SetGameplayInputLocked(false);
    }

    private static void ConfigureRemoteNetworkReplica(Player player, Vector3 spawnPosition)
    {
        player.SetProfileContext(
            localProfile: false,
            profileDataWasCreated: player.WasProfileDataCreated,
            runtimeProfileName: player.ProfileName);
        player.transform.position = spawnPosition;
        player.Data.transform.position = spawnPosition;

        GameController controller = player.GetComponentInChildren<GameController>(true);
        controller?.SetGameplayInputLocked(true);

        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.velocity = Vector2.zero;
            body.bodyType = RigidbodyType2D.Kinematic;
            // 网络层已经按每帧生成平滑视觉坐标，关闭物理插值避免再次插值造成节拍抖动。
            body.interpolation = RigidbodyInterpolation2D.None;
        }
    }

    private Data_Player LoadOrCreatePlayerData(string playerName, out bool wasCreated)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            throw new ArgumentException("玩家档案名不能为空", nameof(playerName));

        playerName = playerName.Trim();
        Data_Player playerData;
        //检测存档中是否存在玩家数据
        if (SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(playerName, out var loadedPlayerData))
        {
            playerData = loadedPlayerData;
            wasCreated = false;

            // 档案字典键是玩家身份真源；修复旧存档中空名、默认名或临时身份写回造成的错位。
            if (!string.Equals(playerData.Name_User, playerName, StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    $"[ItemMgr] 玩家档案名与数据名不一致，已按档案键修复：" +
                    $"data={playerData.Name_User ?? "<null>"}, profile={playerName}");
                playerData.Name_User = playerName;
            }
        }
        else //如果不存在，则创建默认玩家数据
        {
            playerData = CreateDefaultPlayerData(playerName);
            wasCreated = true;
        }
        return playerData;
    }

    private Data_Player CreateDefaultPlayerData(string playerName)
    {
        var prefab = GameRes.Instance.GetPrefab("Player");
        var defaultPlayer = prefab.GetComponent<Player>();
        var playerData = defaultPlayer.Data.DeepClone();
        playerData.Guid = playerName.GetHashCode();
        playerData.Name_User = playerName;
        return playerData;
    }

    private Player CreatePlayer(Data_Player data)
    {
        Player newPlayer = (Player)ItemMgr.Instance.InstantiateItem(data, Vector3.zero, Quaternion.identity, Vector3.one, new GameObject("Players"));

        // ✅ 将父对象设置为空（放到场景根节点下）
        newPlayer.transform.SetParent(null, true);

        return newPlayer;
    }

    private PlayerCreationTemplateConfig ResolveDefaultPlayerCreationTemplate()
    {
        string templateId = string.IsNullOrWhiteSpace(defaultPlayerCreationTemplateId)
            ? PlayerCreationTemplateCatalogService.DefaultProfileId
            : defaultPlayerCreationTemplateId.Trim();
        return PlayerCreationTemplateCatalogService.GetRequired(templateId);
    }

    private static void ApplyPlayerCreationTemplate(Player player, PlayerCreationTemplateConfig creationTemplate)
    {
        creationTemplate?.ApplyTo(player);
    }

    /// <summary>获取 Player 在存档和运行时字典中的稳定身份键。</summary>
    private static string RequireProfileName(Player player)
    {
        string profileName = player?.ProfileName;
        if (string.IsNullOrWhiteSpace(profileName))
            throw new InvalidOperationException("玩家缺少稳定档案名，禁止保存到错误角色槽位");

        return profileName;
    }

    #endregion
}
