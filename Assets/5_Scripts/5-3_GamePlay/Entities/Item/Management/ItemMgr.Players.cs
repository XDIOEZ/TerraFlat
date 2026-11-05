using Force.DeepCloner;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ItemMgr
{
    [ShowInInspector]
    public Dictionary<string, Item> ProfileActors => ActorProfileRegistry.ProfileActors;

    private readonly HashSet<Item> _networkPlayers = new();
    private readonly HashSet<Item> _networkRemoteReplicas = new();
    private readonly HashSet<Item> _networkInitializedPlayers = new();

    #region 加载玩家

    /// <summary>
    /// 保存场景中的所有玩家
    /// </summary>
    /// <returns>保存的玩家数量</returns>
    [Button("保存玩家")]
    public int SavePlayer()
    {
        int playerCount = 0;
        Item[] players = ItemMgr.Instance.ProfileActors.Values.ToArray();

        foreach (Item player in players)
        {
            if (player == null) continue;
            if (_networkRemoteReplicas.Contains(player)) continue;
            SavePlayerAtAuthoritativePosition(player);

            SaveDataMgr.Instance.SaveData.ProfileActorData[RequireProfileName(player)] = player.itemData;

            playerCount++;
        }

        return playerCount;
    }

    private static void SavePlayerAtAuthoritativePosition(Item player)
    {
        player.Save();
        // 联机存真实世界坐标，远端显示对象的位置不能覆盖服务器记录。
        if (FlatWorld.Networking.GameNetwork.IsOnline && FlatWorld.Networking.GameNetwork.HasStateAuthority &&
            FlatWorld.Spaceflight.SpaceSession.Current?.TryGetNetworkObserver(ActorProfileRegistry.GetProfileId(player), out var observer) == true)
        {
            player.itemData.GetActorProfileMetadata().WorldKey = observer.WorldKey;
            player.itemData.transform.position = new Vector3((float)observer.Position.LocalMeters.X,
                (float)observer.Position.LocalMeters.Y);
        }
    }

    [Button("加载玩家")]
    [Tooltip("按稳定角色 ID 加载玩家数据；不存在时创建新角色。")]
    public Item LoadPlayer(string playerName)
    {
        return LoadPlayer(playerName, null);
    }

    /// <summary>首次创建角色时单独指定显示名，身份键始终使用 playerName 参数。</summary>
    public Item LoadPlayer(string playerName, string newPlayerDisplayName)
    {
        // 加载或者创建玩家数据
        ItemData playerData = LoadOrCreatePlayerData(playerName, out bool wasCreated);
        if (wasCreated && !string.IsNullOrWhiteSpace(newPlayerDisplayName))
            playerData.GetActorProfileMetadata().DisplayName = newPlayerDisplayName.Trim();
        //传入数据创建玩家
        Item player = CreatePlayer(playerData);
        if (wasCreated)
            ApplyPlayerCreationTemplate(player, ResolveDefaultPlayerCreationTemplate());
        ActorProfileRegistry.BindProfile(player, localProfile: true, profileDataWasCreated: wasCreated, runtimeProfileName: playerName);
        //设置玩家数据到玩家引用字典
        ItemMgr.Instance.ProfileActors[ActorProfileRegistry.GetProfileId(player)] = player;

        player.Load();

        return player;
    }

    public void ReleasePlayerForWorldTransition(Item player)
    {
        if (player == null)
            return;

        // 玩家卸载前先解除生态后端代理，避免同帧 LateUpdate 访问已销毁的 Player。
        AiRuntimeBackendService.Ecology?.ReleasePlayer(player);

        string profileName = ActorProfileRegistry.GetProfileId(player);
        if (!string.IsNullOrWhiteSpace(profileName) &&
            ProfileActors.TryGetValue(profileName, out Item registeredPlayer) &&
            registeredPlayer == player)
        {
            ProfileActors.Remove(profileName);
        }

        _networkPlayers.Remove(player);
        _networkRemoteReplicas.Remove(player);
        _networkInitializedPlayers.Remove(player);
        ActorProfileRegistry.SetManualControl(player, false, terminating: true);
        DespawnItem(player, saveData: false);
    }

    /// <summary>
    /// F5 成功发布新资源后，用最新 Player Prefab 重建本地主角。
    /// 玩家 Data 原引用继续使用；旧玩家先完整保存和卸载，运行时 UI 清空一帧后再由新模块重新实例化。
    /// </summary>
    public IEnumerator ReloadLocalPlayerAfterResourceReload()
    {
        Item previousPlayer = LocalControlledActor;
        if (previousPlayer == null || !ActorProfileRegistry.IsLocallyControlled(previousPlayer))
            yield break;

        string profileName = RequireProfileName(previousPlayer);
        ItemData playerData = previousPlayer.itemData;
        if (playerData == null)
            throw new InvalidOperationException("[ItemMgr] F5 重载玩家失败：当前本地玩家缺少通用角色数据。");

        string prefabId = string.IsNullOrWhiteSpace(playerData.IDName) ? "Human" : playerData.IDName;
        GameObject latestPlayerPrefab = GameRes.Instance.GetPrefab(prefabId);
        if (latestPlayerPrefab == null || latestPlayerPrefab.GetComponent<Item>() == null)
            throw new MissingReferenceException($"[ItemMgr] F5 重载玩家失败：最新资源目录中的 {prefabId} Prefab 缺少 Item。");

        previousPlayer.Save();
        playerData.GetActorProfileMetadata().WorldKey = GameplayWorldRegistry.GetItemWorldKey(previousPlayer) ?? GameplayWorldRegistry.ExecutionWorldKey;
        SaveDataMgr.Instance.SaveData.ProfileActorData[profileName] = playerData;

        ReleasePlayerForWorldTransition(previousPlayer);
        UIManager.ExistingInstance?.DestroyRuntimeUiInstancesForResourceReload();

        // Destroy 在帧末生效；等旧 Player / UI 真正销毁后再创建新实例，避免同帧残留输入和 Canvas。
        yield return null;

        Item reloadedPlayer = CreatePlayer(playerData);
        ActorProfileRegistry.BindProfile(reloadedPlayer, localProfile: true, profileDataWasCreated: false, runtimeProfileName: profileName);
        ProfileActors[profileName] = reloadedPlayer;
        reloadedPlayer.Load();

        reloadedPlayer.itemData.GetActorProfileMetadata().WorldKey = GameplayWorldRegistry.GetItemWorldKey(reloadedPlayer) ?? GameplayWorldRegistry.ExecutionWorldKey;
        SaveDataMgr.Instance.SaveData.ProfileActorData[profileName] = reloadedPlayer.itemData;

        GameManager.NotifyLocalPlayerRuntimeReloaded(reloadedPlayer);
        reloadedPlayer.itemMods
            .GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader)?
            .RefreshChunksAroundPlayer();
    }

    [Tooltip("实例化玩家 但是不初始化")]
    public Item CreatePlayer(string playerName)
    {
        return CreatePlayer(playerName, ResolveDefaultPlayerCreationTemplate());
    }

    public Item CreatePlayer(string playerName, PlayerCreationTemplateConfig creationTemplate)
    {
        if (creationTemplate == null)
            throw new ArgumentNullException(nameof(creationTemplate));

        // 加载或者创建玩家数据
        ItemData playerData = LoadOrCreatePlayerData(playerName, out bool wasCreated);
        //传入数据创建玩家
        Item player = CreatePlayer(playerData);
        if (wasCreated)
            ApplyPlayerCreationTemplate(player, creationTemplate);
        ActorProfileRegistry.BindProfile(player, localProfile: true, profileDataWasCreated: wasCreated, runtimeProfileName: playerName);
        //设置玩家数据到玩家引用字典
        ItemMgr.Instance.ProfileActors[ActorProfileRegistry.GetProfileId(player)] = player;

        return player;
    }

    /// <summary>
    /// 为 Mirror 网络身份创建对应的档案角色 Item。
    /// 本地玩家完整加载模块；远端玩家只创建数据与外观副本，避免重复启用输入、UI 和相机。
    /// </summary>
    public Item LoadNetworkPlayer(string playerName, int networkGuid, Vector3 spawnPosition, bool initializeLocalModules, string actorDefinitionId = "Human")
    {
        if (string.IsNullOrWhiteSpace(playerName))
            throw new ArgumentException("联机玩家名称不能为空", nameof(playerName));

        if (ProfileActors.TryGetValue(playerName, out Item existingPlayer) && existingPlayer != null)
        {
            if (_networkPlayers.Contains(existingPlayer) && initializeLocalModules)
                PromoteNetworkPlayerToLocal(existingPlayer, spawnPosition);

            return existingPlayer;
        }

        bool hasSavedData = SaveDataMgr.Instance.SaveData.ProfileActorData.TryGetValue(playerName, out ItemData playerData);
        if (!hasSavedData || playerData == null)
        {
            playerData = CreateDefaultPlayerData(playerName, actorDefinitionId);
            if (networkGuid != 0)
                playerData.Guid = networkGuid;
        }

        if (!hasSavedData)
            playerData.GetActorProfileMetadata().DisplayName = playerName;
        if (!hasSavedData || string.IsNullOrEmpty(playerData.GetActorProfileMetadata().WorldKey))
            playerData.GetActorProfileMetadata().WorldKey = GameplayWorldRegistry.ExecutionWorldKey;
        if (!SpaceMgr.IsSpaceScene(playerData.GetActorProfileMetadata().WorldKey))
            playerData.transform.position = WorldLocalPresentation.ToLogical(spawnPosition);
        playerData.transform.rotation = Quaternion.identity;
        if (playerData.transform.scale == Vector3.zero)
            playerData.transform.scale = Vector3.one;

        Item player = CreatePlayer(playerData);
        if (!hasSavedData && FlatWorld.Networking.GameNetwork.HasStateAuthority && playerData.IDName == "Human")
            ApplyPlayerCreationTemplate(player, ResolveDefaultPlayerCreationTemplate());
        ActorProfileRegistry.BindProfile(player, localProfile: initializeLocalModules, profileDataWasCreated: !hasSavedData, runtimeProfileName: playerName);
        ProfileActors[playerName] = player;
        _networkPlayers.Add(player);
        SaveDataMgr.Instance.SaveData.ProfileActorData[playerName] = playerData;

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

    public void PromoteNetworkPlayerToLocal(Item player, Vector3 spawnPosition)
    {
        if (player == null || !_networkPlayers.Contains(player))
            return;

        bool wasAuthority = _networkAuthorityPlayers.Remove(player);
        if (wasAuthority) player.ModuleUnload();
        _networkRemoteReplicas.Remove(player);
        ActorProfileRegistry.BindProfile(player, localProfile: true, profileDataWasCreated: ActorProfileRegistry.WasProfileCreated(player), runtimeProfileName: ActorProfileRegistry.GetProfileId(player));
        if (wasAuthority)
            foreach (Module module in player.Mods.Values) module?.LoadRuntime();
        InitializeNetworkLocalPlayer(player, spawnPosition);
    }

    public void ReleaseNetworkPlayer(Item player, bool persistData)
    {
        if (player == null || !_networkPlayers.Remove(player))
            return;

        _networkRemoteReplicas.Remove(player);
        _networkInitializedPlayers.Remove(player);
        _networkAuthorityPlayers.Remove(player);

        if (player.itemData != null)
        {
            string profileName = RequireProfileName(player);
            if (ProfileActors.TryGetValue(profileName, out Item registeredPlayer) && registeredPlayer == player)
                ProfileActors.Remove(profileName);

            if (persistData)
            {
                SavePlayerAtAuthoritativePosition(player);
                SaveDataMgr.Instance.SaveData.ProfileActorData[profileName] = player.itemData;
            }
        }

        ActorProfileRegistry.SetManualControl(player, false, terminating: true);
        DespawnItem(player, saveData: false);
    }

    private void InitializeNetworkLocalPlayer(Item player, Vector3 spawnPosition)
    {
        Vector3 logicalSpawnPosition = WorldLocalPresentation.ToLogical(spawnPosition);
        Vector3 presentationSpawnPosition = WorldLocalPresentation.ProjectPosition(logicalSpawnPosition);
        Rigidbody2D body = WorldPhysicsService2D.GetItemRigidbody(player);
        if (body != null)
        {
            WorldPhysicsService2D.ConfigureItemBodyForIndependentMotion(player, body);
            body.velocity = Vector2.zero;
            body.interpolation = Physics2D.simulationMode == SimulationMode2D.Script
                ? RigidbodyInterpolation2D.None : RigidbodyInterpolation2D.Interpolate;
        }

        if (_networkInitializedPlayers.Add(player))
            player.Load();

        player.transform.position = presentationSpawnPosition;
        if (body != null)
            body.position = presentationSpawnPosition;
        player.itemData.transform.position = logicalSpawnPosition;

        Mod_GameController controller = player.GetComponentInChildren<Mod_GameController>(true);
        if (controller != null) controller.enabled = true;
        controller?.SetGameplayInputLocked(false);
    }

    #region 远端玩家服务器结算模块
    private readonly HashSet<Item> _networkAuthorityPlayers = new();
    public void InitializeNetworkAuthorityPlayer(Item player, bool surfaceCombat = false)
    {
        if (player == null || ActorProfileRegistry.IsLocallyControlled(player) || !_networkAuthorityPlayers.Add(player)) return;
        _networkRemoteReplicas.Remove(player);
        // 只加载权威结算与手持模块，输入、相机、地表流送仍由本机玩家负责。
        player.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.ClearNetworkVisualHeldItem();
        var enabledStates = new Dictionary<Module, bool>();
        foreach (Module module in player.Mods.Values)
            if (module?._Data != null)
            {
                enabledStates[module] = module._Data.Enabled;
                module._Data.Enabled = enabledStates[module] && module is not (Mod_Cam or Mod_ChunkLoader or Mod_InteractSender);
            }
        try { player.ResumeRuntimeAfterReplacement(); }
        finally { foreach (var state in enabledStates) if (state.Key?._Data != null) state.Key._Data.Enabled = state.Value; }
        player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.EnableNetworkAuthorityMovement();
        Mod_GameController controller = player.GetComponentInChildren<Mod_GameController>(true);
        if (controller != null)
        {
            controller.enabled = false;
            controller.SetGameplayInputLocked(false);
        }
        Rigidbody2D body = WorldPhysicsService2D.GetItemRigidbody(player);
        if (body != null) WorldPhysicsService2D.ConfigureItemBodyForNetworkRepresentation(player, body, true);
        Mod_ItemPicker picker = player.GetComponentInChildren<Mod_ItemPicker>(true);
        if (picker != null)
        {
            picker.AddTargetInventories.Clear();
            if (player.itemMods.GetMod_ByID(ModText.Hotbar) is IInventory hotbar) picker.AddTargetInventories.Add(hotbar);
            if (player.itemMods.GetMod_ByID(ModText.Bag) is IInventory bag) picker.AddTargetInventories.Add(bag);
        }
    }
    public void ReleaseNetworkAuthorityPlayer(Item player)
    {
        if (player == null || ActorProfileRegistry.IsLocallyControlled(player) || !_networkAuthorityPlayers.Remove(player)) return;
        player.SuspendRuntimeForNetworkPresentation();
        _networkRemoteReplicas.Add(player);
        ConfigureRemoteNetworkReplica(player, player.transform.position);
        foreach (Module module in player.Mods.Values)
            if (module is Mod_TurnBack or Mod_AnimatorController) module.LoadRuntime();
        Mod_TurnBack turn = player.itemMods.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody);
        if (turn != null) turn.faceMouse = null;
        Mod_HotBar hotbar = player.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
        if (hotbar?._Data != null) hotbar.ApplyRemoteNetworkData(player, hotbar._Data);
    }
    #endregion

    private static void ConfigureRemoteNetworkReplica(Item player, Vector3 spawnPosition)
    {
        Vector3 logicalSpawnPosition = WorldLocalPresentation.ToLogical(spawnPosition);
        Vector3 presentationSpawnPosition = WorldLocalPresentation.ProjectPosition(logicalSpawnPosition);
        ActorProfileRegistry.BindProfile(player, localProfile: false, profileDataWasCreated: ActorProfileRegistry.WasProfileCreated(player), runtimeProfileName: ActorProfileRegistry.GetProfileId(player));
        player.transform.position = presentationSpawnPosition;
        player.itemData.transform.position = logicalSpawnPosition;

        Mod_GameController controller = player.GetComponentInChildren<Mod_GameController>(true);
        controller?.SetGameplayInputLocked(true);

        Rigidbody2D body = WorldPhysicsService2D.GetItemRigidbody(player);
        if (body != null)
        {
            body.position = presentationSpawnPosition;
            body.velocity = Vector2.zero;
            WorldPhysicsService2D.ConfigureItemBodyForNetworkRepresentation(player, body, body.simulated);
            // 网络层已经按每帧生成平滑视觉坐标，关闭物理插值避免再次插值造成节拍抖动。
            body.interpolation = RigidbodyInterpolation2D.None;
        }
    }

    private ItemData LoadOrCreatePlayerData(string playerName, out bool wasCreated)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            throw new ArgumentException("玩家档案名不能为空", nameof(playerName));

        playerName = playerName.Trim();
        ItemData playerData;
        //检测存档中是否存在玩家数据
        if (SaveDataMgr.Instance.SaveData.ProfileActorData.TryGetValue(playerName, out ItemData loadedPlayerData))
        {
            playerData = loadedPlayerData;
            wasCreated = false;

            playerData = ItemDefinitionRuntime.RebasePersistedData(GameRes.Instance, playerData);

            // 档案键作为稳定 ID，改显示名不能改变身份。
            if (string.IsNullOrWhiteSpace(playerData.GetActorProfileMetadata().DisplayName))
                playerData.GetActorProfileMetadata().DisplayName = playerName;
        }
        else //如果不存在，则创建默认玩家数据
        {
            playerData = CreateDefaultPlayerData(playerName);
            wasCreated = true;
        }
        return playerData;
    }

    private ItemData CreateDefaultPlayerData(string playerName, string actorDefinitionId = "Human")
    {
        GameRes.Instance.TryGetItemDefinition(actorDefinitionId, out RuntimeItemDefinition definition);
        ItemData playerData = definition.CreateItemData();
        playerData.Guid = playerName.GetHashCode();
        playerData.GetActorProfileMetadata().DisplayName = playerName;
        return playerData;
    }

    private Item CreatePlayer(ItemData data)
    {
        // 已有角色必须以存档坐标创建刚体，不能先在原点注册后再由加载流程纠正。
        ItemTransform savedTransform = data?.transform ??
            throw new InvalidOperationException("玩家数据缺少保存位置，无法恢复角色。");
        Transform parent = RuntimeSceneObjectHierarchy.GetOrCreateCategoryRoot(
            GameplayWorldRegistry.CurrentScene, RuntimeSceneObjectCategory.Players);
        return (Item)ItemMgr.Instance.InstantiateItem(data,
            WorldLocalPresentation.ProjectPosition(savedTransform.position),
            savedTransform.rotation, savedTransform.scale, parent.gameObject);
    }

    private PlayerCreationTemplateConfig ResolveDefaultPlayerCreationTemplate()
    {
        string templateId = string.IsNullOrWhiteSpace(defaultPlayerCreationTemplateId)
            ? PlayerCreationTemplateCatalogService.DefaultProfileId
            : defaultPlayerCreationTemplateId.Trim();
        return PlayerCreationTemplateCatalogService.GetRequired(templateId);
    }

    private static void ApplyPlayerCreationTemplate(Item player, PlayerCreationTemplateConfig creationTemplate)
    {
        creationTemplate?.ApplyTo(player);
    }

    /// <summary>获取 Player 在存档和运行时字典中的稳定身份键。</summary>
    private static string RequireProfileName(Item player)
    {
        string profileName = ActorProfileRegistry.GetProfileId(player);
        if (string.IsNullOrWhiteSpace(profileName))
            throw new InvalidOperationException("玩家缺少稳定档案名，禁止保存到错误角色槽位");

        return profileName;
    }

    #endregion
}
