using Sirenix.OdinInspector;
using MemoryPack;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家死亡状态模块：监听 DamageReceiver 的死亡事件，处理濒死UI、重生与回主菜单。
/// </summary>
public partial class Mod_PlayerDeathState : Module
{
#region 基础参数

    public const string ModuleId = "PlayerDeathState";

    [System.Serializable]
    [MemoryPackable]
    public partial class SaveData
    {
        public bool HasSleepRespawnPoint = false;
        public float SleepRespawnX = 0f;
        public float SleepRespawnY = 0f;
        public float SleepRespawnZ = 0f;

        public Vector3 SleepRespawnPoint
        {
            get => new Vector3(SleepRespawnX, SleepRespawnY, SleepRespawnZ);
            set
            {
                SleepRespawnX = value.x;
                SleepRespawnY = value.y;
                SleepRespawnZ = value.z;
            }
        }
    }

    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data { get => ModData; set => ModData = value as Ex_ModData_MemoryPackable; }

#endregion

#region 配置参数

    [Header("濒死状态设置")]
    public float respawnHpRate = 1f; // 重生血量比例
    public GameObject dyingPanelPrefab; // 濒死UI预制体
    public string dyingPanelPrefabName = "UI_Death"; // GameRes中默认预制体名

#endregion

#region 运行时缓存

    public SaveData Data = new SaveData(); // 运行时数据

    private Player _player; // 玩家引用
    private DamageReceiver _damageReceiver; // 血量模块
    private Mod_Food _food; // 食物模块
    private GameController _gameController; // 输入控制器
    private Mover _mover; // 移动模块
    private Mod_ChunkLoader _chunkLoader; // 区块加载模块
    private Rigidbody2D _rb; // 刚体缓存

    private bool _isInDyingState; // 是否已进入濒死

    private BasePanel _dyingPanel; // 濒死UI面板
    private Button _respawnButton; // 重生按钮
    private Button _exitButton; // 回主菜单按钮

#endregion

#region 生命周期

    public override void Awake()
    {
        _Data.ID = ModuleId;
    }

    private void OnValidate()
    {
        _Data.ID = ModuleId;
    }

    public override void Load()
    {
        ModData.ReadData(ref Data);

        _player = item as Player;
        if (_player == null)
        {
            throw new MissingComponentException("[Mod_PlayerDeathState] 当前 item 不是 Player，无法启用玩家死亡状态模块");
        }

        _damageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (_damageReceiver == null)
        {
            throw new MissingComponentException("[Mod_PlayerDeathState] 玩家缺少 DamageReceiver，无法监听死亡事件");
        }

        _gameController = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        if (_gameController == null)
        {
            throw new MissingComponentException("[Mod_PlayerDeathState] 玩家缺少 GameController，无法锁定输入");
        }

        _food = item.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);
        _mover = item.itemMods.GetMod_ByID<Mover>(ModText.Mover);
        _chunkLoader = item.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader);
        _rb = item.GetComponent<Rigidbody2D>();

        _damageReceiver.OnDead -= OnPlayerDead;
        _damageReceiver.OnDead += OnPlayerDead;
    }

    public override void Save()
    {
        ModData.WriteData(Data);
        item.itemData.ModuleDataDic[_Data.Name] = ModData;
    }

    private void OnDestroy()
    {
        if (_damageReceiver != null)
        {
            _damageReceiver.OnDead -= OnPlayerDead;
        }

        if (_gameController != null)
        {
            _gameController.SetGameplayInputLocked(false);
        }

        CloseAndDestroyDyingPanel();
    }

#endregion

#region 对外接口

    public void SetSleepRespawnPoint(Vector3 sleepPoint)
    {
        Data.HasSleepRespawnPoint = true;
        Data.SleepRespawnPoint = new Vector3(sleepPoint.x, sleepPoint.y, 0f);
        Debug.Log($"[Mod_PlayerDeathState] 已记录睡觉重生点: {Data.SleepRespawnPoint}");
    }

    public void ExitToMainMenuFromDying()
    {
        if (!_isInDyingState)
        {
            return;
        }

        if (GameManager.Instance == null)
        {
            throw new MissingReferenceException("[Mod_PlayerDeathState] GameManager.Instance 为空，无法退出到主界面");
        }

        CloseAndDestroyDyingPanel();
        GameManager.Instance.StartCoroutine(GameManager.Instance.BackToHelloScene_Coroutine(item));
    }

    public void RespawnFromDying()
    {
        if (!_isInDyingState)
        {
            return;
        }

        Vector3 respawnPos = item.transform.position;

        if (Data.HasSleepRespawnPoint)
        {
            respawnPos = Data.SleepRespawnPoint;
        }
        else if (GameManager.Instance != null && GameManager.Instance.TryGetDefaultPlayerSpawnPosition(out Vector3 defaultSpawnPos))
        {
            respawnPos = defaultSpawnPos;
        }
        else
        {
            Debug.LogWarning("[Mod_PlayerDeathState] 未找到默认出生点，回退到当前位置重生");
        }

        item.transform.position = new Vector3(respawnPos.x, respawnPos.y, 0f);
        _player.Data.transform.position = item.transform.position;

        float respawnHp = _damageReceiver.MaxHp * Mathf.Clamp01(respawnHpRate);
        _damageReceiver.Hp = respawnHp > 0f ? respawnHp : _damageReceiver.MaxHp;
        _damageReceiver.Data.AttackersUIDs.Clear();
        RestoreStatusModulesForRespawn();
        RestartChunkStreamingForRespawn();

        if (_rb != null)
        {
            _rb.velocity = Vector2.zero;
        }

        if (_mover != null)
        {
            _mover.enabled = true;
        }

        _isInDyingState = false;
        _gameController.SetGameplayInputLocked(false);
        CloseAndDestroyDyingPanel();

        if (_damageReceiver.Data.ShowCanvas)
        {
            _damageReceiver.RefreshUI();
        }

        Debug.Log($"[Mod_PlayerDeathState] 玩家已重生，位置={item.transform.position}, 血量={_damageReceiver.Hp:F1}/{_damageReceiver.MaxHp:F1}");
    }

