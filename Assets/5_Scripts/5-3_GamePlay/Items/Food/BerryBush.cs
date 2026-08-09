using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 浆果丛：周期成熟后显示提示Sprite，玩家按E交互可在周围生成浆果物品。
/// </summary>
public class BerryBush : MonoBehaviour, IInteractable
{
#region 配置

	[Header("产出配置")]
	public string BerryItemId = "Berry"; // 产出物品ID

	[Min(0.1f)]
	public float ProductionIntervalSeconds = 45f; // 每次成熟间隔

	[Min(1)]
	public int MaxBerryCount = 12; // 浆果库存上限

	[Header("自然生成")]
	[Min(0)]
	public int NaturalInitialBerryCountMin = 1;

	[Min(0)]
	public int NaturalInitialBerryCountMax = 2;

	[Min(0.05f)]
	public float SpawnRadius = 1.2f; // 采摘后物品散落半径

	[Min(0.05f)]
	public float ThrowDuration = 0.5f; // 抛物线投掷时长

	public float ThrowBezierOffset = 0.8f; // 抛物线控制点高度偏移

	public float ThrowArcHeight = 0.6f; // 抛物线额外弧高

	[Header("成熟提示Sprite")]
	public List<SpriteRenderer> ReadySpriteRenderers = new List<SpriteRenderer>(); // 成熟提示渲染器数组（按顺序逐个显示）
	public Sprite ReadySpriteOverride; // 手动覆盖成熟Sprite

	[Header("成熟提示图层")]
	public bool UseBushRendererSorting = true; // 是否跟随浆果丛本体渲染层级
	public int ReadySpriteSortingOrderOffset = -1; // 相对浆果丛本体的层级偏移（默认在其后方）
	public string ReadySpriteSortingLayerName = "Default"; // 不跟随本体时使用的SortingLayer
	public int ReadySpriteSortingOrder = 0; // 不跟随本体时使用的SortingOrder

#endregion

#region 运行时

	private float _productionTimer;
	private int _currentBerryCount;
	private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
	private SpriteRenderer _bushRenderer;

	public int CurrentBerryCount => _currentBerryCount;

#endregion

#region 生命周期

	private void Awake()
	{
		if (string.IsNullOrWhiteSpace(BerryItemId))
		{
			throw new ArgumentException("[BerryBush] BerryItemId 不能为空。", nameof(BerryItemId));
		}

		_bushRenderer = GetComponentInChildren<SpriteRenderer>();
		EnsureRendererList();

		EnsureReadySpriteConfigured();
		ApplyReadySpriteSorting();
		RefreshReadySpriteVisual();
	}

	private void Update()
	{
		if (_currentBerryCount >= Mathf.Max(1, MaxBerryCount))
		{
			return;
		}

		_productionTimer +=
			Time.deltaTime *
			GameDifficultyService.Current.Production.CropGrowthMultiplier;
		if (_productionTimer >= Mathf.Max(0.1f, ProductionIntervalSeconds))
		{
			_productionTimer = 0f;
			_currentBerryCount++;
			RefreshReadySpriteVisual();
		}
	}

#endregion

#region 交互

	/// <summary>
	/// 玩家按下E进入交互时触发：若已成熟则立即采摘并在周围生成浆果。
	/// </summary>
	public void OnInteractStart(Item playerItem)
	{
		if (playerItem == null)
		{
			throw new ArgumentNullException(nameof(playerItem), "[BerryBush] 交互失败：playerItem 为空。");
		}

		if (_currentBerryCount <= 0)
		{
			return;
		}

		HarvestBerries();
	}

	public void OnInteractCancel(Item playerItem)
	{
	}

#endregion

#region 产出逻辑

	/// <summary>
	/// 仅供自然资源生成流程调用；使用确定性随机值设置初始浆果库存。
	/// </summary>
	public void InitializeNaturalStock(uint deterministicRandomValue)
	{
		int capacity = Mathf.Max(1, MaxBerryCount);
		int minCount = Mathf.Clamp(NaturalInitialBerryCountMin, 0, capacity);
		int maxCount = Mathf.Clamp(NaturalInitialBerryCountMax, minCount, capacity);
		uint range = (uint)(maxCount - minCount + 1);

		_currentBerryCount = minCount + (int)(deterministicRandomValue % range);
		_productionTimer = 0f;
		RefreshReadySpriteVisual();
	}

