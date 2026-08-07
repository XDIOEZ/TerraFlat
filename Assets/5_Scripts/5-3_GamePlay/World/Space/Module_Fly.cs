using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class Module_Fly : Module, IInteract, IInteractable
{
	#region 数据结构
	[System.Serializable]
	public class FlySaveData
	{
		public float LastCameraRate = 1f;
	}
	#endregion

	#region 字段
	public Ex_ModData ModData;
	public override ModuleData _Data
	{
		get => ModData;
		set => ModData = value as Ex_ModData;
	}

	[Header("飞行参数")]
	public float renderUpSpeed = 2.5f; // 空格时Render向上速度
	public float switchToSpaceAfterSeconds = 5f; // 飞行多久后切换太空场景
	public string targetSpaceSceneName = "SpaceScene"; // 目标太空场景名

	[Header("相机参数")]
	public float cameraZoomGrowPerSecond = 0.08f; // 空格上升时相机倍率增长速度
	public float cameraZoomMaxRate = 2f; // 最大倍率

	[Header("同步参数")]
	public Vector3 playerFollowOffset = Vector3.zero; // 玩家与火箭同步偏移
	public string renderNodeName = "Render"; // 火箭可视节点名
	public float reEnterBlockSeconds = 0.25f; // 脱离后短时间禁止重新上船，避免同帧抖动

	[Header("太空生成参数")]
	public Vector3 spaceRocketSpawnOffset = new Vector3(4f, 0f, 0f); // 火箭在目标星球旁的生成偏移
	public bool disableLaunchInSpaceScene = true; // 在太空场景禁用火箭发射

	[ShowInInspector, ReadOnly]
	private bool _isControlling;

	[ShowInInspector, ReadOnly]
	private float _flightTimer;

	[ShowInInspector, ReadOnly]
	private bool _launchStarted;

	[ShowInInspector, ReadOnly]
	private float _cameraRate = 1f;

	private bool _isSwitchingScene;
	private Item _driverItem;
	private GameController _driverController;
	private Mover _driverMover;
	private Mod_Cam _driverCam;
	private Mod_InteractReciver _interactReciver;
	private Transform _renderTransform;
	private Transform _rocketControlTransform;
	private float _driverBaseCameraSize = -1f;
	private bool _driverMoverWasEnabled;
	private bool _driverMoverWasLock;
	private float _reEnterBlockedUntil;
	private int _lastStartControlFrame = -1;
	#endregion

	#region 生命周期
	public override void Awake()
	{
		base.Awake();
		if (_Data != null && string.IsNullOrEmpty(_Data.ID))
		{
			_Data.ID = "飞行模块";
		}
	}

	private void OnValidate()
	{
		if (_Data != null)
		{
			_Data.ID = "飞行模块";
		}
	}

	public override void Load()
	{
		if (ModData != null)
		{
			FlySaveData data = ModData.GetData<FlySaveData>();
			if (data != null)
			{
				_cameraRate = Mathf.Clamp(data.LastCameraRate, 1f, cameraZoomMaxRate);
			}
		}

		ResolveRocketControlTransform();
		ResolveRenderTransform();

		_interactReciver = item != null ? item.GetMod<Mod_InteractReciver>() : null;
		if (_interactReciver != null)
		{
			_interactReciver.OnAction_Start += OnReceiverInteractStart;
			_interactReciver.OnAction_Stop += OnReceiverInteractStop;
		}
	}

	public override void Save()
	{
		ModData?.WriteData(new FlySaveData
		{
			LastCameraRate = _cameraRate
		});
	}

	public override void ModUpdate(float deltaTime)
	{
		if (!_isControlling || _isSwitchingScene)
		{
			return;
		}

		TryStartLaunchBySpaceSinglePress();

		SyncPlayerToRocket();
		UpdateSpaceLiftAndCamera(deltaTime);
		TrySwitchSpaceScene();
	}

	private void OnDisable()
	{
		if (_interactReciver != null)
		{
			_interactReciver.OnAction_Start -= OnReceiverInteractStart;
			_interactReciver.OnAction_Stop -= OnReceiverInteractStop;
			_interactReciver = null;
		}

		CancelControl();
	}
	#endregion

	#region 交互事件桥接
	private void OnReceiverInteractStart(Item playerItem)
	{
		TryStartControl(playerItem);
	}

	private void OnReceiverInteractStop(Item playerItem)
	{
		CancelControl();
	}
	#endregion

	#region IInteractable
	public void OnInteractStart(Item playerItem)
	{
		TryStartControl(playerItem);
	}

	public void OnInteractUpdate(Item playerItem)
	{
	}

	public void OnInteractCancel(Item playerItem)
	{
		CancelControl();
	}
	#endregion

	#region IInteract
	private sealed class ItemInteractorAdapter : IInteractor
	{
		public GameObject User { get; private set; }
		public Item Item { get; set; }

		public ItemInteractorAdapter(Item item)
		{
			Item = item;
			User = item != null ? item.gameObject : null;
		}
	}

	public void Interact_Start(IInteractor interacter = null)
	{
		if (interacter?.Item == null)
		{
			Debug.LogError("[Module_Fly] Interact_Start 失败：interacter.Item 为空", this);
			return;
		}

		TryStartControl(interacter.Item);
	}

	public void Interact_Update(IInteractor interacter = null)
	{
	}

	public void Interact_Cancel(IInteractor interacter = null)
	{
		CancelControl();
	}
	#endregion

	#region 控制流程
	private void TryStartControl(Item playerItem)
	{
		if (Time.unscaledTime < _reEnterBlockedUntil)
		{
			return;
		}

		if (_isControlling)
		{
			return;
		}

		if (_lastStartControlFrame == Time.frameCount)
		{
			return;
		}

		_lastStartControlFrame = Time.frameCount;
		StartControl(playerItem);
	}

	private void StartControl(Item playerItem)
	{
		if (playerItem == null)
		{
			Debug.LogError("[Module_Fly] StartControl 失败：playerItem 为空", this);
			return;
		}

		if (_isSwitchingScene)
		{
			return;
		}

		if (_isControlling)
		{
			return;
		}

		ResolveRocketControlTransform();
		ResolveRenderTransform();

		_driverItem = playerItem;
		_driverController = _driverItem.itemMods.GetMod_ByID<GameController>(ModText.Controller);
		if (_driverController == null || _driverController._inputActions == null)
		{
			Debug.LogError($"[Module_Fly] 玩家缺少GameController或输入资产，无法接管火箭。玩家={_driverItem.name}", this);
			_driverItem = null;
			return;
		}

		_driverMover = _driverItem.itemMods.GetMod_ByID<Mover>(ModText.Mover);
		_driverCam = _driverItem.GetMod<Mod_Cam>();

		if (_driverMover != null)
		{
			_driverMoverWasEnabled = _driverMover.enabled;
			_driverMoverWasLock = _driverMover.IsLock;
			_driverMover.IsLock = true;
			_driverMover.enabled = false;

			if (_driverMover.rb != null)
			{
				_driverMover.rb.velocity = Vector2.zero;
			}
		}

		CacheBaseCameraSize();

		_flightTimer = 0f;
		_launchStarted = false;
		_cameraRate = 1f;
		_isControlling = true;

		SyncPlayerToRocket();
		ApplyCameraRate();

		Debug.Log($"[Module_Fly] 玩家开始驾驶火箭：玩家={_driverItem.name}, 火箭={name}");
	}

	private void CancelControl()
	{
		if (!_isControlling && _driverItem == null)
		{
			return;
		}

		if (_driverMover != null)
		{
			_driverMover.IsLock = _driverMoverWasLock;
			_driverMover.enabled = _driverMoverWasEnabled;

			if (_driverMover.rb != null)
			{
				_driverMover.rb.velocity = Vector2.zero;
			}
		}

		_reEnterBlockedUntil = Time.unscaledTime + Mathf.Max(0f, reEnterBlockSeconds);
		_isControlling = false;
		_isSwitchingScene = false;
		_launchStarted = false;
		_driverController = null;
		_driverMover = null;
		_driverCam = null;
		_driverItem = null;
		_driverBaseCameraSize = -1f;
		_rocketControlTransform = null;
	}
	#endregion

	#region 飞行逻辑
	private void TryStartLaunchBySpaceSinglePress()
	{
		if (_launchStarted)
		{
			return;
		}

		if (disableLaunchInSpaceScene && IsInSpaceScene())
		{
			return;
		}

		if (Keyboard.current == null)
		{
			return;
		}

		if (!Keyboard.current.spaceKey.wasPressedThisFrame)
		{
			return;
		}

		_launchStarted = true;
		_flightTimer = 0f;
		Debug.Log($"[Module_Fly] 火箭起飞动画开始：火箭={name}", this);
	}

	private void UpdateSpaceLiftAndCamera(float deltaTime)
	{
		if (!_launchStarted)
		{
			return;
		}

		if (_renderTransform != null)
		{
			_renderTransform.localPosition += Vector3.up * (renderUpSpeed * deltaTime);
		}

		_cameraRate = Mathf.Min(cameraZoomMaxRate, _cameraRate + cameraZoomGrowPerSecond * deltaTime);
		ApplyCameraRate();
		_flightTimer += deltaTime;
	}

	private void SyncPlayerToRocket()
	{
		if (_driverItem == null)
		{
			return;
		}

		if (_rocketControlTransform == null)
		{
			ResolveRocketControlTransform();
		}

		if (_rocketControlTransform == null)
		{
			throw new MissingReferenceException("[Module_Fly] 玩家同步失败：火箭控制目标为空。");
		}

		_driverItem.transform.position = _rocketControlTransform.position + playerFollowOffset;
	}
	#endregion

	#region 火箭目标解析
	private void ResolveRocketControlTransform()
	{
		if (item != null)
		{
			_rocketControlTransform = item.transform;
			return;
		}

		if (transform.parent != null)
		{
			_rocketControlTransform = transform.parent;
			return;
		}

		_rocketControlTransform = transform;
	}

	private void ResolveRenderTransform()
	{
		if (_rocketControlTransform == null)
		{
			ResolveRocketControlTransform();
		}

		if (_rocketControlTransform != null)
		{
			Transform found = _rocketControlTransform.Find(renderNodeName);
			if (found != null)
			{
				_renderTransform = found;
				return;
			}
		}

		_renderTransform = _rocketControlTransform != null ? _rocketControlTransform : transform;
	}
	#endregion

	#region 相机逻辑
	private void CacheBaseCameraSize()
	{
		if (_driverCam == null)
		{
			return;
		}

		if (_driverCam.Vcam != null)
		{
			_driverBaseCameraSize = _driverCam.Vcam.m_Lens.OrthographicSize;
			return;
		}

		if (_driverCam.ControllerCamera != null)
		{
			_driverBaseCameraSize = _driverCam.ControllerCamera.orthographicSize;
		}
	}

	private void ApplyCameraRate()
	{
		if (_driverCam == null || _driverBaseCameraSize <= 0f)
		{
			return;
		}

		float size = _driverBaseCameraSize * _cameraRate;
		if (_driverCam.Vcam != null)
		{
			_driverCam.Vcam.m_Lens.OrthographicSize = size;
		}

		if (_driverCam.ControllerCamera != null)
		{
			_driverCam.ControllerCamera.orthographicSize = size;
		}
	}
	#endregion

	#region 场景切换
	private void TrySwitchSpaceScene()
	{
		if (_isSwitchingScene)
		{
			return;
		}

		if (!_launchStarted)
		{
			return;
		}

		if (_flightTimer < switchToSpaceAfterSeconds)
		{
			return;
		}

		if (string.IsNullOrEmpty(targetSpaceSceneName))
		{
			Debug.LogError("[Module_Fly] 目标太空场景名为空，无法切换场景", this);
			return;
		}

		StartCoroutine(SwitchToSpaceSceneCoroutine());
	}

	private IEnumerator SwitchToSpaceSceneCoroutine()
	{
		_isSwitchingScene = true;

		if (_driverItem == null)
		{
			Debug.LogError("[Module_Fly] 切换太空场景失败：驾驶玩家为空", this);
			_isSwitchingScene = false;
			yield break;
		}

		ItemData rocketItemData = item != null ? item.itemData : null;
		ItemData playerItemData = _driverItem.itemData;
		string sourcePlanetBodyId = ResolveSourcePlanetBodyId();

		if (rocketItemData == null)
		{
			throw new System.InvalidOperationException("[Module_Fly] 火箭itemData为空，无法在太空场景重建火箭");
		}

		if (playerItemData == null)
		{
			throw new System.InvalidOperationException("[Module_Fly] 玩家itemData为空，无法在太空场景重建玩家");
		}

		Debug.Log($"[Module_Fly] 提交太空迁移任务，target={targetSpaceSceneName}, planetBodyId={sourcePlanetBodyId}", this);
		GameManager.Instance.StartSpaceTransferWithSpawn(
			targetSpaceSceneName,
			rocketItemData,
			playerItemData,
			sourcePlanetBodyId,
			spaceRocketSpawnOffset,
			spaceRocketSpawnOffset + playerFollowOffset
		);

		_isSwitchingScene = false;
		yield break;
	}

	private bool IsInSpaceScene()
	{
		return SceneManager.GetActiveScene().name == targetSpaceSceneName;
	}

	private string ResolveSourcePlanetBodyId()
	{
		PlanetData activePlanet = SaveDataMgr.Instance != null ? SaveDataMgr.Instance.Active_PlanetData : null;
		if (activePlanet != null && !string.IsNullOrEmpty(activePlanet.BodyId))
		{
			return activePlanet.BodyId;
		}

		PlanetData readyPlanet = GameManager.Instance != null ? GameManager.Instance.ReadyPlanetData : null;
		if (readyPlanet != null && !string.IsNullOrEmpty(readyPlanet.BodyId))
		{
			return readyPlanet.BodyId;
		}

		Debug.LogWarning("[Module_Fly] 未获取到当前星球BodyId，默认使用 earth 作为太空生成目标");
		return "earth";
	}

	private void SpawnRocketAndPlayerInSpace(ItemData rocketItemData, ItemData playerItemData, string planetBodyId)
	{
		if (SpaceMgr.Instance == null)
		{
			throw new System.InvalidOperationException("[Module_Fly] SpaceMgr.Instance 为空，无法在太空场景生成玩家和火箭");
		}

		Item rocketItem = SpaceMgr.Instance.InstantiateItemNearPlanet(rocketItemData, planetBodyId, spaceRocketSpawnOffset);
		Item playerItem = SpaceMgr.Instance.InstantiateItemNearPlanet(playerItemData, planetBodyId, spaceRocketSpawnOffset + playerFollowOffset);

		Module_Fly spaceFly = rocketItem.GetMod<Module_Fly>();
		if (spaceFly == null)
		{
			throw new System.InvalidOperationException($"[Module_Fly] 太空火箭缺少 Module_Fly，rocketItemId={rocketItemData.IDName}");
		}

		spaceFly.StartControl(playerItem);

		if (!spaceFly._isControlling)
		{
			playerItem.transform.position = rocketItem.transform.position + spaceFly.playerFollowOffset;
		}

		Debug.Log($"[Module_Fly] 太空重建完成，planetBodyId={planetBodyId}, rocket={rocketItemData.IDName}, player={playerItemData.IDName}");
	}

	public void EnterControlFromTransfer(Item playerItem)
	{
		StartControl(playerItem);

		if (!_isControlling && playerItem != null)
		{
			playerItem.transform.position = transform.position + playerFollowOffset;
		}
	}
	#endregion
}