#endregion

#region 重生恢复

    private void RestoreStatusModulesForRespawn()
    {
        if (_food != null)
        {
            _food.RestoreOnRespawn();
        }
    }

    private void RestartChunkStreamingForRespawn()
    {
        if (ChunkMgr.Instance == null)
        {
            Debug.LogWarning("[Mod_PlayerDeathState] ChunkMgr.Instance 为空，跳过重生区块刷新");
            return;
        }

        ChunkMgr.Instance.ResetChunkLoadQueue();

        if (_chunkLoader != null)
        {
            _chunkLoader.RefreshChunksAroundPlayer();
            return;
        }

        ChunkMgr.Instance.LoadChunkCloseToPlayer(gameObject, Distance: 1);
    }

#endregion

#region 死亡监听

    private void OnPlayerDead()
    {
        if (_isInDyingState)
        {
            ShowDyingPanel();
            _damageReceiver.ConsumeCurrentDeath();
            return;
        }

        _isInDyingState = true;
        _gameController.SetGameplayInputLocked(true);
        _damageReceiver.ConsumeCurrentDeath();
        _damageReceiver.Hp = 0f;
        _damageReceiver.Data.AttackersUIDs.Clear();

        if (_mover != null)
        {
            _mover.SetRunState(false);
            _mover.enabled = false;
        }

        if (_rb != null)
        {
            _rb.velocity = Vector2.zero;
        }

        ShowDyingPanel();
        Debug.Log($"[Mod_PlayerDeathState] 玩家进入濒死状态，场景={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
    }

#endregion

#region UI逻辑

    private void ShowDyingPanel()
    {
        if (!_isInDyingState)
        {
            return;
        }

        EnsureDyingPanelPrefab();
        if (dyingPanelPrefab == null)
        {
            throw new MissingReferenceException($"[Mod_PlayerDeathState] 濒死UI预制体为空，请配置 {dyingPanelPrefabName}");
        }

        if (_dyingPanel == null)
        {
            _dyingPanel = UIManager.Instance.CreatePanelFromGameObject(dyingPanelPrefab, dyingPanelPrefabName);
            _dyingPanel.CollectUIComponents();
            BindDyingPanelButtons();
        }

        _dyingPanel.Open();
    }

    private void EnsureDyingPanelPrefab()
    {
        if (dyingPanelPrefab != null)
        {
            return;
        }

        if (GameRes.Instance == null)
        {
            return;
        }

        dyingPanelPrefab = GameRes.Instance.GetPrefab(dyingPanelPrefabName);
    }

    private void BindDyingPanelButtons()
    {
        _respawnButton = _dyingPanel.GetButton("重生");
        _exitButton = _dyingPanel.GetButton("回到主菜单");

        if (_respawnButton == null || _exitButton == null)
        {
            throw new MissingReferenceException("[Mod_PlayerDeathState] UI_Death 缺少按钮：重生 或 回到主菜单");
        }

        _respawnButton.onClick.RemoveListener(RespawnFromDying);
        _respawnButton.onClick.AddListener(RespawnFromDying);

        _exitButton.onClick.RemoveListener(ExitToMainMenuFromDying);
        _exitButton.onClick.AddListener(ExitToMainMenuFromDying);
    }

    private void CloseAndDestroyDyingPanel()
    {
        if (_dyingPanel == null)
        {
            return;
        }

        if (_respawnButton != null)
        {
            _respawnButton.onClick.RemoveListener(RespawnFromDying);
        }

        if (_exitButton != null)
        {
            _exitButton.onClick.RemoveListener(ExitToMainMenuFromDying);
        }

        _dyingPanel.Destroy();
        _dyingPanel = null;
        _respawnButton = null;
        _exitButton = null;
    }

#endregion
}