	private void HarvestBerries()
	{
		Vector2 harvestStartPos = ResolveHarvestStartPosition();
		Item berry = InstantiateBerryLikePlayerDrop(harvestStartPos);
		int outputAmount = GameDifficultyService.ScaleRandomizedAmount(
			1,
			GameDifficultyService.Current.World.LootAmountMultiplier);
		berry.itemData.Stack.Amount = outputAmount;
		if (outputAmount <= 0)
		{
			berry.DestroySelf();
			_currentBerryCount = Mathf.Max(0, _currentBerryCount - 1);
			RefreshReadySpriteVisual();
			return;
		}
		ApplyParabolaThrowLikePlayerDrop(berry, harvestStartPos);

		_currentBerryCount = Mathf.Max(0, _currentBerryCount - 1);
		Debug.Log($"[BerryBush] 采摘完成，生成浆果数量={outputAmount}, 剩余库存={_currentBerryCount}, 物品ID={BerryItemId}");

		RefreshReadySpriteVisual();
	}

	/// <summary>
	/// 按玩家丢弃手持物的流程创建掉落物：新版生态区块挂到 NaturalItems，旧场景才回退旧 Chunk。
	/// </summary>
	private Item InstantiateBerryLikePlayerDrop(Vector2 spawnPos)
	{
		GameObject dropParent = ResolveBerryDropParent(spawnPos);
		Item berry = ItemMgr.Instance.InstantiateItem(
			BerryItemId,
			spawnPos,
			Quaternion.identity,
			Vector3.one,
			dropParent);

		if (berry == null)
		{
			throw new MissingReferenceException($"[BerryBush] 采摘失败：实例化浆果失败，物品ID={BerryItemId}");
		}

		berry.Load();
		berry.SetInHand(false);
		GetComponentInParent<ChunkNaturalItemRenderer>(true)?.RegisterTransientItem(berry);
		return berry;
	}

	/// <summary>
	/// 新版 WorldModel 物品不能为了掉落物触发旧 Chunk 加载；旧场景仍保留原有归属。
	/// </summary>
	private GameObject ResolveBerryDropParent(Vector2 spawnPos)
	{
		ChunkNaturalItemRenderer naturalRenderer =
			GetComponentInParent<ChunkNaturalItemRenderer>(true);
		if (naturalRenderer != null)
			return naturalRenderer.gameObject;

		// 新版区块已经接管窗口时，找不到自然物父节点也不能触发旧 Chunk 查找。
		if (ChunkMgr.ExistingInstance != null &&
			ChunkMgr.ExistingInstance.IsWorldModelRuntimeActive)
			return null;

		ChunkMgr chunkMgr = ChunkMgr.Instance;
		if (chunkMgr != null &&
			chunkMgr.TryGetActiveChunkByPos(Chunk.GetChunkPosition(spawnPos), out Chunk chunk) &&
			chunk != null)
		{
			return chunk.gameObject;
		}

		return null;
	}

	/// <summary>
	/// 复用玩家丢弃逻辑：通过 Mod_BaseDroper 统一创建掉落轨迹与 Drop 模块。
	/// </summary>
	private void ApplyParabolaThrowLikePlayerDrop(Item berry, Vector2 startPos)
	{
		if (berry == null)
		{
			throw new ArgumentNullException(nameof(berry), "[BerryBush] 投掷失败：berry 为空。");
		}

		Vector2 randomDir = UnityEngine.Random.insideUnitCircle.normalized;
		float randomDist = UnityEngine.Random.Range(0.5f * SpawnRadius, SpawnRadius);
		Vector2 endPos = startPos + randomDir * randomDist;

		Mod_BaseDroper.StaticDropItem_Pos(
			berry,
			startPos,
			endPos,
			Mathf.Max(0.05f, ThrowDuration),
			Mod_BaseDroper.MoveMode.BezierCurve,
			ThrowBezierOffset,
			ThrowArcHeight);
	}

	/// <summary>
	/// 采摘起点优先取可见浆果Sprite的位置，避免总从灌木根部飞出。
	/// </summary>
	private Vector2 ResolveHarvestStartPosition()
	{
		if (ReadySpriteRenderers == null || ReadySpriteRenderers.Count == 0)
		{
			return transform.position;
		}

		int rendererCount = ReadySpriteRenderers.Count;
		int logicalIndex = _currentBerryCount > rendererCount
			? (_currentBerryCount - 1) % rendererCount
			: Mathf.Clamp(_currentBerryCount - 1, 0, rendererCount - 1);

		for (int step = 0; step < rendererCount; step++)
		{
			int index = (logicalIndex - step + rendererCount) % rendererCount;
			SpriteRenderer renderer = ReadySpriteRenderers[index];
			if (renderer == null || renderer.sprite == null)
			{
				continue;
			}

			return renderer.transform.position;
		}

		return transform.position;
	}

#endregion

#region Sprite解析

	/// <summary>
	/// 优先使用手动覆盖Sprite；默认从 JSON 构建的运行时物品定义读取产物图标。
	/// </summary>
	private void EnsureReadySpriteConfigured()
	{
		if (ReadySpriteRenderers == null || ReadySpriteRenderers.Count == 0)
		{
			return;
		}

		Sprite sprite = ReadySpriteOverride != null ? ReadySpriteOverride : ResolveRuntimeItemSprite(BerryItemId);
		for (int i = 0; i < ReadySpriteRenderers.Count; i++)
		{
			SpriteRenderer renderer = ReadySpriteRenderers[i];
			if (renderer == null)
			{
				continue;
			}

			renderer.sprite = sprite;
		}
	}

	private void ApplyReadySpriteSorting()
	{
		if (ReadySpriteRenderers == null || ReadySpriteRenderers.Count == 0)
		{
			return;
		}

		for (int i = 0; i < ReadySpriteRenderers.Count; i++)
		{
			SpriteRenderer renderer = ReadySpriteRenderers[i];
			if (renderer == null)
			{
				continue;
			}

			if (UseBushRendererSorting && _bushRenderer != null)
			{
				renderer.sortingLayerID = _bushRenderer.sortingLayerID;
				renderer.sortingOrder = _bushRenderer.sortingOrder + ReadySpriteSortingOrderOffset;
			}
			else
			{
				renderer.sortingLayerName = ReadySpriteSortingLayerName;
				renderer.sortingOrder = ReadySpriteSortingOrder;
			}
		}
	}

	private void RefreshReadySpriteVisual()
	{
		if (ReadySpriteRenderers == null || ReadySpriteRenderers.Count == 0)
		{
			return;
		}

		int visibleCount = Mathf.Min(_currentBerryCount, ReadySpriteRenderers.Count);
		for (int i = 0; i < ReadySpriteRenderers.Count; i++)
		{
			SpriteRenderer renderer = ReadySpriteRenderers[i];
			if (renderer == null)
			{
				continue;
			}

			renderer.enabled = i < visibleCount;
		}
	}

	private void EnsureRendererList()
	{
		if (ReadySpriteRenderers == null)
		{
			ReadySpriteRenderers = new List<SpriteRenderer>();
		}

		if (ReadySpriteRenderers.Count > 0)
		{
			return;
		}

		SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
		for (int i = 0; i < renderers.Length; i++)
		{
			if (renderers[i] == _bushRenderer)
			{
				continue;
			}

			ReadySpriteRenderers.Add(renderers[i]);
		}
	}

	private Sprite ResolveRuntimeItemSprite(string itemId)
	{
		if (string.IsNullOrWhiteSpace(itemId))
		{
			return null;
		}

		if (_spriteCache.TryGetValue(itemId, out Sprite cache))
		{
			return cache;
		}

		GameRes gameRes = GameRes.Instance;
		if (gameRes != null &&
			gameRes.TryGetItemDefinition(itemId.Trim(), out RuntimeItemDefinition definition) &&
			definition.Sprite != null)
		{
			_spriteCache[itemId] = definition.Sprite;
			return definition.Sprite;
		}

		Debug.LogWarning(
			$"[BerryBush] 未能从 JSON 运行时物品定义解析成熟提示图标，物品ID={itemId}",
			this);
		return null;
	}

#endregion

#region 调试

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = new Color(0.8f, 0.2f, 0.9f, 0.35f);
		Gizmos.DrawWireSphere(transform.position, SpawnRadius);
	}

#endregion
}
